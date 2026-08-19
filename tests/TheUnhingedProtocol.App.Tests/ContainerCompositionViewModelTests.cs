using TheUnhingedProtocol.App.ViewModels;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App.Tests;

public sealed class ContainerCompositionViewModelTests
{
    [Fact]
    public async Task CompleteCompositionWorkflowPreservesIdentityAndSourceFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"protocol-composition-ui-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "unchanged", TestContext.Current.CancellationToken);
        try
        {
            FakeContainerService service = new();
            service.Containers.Add(ContainerDefinition.CreateReferenceGroup("Workspace"));
            MainPageViewModel viewModel = new(service);
            await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
            ContainerCardViewModel container = Assert.Single(viewModel.Containers);

            await viewModel.AddReferenceAsync(container, path, ItemKind.File, TestContext.Current.CancellationToken);
            Guid itemId = Assert.Single(container.Items).Id;
            await viewModel.AddSectionAsync(container, "Planning", TestContext.Current.CancellationToken);
            Guid planningId = container.ActiveSection.Id;
            await viewModel.SetCompositionModeAsync(container, ContainerCompositionMode.Stack, TestContext.Current.CancellationToken);
            await viewModel.SetSectionExpandedAsync(container, planningId, false, TestContext.Current.CancellationToken);
            await viewModel.SetCompositionModeAsync(container, ContainerCompositionMode.Pages, TestContext.Current.CancellationToken);
            await viewModel.SetCompositionModeAsync(container, ContainerCompositionMode.Tabs, TestContext.Current.CancellationToken);

            Assert.Equal(itemId, Assert.Single(container.Items).Id);
            Assert.Equal(planningId, container.ActiveSection.Id);
            Assert.False(container.ActiveSection.IsExpanded);
            Assert.Equal("unchanged", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CompactAppearanceAndLayoutOptionsPersistAndExposeAccessibleState()
    {
        FakeContainerService service = new();
        service.Containers.Add(ContainerDefinition.CreateReferenceGroup("Accessible"));
        MainPageViewModel viewModel = new(service);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        ContainerCardViewModel container = Assert.Single(viewModel.Containers);

        await viewModel.SetDisplayStateAsync(container, ContainerDisplayState.Capsule, TestContext.Current.CancellationToken);
        await viewModel.UpdateAppearanceAsync(
            container,
            0.7,
            ContainerColor.Blue,
            ContainerIconTreatment.Monochrome,
            ContainerBackgroundStyle.SubtleTint,
            TestContext.Current.CancellationToken);
        await viewModel.SetSnapGridAsync(container, 16, TestContext.Current.CancellationToken);
        await viewModel.SetLayoutOptionsAsync(
            container,
            isPinned: true,
            isLocked: true,
            isAutoSize: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(48, container.Height);
        Assert.False(container.CanChangeLayout);
        Assert.False(container.CanResizeLayout);
        Assert.Contains("Capsule", container.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("locked", container.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("pinned", container.AccessibleName, StringComparison.Ordinal);
        ContainerDefinition stored = Assert.Single(service.Containers);
        Assert.Equal(ContainerColor.Blue, stored.Color);
        Assert.Equal(16, stored.SnapGridSize);
    }

    [Fact]
    public async Task LockedContainerRejectsGeometryAndRestoresPersistedBounds()
    {
        ContainerDefinition locked = ContainerDefinition.CreateReferenceGroup(
                "Locked",
                ContainerBounds.Create(24, 24, 320, 240))
            .WithLayoutOptions(isPinned: false, isLocked: true, isAutoSize: false);
        FakeContainerService service = new();
        service.Containers.Add(locked);
        MainPageViewModel viewModel = new(service);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        ContainerCardViewModel container = Assert.Single(viewModel.Containers);

        container.SetInteractiveBounds(ContainerBounds.Create(80, 80, 400, 300));
        await viewModel.SaveContainerBoundsAsync(
            container,
            rasterizationScale: 1.5,
            workspaceWidth: 1280,
            workspaceHeight: 720,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(viewModel.HasError);
        Assert.Equal(locked.Bounds, container.CaptureBounds());
        Assert.Equal(locked, Assert.Single(service.Containers));
    }

    private sealed class FakeContainerService : IContainerService
    {
        public List<ContainerDefinition> Containers { get; } = [];

        public Task<IReadOnlyList<ContainerDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContainerDefinition>>([.. Containers]);

        public Task<ContainerDefinition> CreateReferenceGroupAsync(
            string name,
            ContainerBounds bounds,
            CancellationToken cancellationToken)
        {
            ContainerDefinition container = ContainerDefinition.CreateReferenceGroup(name, bounds);
            Containers.Add(container);
            return Task.FromResult(container);
        }

        public Task<ContainerDefinition> UpdateBoundsAsync(
            Guid containerId,
            ContainerBounds bounds,
            CancellationToken cancellationToken)
        {
            ContainerDefinition existing = Containers.Single(container => container.Id == containerId);
            return UpdateAsync(existing.WithBounds(bounds), cancellationToken);
        }

        public Task<ContainerDefinition> UpdateAsync(
            ContainerDefinition container,
            CancellationToken cancellationToken)
        {
            int index = Containers.FindIndex(existing => existing.Id == container.Id);
            Containers[index] = container;
            return Task.FromResult(container);
        }
    }
}
