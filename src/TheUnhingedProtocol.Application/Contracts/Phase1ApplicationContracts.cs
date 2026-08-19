using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.Application.Contracts;

public interface IOrganizerPreferencesService
{
    public Task<OrganizerPreferences> GetAsync(CancellationToken cancellationToken);
    public Task<OrganizerPreferences> SaveAsync(OrganizerPreferences preferences, CancellationToken cancellationToken);
}

public interface IDisplayEnvironmentService
{
    public Task<DisplayProfile> CaptureAsync(CancellationToken cancellationToken);
}

public interface ILayoutSnapshotService
{
    public Task<IReadOnlyList<LayoutArchive>> GetAllAsync(CancellationToken cancellationToken);
    public Task<LayoutArchive> CreateAsync(string name, LayoutSnapshotKind kind, DisplayProfile displayProfile, CancellationToken cancellationToken);
    public Task<LayoutDifference> CompareAsync(Guid snapshotId, DisplayProfile currentDisplayProfile, CancellationToken cancellationToken);
    public Task RestoreAsync(Guid snapshotId, CancellationToken cancellationToken);
    public Task ExportAsync(Guid snapshotId, string destinationPath, CancellationToken cancellationToken);
    public Task<LayoutArchive> ImportAsync(string sourcePath, CancellationToken cancellationToken);
}

public interface IWindowsSearchAdapter
{
    public Task<SearchResponse> SearchAsync(string query, CancellationToken cancellationToken);
}

public interface IUnifiedSearchService
{
    public Task<SearchResponse> SearchAsync(
        string query,
        IReadOnlyList<ContainerDefinition> containers,
        IReadOnlyList<FolderPortal> portals,
        CancellationToken cancellationToken);
}

public interface IOnboardingService
{
    public Task<OnboardingScanResult> ScanAsync(string desktopPath, bool consentGranted, CancellationToken cancellationToken);
    public Task<IReadOnlyList<ContainerDefinition>> ApplyAsync(
        IReadOnlyList<OnboardingSuggestion> suggestions,
        CancellationToken cancellationToken);
}
