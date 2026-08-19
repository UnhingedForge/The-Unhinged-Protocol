using System.Diagnostics;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.Infrastructure.Shell;

/// <summary>
/// Reads live file-system state without obtaining authority to modify it.
/// </summary>
public sealed class FileSystemFolderBrowser : IFolderBrowserService
{
    private const int MaximumPreviewCharacters = 16_384;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".xml", ".csv", ".log", ".cs", ".xaml", ".ps1",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp",
    };

    public Task<PortalLoadResult> BrowseAsync(FolderPortalTab tab, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tab);
        tab.EnsureValid();
        return Task.Run(() => BrowseCore(tab, cancellationToken), cancellationToken);
    }

    public async Task<PortalPreview> GetPreviewAsync(PortalItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Kind == PortalItemKind.Folder)
        {
            return new PortalPreview
            {
                Title = item.Name,
                Description = $"Folder · Modified {item.ModifiedAt.LocalDateTime:g}",
            };
        }

        string extension = Path.GetExtension(item.FullPath);
        string? text = null;
        string? imagePath = null;
        if (TextExtensions.Contains(extension) && item.SizeBytes <= 1_048_576)
        {
            char[] buffer = new char[MaximumPreviewCharacters];
            await using FileStream stream = new(
                item.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16_384,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using StreamReader reader = new(stream, detectEncodingFromByteOrderMarks: true);
            int read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            text = new string(buffer, 0, read);
        }
        else if (ImageExtensions.Contains(extension))
        {
            imagePath = item.FullPath;
        }

        return new PortalPreview
        {
            Title = item.Name,
            Description = $"{item.TypeLabel} · {FormatSize(item.SizeBytes)} · Modified {item.ModifiedAt.LocalDateTime:g}",
            TextContent = text,
            ImagePath = imagePath,
        };
    }

    private static PortalLoadResult BrowseCore(FolderPortalTab tab, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            PortalTargetState unavailableState = GetUnavailableState(tab.CurrentPath);
            if (unavailableState != PortalTargetState.Ready)
            {
                return Result(unavailableState, DescribeState(unavailableState), [], stopwatch);
            }

            IEnumerable<PortalItem> items = Directory
                .EnumerateFileSystemEntries(tab.CurrentPath, "*", new EnumerationOptions
                {
                    IgnoreInaccessible = false,
                    RecurseSubdirectories = false,
                    ReturnSpecialDirectories = false,
                    AttributesToSkip = 0,
                })
                .Select(path => CreateItem(path, cancellationToken));

            if (!string.IsNullOrWhiteSpace(tab.SearchQuery))
            {
                items = items.Where(item => item.Name.Contains(tab.SearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            PortalItem[] materialized = Sort(items, tab.SortMode).ToArray();
            return Result(
                PortalTargetState.Ready,
                materialized.Length == 1 ? "1 item" : $"{materialized.Length:N0} items",
                materialized,
                stopwatch);
        }
        catch (UnauthorizedAccessException)
        {
            return Result(PortalTargetState.Inaccessible, DescribeState(PortalTargetState.Inaccessible), [], stopwatch);
        }
        catch (DirectoryNotFoundException)
        {
            return Result(GetUnavailableState(tab.CurrentPath), DescribeState(GetUnavailableState(tab.CurrentPath)), [], stopwatch);
        }
        catch (IOException)
        {
            PortalTargetState state = IsDisconnected(tab.CurrentPath) ? PortalTargetState.Disconnected : PortalTargetState.Error;
            return Result(state, DescribeState(state), [], stopwatch);
        }
    }

    public static IEnumerable<PortalItem> Sort(IEnumerable<PortalItem> items, PortalSortMode sortMode)
    {
        ArgumentNullException.ThrowIfNull(items);
        IOrderedEnumerable<PortalItem> sorted = sortMode switch
        {
            PortalSortMode.NameAscending => items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            PortalSortMode.NameDescending => items.OrderByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase),
            PortalSortMode.TypeThenName => items.OrderBy(item => item.Kind).ThenBy(item => item.TypeLabel, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            PortalSortMode.ModifiedNewest => items.OrderByDescending(item => item.ModifiedAt).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            PortalSortMode.ModifiedOldest => items.OrderBy(item => item.ModifiedAt).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            PortalSortMode.SizeDescending => items.OrderByDescending(item => item.SizeBytes).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            _ => throw new ArgumentOutOfRangeException(nameof(sortMode)),
        };
        return sorted.ThenBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase);
    }

    private static PortalItem CreateItem(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileAttributes attributes = File.GetAttributes(path);
        bool isDirectory = attributes.HasFlag(FileAttributes.Directory);
        FileSystemInfo info = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
        long size = info is FileInfo file ? file.Length : 0;
        string extension = isDirectory ? "Folder" : Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return new PortalItem
        {
            FullPath = Path.GetFullPath(path),
            Name = info.Name,
            Kind = isDirectory ? PortalItemKind.Folder : PortalItemKind.File,
            SizeBytes = size,
            ModifiedAt = info.LastWriteTimeUtc,
            TypeLabel = string.IsNullOrWhiteSpace(extension) ? "File" : extension,
            IsHidden = attributes.HasFlag(FileAttributes.Hidden),
        };
    }

    private static PortalTargetState GetUnavailableState(string path)
    {
        if (Directory.Exists(path))
        {
            return PortalTargetState.Ready;
        }

        return IsDisconnected(path) ? PortalTargetState.Disconnected : PortalTargetState.Missing;
    }

    private static bool IsDisconnected(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            return !new DriveInfo(root).IsReady;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static PortalLoadResult Result(
        PortalTargetState state,
        string message,
        PortalItem[] items,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new PortalLoadResult { State = state, Message = message, Items = items, Elapsed = stopwatch.Elapsed };
    }

    private static string DescribeState(PortalTargetState state) => state switch
    {
        PortalTargetState.Missing => "This folder was renamed, moved, or removed. Choose a new folder or refresh after restoring it.",
        PortalTargetState.Inaccessible => "Windows denied access to this folder. Check permissions or choose another folder.",
        PortalTargetState.Disconnected => "This drive or network folder is disconnected. Reconnect it, choose another folder, or refresh.",
        PortalTargetState.Error => "This folder could not be read. Nothing was changed; retry with Refresh.",
        _ => string.Empty,
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1_024 => $"{bytes} B",
        < 1_048_576 => $"{bytes / 1_024d:F1} KB",
        < 1_073_741_824 => $"{bytes / 1_048_576d:F1} MB",
        _ => $"{bytes / 1_073_741_824d:F1} GB",
    };
}
