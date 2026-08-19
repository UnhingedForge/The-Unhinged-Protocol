using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App.ViewModels;

/// <summary>
/// Mutable presentation state for a persisted reference container.
/// </summary>
public sealed class ContainerCardViewModel : ObservableObject
{
    private ContainerDefinition persistedDefinition;
    private ContainerSectionViewModel activeSection = null!;
    private double boundsHeight;
    private string iconGlyph = string.Empty;
    private string? label;
    private string name = string.Empty;
    private ContainerSortMode sortMode;
    private string tagsText = string.Empty;
    private double width;
    private double x;
    private double y;

    private ContainerCardViewModel(ContainerDefinition definition)
    {
        persistedDefinition = definition;
        ApplyDefinition(definition);
    }

    public Guid Id => persistedDefinition.Id;

    public string Name
    {
        get => name;
        private set => SetProperty(ref name, value);
    }

    public string? Label
    {
        get => label;
        private set => SetProperty(ref label, value);
    }

    public string TagsText
    {
        get => tagsText;
        private set => SetProperty(ref tagsText, value);
    }

    public string IconGlyph
    {
        get => iconGlyph;
        private set => SetProperty(ref iconGlyph, value);
    }

    public ContainerSortMode SortMode
    {
        get => sortMode;
        private set => SetProperty(ref sortMode, value);
    }

    public int SortModeIndex => (int)SortMode;

    public ContainerCompositionMode CompositionMode => persistedDefinition.CompositionMode;

    public int CompositionModeIndex => (int)CompositionMode;

    public ContainerDisplayState DisplayState => persistedDefinition.DisplayState;

    public bool IsExpanded => DisplayState == ContainerDisplayState.Expanded;

    public bool IsRolledUp => DisplayState == ContainerDisplayState.RolledUp;

    public bool IsCollapsed => DisplayState == ContainerDisplayState.Collapsed;

    public bool IsCapsule => DisplayState == ContainerDisplayState.Capsule;

    public bool IsTabs => CompositionMode == ContainerCompositionMode.Tabs;

    public bool IsStack => CompositionMode == ContainerCompositionMode.Stack;

    public bool IsPages => CompositionMode == ContainerCompositionMode.Pages;

    public bool ShowMetadata => IsExpanded || IsCollapsed;

    public bool ShowItemContent => IsExpanded && (!IsStack || ActiveSection.IsExpanded);

    public bool IsPinned => persistedDefinition.IsPinned;

    public bool IsLocked => persistedDefinition.IsLocked;

    public bool IsAutoSize => persistedDefinition.IsAutoSize;

    public bool IsVisible => persistedDefinition.IsVisible;

    public bool CanChangeLayout => !IsLocked;

    public bool CanResizeLayout => !IsLocked && !IsAutoSize && IsExpanded;

    public double Opacity => persistedDefinition.Opacity;

    public int OpacityPercent => (int)Math.Round(Opacity * 100);

    public ContainerColor Color => persistedDefinition.Color;

    public ContainerIconTreatment IconTreatment => persistedDefinition.IconTreatment;

    public ContainerBackgroundStyle BackgroundStyle => persistedDefinition.BackgroundStyle;

    public double SnapGridSize => persistedDefinition.SnapGridSize;

    public int ZIndex => IsPinned ? 100 : 0;

    public string AppearanceKey => $"{Color}.{BackgroundStyle}";

    public string IconTreatmentKey => IconTreatment.ToString();

    public string KindLabel => persistedDefinition.Kind == ContainerKind.ReferenceGroup
        ? "Reference container"
        : persistedDefinition.Kind.ToString();

    public string ItemCountLabel => Items.Count == 1 ? "1 item" : $"{Items.Count} items";

    public string StateSummary => $"{DisplayState} · {CompositionMode} · {OpacityPercent}%";

    public string AccessibleName =>
        $"{Name}, {ItemCountLabel}, {CompositionMode}, {DisplayState}{(IsLocked ? ", locked" : string.Empty)}{(IsPinned ? ", pinned" : string.Empty)}{(!IsVisible ? ", hidden" : string.Empty)}";

    public double X
    {
        get => x;
        private set => SetProperty(ref x, value);
    }

    public double Y
    {
        get => y;
        private set => SetProperty(ref y, value);
    }

    public double Width
    {
        get => width;
        private set => SetProperty(ref width, value);
    }

    public double BoundsHeight => boundsHeight;

    public double Height => DisplayState switch
    {
        ContainerDisplayState.RolledUp => 58,
        ContainerDisplayState.Collapsed => 112,
        ContainerDisplayState.Capsule => 48,
        _ when IsAutoSize => ContainerLayoutPolicy.AutoSizeHeight(VisibleItemCount, CompositionMode, Sections.Count),
        _ => Math.Max(boundsHeight, 240),
    };

    public int VisibleItemCount => IsStack
        ? Sections.Where(section => section.IsExpanded).Sum(section => section.Items.Count)
        : ActiveSection.Items.Count;

    public string VisibleItemCountLabel => VisibleItemCount == 1 ? "1 visible item" : $"{VisibleItemCount} visible items";

    public string PageLabel
    {
        get
        {
            int index = Sections.IndexOf(ActiveSection);
            return $"Page {index + 1} of {Sections.Count}: {ActiveSection.Name}";
        }
    }

    public bool CanGoToPreviousPage => Sections.IndexOf(ActiveSection) > 0;

    public bool CanGoToNextPage => Sections.IndexOf(ActiveSection) < Sections.Count - 1;

    public string MoveAutomationName => $"Move {Name}";

    public string ResizeAutomationName => $"Resize {Name}";

    public ObservableCollection<ItemCardViewModel> Items { get; } = [];

    public ObservableCollection<ContainerSectionViewModel> Sections { get; } = [];

    public ContainerSectionViewModel ActiveSection
    {
        get => activeSection;
        private set => SetProperty(ref activeSection, value);
    }

    public static ContainerCardViewModel FromDefinition(ContainerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new ContainerCardViewModel(definition.UpgradeToCurrent());
    }

    public ContainerBounds CaptureBounds() => ContainerBounds.Create(X, Y, Width, BoundsHeight);

    public ContainerDefinition CaptureDefinition() => (persistedDefinition with
    {
        Name = Name,
        Label = Label,
        Tags = ParseTags(TagsText),
        IconGlyph = IconGlyph,
        SortMode = SortMode,
        Bounds = CaptureBounds(),
        Items = Items.Select(item => item.Reference).ToArray(),
        Sections = Sections.Select(section => section.CaptureDefinition()).ToArray(),
        ActiveSectionId = ActiveSection.Id,
    }).EnsureValid();

    public void SetInteractiveBounds(ContainerBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        bounds.EnsureValid();
        ApplyBounds(bounds);
    }

    public void CommitBounds(ContainerBounds bounds)
    {
        SetInteractiveBounds(bounds);
        persistedDefinition = persistedDefinition with { Bounds = bounds };
    }

    public void CommitDefinition(ContainerDefinition definition)
    {
        persistedDefinition = definition.UpgradeToCurrent();
        ApplyDefinition(persistedDefinition);
    }

    public void RevertBounds() => ApplyBounds(persistedDefinition.Bounds);

    public void RevertDefinition() => ApplyDefinition(persistedDefinition);

    public ContainerDefinition BuildPresentation(
        string newName,
        string? newLabel,
        string tags,
        string newIconGlyph) =>
        CaptureDefinition().WithPresentation(newName, newLabel, ParseTags(tags), newIconGlyph);

    private void ApplyDefinition(ContainerDefinition definition)
    {
        Name = definition.Name;
        Label = definition.Label;
        TagsText = string.Join(", ", definition.Tags);
        IconGlyph = definition.IconGlyph;
        SortMode = definition.SortMode;
        ApplyBounds(definition.Bounds);

        Items.Clear();
        Dictionary<Guid, ItemCardViewModel> itemsById = definition.Items
            .OrderBy(item => item.SortOrder)
            .Select(item => new ItemCardViewModel(item))
            .ToDictionary(item => item.Id);
        foreach (ItemCardViewModel item in itemsById.Values.OrderBy(item => item.Reference.SortOrder))
        {
            Items.Add(item);
        }

        Sections.Clear();
        foreach (ContainerSectionDefinition section in definition.Sections)
        {
            Sections.Add(new ContainerSectionViewModel(
                section,
                section.ItemIds.Select(id => itemsById[id]),
                section.Id == definition.ActiveSectionId));
        }

        ActiveSection = Sections.Single(section => section.Id == definition.ActiveSectionId);
        RaisePresentationProperties();
    }

    private void ApplyBounds(ContainerBounds bounds)
    {
        X = bounds.X;
        Y = bounds.Y;
        Width = bounds.Width;
        boundsHeight = bounds.Height;
        OnPropertyChanged(nameof(BoundsHeight));
        OnPropertyChanged(nameof(Height));
    }

    private void RaisePresentationProperties()
    {
        OnPropertyChanged(nameof(SortModeIndex));
        OnPropertyChanged(nameof(CompositionMode));
        OnPropertyChanged(nameof(CompositionModeIndex));
        OnPropertyChanged(nameof(DisplayState));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(IsRolledUp));
        OnPropertyChanged(nameof(IsCollapsed));
        OnPropertyChanged(nameof(IsCapsule));
        OnPropertyChanged(nameof(IsTabs));
        OnPropertyChanged(nameof(IsStack));
        OnPropertyChanged(nameof(IsPages));
        OnPropertyChanged(nameof(ShowMetadata));
        OnPropertyChanged(nameof(ShowItemContent));
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsAutoSize));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(CanChangeLayout));
        OnPropertyChanged(nameof(CanResizeLayout));
        OnPropertyChanged(nameof(Opacity));
        OnPropertyChanged(nameof(OpacityPercent));
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(IconTreatment));
        OnPropertyChanged(nameof(BackgroundStyle));
        OnPropertyChanged(nameof(SnapGridSize));
        OnPropertyChanged(nameof(ZIndex));
        OnPropertyChanged(nameof(AppearanceKey));
        OnPropertyChanged(nameof(IconTreatmentKey));
        OnPropertyChanged(nameof(ItemCountLabel));
        OnPropertyChanged(nameof(StateSummary));
        OnPropertyChanged(nameof(AccessibleName));
        OnPropertyChanged(nameof(VisibleItemCount));
        OnPropertyChanged(nameof(VisibleItemCountLabel));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        OnPropertyChanged(nameof(MoveAutomationName));
        OnPropertyChanged(nameof(ResizeAutomationName));
    }

    private static string[] ParseTags(string tags) => tags
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
