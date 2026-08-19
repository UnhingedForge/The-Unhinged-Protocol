using System.Text.Json;
using Microsoft.Data.Sqlite;
using TheUnhingedProtocol.Domain.Contracts;
using TheUnhingedProtocol.Infrastructure.Persistence;

namespace TheUnhingedProtocol.Domain.Tests;

public sealed class SqliteContainerServiceTests
{
    [Fact]
    public async Task CreatedReferenceContainerPersistsAcrossServiceInstances()
    {
        string databasePath = CreateTemporaryDatabasePath();
        try
        {
            SqliteContainerService writer = new(databasePath);
            ContainerDefinition created = await writer.CreateReferenceGroupAsync(
                "  Active projects  ",
                ContainerBounds.Create(40, 56, 320, 224),
                TestContext.Current.CancellationToken);

            SqliteContainerService reader = new(databasePath);
            IReadOnlyList<ContainerDefinition> restored = await reader.GetAllAsync(
                TestContext.Current.CancellationToken);

            ContainerDefinition actual = Assert.Single(restored);
            Assert.Equal(created.Id, actual.Id);
            Assert.Equal("Active projects", actual.Name);
            Assert.Equal(ContainerKind.ReferenceGroup, actual.Kind);
            Assert.Equal(ContractSchema.CurrentVersion, actual.SchemaVersion);
            Assert.Equal(ContainerBounds.Create(40, 56, 320, 224), actual.Bounds);
        }
        finally
        {
            DeleteTemporaryDatabase(databasePath);
        }
    }

    [Fact]
    public async Task MultipleContainersAreReturnedInCreationOrder()
    {
        string databasePath = CreateTemporaryDatabasePath();
        try
        {
            SqliteContainerService service = new(databasePath);
            await service.CreateReferenceGroupAsync(
                "Container 1",
                new ContainerBounds(),
                TestContext.Current.CancellationToken);
            await service.CreateReferenceGroupAsync(
                "Container 2",
                new ContainerBounds(),
                TestContext.Current.CancellationToken);

            IReadOnlyList<ContainerDefinition> containers = await service.GetAllAsync(
                TestContext.Current.CancellationToken);

            Assert.Equal(["Container 1", "Container 2"], containers.Select(container => container.Name));
        }
        finally
        {
            DeleteTemporaryDatabase(databasePath);
        }
    }

    [Fact]
    public async Task UpdatedBoundsPersistAcrossServiceInstances()
    {
        string databasePath = CreateTemporaryDatabasePath();
        try
        {
            SqliteContainerService writer = new(databasePath);
            ContainerDefinition created = await writer.CreateReferenceGroupAsync(
                "Resizable",
                new ContainerBounds(),
                TestContext.Current.CancellationToken);
            ContainerBounds expected = ContainerBounds.Create(96, 72, 480, 320);

            ContainerDefinition updated = await writer.UpdateBoundsAsync(
                created.Id,
                expected,
                TestContext.Current.CancellationToken);
            SqliteContainerService reader = new(databasePath);
            ContainerDefinition restored = Assert.Single(
                await reader.GetAllAsync(TestContext.Current.CancellationToken));

            Assert.Equal(expected, updated.Bounds);
            Assert.Equal(expected, restored.Bounds);
        }
        finally
        {
            DeleteTemporaryDatabase(databasePath);
        }
    }

    [Fact]
    public async Task CompleteContainerStateRoundTripsAcrossServiceInstances()
    {
        string databasePath = CreateTemporaryDatabasePath();
        try
        {
            SqliteContainerService writer = new(databasePath);
            ContainerDefinition created = await writer.CreateReferenceGroupAsync(
                "References",
                ContainerBounds.Create(80, 96, 520, 340),
                TestContext.Current.CancellationToken);
            string root = Path.GetPathRoot(Environment.SystemDirectory)!;
            ContainerDefinition expected = created
                .WithPresentation("Launch pad", "Daily", ["work", "priority"], ContainerDefinition.ApprovedIconGlyphs[2])
                .AddItem(ItemReference.Create(Path.Combine(root, "notes.txt"), ItemKind.File))
                .AddItem(ItemReference.Create("https://example.com", ItemKind.Url))
                .WithSortMode(ContainerSortMode.KindThenName);

            await writer.UpdateAsync(expected, TestContext.Current.CancellationToken);
            SqliteContainerService reader = new(databasePath);
            ContainerDefinition restored = Assert.Single(
                await reader.GetAllAsync(TestContext.Current.CancellationToken));

            Assert.Equal(expected.Id, restored.Id);
            Assert.Equal(expected.Name, restored.Name);
            Assert.Equal(expected.Bounds, restored.Bounds);
            Assert.Equal(expected.Label, restored.Label);
            Assert.Equal(expected.Tags, restored.Tags);
            Assert.Equal(expected.IconGlyph, restored.IconGlyph);
            Assert.Equal(expected.SortMode, restored.SortMode);
            Assert.Equal(
                expected.Items.Select(item => (item.Id, item.CanonicalPath, item.DisplayName, item.Kind, item.SortOrder)),
                restored.Items.Select(item => (item.Id, item.CanonicalPath, item.DisplayName, item.Kind, item.SortOrder)));
            Assert.All(restored.Items, item => Assert.False(item.AllowPhysicalMove));
        }
        finally
        {
            DeleteTemporaryDatabase(databasePath);
        }
    }

    [Fact]
    public async Task InvalidUpdateLeavesLastValidContainerRecoverable()
    {
        string databasePath = CreateTemporaryDatabasePath();
        try
        {
            SqliteContainerService service = new(databasePath);
            ContainerDefinition created = await service.CreateReferenceGroupAsync(
                "Recovery",
                new ContainerBounds(),
                TestContext.Current.CancellationToken);
            ContainerDefinition invalid = created with { SchemaVersion = ContractSchema.CurrentVersion + 1 };

            await Assert.ThrowsAsync<NotSupportedException>(
                () => service.UpdateAsync(invalid, TestContext.Current.CancellationToken));
            ContainerDefinition restored = Assert.Single(
                await service.GetAllAsync(TestContext.Current.CancellationToken));

            Assert.Equal(created.Id, restored.Id);
            Assert.Equal(created.Name, restored.Name);
            Assert.Equal(created.Bounds, restored.Bounds);
            Assert.Equal(created.ActiveSectionId, restored.ActiveSectionId);
            Assert.Equal(
                created.Sections.Select(section => (section.Id, section.Name, section.IsExpanded, ItemIds: string.Join(',', section.ItemIds))),
                restored.Sections.Select(section => (section.Id, section.Name, section.IsExpanded, ItemIds: string.Join(',', section.ItemIds))));
        }
        finally
        {
            DeleteTemporaryDatabase(databasePath);
        }
    }

    [Fact]
    public async Task VersionOnePayloadIsUpgradedInMemoryWithoutLosingState()
    {
        string databasePath = CreateTemporaryDatabasePath();
        try
        {
            await using (SqliteConnection connection = new($"Data Source={databasePath}"))
            {
                await SqliteSchemaMigrator.MigrateAsync(connection, TestContext.Current.CancellationToken);
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO containers (id, schema_version, payload_json, updated_utc)
                    VALUES ($id, 1, $payload, $updated);
                    """;
                Guid id = Guid.NewGuid();
                command.Parameters.AddWithValue("$id", id.ToString("D"));
                string payload = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    id,
                    name = "Legacy",
                    kind = 0,
                    isLocked = false,
                    opacity = 1,
                    bounds = new { x = 40, y = 48, width = 320, height = 240 },
                });
                command.Parameters.AddWithValue("$payload", payload);
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            SqliteContainerService service = new(databasePath);
            ContainerDefinition restored = Assert.Single(
                await service.GetAllAsync(TestContext.Current.CancellationToken));

            Assert.Equal(ContractSchema.CurrentVersion, restored.SchemaVersion);
            Assert.Equal("Legacy", restored.Name);
            Assert.Empty(restored.Items);
            Assert.Equal(ContainerDefinition.ApprovedIconGlyphs[0], restored.IconGlyph);
        }
        finally
        {
            DeleteTemporaryDatabase(databasePath);
        }
    }

    private static string CreateTemporaryDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        $"the-unhinged-protocol-{Guid.NewGuid():N}.db");

    private static void DeleteTemporaryDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
