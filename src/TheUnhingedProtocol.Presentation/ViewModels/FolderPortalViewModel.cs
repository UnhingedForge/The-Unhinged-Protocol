using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App.ViewModels;

public sealed class FolderPortalViewModel : ObservableObject
{
    private readonly IFolderBrowserService browserService;
    private readonly IFolderPortalService portalService;
    private FolderPortal persistedPortal;
    private PortalTabViewModel activeTab;
    private bool hasError;
    private string statusMessage = "Folder portal ready.";

    private FolderPortalViewModel(
        FolderPortal portal,
        IFolderPortalService portalService,
        IFolderBrowserService browserService)
    {
        persistedPortal = portal.EnsureValid();
        this.portalService = portalService;
        this.browserService = browserService;
        foreach (FolderPortalTab tab in persistedPortal.Tabs)
        {
            Tabs.Add(new PortalTabViewModel(tab));
        }

        activeTab = Tabs.Single(tab => tab.Id == persistedPortal.ActiveTabId);
    }

    public Guid Id => persistedPortal.Id;

    public bool IsVisible => persistedPortal.IsVisible;

    public string Name => persistedPortal.Name;

    public string AccessibleName => $"{Name}, live folder portal, {Tabs.Count} {(Tabs.Count == 1 ? "tab" : "tabs")}";

    public ObservableCollection<PortalTabViewModel> Tabs { get; } = [];

    public PortalTabViewModel ActiveTab
    {
        get => activeTab;
        private set => SetProperty(ref activeTab, value);
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

    public static FolderPortalViewModel FromDefinition(
        FolderPortal portal,
        IFolderPortalService portalService,
        IFolderBrowserService browserService) =>
        new(portal, portalService, browserService);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        PortalTabViewModel tab = ActiveTab;
        tab.BeginLoad();
        HasError = false;
        try
        {
            PortalLoadResult result = await browserService.BrowseAsync(tab.CaptureState(), cancellationToken);
            tab.CompleteLoad(result);
            HasError = result.State != PortalTargetState.Ready;
            StatusMessage = tab.StateMessage;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            tab.CancelLoad();
            StatusMessage = tab.StateMessage;
        }
        catch (Exception)
        {
            tab.CompleteLoad(new PortalLoadResult
            {
                State = PortalTargetState.Error,
                Message = "This folder could not be read. Nothing was changed; retry with Refresh.",
            });
            HasError = true;
            StatusMessage = tab.StateMessage;
        }
    }

    public async Task SelectTabAsync(Guid tabId, CancellationToken cancellationToken = default)
    {
        PortalTabViewModel selected = Tabs.Single(tab => tab.Id == tabId);
        await SaveAsync(persistedPortal.SelectTab(tabId), cancellationToken);
        ActiveTab = selected;
        await RefreshAsync(cancellationToken);
    }

    public async Task AddTabAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        FolderPortal candidate = persistedPortal.AddTab(folderPath);
        await SaveAsync(candidate, cancellationToken);
        ApplyPortal(candidate);
        await RefreshAsync(cancellationToken);
    }

    public async Task CloseTabAsync(Guid tabId, CancellationToken cancellationToken = default)
    {
        FolderPortal candidate = persistedPortal.CloseTab(tabId);
        await SaveAsync(candidate, cancellationToken);
        ApplyPortal(candidate);
        await RefreshAsync(cancellationToken);
    }

    public Task NavigateAsync(string folderPath, CancellationToken cancellationToken = default) =>
        ChangeActiveTabAsync(tab => tab.Navigate(folderPath), cancellationToken);

    public Task GoBackAsync(CancellationToken cancellationToken = default) =>
        ChangeActiveTabAsync(tab => tab.GoBack(), cancellationToken);

    public Task GoForwardAsync(CancellationToken cancellationToken = default) =>
        ChangeActiveTabAsync(tab => tab.GoForward(), cancellationToken);

    public Task GoUpAsync(CancellationToken cancellationToken = default) =>
        ChangeActiveTabAsync(tab => tab.GoUp(), cancellationToken);

    public Task SetViewModeAsync(PortalViewMode viewMode, CancellationToken cancellationToken = default) =>
        ChangeActiveTabAsync(tab => tab.WithView(viewMode), cancellationToken);

    public Task SetSortModeAsync(PortalSortMode sortMode, CancellationToken cancellationToken = default) =>
        ChangeActiveTabAsync(tab => tab.WithSort(sortMode), cancellationToken);

    public Task SetSearchAsync(string? query, CancellationToken cancellationToken = default) =>
        ChangeActiveTabAsync(tab => tab.WithSearch(query), cancellationToken);

    public Task ChangeTargetAsync(string folderPath, CancellationToken cancellationToken = default) =>
        NavigateAsync(folderPath, cancellationToken);

    public async Task SetVisibilityAsync(bool isVisible, CancellationToken cancellationToken = default)
    {
        FolderPortal candidate = persistedPortal.WithVisibility(isVisible);
        await SaveAsync(candidate, cancellationToken);
        ApplyPortal(candidate);
    }

    public FolderPortal CaptureDefinition() => persistedPortal;

    public Task<PortalPreview> GetPreviewAsync(PortalItemViewModel item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return browserService.GetPreviewAsync(item.Item, cancellationToken);
    }

    private async Task ChangeActiveTabAsync(
        Func<FolderPortalTab, FolderPortalTab> change,
        CancellationToken cancellationToken)
    {
        FolderPortalTab changed = change(ActiveTab.CaptureState());
        FolderPortal candidate = persistedPortal.UpdateTab(changed);
        await SaveAsync(candidate, cancellationToken);
        ApplyPortal(candidate);
        await RefreshAsync(cancellationToken);
    }

    private async Task SaveAsync(FolderPortal candidate, CancellationToken cancellationToken)
    {
        HasError = false;
        try
        {
            persistedPortal = await portalService.UpdateAsync(candidate, cancellationToken);
            StatusMessage = "Portal state saved. No files or folders were changed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Portal change was canceled. Nothing was changed.";
            throw;
        }
        catch
        {
            HasError = true;
            StatusMessage = "Portal state could not be saved. No files or folders were changed.";
            throw;
        }
    }

    private void ApplyPortal(FolderPortal portal)
    {
        persistedPortal = portal.EnsureValid();
        foreach (FolderPortalTab state in persistedPortal.Tabs)
        {
            PortalTabViewModel? existing = Tabs.FirstOrDefault(tab => tab.Id == state.Id);
            if (existing is null)
            {
                Tabs.Add(new PortalTabViewModel(state));
            }
            else
            {
                existing.ApplyState(state);
            }
        }

        foreach (PortalTabViewModel removed in Tabs.Where(tab => !persistedPortal.Tabs.Any(state => state.Id == tab.Id)).ToArray())
        {
            Tabs.Remove(removed);
        }

        ActiveTab = Tabs.Single(tab => tab.Id == persistedPortal.ActiveTabId);
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(AccessibleName));
    }
}
