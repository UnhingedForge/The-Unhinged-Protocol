using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App.ViewModels;

public sealed class Phase1ToolsViewModel : ObservableObject
{
    private readonly IDisplayEnvironmentService displayEnvironmentService;
    private readonly ILayoutSnapshotService layoutSnapshotService;
    private readonly IOnboardingService onboardingService;
    private readonly IOrganizerPreferencesService preferencesService;
    private readonly IUnifiedSearchService searchService;
    private DisplayProfile? displayProfile;
    private bool hasError;
    private bool isBusy;
    private OnboardingScanResult? onboardingScan;
    private OrganizerPreferences preferences = new();
    private string statusMessage = "Phase 1 controls are loading…";
    private string windowsSearchStatus = "Windows Search has not been queried.";

    public Phase1ToolsViewModel(
        IOrganizerPreferencesService preferencesService,
        IDisplayEnvironmentService displayEnvironmentService,
        ILayoutSnapshotService layoutSnapshotService,
        IUnifiedSearchService searchService,
        IOnboardingService onboardingService)
    {
        this.preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        this.displayEnvironmentService = displayEnvironmentService ?? throw new ArgumentNullException(nameof(displayEnvironmentService));
        this.layoutSnapshotService = layoutSnapshotService ?? throw new ArgumentNullException(nameof(layoutSnapshotService));
        this.searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        this.onboardingService = onboardingService ?? throw new ArgumentNullException(nameof(onboardingService));
    }

    public ObservableCollection<LayoutArchive> Snapshots { get; } = [];

    public ObservableCollection<SearchResult> SearchResults { get; } = [];

    public ObservableCollection<OnboardingSuggestionViewModel> OnboardingSuggestions { get; } = [];

    public OrganizerPreferences Preferences => preferences;

    public bool IsOrganizerVisible => preferences.IsOrganizerVisible;

    public bool IsOrganizerHidden => !preferences.IsOrganizerVisible;

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public bool HasError
    {
        get => hasError;
        private set => SetProperty(ref hasError, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string DisplaySummary => displayProfile is null
        ? "Display profile unavailable"
        : $"{displayProfile.Displays.Length} display(s) · profile {displayProfile.Fingerprint}{(displayProfile.IsRemoteSession ? " · Remote Desktop" : string.Empty)}";

    public string VirtualDesktopStatus => displayProfile?.VirtualDesktopPlacementAvailable == true
        ? "Virtual-desktop placement is available."
        : "Windows does not expose a supported organizer-placement API for virtual desktops; layout remains safely visible and recoverable.";

    public string VisibilityHotKeyLabel => preferences.VisibilityHotKey.ToString();

    public string PeekHotKeyLabel => preferences.PeekHotKey.ToString();

    public string DesktopGestureLabel => preferences.DesktopGesture switch
    {
        DesktopGestureAction.Disabled => "Disabled (recommended until explicitly enabled)",
        DesktopGestureAction.ToggleOrganizerVisibility => "Double-click empty desktop to toggle visibility",
        DesktopGestureAction.TogglePeek => "Double-click empty desktop to toggle Peek",
        _ => "Unknown",
    };

    public string WindowsSearchStatus
    {
        get => windowsSearchStatus;
        private set => SetProperty(ref windowsSearchStatus, value);
    }

    public OnboardingScanResult? OnboardingScan => onboardingScan;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        HasError = false;
        try
        {
            preferences = await preferencesService.GetAsync(cancellationToken);
            displayProfile = await displayEnvironmentService.CaptureAsync(cancellationToken);
            await RefreshSnapshotsAsync(cancellationToken);
            RaisePreferenceProperties();
            OnPropertyChanged(nameof(DisplaySummary));
            OnPropertyChanged(nameof(VirtualDesktopStatus));
            StatusMessage = "Phase 1 focus, recovery, search, and onboarding controls are ready.";
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            HasError = true;
            StatusMessage = "Some Phase 1 controls could not be loaded. Existing organizer state was not changed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<OrganizerPreferences> SavePreferencesAsync(
        OrganizerPreferences updated,
        CancellationToken cancellationToken = default)
    {
        try
        {
            preferences = await preferencesService.SaveAsync(updated, cancellationToken);
            RaisePreferenceProperties();
            HasError = false;
            StatusMessage = "Focus controls were saved locally. Explorer and the Windows shell were not modified.";
            return preferences;
        }
        catch
        {
            HasError = true;
            StatusMessage = "Focus controls could not be saved; the last valid settings remain active.";
            throw;
        }
    }

    public Task<OrganizerPreferences> SetGlobalVisibilityAsync(bool isVisible, CancellationToken cancellationToken = default) =>
        SavePreferencesAsync(preferences with { IsOrganizerVisible = isVisible }, cancellationToken);

    public async Task RefreshDisplayAsync(CancellationToken cancellationToken = default)
    {
        displayProfile = await displayEnvironmentService.CaptureAsync(cancellationToken);
        OnPropertyChanged(nameof(DisplaySummary));
        OnPropertyChanged(nameof(VirtualDesktopStatus));
        StatusMessage = "Display and Remote Desktop state was refreshed without changing the Windows shell.";
    }

    public async Task<LayoutArchive> CreateSnapshotAsync(
        string name,
        LayoutSnapshotKind kind,
        CancellationToken cancellationToken = default)
    {
        DisplayProfile profile = displayProfile ?? await displayEnvironmentService.CaptureAsync(cancellationToken);
        LayoutArchive snapshot = await layoutSnapshotService.CreateAsync(name, kind, profile, cancellationToken);
        await RefreshSnapshotsAsync(cancellationToken);
        StatusMessage = $"Snapshot “{snapshot.Name}” was checksum-validated and saved.";
        return snapshot;
    }

    public async Task<LayoutDifference> CompareSnapshotAsync(Guid id, CancellationToken cancellationToken = default)
    {
        DisplayProfile profile = displayProfile ?? await displayEnvironmentService.CaptureAsync(cancellationToken);
        LayoutDifference difference = await layoutSnapshotService.CompareAsync(id, profile, cancellationToken);
        StatusMessage = difference.Summary;
        return difference;
    }

    public async Task RestoreSnapshotAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await layoutSnapshotService.RestoreAsync(id, cancellationToken);
        StatusMessage = "The layout was restored transactionally. A recovery snapshot preserves the previous layout; user files were not changed.";
    }

    public Task ExportSnapshotAsync(Guid id, string path, CancellationToken cancellationToken = default) =>
        layoutSnapshotService.ExportAsync(id, path, cancellationToken);

    public async Task ImportSnapshotAsync(string path, CancellationToken cancellationToken = default)
    {
        LayoutArchive archive = await layoutSnapshotService.ImportAsync(path, cancellationToken);
        await RefreshSnapshotsAsync(cancellationToken);
        StatusMessage = $"Snapshot “{archive.Name}” passed schema and checksum validation and was imported.";
    }

    public async Task SearchAsync(
        string query,
        IReadOnlyList<ContainerDefinition> containers,
        IReadOnlyList<FolderPortal> portals,
        CancellationToken cancellationToken = default)
    {
        SearchResponse response = await searchService.SearchAsync(query, containers, portals, cancellationToken);
        SearchResults.Clear();
        foreach (SearchResult result in response.Results) SearchResults.Add(result);
        WindowsSearchStatus = response.WindowsSearchMessage ?? response.WindowsSearchState switch
        {
            SearchAvailability.Ready => "Windows Search adapter is ready. All results stayed on this PC.",
            _ => $"Windows Search: {response.WindowsSearchState}. Local results remain available.",
        };
        StatusMessage = $"Found {SearchResults.Count} local result(s). No search data was transmitted.";
    }

    public async Task<OnboardingScanResult> ScanDesktopAsync(
        string desktopPath,
        bool consentGranted,
        CancellationToken cancellationToken = default)
    {
        onboardingScan = await onboardingService.ScanAsync(desktopPath, consentGranted, cancellationToken);
        OnPropertyChanged(nameof(OnboardingScan));
        OnboardingSuggestions.Clear();
        foreach (OnboardingSuggestion suggestion in onboardingScan.Suggestions)
        {
            OnboardingSuggestions.Add(new OnboardingSuggestionViewModel(suggestion));
        }
        StatusMessage = onboardingScan.Message;
        return onboardingScan;
    }

    public async Task<IReadOnlyList<ContainerDefinition>> ApplyOnboardingAsync(CancellationToken cancellationToken = default)
    {
        OnboardingSuggestion[] suggestions = OnboardingSuggestions.Select(suggestion => suggestion.ToDefinition()).ToArray();
        IReadOnlyList<ContainerDefinition> created = await onboardingService.ApplyAsync(suggestions, cancellationToken);
        StatusMessage = $"Created {created.Count} reference-only container(s). Original desktop files remain unchanged.";
        return created;
    }

    public void ReportRecovery(string message)
    {
        HasError = false;
        StatusMessage = message;
    }

    private async Task RefreshSnapshotsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LayoutArchive> snapshots = await layoutSnapshotService.GetAllAsync(cancellationToken);
        Snapshots.Clear();
        foreach (LayoutArchive snapshot in snapshots) Snapshots.Add(snapshot);
    }

    private void RaisePreferenceProperties()
    {
        OnPropertyChanged(nameof(Preferences));
        OnPropertyChanged(nameof(IsOrganizerVisible));
        OnPropertyChanged(nameof(IsOrganizerHidden));
        OnPropertyChanged(nameof(VisibilityHotKeyLabel));
        OnPropertyChanged(nameof(PeekHotKeyLabel));
        OnPropertyChanged(nameof(DesktopGestureLabel));
    }
}

public sealed class OnboardingSuggestionViewModel : ObservableObject
{
    private bool isAccepted;

    public OnboardingSuggestionViewModel(OnboardingSuggestion suggestion)
    {
        Definition = suggestion;
        isAccepted = suggestion.IsAccepted;
    }

    public OnboardingSuggestion Definition { get; }

    public Guid Id => Definition.Id;

    public string Category => Definition.Category;

    public int ItemCount => Definition.Candidates.Length;

    public string Summary => $"{Category} — {ItemCount} item(s)";

    public bool IsAccepted
    {
        get => isAccepted;
        set => SetProperty(ref isAccepted, value);
    }

    public OnboardingSuggestion ToDefinition() => Definition with { IsAccepted = IsAccepted };
}
