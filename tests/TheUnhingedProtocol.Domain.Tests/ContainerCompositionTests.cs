using System.Text.Json;
using TheUnhingedProtocol.Domain.Contracts;
using TheUnhingedProtocol.Infrastructure.Persistence;

namespace TheUnhingedProtocol.Domain.Tests;

public sealed class ContainerCompositionTests
{
    [Fact]
    public void TabsStacksAndPagesPreserveSectionAndItemIdentity()
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory)!;
        ItemReference first = ItemReference.Create(Path.Combine(root, "first.txt"), ItemKind.File);
        ItemReference second = ItemReference.Create(Path.Combine(root, "second.txt"), ItemKind.File);
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup("Composition").AddItem(first);
        Guid firstSectionId = container.ActiveSectionId;
        container = container.AddSection("Secondary").AddItem(second);
        Guid secondSectionId = container.ActiveSectionId;

        foreach (ContainerCompositionMode mode in Enum.GetValues<ContainerCompositionMode>())
        {
            container = container.WithCompositionMode(mode).SelectSection(firstSectionId);
            Assert.Equal([first.Id, second.Id], container.Items.Select(item => item.Id));
            Assert.Equal(firstSectionId, container.ActiveSectionId);
            Assert.Equal(first.Id, Assert.Single(container.Sections.Single(section => section.Id == firstSectionId).ItemIds));
            Assert.Equal(second.Id, Assert.Single(container.Sections.Single(section => section.Id == secondSectionId).ItemIds));
        }
    }

    [Fact]
    public void RemovingASectionRehomesReferencesWithoutChangingThem()
    {
        string path = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "preserved.txt");
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup("Safe")
            .AddSection("Temporary")
            .AddItem(ItemReference.Create(path, ItemKind.File));
        Guid removedSectionId = container.ActiveSectionId;

        ContainerDefinition updated = container.RemoveSection(removedSectionId);

        ItemReference preserved = Assert.Single(updated.Items);
        Assert.Equal(path, preserved.CanonicalPath);
        Assert.Contains(preserved.Id, Assert.Single(updated.Sections).ItemIds);
        Assert.Throws<InvalidOperationException>(() => updated.RemoveSection(updated.ActiveSectionId));
    }

    [Fact]
    public void DisplayAndLayoutStatesPersistWhileLockPreventsGeometryChanges()
    {
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup("States")
            .WithDisplayState(ContainerDisplayState.Capsule)
            .WithLayoutOptions(isPinned: true, isLocked: false, isAutoSize: true)
            .WithSnapGrid(16)
            .WithLayoutOptions(isPinned: true, isLocked: true, isAutoSize: true);

        Assert.True(container.IsPinned);
        Assert.True(container.IsLocked);
        Assert.True(container.IsAutoSize);
        Assert.Equal(ContainerDisplayState.Capsule, container.DisplayState);
        Assert.Equal(16, container.SnapGridSize);
        Assert.Throws<InvalidOperationException>(() => container.WithBounds(ContainerBounds.Create(40, 40, 320, 240)));
        Assert.Throws<InvalidOperationException>(() => container.WithCompositionMode(ContainerCompositionMode.Pages));
        Assert.Throws<InvalidOperationException>(() => container.WithSnapGrid(24));

        ContainerDefinition expanded = container.WithDisplayState(ContainerDisplayState.Expanded);
        Assert.Equal(ContainerDisplayState.Expanded, expanded.DisplayState);
        Assert.Equal(container.ActiveSectionId, expanded.SelectSection(container.ActiveSectionId).ActiveSectionId);
    }

    [Fact]
    public void SnapGridIsDeterministicAtEverySupportedScalingLevel()
    {
        ContainerBounds candidate = ContainerBounds.Create(37.2, 51.8, 333.3, 247.7);
        foreach (double scale in new[] { 1d, 1.25, 1.5, 1.75, 2, 2.25, 2.5, 3 })
        {
            ContainerBounds snapped = ContainerLayoutPolicy.SnapBounds(candidate, 16, scale, 1920 / scale, 1080 / scale);
            ContainerBounds repeated = ContainerLayoutPolicy.SnapBounds(snapped, 16, scale, 1920 / scale, 1080 / scale);

            Assert.Equal(snapped, repeated);
            Assert.True(IsPhysicalGridMultiple(snapped.X, scale, 16));
            Assert.True(IsPhysicalGridMultiple(snapped.Y, scale, 16));
            Assert.True(IsPhysicalGridMultiple(snapped.Width, scale, 16));
            Assert.True(IsPhysicalGridMultiple(snapped.Height, scale, 16));
        }
    }

    [Fact]
    public void ApprovedAppearanceRoundTripsWithoutArtworkPaths()
    {
        ContainerDefinition expected = ContainerDefinition.CreateReferenceGroup("Appearance")
            .WithAppearance(
                0.75,
                ContainerColor.Teal,
                ContainerIconTreatment.Monochrome,
                ContainerBackgroundStyle.SubtleTint);
        JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web);
        string json = JsonSerializer.Serialize(expected, serializerOptions);
        ContainerDefinition restored = JsonSerializer.Deserialize<ContainerDefinition>(
            json,
            serializerOptions)!.UpgradeToCurrent();

        Assert.Equal(expected.Opacity, restored.Opacity);
        Assert.Equal(expected.Color, restored.Color);
        Assert.Equal(expected.IconTreatment, restored.IconTreatment);
        Assert.Equal(expected.BackgroundStyle, restored.BackgroundStyle);
        Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => expected.WithAppearance(0.59, ContainerColor.Blue, ContainerIconTreatment.Accent, ContainerBackgroundStyle.System));
    }

    [Fact]
    public async Task CompleteCompositionPersistsAcrossSqliteServiceInstances()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"protocol-composition-{Guid.NewGuid():N}.db");
        try
        {
            SqliteContainerService writer = new(databasePath);
            ContainerDefinition created = await writer.CreateReferenceGroupAsync(
                "Persisted",
                ContainerBounds.Create(48, 64, 420, 300),
                TestContext.Current.CancellationToken);
            ContainerDefinition expected = created
                .AddSection("Second page")
                .WithCompositionMode(ContainerCompositionMode.Pages)
                .WithDisplayState(ContainerDisplayState.Collapsed)
                .WithAppearance(0.8, ContainerColor.Rose, ContainerIconTreatment.Neutral, ContainerBackgroundStyle.SubtleTint)
                .WithSnapGrid(24)
                .WithLayoutOptions(isPinned: true, isLocked: true, isAutoSize: false);
            await writer.UpdateAsync(expected, TestContext.Current.CancellationToken);

            SqliteContainerService reader = new(databasePath);
            ContainerDefinition restored = Assert.Single(await reader.GetAllAsync(TestContext.Current.CancellationToken));

            Assert.Equal(expected.Id, restored.Id);
            Assert.Equal(expected.CompositionMode, restored.CompositionMode);
            Assert.Equal(expected.DisplayState, restored.DisplayState);
            Assert.Equal(expected.IsPinned, restored.IsPinned);
            Assert.Equal(expected.IsLocked, restored.IsLocked);
            Assert.Equal(expected.IsAutoSize, restored.IsAutoSize);
            Assert.Equal(expected.Opacity, restored.Opacity);
            Assert.Equal(expected.SnapGridSize, restored.SnapGridSize);
            Assert.Equal(expected.Color, restored.Color);
            Assert.Equal(expected.IconTreatment, restored.IconTreatment);
            Assert.Equal(expected.BackgroundStyle, restored.BackgroundStyle);
            Assert.Equal(expected.ActiveSectionId, restored.ActiveSectionId);
            Assert.Equal(
                expected.Sections.Select(section => (section.Id, section.Name, section.IsExpanded, ItemIds: string.Join(',', section.ItemIds))),
                restored.Sections.Select(section => (section.Id, section.Name, section.IsExpanded, ItemIds: string.Join(',', section.ItemIds))));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public void AutoSizeIsBoundedAndAccountsForExpandedStackSections()
    {
        Assert.Equal(260, ContainerLayoutPolicy.AutoSizeHeight(0, ContainerCompositionMode.Tabs, 1));
        Assert.Equal(640, ContainerLayoutPolicy.AutoSizeHeight(100, ContainerCompositionMode.Stack, 12));
        Assert.True(
            ContainerLayoutPolicy.AutoSizeHeight(4, ContainerCompositionMode.Stack, 3) >
            ContainerLayoutPolicy.AutoSizeHeight(4, ContainerCompositionMode.Tabs, 3));
    }

    private static bool IsPhysicalGridMultiple(double dipValue, double scale, double gridSize)
    {
        double physical = dipValue * scale;
        return Math.Abs(physical - (Math.Round(physical / gridSize) * gridSize)) < 0.0001;
    }
}
