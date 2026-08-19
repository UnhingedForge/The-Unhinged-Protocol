using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.Infrastructure.Search;

public sealed class UnifiedSearchService(IWindowsSearchAdapter windowsSearchAdapter) : IUnifiedSearchService
{
    private static readonly (string Title, string Subtitle, string Target)[] Settings =
    [
        ("Visibility controls", "Show or hide organizer surfaces", "settings:visibility"),
        ("Global hotkeys", "Configure keyboard shortcuts", "settings:hotkeys"),
        ("Desktop double-click", "Opt-in desktop gesture", "settings:gesture"),
        ("Layout snapshots", "Create, compare, restore, import, or export layouts", "settings:layouts"),
        ("Guided onboarding", "Scan approved desktop metadata without changing files", "settings:onboarding"),
    ];

    private readonly IWindowsSearchAdapter windowsSearchAdapter =
        windowsSearchAdapter ?? throw new ArgumentNullException(nameof(windowsSearchAdapter));

    public async Task<SearchResponse> SearchAsync(
        string query,
        IReadOnlyList<ContainerDefinition> containers,
        IReadOnlyList<FolderPortal> portals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(containers);
        ArgumentNullException.ThrowIfNull(portals);
        string normalized = query?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return new SearchResponse { WindowsSearchState = SearchAvailability.Ready };
        }

        cancellationToken.ThrowIfCancellationRequested();
        List<SearchResult> local = [];
        foreach (ContainerDefinition container in containers)
        {
            AddIfMatch(local, normalized, container.Id.ToString("D"), container.Name, container.Label,
                null, SearchResultSource.Container, "Focus container");
            foreach (string tag in container.Tags)
            {
                AddIfMatch(local, normalized, $"tag:{tag}", tag, $"Tag in {container.Name}", null,
                    SearchResultSource.Tag, "Show tagged items");
            }

            foreach (ItemReference item in container.Items)
            {
                SearchResultSource source = item.Kind == ItemKind.Application
                    ? SearchResultSource.Application
                    : SearchResultSource.DesktopItem;
                AddIfMatch(local, normalized, item.Id.ToString("D"), item.DisplayName,
                    $"{item.Kind} in {container.Name}", item.CanonicalPath, source, "Open");
                foreach (string tag in item.Tags)
                {
                    AddIfMatch(local, normalized, $"{item.Id:D}:tag:{tag}", tag,
                        $"Tag on {item.DisplayName}", item.CanonicalPath, SearchResultSource.Tag, "Open");
                }
            }
        }

        foreach (FolderPortal portal in portals)
        {
            AddIfMatch(local, normalized, portal.Id.ToString("D"), portal.Name,
                portal.Tabs.Single(tab => tab.Id == portal.ActiveTabId).CurrentPath,
                null, SearchResultSource.Portal, "Focus portal");
        }

        foreach ((string title, string subtitle, string target) in Settings)
        {
            AddIfMatch(local, normalized, target, title, subtitle, target, SearchResultSource.Setting, "Open setting");
        }

        SearchResponse windows;
        try
        {
            windows = await windowsSearchAdapter.SearchAsync(normalized, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            windows = new SearchResponse
            {
                WindowsSearchState = SearchAvailability.PermissionDenied,
                WindowsSearchMessage = "Windows Search denied access. Local organizer results remain available.",
            };
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            windows = new SearchResponse
            {
                WindowsSearchState = SearchAvailability.IndexUnavailable,
                WindowsSearchMessage = "Windows Search is unavailable. Local organizer results remain available.",
            };
        }

        SearchResult[] results = local
            .Concat(windows.Results)
            .GroupBy(result => $"{result.Source}:{result.Id}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Source)
            .Take(200)
            .ToArray();
        return new SearchResponse
        {
            Results = results,
            WindowsSearchState = windows.WindowsSearchState,
            WindowsSearchMessage = windows.WindowsSearchMessage,
        };
    }

    private static void AddIfMatch(
        List<SearchResult> results,
        string query,
        string id,
        string title,
        string? subtitle,
        string? target,
        SearchResultSource source,
        string actionLabel)
    {
        int score = Score(query, title, subtitle, target);
        if (score <= 0)
        {
            return;
        }

        results.Add(new SearchResult
        {
            Id = id,
            Title = title,
            Subtitle = subtitle,
            Target = target,
            Source = source,
            Score = score,
            ActionLabel = actionLabel,
        });
    }

    private static int Score(string query, params string?[] fields)
    {
        int best = 0;
        foreach (string? field in fields)
        {
            if (string.IsNullOrWhiteSpace(field)) continue;
            if (field.Equals(query, StringComparison.OrdinalIgnoreCase)) best = Math.Max(best, 1000);
            else if (field.StartsWith(query, StringComparison.OrdinalIgnoreCase)) best = Math.Max(best, 750);
            else if (field.Contains(query, StringComparison.OrdinalIgnoreCase)) best = Math.Max(best, 500);
        }

        return best;
    }
}
