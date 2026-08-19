using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;
using Windows.Storage;
using Windows.Storage.Search;

namespace TheUnhingedProtocol.App;

public sealed class WindowsSearchAdapter : IWindowsSearchAdapter
{
    public async Task<SearchResponse> SearchAsync(string query, CancellationToken cancellationToken)
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopPath) || !Directory.Exists(desktopPath))
        {
            return new SearchResponse
            {
                WindowsSearchState = SearchAvailability.Offline,
                WindowsSearchMessage = "The Windows Desktop search location is unavailable.",
            };
        }

        StorageFolder desktop = await StorageFolder.GetFolderFromPathAsync(desktopPath).AsTask(cancellationToken);
        QueryOptions options = new(CommonFileQuery.OrderBySearchRank, null)
        {
            FolderDepth = FolderDepth.Deep,
            IndexerOption = IndexerOption.UseIndexerWhenAvailable,
            UserSearchFilter = query,
        };
        StorageFileQueryResult fileQuery = desktop.CreateFileQueryWithOptions(options);
        IReadOnlyList<StorageFile> files = await fileQuery.GetFilesAsync(0, 100).AsTask(cancellationToken);
        SearchResult[] results = files.Select((file, index) => new SearchResult
        {
            Id = file.Path,
            Title = file.DisplayName,
            Subtitle = file.Path,
            Target = file.Path,
            Source = SearchResultSource.WindowsSearch,
            Score = 450 - Math.Min(index, 100),
            ActionLabel = "Open",
        }).ToArray();
        return new SearchResponse
        {
            Results = results,
            WindowsSearchState = SearchAvailability.Ready,
            WindowsSearchMessage = "Windows indexed Desktop results are ready. No query or result left this PC.",
        };
    }
}
