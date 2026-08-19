using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App.ViewModels;

/// <summary>
/// Coordinates the first Phase 1 container-creation vertical slice.
/// </summary>
public sealed class MainPageViewModel : ObservableObject
{
    private readonly IContainerService containerService;
    private readonly IFolderBrowserService? folderBrowserService;
    private readonly IFolderPortalService? folderPortalService;
    private bool hasError;
    private bool initialized;
    private bool isBusy;
    private string statusMessage = "Loading your desktop containers…";

    public MainPageViewModel(
        IContainerService containerService,
        IFolderPortalService? folderPortalService = null,
        IFolderBrowserService? folderBrowserService = null)
    {
        this.containerService = containerService ?? throw new ArgumentNullException(nameof(containerService));
        if ((folderPortalService is null) != (folderBrowserService is null))
        {
            throw new ArgumentException("Portal persistence and browsing services must be supplied together.");
        }

        this.folderPortalService = folderPortalService;
        this.folderBrowserService = folderBrowserService;
        CreateContainerCommand = new AsyncRelayCommand(CreateContainerAsync, CanCreateContainer);
    }

    public string ProductName { get; } = "The Unhinged Protocol";

    public string CurrentPhase { get; } = "Phase 1 — Core Desktop Organizer";

    public string Summary { get; } =
        "Create a safe, reference-based home for the things you use—without moving the original files.";

    public ObservableCollection<ContainerCardViewModel> Containers { get; } = [];

    public ObservableCollection<FolderPortalViewModel> Portals { get; } = [];

    public IAsyncRelayCommand CreateContainerCommand { get; }

    public bool HasError
    {
        get => hasError;
        private set => SetProperty(ref hasError, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                CreateContainerCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        IsBusy = true;
        HasError = false;
        try
        {
            IReadOnlyList<ContainerDefinition> containers =
                await containerService.GetAllAsync(cancellationToken);
            foreach (ContainerDefinition container in containers)
            {
                Containers.Add(ContainerCardViewModel.FromDefinition(container));
            }

            if (folderPortalService is not null && folderBrowserService is not null)
            {
                IReadOnlyList<FolderPortal> portals = await folderPortalService.GetAllAsync(cancellationToken);
                foreach (FolderPortal portal in portals)
                {
                    FolderPortalViewModel portalViewModel = FolderPortalViewModel.FromDefinition(
                        portal,
                        folderPortalService,
                        folderBrowserService);
                    Portals.Add(portalViewModel);
                    await portalViewModel.RefreshAsync(cancellationToken);
                }
            }

            initialized = true;
            StatusMessage = Containers.Count == 0 && Portals.Count == 0
                ? "No containers or folder portals yet. Create your first one when you are ready."
                : $"Loaded {Containers.Count} {DescribeContainerCount(Containers.Count)} and {Portals.Count} {DescribePortalCount(Portals.Count)}.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Container loading was canceled.";
        }
        catch (Exception)
        {
            HasError = true;
            StatusMessage = "Containers could not be loaded. Your files were not changed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        Containers.Clear();
        Portals.Clear();
        initialized = false;
        await InitializeAsync(cancellationToken);
    }

    public async Task CreatePortalAsync(
        string name,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        if (folderPortalService is null || folderBrowserService is null)
        {
            throw new InvalidOperationException("Folder portal services are not available.");
        }

        IsBusy = true;
        HasError = false;
        try
        {
            FolderPortal portal = await folderPortalService.CreateAsync(name, folderPath, cancellationToken);
            FolderPortalViewModel viewModel = FolderPortalViewModel.FromDefinition(
                portal,
                folderPortalService,
                folderBrowserService);
            Portals.Add(viewModel);
            await viewModel.RefreshAsync(cancellationToken);
            StatusMessage = $"{portal.Name} now shows a live reference view. No files or folders were changed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Folder portal creation was canceled. Nothing was changed.";
        }
        catch (Exception)
        {
            HasError = true;
            StatusMessage = "The folder portal could not be created. No files or folders were changed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreateContainer() => initialized && !IsBusy;

    private Task CreateContainerAsync(CancellationToken cancellationToken) =>
        CreateContainerAsync(ContainerTemplateKind.Standard, null, cancellationToken);

    public async Task CreateContainerAsync(
        ContainerTemplateKind template,
        ContainerBounds? drawnBounds = null,
        CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        HasError = false;
        try
        {
            string name = $"Container {Containers.Count + 1}";
            ContainerBounds initialBounds = drawnBounds ?? CreateTemplateBounds(template, Containers.Count);
            ContainerDefinition container =
                await containerService.CreateReferenceGroupAsync(name, initialBounds, cancellationToken);
            Containers.Add(ContainerCardViewModel.FromDefinition(container));
            StatusMessage = $"{container.Name} was created. No files were moved.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Container creation was canceled. No files were changed.";
        }
        catch (Exception)
        {
            HasError = true;
            StatusMessage = "The container could not be created. Your files were not changed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveContainerBoundsAsync(
        ContainerCardViewModel container,
        double rasterizationScale = 1,
        double workspaceWidth = ContainerBounds.MaximumPosition,
        double workspaceHeight = ContainerBounds.MaximumPosition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(container);

        HasError = false;
        try
        {
            ContainerDefinition updated = await containerService.UpdateAsync(
                container.CaptureDefinition().WithSnappedBounds(
                    container.CaptureBounds(),
                    rasterizationScale,
                    workspaceWidth,
                    workspaceHeight),
                cancellationToken);
            container.CommitBounds(updated.Bounds);
            StatusMessage = $"{container.Name} position and size were saved.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            container.RevertBounds();
            StatusMessage = $"{container.Name} layout change was canceled.";
        }
        catch (Exception)
        {
            container.RevertBounds();
            HasError = true;
            StatusMessage = $"{container.Name} layout could not be saved and was restored.";
        }
    }

    public async Task AddReferenceAsync(
        ContainerCardViewModel container,
        string target,
        ItemKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(container);
        EnsureTargetExists(target, kind);
        ItemReference item = ItemReference.Create(target, kind);
        await SaveDefinitionAsync(
            container,
            container.CaptureDefinition().AddItem(item),
            $"{item.DisplayName} was added as a reference. The original item was not moved.",
            cancellationToken);
    }

    public Task RemoveReferenceAsync(
        ContainerCardViewModel container,
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().RemoveItem(itemId),
            "The reference was removed. The original item was not changed.",
            cancellationToken);

    public Task MoveReferenceAsync(
        ContainerCardViewModel container,
        Guid itemId,
        int direction,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().MoveItem(itemId, direction),
            "The manual item order was saved.",
            cancellationToken);

    public Task SetSortModeAsync(
        ContainerCardViewModel container,
        ContainerSortMode sortMode,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().WithSortMode(sortMode),
            $"{container.Name} is sorted by {DescribeSortMode(sortMode)}.",
            cancellationToken);

    public Task UpdateReferenceAsync(
        ContainerCardViewModel container,
        ItemReference item,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().UpdateItem(item),
            $"{item.DisplayName} details were saved.",
            cancellationToken);

    public Task UpdatePresentationAsync(
        ContainerCardViewModel container,
        string name,
        string? label,
        string tags,
        string iconGlyph,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.BuildPresentation(name, label, tags, iconGlyph),
            $"{name.Trim()} appearance and details were saved.",
            cancellationToken);

    public Task SetCompositionModeAsync(
        ContainerCardViewModel container,
        ContainerCompositionMode mode,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().WithCompositionMode(mode),
            $"{container.Name} now uses the {mode.ToString().ToLowerInvariant()} composition.",
            cancellationToken);

    public Task AddSectionAsync(
        ContainerCardViewModel container,
        string name,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().AddSection(name),
            $"{name.Trim()} was added without changing any original files.",
            cancellationToken);

    public Task RenameSectionAsync(
        ContainerCardViewModel container,
        Guid sectionId,
        string name,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().RenameSection(sectionId, name),
            $"The section is now named {name.Trim()}.",
            cancellationToken);

    public Task RemoveSectionAsync(
        ContainerCardViewModel container,
        Guid sectionId,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().RemoveSection(sectionId),
            "The section was removed and its references were preserved in the first section.",
            cancellationToken);

    public Task SelectSectionAsync(
        ContainerCardViewModel container,
        Guid sectionId,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().SelectSection(sectionId),
            $"Showing {container.Sections.Single(section => section.Id == sectionId).Name}.",
            cancellationToken);

    public Task SetSectionExpandedAsync(
        ContainerCardViewModel container,
        Guid sectionId,
        bool isExpanded,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().SetSectionExpanded(sectionId, isExpanded),
            $"The stack section was {(isExpanded ? "expanded" : "collapsed")}.",
            cancellationToken);

    public Task SetDisplayStateAsync(
        ContainerCardViewModel container,
        ContainerDisplayState state,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().WithDisplayState(state),
            $"{container.Name} is now {state.ToString().ToLowerInvariant()}.",
            cancellationToken);

    public Task SetLayoutOptionsAsync(
        ContainerCardViewModel container,
        bool isPinned,
        bool isLocked,
        bool isAutoSize,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().WithLayoutOptions(isPinned, isLocked, isAutoSize),
            $"{container.Name} layout options were saved.",
            cancellationToken);

    public Task SetSnapGridAsync(
        ContainerCardViewModel container,
        double physicalGridSize,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().WithSnapGrid(physicalGridSize),
            $"{container.Name} now snaps to a {physicalGridSize:0}-pixel grid.",
            cancellationToken);

    public Task UpdateAppearanceAsync(
        ContainerCardViewModel container,
        double opacity,
        ContainerColor color,
        ContainerIconTreatment iconTreatment,
        ContainerBackgroundStyle backgroundStyle,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().WithAppearance(opacity, color, iconTreatment, backgroundStyle),
            $"{container.Name} appearance was saved using approved built-in styling.",
            cancellationToken);

    public Task SetContainerVisibilityAsync(
        ContainerCardViewModel container,
        bool isVisible,
        CancellationToken cancellationToken = default) =>
        SaveDefinitionAsync(
            container,
            container.CaptureDefinition().WithVisibility(isVisible),
            $"{container.Name} is now {(isVisible ? "visible" : "hidden")}. Global visibility remains independent.",
            cancellationToken);

    public void ShowGeometryKeyboardHelp(ContainerCardViewModel container, bool isResize)
    {
        ArgumentNullException.ThrowIfNull(container);
        HasError = false;
        StatusMessage = isResize
            ? $"Use the arrow keys to resize {container.Name}."
            : $"Use the arrow keys to move {container.Name}.";
    }

    public void ReportNonDestructiveError(string message)
    {
        HasError = true;
        StatusMessage = $"{message} No original file or folder was changed.";
    }

    private async Task SaveDefinitionAsync(
        ContainerCardViewModel container,
        ContainerDefinition candidate,
        string successMessage,
        CancellationToken cancellationToken)
    {
        HasError = false;
        try
        {
            ContainerDefinition updated = await containerService.UpdateAsync(candidate, cancellationToken);
            container.CommitDefinition(updated);
            StatusMessage = successMessage;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            container.RevertDefinition();
            StatusMessage = $"{container.Name} change was canceled.";
        }
        catch (Exception)
        {
            container.RevertDefinition();
            HasError = true;
            StatusMessage = $"{container.Name} could not be updated. The original item was not changed.";
        }
    }

    private static ContainerBounds CreateTemplateBounds(ContainerTemplateKind template, int count)
    {
        double offset = 24 + ((count % 8) * 32);
        (double width, double height) = template switch
        {
            ContainerTemplateKind.Compact => (240, 180),
            ContainerTemplateKind.Standard => (320, 240),
            ContainerTemplateKind.Wide => (480, 240),
            _ => throw new ArgumentOutOfRangeException(nameof(template)),
        };
        return ContainerBounds.Create(offset, offset, width, height);
    }

    private static void EnsureTargetExists(string target, ItemKind kind)
    {
        if (kind == ItemKind.Url)
        {
            return;
        }

        bool exists = kind == ItemKind.Folder ? Directory.Exists(target) : File.Exists(target);
        if (!exists)
        {
            throw new FileNotFoundException("The selected shell item is unavailable.", target);
        }
    }

    private static string DescribeSortMode(ContainerSortMode sortMode) => sortMode switch
    {
        ContainerSortMode.Manual => "manual order",
        ContainerSortMode.NameAscending => "name, A to Z",
        ContainerSortMode.NameDescending => "name, Z to A",
        ContainerSortMode.KindThenName => "type, then name",
        _ => throw new ArgumentOutOfRangeException(nameof(sortMode)),
    };

    private static string DescribeContainerCount(int count) => count == 1 ? "container" : "containers";

    private static string DescribePortalCount(int count) => count == 1 ? "folder portal" : "folder portals";
}
