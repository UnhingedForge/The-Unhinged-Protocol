using System.Text.Json;
using Microsoft.Data.Sqlite;
using TheUnhingedProtocol.Domain.Contracts;
using TheUnhingedProtocol.Infrastructure.Persistence;
using TheUnhingedProtocol.Infrastructure.Shell;

namespace TheUnhingedProtocol.Domain.Tests;

public sealed class FoundationContractTests
{
    [Fact]
    public void ExportBundleRoundTripsThroughJson()
    {
        ExportBundle original = new()
        {
            Containers =
            [
                new ContainerDefinition
                {
                    Name = "Foundation",
                    Kind = ContainerKind.ReferenceGroup,
                },
            ],
        };

        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        string json = JsonSerializer.Serialize(original, options);
        ExportBundle? restored = JsonSerializer.Deserialize<ExportBundle>(json, options);

        Assert.NotNull(restored);
        Assert.Equal(ContractSchema.CurrentVersion, restored.SchemaVersion);
        Assert.Single(restored.Containers);
        Assert.Equal("Foundation", restored.Containers[0].Name);
    }

    [Fact]
    public void SafetySensitiveContractsRequireConfirmationByDefault()
    {
        RuleAction action = new();
        ActionPlan plan = new();
        FileOperation operation = new();

        Assert.True(action.RequiresConfirmation);
        Assert.True(plan.RequiresUserConfirmation);
        Assert.Equal(ConflictPolicy.RequireDecision, operation.ConflictPolicy);
    }

    [Fact]
    public async Task FoundationMigrationCreatesExpectedTables()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await SqliteSchemaMigrator.MigrateAsync(connection, TestContext.Current.CancellationToken);

        string[] expectedTables =
        [
            "containers",
            "file_transactions",
            "folder_portals",
            "item_references",
            "layout_snapshots",
            "rules",
            "schema_info",
        ];

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";

        List<string> actualTables = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            actualTables.Add(reader.GetString(0));
        }

        Assert.Equal(expectedTables, actualTables);

        await using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT version FROM schema_info ORDER BY version;";
        List<long> versions = [];
        await using SqliteDataReader versionReader = await versionCommand.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await versionReader.ReadAsync(TestContext.Current.CancellationToken))
        {
            versions.Add(versionReader.GetInt64(0));
        }
        Assert.Equal([1, 2, 3, 4, 5], versions);
    }

    [Fact]
    public void UnsupportedSchemaVersionIsRejected()
    {
        Assert.Throws<NotSupportedException>(() => ContractSchema.EnsureSupported(ContractSchema.CurrentVersion + 1));
    }

    [Fact]
    public void ReferenceContainerFactoryNormalizesAndValidatesNames()
    {
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup("  Projects  ");

        Assert.Equal("Projects", container.Name);
        Assert.Equal(ContainerKind.ReferenceGroup, container.Kind);
        Assert.False(container.IsLocked);
        Assert.Equal(1.0, container.Opacity);
        Assert.Equal(ContainerBounds.DefaultWidth, container.Bounds.Width);
        Assert.Equal(ContainerBounds.DefaultHeight, container.Bounds.Height);
        Assert.Throws<ArgumentException>(() => ContainerDefinition.CreateReferenceGroup("   "));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContainerDefinition.CreateReferenceGroup(
                new string('x', ContainerDefinition.MaximumNameLength + 1)));
    }

    [Fact]
    public void ContainerBoundsRejectInvalidGeometry()
    {
        ContainerBounds bounds = ContainerBounds.Create(32, 48, 360, 240);
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup("Work", bounds);

        Assert.Equal(bounds, container.Bounds);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContainerBounds.Create(-1, 0, ContainerBounds.DefaultWidth, ContainerBounds.DefaultHeight));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContainerBounds.Create(0, 0, ContainerBounds.MinimumWidth - 1, ContainerBounds.DefaultHeight));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContainerBounds.Create(0, 0, double.NaN, ContainerBounds.DefaultHeight));
    }

    [Fact]
    public void EverySupportedShellKindCreatesAReferenceWithoutMoveAuthority()
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory)!;
        ItemReference[] references =
        [
            ItemReference.Create(Path.Combine(root, "sample.txt"), ItemKind.File),
            ItemReference.Create(Path.Combine(root, "sample-folder"), ItemKind.Folder),
            ItemReference.Create(Path.Combine(root, "sample.lnk"), ItemKind.Shortcut),
            ItemReference.Create(Path.Combine(root, "sample.exe"), ItemKind.Application),
            ItemReference.Create("https://example.com/path", ItemKind.Url),
        ];

        Assert.Equal(Enum.GetValues<ItemKind>(), references.Select(reference => reference.Kind));
        Assert.All(references, reference => Assert.False(reference.AllowPhysicalMove));
        Assert.Throws<ArgumentException>(() => ItemReference.Create("relative.txt", ItemKind.File));
        Assert.Throws<ArgumentException>(() => ItemReference.Create("file:///unsafe", ItemKind.Url));
    }

    [Fact]
    public void ContainerSupportsAddRemoveManualOrderAndDeterministicSort()
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory)!;
        ItemReference beta = ItemReference.Create(Path.Combine(root, "beta.txt"), ItemKind.File);
        ItemReference alpha = ItemReference.Create(Path.Combine(root, "alpha.txt"), ItemKind.File);
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup("Items")
            .AddItem(beta)
            .AddItem(alpha);

        Assert.Equal(["beta.txt", "alpha.txt"], container.Items.Select(item => item.DisplayName));
        container = container.MoveItem(alpha.Id, -1);
        Assert.Equal(["alpha.txt", "beta.txt"], container.Items.Select(item => item.DisplayName));
        container = container.WithSortMode(ContainerSortMode.NameDescending);
        Assert.Equal(["beta.txt", "alpha.txt"], container.Items.Select(item => item.DisplayName));
        container = container.RemoveItem(beta.Id);
        Assert.Equal("alpha.txt", Assert.Single(container.Items).DisplayName);
    }

    [Fact]
    public void DuplicateReferencesAndUnsafeMetadataAreRejected()
    {
        string path = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "duplicate.txt");
        ItemReference item = ItemReference.Create(path, ItemKind.File);
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup("Duplicates").AddItem(item);

        Assert.Throws<InvalidOperationException>(() => container.AddItem(ItemReference.Create(path, ItemKind.File)));
        Assert.Throws<ArgumentOutOfRangeException>(() => container.WithPresentation("Safe", null, [], "not-approved"));
        Assert.Throws<InvalidDataException>(() => (item with { AllowPhysicalMove = true }).EnsureValid());
    }

    [Fact]
    public void LabelsTagsThumbnailsAndApprovedIconsRoundTrip()
    {
        string path = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "image.png");
        ItemReference item = ItemReference.Create(path, ItemKind.File).WithMetadata(
            "Preview",
            "Design",
            ["visual", "Visual", "approved"],
            ContainerDefinition.ApprovedIconGlyphs[1],
            showThumbnail: true);
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup("Media")
            .WithPresentation("Media", "Current", ["work", "Work"], ContainerDefinition.ApprovedIconGlyphs[2])
            .AddItem(item);

        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        string json = JsonSerializer.Serialize(container, options);
        ContainerDefinition restored = JsonSerializer.Deserialize<ContainerDefinition>(
            json,
            options)!.UpgradeToCurrent();

        Assert.Equal(["Work"], restored.Tags, StringComparer.OrdinalIgnoreCase);
        ItemReference restoredItem = Assert.Single(restored.Items);
        Assert.Equal("Design", restoredItem.Label);
        Assert.Equal(["approved", "visual"], restoredItem.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.True(restoredItem.ShowThumbnail);
    }

    [Fact]
    public void ShellReferenceFactoryRejectsUnavailableTargetsWithoutCreatingAnything()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt");
        Assert.Throws<FileNotFoundException>(() => ShellReferenceFactory.CreateAvailable(missing, ItemKind.File));
        ItemReference url = ShellReferenceFactory.CreateAvailable("https://example.com", ItemKind.Url);
        Assert.Equal(ItemKind.Url, url.Kind);
    }
}
