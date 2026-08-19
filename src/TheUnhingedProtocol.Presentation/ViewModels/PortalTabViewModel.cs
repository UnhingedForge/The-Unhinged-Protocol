using CommunityToolkit.Mvvm.ComponentModel;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App.ViewModels;

public sealed class PortalTabViewModel : ObservableObject
{
    private FolderPortalTab state;
    private bool isLoading;
    private IReadOnlyList<PortalItemViewModel> items = [];
    private string stateMessage = "Ready to load this folder.";
    private PortalTargetState targetState = PortalTargetState.Ready;

    public PortalTabViewModel(FolderPortalTab state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        state.EnsureValid();
    }

    public Guid Id => state.Id;

    public string CurrentPath => state.CurrentPath;

    public string Header => Path.GetFileName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name
        ? name
        : CurrentPath;

    public string SearchQuery => state.SearchQuery;

    public PortalViewMode ViewMode => state.ViewMode;

    public int ViewModeIndex => (int)state.ViewMode;

    public PortalSortMode SortMode => state.SortMode;

    public int SortModeIndex => (int)state.SortMode;

    public bool CanGoBack => state.BackHistory.Length > 0;

    public bool CanGoForward => state.ForwardHistory.Length > 0;

    public bool CanGoUp => Directory.GetParent(CurrentPath) is not null;

    public bool IsGridView => ViewMode == PortalViewMode.Grid;

    public bool IsListView => ViewMode == PortalViewMode.List;

    public bool IsDetailsView => ViewMode == PortalViewMode.Details;

    public bool IsLoading
    {
        get => isLoading;
        private set => SetProperty(ref isLoading, value);
    }

    public PortalTargetState TargetState
    {
        get => targetState;
        private set
        {
            if (SetProperty(ref targetState, value))
            {
                OnPropertyChanged(nameof(HasRecoverableError));
            }
        }
    }

    public bool HasRecoverableError => TargetState != PortalTargetState.Ready;

    public string StateMessage
    {
        get => stateMessage;
        private set => SetProperty(ref stateMessage, value);
    }

    public IReadOnlyList<PortalItemViewModel> Items
    {
        get => items;
        private set
        {
            if (SetProperty(ref items, value))
            {
                RaiseViewItems();
            }
        }
    }

    public IReadOnlyList<PortalItemViewModel> GridItems => IsGridView ? Items : [];

    public IReadOnlyList<PortalItemViewModel> ListItems => IsListView ? Items : [];

    public IReadOnlyList<PortalItemViewModel> DetailsItems => IsDetailsView ? Items : [];

    public FolderPortalTab CaptureState() => state;

    public void ApplyState(FolderPortalTab updated)
    {
        state = updated ?? throw new ArgumentNullException(nameof(updated));
        state.EnsureValid();
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(SearchQuery));
        OnPropertyChanged(nameof(ViewMode));
        OnPropertyChanged(nameof(ViewModeIndex));
        OnPropertyChanged(nameof(SortMode));
        OnPropertyChanged(nameof(SortModeIndex));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(IsDetailsView));
        RaiseViewItems();
    }

    public void BeginLoad()
    {
        IsLoading = true;
        StateMessage = "Loading folder contents…";
    }

    public void CompleteLoad(PortalLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Items = result.Items.Select(item => new PortalItemViewModel(item)).ToArray();
        TargetState = result.State;
        StateMessage = result.State == PortalTargetState.Ready
            ? $"{result.Message} · refreshed in {result.Elapsed.TotalMilliseconds:N0} ms"
            : result.Message;
        IsLoading = false;
    }

    public void CancelLoad()
    {
        IsLoading = false;
        StateMessage = "Folder refresh was canceled. Nothing was changed.";
    }

    private void RaiseViewItems()
    {
        OnPropertyChanged(nameof(GridItems));
        OnPropertyChanged(nameof(ListItems));
        OnPropertyChanged(nameof(DetailsItems));
    }
}
