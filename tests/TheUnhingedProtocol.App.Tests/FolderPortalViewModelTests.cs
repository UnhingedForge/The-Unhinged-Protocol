using System.Diagnostics;
using TheUnhingedProtocol.App.ViewModels;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App.Tests;

public sealed class FolderPortalViewModelTests
{
    [Fact]
    public async Task TabChangesPersistAndRemainIndependent()
    {
        string firstPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "portal-vm-first"));
        string secondPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "portal-vm-second"));
        FakePortalService persistence = new(FolderPortal.Create("Portal", firstPath));
        FakeBrowserService browser = new();
        FolderPortalViewModel viewModel = FolderPortalViewModel.FromDefinition(persistence.Portal, persistence, browser);

        await viewModel.SetViewModeAsync(PortalViewMode.Details, TestContext.Current.CancellationToken);
        await viewModel.SetSortModeAsync(PortalSortMode.ModifiedNewest, TestContext.Current.CancellationToken);
        await viewModel.SetSearchAsync("report", TestContext.Current.CancellationToken);
        await viewModel.AddTabAsync(secondPath, TestContext.Current.CancellationToken);

        Assert.Equal(2, viewModel.Tabs.Count);
        Assert.Equal(secondPath, viewModel.ActiveTab.CurrentPath);
        PortalTabViewModel first = viewModel.Tabs[0];
        Assert.Equal(PortalViewMode.Details, first.ViewMode);
        Assert.Equal(PortalSortMode.ModifiedNewest, first.SortMode);
        Assert.Equal("report", first.SearchQuery);
        Assert.Equal(PortalViewMode.Grid, viewModel.ActiveTab.ViewMode);
        Assert.True(persistence.UpdateCount >= 4);
    }

    [Fact]
    public async Task TenThousandItemsLoadWithSingleListReplacementWithinBudget()
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "portal-vm-benchmark"));
        FakePortalService persistence = new(FolderPortal.Create("Benchmark", path));
        FakeBrowserService browser = new(itemCount: 10_000);
        FolderPortalViewModel viewModel = FolderPortalViewModel.FromDefinition(persistence.Portal, persistence, browser);
        Stopwatch stopwatch = Stopwatch.StartNew();

        await viewModel.RefreshAsync(TestContext.Current.CancellationToken);

        stopwatch.Stop();
        Assert.Equal(10_000, viewModel.ActiveTab.Items.Count);
        Assert.False(viewModel.ActiveTab.IsLoading);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"10,000-item view-model load took {stopwatch.Elapsed.TotalMilliseconds:N0} ms.");
    }

    [Fact]
    public async Task RecoverableBrowserFailureRemainsVisibleWithoutChangingFiles()
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "portal-vm-missing"));
        FakePortalService persistence = new(FolderPortal.Create("Missing", path));
        FakeBrowserService browser = new(state: PortalTargetState.Disconnected);
        FolderPortalViewModel viewModel = FolderPortalViewModel.FromDefinition(persistence.Portal, persistence, browser);

        await viewModel.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.HasError);
        Assert.True(viewModel.ActiveTab.HasRecoverableError);
        Assert.Equal(PortalTargetState.Disconnected, viewModel.ActiveTab.TargetState);
        Assert.Contains("Reconnect", viewModel.ActiveTab.StateMessage, StringComparison.Ordinal);
    }

    private sealed class FakePortalService(FolderPortal portal) : IFolderPortalService
    {
        public FolderPortal Portal { get; private set; } = portal;

        public int UpdateCount { get; private set; }

        public Task<IReadOnlyList<FolderPortal>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FolderPortal>>([Portal]);

        public Task<FolderPortal> CreateAsync(string name, string folderPath, CancellationToken cancellationToken)
        {
            Portal = FolderPortal.Create(name, folderPath);
            return Task.FromResult(Portal);
        }

        public Task<FolderPortal> UpdateAsync(FolderPortal portal, CancellationToken cancellationToken)
        {
            Portal = portal.EnsureValid();
            UpdateCount++;
            return Task.FromResult(Portal);
        }

        public Task DeleteAsync(Guid portalId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeBrowserService(int itemCount = 0, PortalTargetState state = PortalTargetState.Ready) : IFolderBrowserService
    {
        public Task<PortalLoadResult> BrowseAsync(FolderPortalTab tab, CancellationToken cancellationToken)
        {
            PortalItem[] items = Enumerable.Range(0, itemCount)
                .Select(index => new PortalItem
                {
                    FullPath = Path.Combine(tab.CurrentPath, $"item-{index:D5}.txt"),
                    Name = $"item-{index:D5}.txt",
                    Kind = PortalItemKind.File,
                    SizeBytes = index,
                    ModifiedAt = DateTimeOffset.UnixEpoch,
                    TypeLabel = "TXT",
                })
                .ToArray();
            return Task.FromResult(new PortalLoadResult
            {
                State = state,
                Message = state == PortalTargetState.Ready ? $"{items.Length:N0} items" : "Reconnect the target and refresh.",
                Items = items,
                Elapsed = TimeSpan.FromMilliseconds(10),
            });
        }

        public Task<PortalPreview> GetPreviewAsync(PortalItem item, CancellationToken cancellationToken) =>
            Task.FromResult(new PortalPreview { Title = item.Name });
    }
}
