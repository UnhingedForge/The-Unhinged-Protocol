using TheUnhingedProtocol.App.ViewModels;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App.Tests;

public sealed class MainPageViewModelTests
{
    [Fact]
    public async Task TemplateCreationProducesPersistedContainerPresentation()
    {
        FakeContainerService service = new();
        MainPageViewModel viewModel = new(service);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        await viewModel.CreateContainerAsync(
            ContainerTemplateKind.Wide,
            cancellationToken: TestContext.Current.CancellationToken);

        ContainerCardViewModel container = Assert.Single(viewModel.Containers);
        Assert.Equal(480, container.Width);
        Assert.Equal(240, container.Height);
        Assert.Single(service.Containers);
        Assert.Contains("No files were moved", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddSortEditAndRemoveReferenceNeverChangesOriginalFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"protocol-ui-test-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "unchanged", TestContext.Current.CancellationToken);
        try
        {
            FakeContainerService service = new();
            ContainerDefinition definition = ContainerDefinition.CreateReferenceGroup("Test");
            service.Containers.Add(definition);
            MainPageViewModel viewModel = new(service);
            await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
            ContainerCardViewModel container = Assert.Single(viewModel.Containers);

            await viewModel.AddReferenceAsync(container, path, ItemKind.File, TestContext.Current.CancellationToken);
            ItemCardViewModel item = Assert.Single(container.Items);
            ItemReference edited = item.Reference.WithMetadata("Pinned note", "Daily", ["work"], null, false);
            await viewModel.UpdateReferenceAsync(container, edited, TestContext.Current.CancellationToken);
            await viewModel.SetSortModeAsync(container, ContainerSortMode.NameAscending, TestContext.Current.CancellationToken);
            await viewModel.RemoveReferenceAsync(container, item.Id, TestContext.Current.CancellationToken);

            Assert.Empty(container.Items);
            Assert.True(File.Exists(path));
            Assert.Equal("unchanged", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FailedPersistenceRestoresPriorPresentationAndReferenceState()
    {
        string path = Path.Combine(Path.GetTempPath(), $"protocol-recovery-test-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "safe", TestContext.Current.CancellationToken);
        try
        {
            FakeContainerService service = new();
            service.Containers.Add(ContainerDefinition.CreateReferenceGroup("Recovery"));
            MainPageViewModel viewModel = new(service);
            await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
            ContainerCardViewModel container = Assert.Single(viewModel.Containers);
            service.FailUpdates = true;

            await viewModel.AddReferenceAsync(container, path, ItemKind.File, TestContext.Current.CancellationToken);

            Assert.True(viewModel.HasError);
            Assert.Empty(container.Items);
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class FakeContainerService : IContainerService
    {
        public List<ContainerDefinition> Containers { get; } = [];

        public bool FailUpdates { get; set; }

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
            if (FailUpdates)
            {
                throw new IOException("Simulated persistence failure.");
            }

            int index = Containers.FindIndex(existing => existing.Id == container.Id);
            Containers[index] = container;
            return Task.FromResult(container);
        }
    }
}
