using System.Diagnostics;
using TheUnhingedProtocol.Domain.Contracts;
using TheUnhingedProtocol.Infrastructure.Persistence;
using TheUnhingedProtocol.Infrastructure.Shell;

namespace TheUnhingedProtocol.Domain.Tests;

public sealed class FolderPortalServiceTests
{
    [Fact]
    public async Task SqlitePortalRoundTripPreservesIndependentTabs()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "state.db");
            SqliteFolderPortalService writer = new(databasePath);
            FolderPortal created = await writer.CreateAsync("Projects", directory, TestContext.Current.CancellationToken);
            FolderPortal updated = created
                .AddTab(Path.GetTempPath())
                .UpdateTab(created.Tabs[0].WithView(PortalViewMode.List).WithSort(PortalSortMode.SizeDescending).WithSearch("notes"));
            await writer.UpdateAsync(updated, TestContext.Current.CancellationToken);

            SqliteFolderPortalService reader = new(databasePath);
            FolderPortal restored = Assert.Single(await reader.GetAllAsync(TestContext.Current.CancellationToken));

            Assert.Equal(2, restored.Tabs.Length);
            Assert.Equal(PortalViewMode.List, restored.Tabs[0].ViewMode);
            Assert.Equal(PortalSortMode.SizeDescending, restored.Tabs[0].SortMode);
            Assert.Equal("notes", restored.Tabs[0].SearchQuery);
            Assert.Equal(updated.ActiveTabId, restored.ActiveTabId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BrowserSearchSortPreviewAndRefreshAreReadOnly()
    {
        string directory = CreateTemporaryDirectory();
        string alpha = Path.Combine(directory, "alpha.txt");
        string beta = Path.Combine(directory, "beta.log");
        string subfolder = Path.Combine(directory, "alpha folder");
        await File.WriteAllTextAsync(alpha, "preview text", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(beta, "unchanged", TestContext.Current.CancellationToken);
        Directory.CreateDirectory(subfolder);
        try
        {
            FileSystemFolderBrowser browser = new();
            FolderPortalTab tab = FolderPortalTab.Create(directory)
                .WithSearch("alpha")
                .WithSort(PortalSortMode.TypeThenName);

            PortalLoadResult result = await browser.BrowseAsync(tab, TestContext.Current.CancellationToken);
            PortalItem file = Assert.Single(result.Items, item => item.Kind == PortalItemKind.File);
            PortalPreview preview = await browser.GetPreviewAsync(file, TestContext.Current.CancellationToken);

            Assert.Equal(PortalTargetState.Ready, result.State);
            Assert.Equal(2, result.Items.Length);
            Assert.Equal(PortalItemKind.File, result.Items[0].Kind);
            Assert.Equal("preview text", preview.TextContent);
            Assert.True(File.Exists(alpha));
            Assert.Equal("unchanged", await File.ReadAllTextAsync(beta, TestContext.Current.CancellationToken));
            Assert.True(Directory.Exists(subfolder));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MissingPortalTargetProducesVisibleRecoverableState()
    {
        string missing = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));
        FileSystemFolderBrowser browser = new();

        PortalLoadResult result = await browser.BrowseAsync(
            FolderPortalTab.Create(missing),
            TestContext.Current.CancellationToken);

        Assert.Equal(PortalTargetState.Missing, result.State);
        Assert.Contains("renamed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Choose a new folder", result.Message, StringComparison.Ordinal);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void TenThousandItemSortMeetsInteractiveBenchmark()
    {
        PortalItem[] items = Enumerable.Range(0, 10_000)
            .Select(index => new PortalItem
            {
                FullPath = $@"C:\Benchmark\item-{index:D5}.txt",
                Name = $"item-{10_000 - index:D5}.txt",
                Kind = PortalItemKind.File,
                SizeBytes = index,
                ModifiedAt = DateTimeOffset.UnixEpoch.AddMinutes(index),
                TypeLabel = "TXT",
            })
            .ToArray();
        Stopwatch stopwatch = Stopwatch.StartNew();

        PortalItem[] sorted = FileSystemFolderBrowser.Sort(items, PortalSortMode.NameAscending).ToArray();

        stopwatch.Stop();
        Assert.Equal(10_000, sorted.Length);
        Assert.Equal("item-00001.txt", sorted[0].Name);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"10,000-item sort took {stopwatch.Elapsed.TotalMilliseconds:N0} ms.");
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"protocol-portal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
