using System.Text.Json;
using Microsoft.Data.Sqlite;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;
using TheUnhingedProtocol.Infrastructure.Persistence;

namespace TheUnhingedProtocol.Infrastructure.Shell;

public sealed class NonDestructiveOnboardingService : IOnboardingService
{
    private const int LargeDesktopThreshold = 500;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string connectionString;

    public NonDestructiveOnboardingService(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        string fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public Task<OnboardingScanResult> ScanAsync(
        string desktopPath,
        bool consentGranted,
        CancellationToken cancellationToken)
    {
        if (!consentGranted)
        {
            return Task.FromResult(new OnboardingScanResult
            {
                State = OnboardingScanState.ConsentRequired,
                Message = "Review and accept the metadata-only scan disclosure before scanning.",
            });
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(desktopPath);
        string fullPath = Path.GetFullPath(desktopPath);
        if (!Directory.Exists(fullPath))
        {
            return Task.FromResult(new OnboardingScanResult
            {
                State = OnboardingScanState.DesktopUnavailable,
                Message = "The selected Desktop location is unavailable. Choose another location or reconnect storage.",
            });
        }

        try
        {
            List<OnboardingCandidate> candidates = [];
            foreach (string path in Directory.EnumerateFileSystemEntries(fullPath, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System))
                {
                    continue;
                }

                bool isDirectory = attributes.HasFlag(FileAttributes.Directory);
                FileInfo? file = isDirectory ? null : new FileInfo(path);
                candidates.Add(new OnboardingCandidate
                {
                    Path = Path.GetFullPath(path),
                    Name = Path.GetFileName(path),
                    Kind = isDirectory ? ItemKind.Folder : Classify(path),
                    Size = file?.Length,
                    ModifiedAt = isDirectory ? Directory.GetLastWriteTimeUtc(path) : file?.LastWriteTimeUtc,
                });
            }

            OnboardingSuggestion[] suggestions = candidates
                .GroupBy(Categorize, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new OnboardingSuggestion
                {
                    Category = group.Key,
                    Candidates = group.OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
                })
                .ToArray();
            bool redirected = fullPath.Contains("OneDrive", StringComparison.OrdinalIgnoreCase);
            OnboardingScanState state = candidates.Count > LargeDesktopThreshold
                ? OnboardingScanState.LargeDesktop
                : redirected ? OnboardingScanState.OneDriveRedirected : OnboardingScanState.Ready;
            string message = state switch
            {
                OnboardingScanState.LargeDesktop => $"Found {candidates.Count} visible items. Review the large-desktop preview before accepting suggestions.",
                OnboardingScanState.OneDriveRedirected => $"Found {candidates.Count} visible items in a OneDrive-redirected Desktop. Only local metadata was inspected.",
                _ => $"Found {candidates.Count} visible items. No file contents were read and nothing was changed.",
            };
            return Task.FromResult(new OnboardingScanResult
            {
                State = state,
                Message = message,
                Suggestions = suggestions,
            });
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(new OnboardingScanResult
            {
                State = OnboardingScanState.Canceled,
                Message = "The scan was canceled. No organization was created and no background work remains.",
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(new OnboardingScanResult
            {
                State = OnboardingScanState.PermissionDenied,
                Message = "Windows denied access to this Desktop location. Nothing was changed.",
            });
        }
    }

    public async Task<IReadOnlyList<ContainerDefinition>> ApplyAsync(
        IReadOnlyList<OnboardingSuggestion> suggestions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        OnboardingSuggestion[] accepted = suggestions.Where(suggestion => suggestion.IsAccepted).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        List<ContainerDefinition> containers = [];
        for (int index = 0; index < accepted.Length; index++)
        {
            OnboardingSuggestion suggestion = accepted[index];
            ContainerDefinition container = ContainerDefinition.CreateReferenceGroup(
                suggestion.Category,
                ContainerBounds.Create(24 + ((index % 6) * 36), 24 + ((index % 6) * 36), 320, 260));
            foreach (OnboardingCandidate candidate in suggestion.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                container = container.AddItem(ItemReference.Create(candidate.Path, candidate.Kind));
            }
            containers.Add(container);
        }

        await using SqliteConnection connection = new(connectionString);
        await SqliteSchemaMigrator.MigrateAsync(connection, cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        try
        {
            foreach (ContainerDefinition container in containers)
            {
                await using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO containers (id, schema_version, payload_json, updated_utc) VALUES ($id, $version, $json, $updated);";
                command.Parameters.AddWithValue("$id", container.Id.ToString("D"));
                command.Parameters.AddWithValue("$version", container.SchemaVersion);
                command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(container, SerializerOptions));
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return containers;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static ItemKind Classify(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)) return ItemKind.Shortcut;
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".com", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)) return ItemKind.Application;
        return ItemKind.File;
    }

    private static string Categorize(OnboardingCandidate candidate)
    {
        if (candidate.Kind == ItemKind.Folder) return "Folders";
        if (candidate.Kind is ItemKind.Application or ItemKind.Shortcut) return "Apps and shortcuts";
        string extension = Path.GetExtension(candidate.Path).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".mp4" or ".mov" or ".mp3" or ".wav") return "Media";
        if (extension is ".doc" or ".docx" or ".pdf" or ".txt" or ".rtf" or ".xls" or ".xlsx" or ".ppt" or ".pptx") return "Documents";
        if (extension is ".zip" or ".7z" or ".rar" or ".tar" or ".gz") return "Archives";
        return "Other files";
    }
}
