namespace TheUnhingedProtocol.Domain.Contracts;

public static class ContractSchema
{
    public const int MinimumSupportedVersion = 1;

    public const int CurrentVersion = 4;

    public static void EnsureSupported(int version)
    {
        if (version < MinimumSupportedVersion || version > CurrentVersion)
        {
            throw new NotSupportedException($"Schema version {version} is not supported.");
        }
    }
}

public enum ContainerKind
{
    ReferenceGroup,
    FolderPortal,
    TabStack,
    Dashboard,
}

public enum ItemKind
{
    File,
    Folder,
    Shortcut,
    Application,
    Url,
}

public enum ContainerTemplateKind
{
    Compact,
    Standard,
    Wide,
}

public enum ContainerSortMode
{
    Manual,
    NameAscending,
    NameDescending,
    KindThenName,
}

public enum ContainerCompositionMode
{
    Tabs,
    Stack,
    Pages,
}

public enum ContainerDisplayState
{
    Expanded,
    RolledUp,
    Collapsed,
    Capsule,
}

public enum ContainerColor
{
    Neutral,
    Violet,
    Blue,
    Teal,
    Amber,
    Rose,
}

public enum ContainerIconTreatment
{
    Accent,
    Neutral,
    Monochrome,
}

public enum ContainerBackgroundStyle
{
    System,
    SubtleTint,
}

public enum PortalViewMode
{
    Grid,
    List,
    Details,
}

public enum PortalSortMode
{
    NameAscending,
    NameDescending,
    TypeThenName,
    ModifiedNewest,
    ModifiedOldest,
    SizeDescending,
}

public enum PortalTargetState
{
    Ready,
    Missing,
    Inaccessible,
    Disconnected,
    Error,
}

public enum PortalItemKind
{
    File,
    Folder,
}

public sealed record ContainerBounds
{
    public const double DefaultWidth = 280;

    public const double DefaultHeight = 188;

    public const double MinimumWidth = 220;

    public const double MinimumHeight = 160;

    public const double MaximumDimension = 4096;

    public const double MaximumPosition = 32768;

    public double X { get; init; } = 24;

    public double Y { get; init; } = 24;

    public double Width { get; init; } = DefaultWidth;

    public double Height { get; init; } = DefaultHeight;

    public static ContainerBounds Create(double x, double y, double width, double height)
    {
        ContainerBounds bounds = new()
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
        };
        bounds.EnsureValid();
        return bounds;
    }

    public void EnsureValid()
    {
        EnsureFiniteRange(X, 0, MaximumPosition, nameof(X));
        EnsureFiniteRange(Y, 0, MaximumPosition, nameof(Y));
        EnsureFiniteRange(Width, MinimumWidth, MaximumDimension, nameof(Width));
        EnsureFiniteRange(Height, MinimumHeight, MaximumDimension, nameof(Height));
    }

    private static void EnsureFiniteRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"{name} must be finite and between {minimum} and {maximum}.");
        }
    }
}

public static class ContainerLayoutPolicy
{
    public const double DefaultSnapGrid = 8;

    public const double MinimumSnapGrid = 4;

    public const double MaximumSnapGrid = 64;

    public const double MinimumRasterizationScale = 1;

    public const double MaximumRasterizationScale = 3;

    public static ContainerBounds SnapBounds(
        ContainerBounds bounds,
        double physicalGridSize,
        double rasterizationScale,
        double workspaceWidth = ContainerBounds.MaximumPosition,
        double workspaceHeight = ContainerBounds.MaximumPosition)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        bounds.EnsureValid();
        EnsureGridSize(physicalGridSize);
        if (!double.IsFinite(rasterizationScale) ||
            rasterizationScale < MinimumRasterizationScale ||
            rasterizationScale > MaximumRasterizationScale)
        {
            throw new ArgumentOutOfRangeException(nameof(rasterizationScale));
        }

        if (!double.IsFinite(workspaceWidth) || !double.IsFinite(workspaceHeight) ||
            workspaceWidth < ContainerBounds.MinimumWidth ||
            workspaceHeight < ContainerBounds.MinimumHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(workspaceWidth));
        }

        double dipStep = physicalGridSize / rasterizationScale;
        double width = Math.Clamp(
            Snap(bounds.Width, dipStep),
            ContainerBounds.MinimumWidth,
            Math.Min(ContainerBounds.MaximumDimension, workspaceWidth));
        double height = Math.Clamp(
            Snap(bounds.Height, dipStep),
            ContainerBounds.MinimumHeight,
            Math.Min(ContainerBounds.MaximumDimension, workspaceHeight));
        double x = Math.Clamp(Snap(bounds.X, dipStep), 0, Math.Max(0, workspaceWidth - width));
        double y = Math.Clamp(Snap(bounds.Y, dipStep), 0, Math.Max(0, workspaceHeight - height));
        return ContainerBounds.Create(x, y, width, height);
    }

    public static double AutoSizeHeight(int visibleItemCount, ContainerCompositionMode mode, int sectionCount)
    {
        if (visibleItemCount < 0 || sectionCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(visibleItemCount));
        }

        int sectionHeaderCount = mode == ContainerCompositionMode.Stack ? sectionCount : 1;
        return Math.Clamp(176 + (visibleItemCount * 48) + (sectionHeaderCount * 34), 260, 640);
    }

    public static void EnsureGridSize(double gridSize)
    {
        if (!double.IsFinite(gridSize) || gridSize < MinimumSnapGrid || gridSize > MaximumSnapGrid)
        {
            throw new ArgumentOutOfRangeException(nameof(gridSize));
        }
    }

    private static double Snap(double value, double step) =>
        Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
}

public sealed record ContainerSectionDefinition
{
    public const int MaximumNameLength = 48;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = "Main";

    public Guid[] ItemIds { get; init; } = [];

    public bool IsExpanded { get; init; } = true;

    public static ContainerSectionDefinition Create(string name) => new()
    {
        Name = ContainerDefinition.NormalizeRequiredText(name, MaximumNameLength, nameof(name)),
    };

    public ContainerSectionDefinition EnsureValid()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidDataException("Container section identifiers cannot be empty.");
        }

        _ = ContainerDefinition.NormalizeRequiredText(Name, MaximumNameLength, nameof(Name));
        if (ItemIds.Distinct().Count() != ItemIds.Length)
        {
            throw new InvalidDataException("A container section cannot contain duplicate item identities.");
        }

        return this;
    }
}

public enum RuleOperator
{
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    EndsWith,
    MatchesRegularExpression,
    GreaterThan,
    LessThan,
}

public enum RuleActionKind
{
    AddReference,
    AddTag,
    MoveWithConfirmation,
    SuggestCategory,
}

public enum FileOperationKind
{
    Move,
    Rename,
    Recycle,
    Restore,
}

public enum ConflictPolicy
{
    RequireDecision,
    GenerateUniqueName,
    Skip,
}

public enum FileTransactionState
{
    Planned,
    Confirmed,
    Running,
    Committed,
    RollingBack,
    RolledBack,
    Failed,
}

public sealed record ContainerDefinition
{
    public const int MaximumNameLength = 80;

    public const int MaximumLabelLength = 60;

    public const int MaximumTagCount = 12;

    public const int MaximumTagLength = 40;

    public const int MaximumSectionCount = 12;

    public static readonly string[] ApprovedIconGlyphs = ["\uE8B7", "\uE8D5", "\uE7C3", "\uE8F1"];

    public int SchemaVersion { get; init; } = ContractSchema.CurrentVersion;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = string.Empty;

    public ContainerKind Kind { get; init; } = ContainerKind.ReferenceGroup;

    public bool IsLocked { get; init; }

    public bool IsPinned { get; init; }

    public bool IsAutoSize { get; init; }

    public bool IsVisible { get; init; } = true;

    public double Opacity { get; init; } = 1.0;

    public double SnapGridSize { get; init; } = ContainerLayoutPolicy.DefaultSnapGrid;

    public ContainerBounds Bounds { get; init; } = new();

    public string? Label { get; init; }

    public string[] Tags { get; init; } = [];

    public string IconGlyph { get; init; } = "\uE8B7";

    public ContainerSortMode SortMode { get; init; } = ContainerSortMode.Manual;

    public ContainerCompositionMode CompositionMode { get; init; } = ContainerCompositionMode.Tabs;

    public ContainerDisplayState DisplayState { get; init; } = ContainerDisplayState.Expanded;

    public ContainerColor Color { get; init; } = ContainerColor.Violet;

    public ContainerIconTreatment IconTreatment { get; init; } = ContainerIconTreatment.Accent;

    public ContainerBackgroundStyle BackgroundStyle { get; init; } = ContainerBackgroundStyle.System;

    public Guid ActiveSectionId { get; init; }

    public ContainerSectionDefinition[] Sections { get; init; } = [];

    public ItemReference[] Items { get; init; } = [];

    public static ContainerDefinition CreateReferenceGroup(
        string name,
        ContainerBounds? bounds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string normalizedName = name.Trim();
        if (normalizedName.Length > MaximumNameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                $"Container names cannot exceed {MaximumNameLength} characters.");
        }

        ContainerBounds validatedBounds = bounds ?? new ContainerBounds();
        validatedBounds.EnsureValid();

        ContainerSectionDefinition mainSection = ContainerSectionDefinition.Create("Main");
        return new ContainerDefinition
        {
            Name = normalizedName,
            Kind = ContainerKind.ReferenceGroup,
            Bounds = validatedBounds,
            ActiveSectionId = mainSection.Id,
            Sections = [mainSection],
        };
    }

    public ContainerDefinition WithBounds(ContainerBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        bounds.EnsureValid();
        EnsureLayoutUnlocked();
        return this with { Bounds = bounds };
    }

    public ContainerDefinition WithSnappedBounds(
        ContainerBounds bounds,
        double rasterizationScale,
        double workspaceWidth,
        double workspaceHeight) =>
        WithBounds(ContainerLayoutPolicy.SnapBounds(
            bounds,
            SnapGridSize,
            rasterizationScale,
            workspaceWidth,
            workspaceHeight));

    public ContainerDefinition WithPresentation(
        string name,
        string? label,
        IEnumerable<string> tags,
        string iconGlyph)
    {
        string normalizedName = NormalizeRequiredText(name, MaximumNameLength, nameof(name));
        string? normalizedLabel = NormalizeOptionalText(label, MaximumLabelLength, nameof(label));
        string[] normalizedTags = NormalizeTags(tags);
        if (!ApprovedIconGlyphs.Contains(iconGlyph, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(iconGlyph), "The icon is not in the approved built-in set.");
        }

        return (this with
        {
            Name = normalizedName,
            Label = normalizedLabel,
            Tags = normalizedTags,
            IconGlyph = iconGlyph,
        }).EnsureValid();
    }

    public ContainerDefinition WithAppearance(
        double opacity,
        ContainerColor color,
        ContainerIconTreatment iconTreatment,
        ContainerBackgroundStyle backgroundStyle)
    {
        if (!double.IsFinite(opacity) || opacity < 0.6 || opacity > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(opacity), "Container opacity must be between 60% and 100%.");
        }

        if (!Enum.IsDefined(color) || !Enum.IsDefined(iconTreatment) || !Enum.IsDefined(backgroundStyle))
        {
            throw new ArgumentOutOfRangeException(nameof(color));
        }

        return (this with
        {
            Opacity = opacity,
            Color = color,
            IconTreatment = iconTreatment,
            BackgroundStyle = backgroundStyle,
        }).EnsureValid();
    }

    public ContainerDefinition WithLayoutOptions(bool isPinned, bool isLocked, bool isAutoSize)
    {
        if (IsLocked && (IsPinned != isPinned || IsAutoSize != isAutoSize) && isLocked)
        {
            throw new InvalidOperationException("Unlock the container before changing its layout options.");
        }

        return (this with
        {
            IsPinned = isPinned,
            IsLocked = isLocked,
            IsAutoSize = isAutoSize,
        }).EnsureValid();
    }

    public ContainerDefinition WithDisplayState(ContainerDisplayState displayState)
    {
        if (!Enum.IsDefined(displayState))
        {
            throw new ArgumentOutOfRangeException(nameof(displayState));
        }

        return (this with { DisplayState = displayState }).EnsureValid();
    }

    public ContainerDefinition WithVisibility(bool isVisible) =>
        (this with { IsVisible = isVisible }).EnsureValid();

    public ContainerDefinition WithSnapGrid(double physicalGridSize)
    {
        EnsureLayoutUnlocked();
        ContainerLayoutPolicy.EnsureGridSize(physicalGridSize);
        return (this with { SnapGridSize = physicalGridSize }).EnsureValid();
    }

    public ContainerDefinition WithCompositionMode(ContainerCompositionMode mode)
    {
        EnsureLayoutUnlocked();
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return (this with { CompositionMode = mode }).EnsureValid();
    }

    public ContainerDefinition AddSection(string name)
    {
        EnsureLayoutUnlocked();
        if (Sections.Length >= MaximumSectionCount)
        {
            throw new InvalidOperationException($"A container supports at most {MaximumSectionCount} sections.");
        }

        ContainerSectionDefinition section = ContainerSectionDefinition.Create(name);
        return (this with
        {
            Sections = [.. Sections, section],
            ActiveSectionId = section.Id,
        }).EnsureValid();
    }

    public ContainerDefinition RenameSection(Guid sectionId, string name)
    {
        EnsureLayoutUnlocked();
        ContainerSectionDefinition[] updated = Sections
            .Select(section => section.Id == sectionId
                ? section with { Name = NormalizeRequiredText(name, ContainerSectionDefinition.MaximumNameLength, nameof(name)) }
                : section)
            .ToArray();
        if (!updated.Any(section => section.Id == sectionId))
        {
            throw new KeyNotFoundException($"Container section {sectionId:D} was not found.");
        }

        return (this with { Sections = updated }).EnsureValid();
    }

    public ContainerDefinition RemoveSection(Guid sectionId)
    {
        EnsureLayoutUnlocked();
        if (Sections.Length == 1)
        {
            throw new InvalidOperationException("A container must retain at least one section.");
        }

        ContainerSectionDefinition removed = Sections.SingleOrDefault(section => section.Id == sectionId)
            ?? throw new KeyNotFoundException($"Container section {sectionId:D} was not found.");
        ContainerSectionDefinition[] remaining = Sections.Where(section => section.Id != sectionId).ToArray();
        remaining[0] = remaining[0] with { ItemIds = [.. remaining[0].ItemIds, .. removed.ItemIds] };
        Guid activeSectionId = ActiveSectionId == sectionId ? remaining[0].Id : ActiveSectionId;
        return (this with { Sections = remaining, ActiveSectionId = activeSectionId }).EnsureValid();
    }

    public ContainerDefinition SelectSection(Guid sectionId)
    {
        if (!Sections.Any(section => section.Id == sectionId))
        {
            throw new KeyNotFoundException($"Container section {sectionId:D} was not found.");
        }

        return (this with { ActiveSectionId = sectionId }).EnsureValid();
    }

    public ContainerDefinition SetSectionExpanded(Guid sectionId, bool isExpanded)
    {
        ContainerSectionDefinition[] updated = Sections
            .Select(section => section.Id == sectionId ? section with { IsExpanded = isExpanded } : section)
            .ToArray();
        if (!updated.Any(section => section.Id == sectionId))
        {
            throw new KeyNotFoundException($"Container section {sectionId:D} was not found.");
        }

        return (this with { Sections = updated, ActiveSectionId = sectionId }).EnsureValid();
    }

    public ContainerDefinition AddItem(ItemReference item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.EnsureValid();
        if (Items.Any(existing =>
            existing.Kind == item.Kind &&
            string.Equals(existing.CanonicalPath, item.CanonicalPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That reference is already in the container.");
        }

        int nextOrder = Items.Length == 0 ? 0 : Items.Max(existing => existing.SortOrder) + 1;
        Guid sectionId = ActiveSectionId == Guid.Empty ? Sections[0].Id : ActiveSectionId;
        ContainerSectionDefinition[] sections = Sections
            .Select(section => section.Id == sectionId
                ? section with { ItemIds = [.. section.ItemIds, item.Id] }
                : section)
            .ToArray();
        return (this with { Items = [.. Items, item with { SortOrder = nextOrder }], Sections = sections }).ApplySort();
    }

    public ContainerDefinition RemoveItem(Guid itemId)
    {
        if (!Items.Any(item => item.Id == itemId))
        {
            throw new KeyNotFoundException($"Item reference {itemId:D} was not found.");
        }

        ContainerSectionDefinition[] sections = Sections
            .Select(section => section with { ItemIds = section.ItemIds.Where(id => id != itemId).ToArray() })
            .ToArray();
        return (this with
        {
            Items = NormalizeOrder(Items.Where(item => item.Id != itemId)),
            Sections = sections,
        }).EnsureValid();
    }

    public ContainerDefinition MoveItem(Guid itemId, int direction)
    {
        if (SortMode != ContainerSortMode.Manual)
        {
            throw new InvalidOperationException("Manual ordering is available only in manual sort mode.");
        }

        ItemReference[] ordered = Items.OrderBy(item => item.SortOrder).ToArray();
        int currentIndex = Array.FindIndex(ordered, item => item.Id == itemId);
        if (currentIndex < 0)
        {
            throw new KeyNotFoundException($"Item reference {itemId:D} was not found.");
        }

        int targetIndex = Math.Clamp(currentIndex + Math.Sign(direction), 0, ordered.Length - 1);
        (ordered[currentIndex], ordered[targetIndex]) = (ordered[targetIndex], ordered[currentIndex]);
        return (this with { Items = NormalizeOrder(ordered) }).EnsureValid();
    }

    public ContainerDefinition UpdateItem(ItemReference item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.EnsureValid();
        int index = Array.FindIndex(Items, existing => existing.Id == item.Id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Item reference {item.Id:D} was not found.");
        }

        ItemReference[] updatedItems = [.. Items];
        updatedItems[index] = item with { SortOrder = Items[index].SortOrder };
        return (this with { Items = updatedItems }).ApplySort();
    }

    public ContainerDefinition WithSortMode(ContainerSortMode sortMode)
    {
        if (!Enum.IsDefined(sortMode))
        {
            throw new ArgumentOutOfRangeException(nameof(sortMode));
        }

        return (this with { SortMode = sortMode }).ApplySort();
    }

    public ContainerDefinition UpgradeToCurrent()
    {
        ContractSchema.EnsureSupported(SchemaVersion);
        ItemReference[] upgradedItems = NormalizeOrder(Items.Select(item => item.UpgradeToCurrent()));
        ContainerSectionDefinition[] upgradedSections = Sections is { Length: > 0 }
            ? Sections
            : [ContainerSectionDefinition.Create("Main") with { ItemIds = upgradedItems.Select(item => item.Id).ToArray() }];
        Guid activeSectionId = upgradedSections.Any(section => section.Id == ActiveSectionId)
            ? ActiveSectionId
            : upgradedSections[0].Id;
        return (this with
        {
            SchemaVersion = ContractSchema.CurrentVersion,
            Tags = NormalizeTags(Tags),
            Items = upgradedItems,
            Sections = upgradedSections,
            ActiveSectionId = activeSectionId,
        }).ApplySort().EnsureValid();
    }

    public ContainerDefinition EnsureValid()
    {
        ContractSchema.EnsureSupported(SchemaVersion);
        _ = NormalizeRequiredText(Name, MaximumNameLength, nameof(Name));
        _ = NormalizeOptionalText(Label, MaximumLabelLength, nameof(Label));
        _ = NormalizeTags(Tags);
        Bounds.EnsureValid();
        ContainerLayoutPolicy.EnsureGridSize(SnapGridSize);
        if (!double.IsFinite(Opacity) || Opacity < 0.6 || Opacity > 1)
        {
            throw new InvalidDataException("Container opacity must be between 60% and 100%.");
        }
        if (!ApprovedIconGlyphs.Contains(IconGlyph, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The container icon is not approved.");
        }

        if (!Enum.IsDefined(SortMode))
        {
            throw new InvalidDataException("The container sort mode is invalid.");
        }

        if (!Enum.IsDefined(CompositionMode) || !Enum.IsDefined(DisplayState) ||
            !Enum.IsDefined(Color) || !Enum.IsDefined(IconTreatment) || !Enum.IsDefined(BackgroundStyle))
        {
            throw new InvalidDataException("The container presentation state is invalid.");
        }

        foreach (ItemReference item in Items)
        {
            item.EnsureValid();
        }

        if (Items.Select(item => item.Id).Distinct().Count() != Items.Length)
        {
            throw new InvalidDataException("Container item identifiers must be unique.");
        }


        if (Sections.Length < 1 || Sections.Length > MaximumSectionCount ||
            Sections.Select(section => section.Id).Distinct().Count() != Sections.Length)
        {
            throw new InvalidDataException("Container sections must have unique identities within the supported limit.");
        }

        foreach (ContainerSectionDefinition section in Sections)
        {
            section.EnsureValid();
        }

        if (!Sections.Any(section => section.Id == ActiveSectionId))
        {
            throw new InvalidDataException("The active container section does not exist.");
        }

        Guid[] assignedIds = Sections.SelectMany(section => section.ItemIds).ToArray();
        if (assignedIds.Distinct().Count() != assignedIds.Length ||
            !assignedIds.Order().SequenceEqual(Items.Select(item => item.Id).Order()))
        {
            throw new InvalidDataException("Every container item must belong to exactly one section.");
        }

        return this;
    }

    private ContainerDefinition ApplySort()
    {
        IEnumerable<ItemReference> sorted = SortMode switch
        {
            ContainerSortMode.Manual => Items.OrderBy(item => item.SortOrder),
            ContainerSortMode.NameAscending => Items.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            ContainerSortMode.NameDescending => Items.OrderByDescending(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            ContainerSortMode.KindThenName => Items.OrderBy(item => item.Kind).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => throw new ArgumentOutOfRangeException(nameof(SortMode)),
        };
        ItemReference[] normalizedItems = NormalizeOrder(sorted);
        Dictionary<Guid, int> order = normalizedItems
            .Select((item, index) => (item.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index);
        ContainerSectionDefinition[] sections = Sections.Select(section => section with
        {
            ItemIds = section.ItemIds.OrderBy(id => order[id]).ToArray(),
        }).ToArray();
        return (this with { Items = normalizedItems, Sections = sections }).EnsureValid();
    }

    private void EnsureLayoutUnlocked()
    {
        if (IsLocked)
        {
            throw new InvalidOperationException("Unlock the container before changing its layout.");
        }
    }

    private static ItemReference[] NormalizeOrder(IEnumerable<ItemReference> items) =>
        items.Select((item, index) => item with { SortOrder = index }).ToArray();

    internal static string NormalizeRequiredText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Text cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    internal static string? NormalizeOptionalText(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Text cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    internal static string[] NormalizeTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        string[] normalized = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length > MaximumTagCount || normalized.Any(tag => tag.Length > MaximumTagLength))
        {
            throw new ArgumentOutOfRangeException(nameof(tags), "The tag count or tag length exceeds the supported limit.");
        }

        return normalized;
    }
}

public sealed record ItemReference
{
    public const int MaximumTargetLength = 2048;

    public const int MaximumDisplayNameLength = 160;

    public int SchemaVersion { get; init; } = ContractSchema.CurrentVersion;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string CanonicalPath { get; init; } = string.Empty;

    public ItemKind Kind { get; init; }

    public bool AllowPhysicalMove { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string? Label { get; init; }

    public string[] Tags { get; init; } = [];

    public string? IconGlyph { get; init; }

    public bool ShowThumbnail { get; init; } = true;

    public int SortOrder { get; init; }

    public static ItemReference Create(string target, ItemKind kind, string? displayName = null)
    {
        string canonicalTarget = NormalizeTarget(target, kind);
        string inferredName = kind == ItemKind.Url
            ? new Uri(canonicalTarget).Host
            : Path.GetFileName(canonicalTarget.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string normalizedName = ContainerDefinition.NormalizeRequiredText(
            displayName ?? inferredName,
            MaximumDisplayNameLength,
            nameof(displayName));
        return new ItemReference
        {
            CanonicalPath = canonicalTarget,
            Kind = kind,
            DisplayName = normalizedName,
            AllowPhysicalMove = false,
        }.EnsureValid();
    }

    public ItemReference WithMetadata(
        string displayName,
        string? label,
        IEnumerable<string> tags,
        string? iconGlyph,
        bool showThumbnail)
    {
        if (iconGlyph is not null && !ContainerDefinition.ApprovedIconGlyphs.Contains(iconGlyph, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(iconGlyph));
        }

        return (this with
        {
            DisplayName = ContainerDefinition.NormalizeRequiredText(displayName, MaximumDisplayNameLength, nameof(displayName)),
            Label = ContainerDefinition.NormalizeOptionalText(label, ContainerDefinition.MaximumLabelLength, nameof(label)),
            Tags = ContainerDefinition.NormalizeTags(tags),
            IconGlyph = iconGlyph,
            ShowThumbnail = showThumbnail,
            AllowPhysicalMove = false,
        }).EnsureValid();
    }

    public ItemReference UpgradeToCurrent() => (this with
    {
        SchemaVersion = ContractSchema.CurrentVersion,
        DisplayName = string.IsNullOrWhiteSpace(DisplayName)
            ? (Kind == ItemKind.Url ? new Uri(CanonicalPath).Host : Path.GetFileName(CanonicalPath))
            : DisplayName,
        Tags = ContainerDefinition.NormalizeTags(Tags),
        AllowPhysicalMove = false,
    }).EnsureValid();

    public ItemReference EnsureValid()
    {
        ContractSchema.EnsureSupported(SchemaVersion);
        _ = NormalizeTarget(CanonicalPath, Kind);
        _ = ContainerDefinition.NormalizeRequiredText(DisplayName, MaximumDisplayNameLength, nameof(DisplayName));
        _ = ContainerDefinition.NormalizeOptionalText(Label, ContainerDefinition.MaximumLabelLength, nameof(Label));
        _ = ContainerDefinition.NormalizeTags(Tags);
        if (AllowPhysicalMove)
        {
            throw new InvalidDataException("PH1-001 references cannot authorize physical file movement.");
        }

        if (SortOrder < 0)
        {
            throw new InvalidDataException("Item sort order cannot be negative.");
        }

        return this;
    }

    private static string NormalizeTarget(string target, ItemKind kind)
    {
        string normalized = ContainerDefinition.NormalizeRequiredText(target, MaximumTargetLength, nameof(target));
        if (kind == ItemKind.Url)
        {
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("URLs must be absolute HTTP or HTTPS addresses.", nameof(target));
            }

            return uri.AbsoluteUri;
        }

        if (!Enum.IsDefined(kind) || !Path.IsPathFullyQualified(normalized))
        {
            throw new ArgumentException("Shell item targets must use a fully qualified path.", nameof(target));
        }

        return Path.GetFullPath(normalized);
    }
}

public sealed record FolderPortal
{
    public const int MaximumNameLength = 80;

    public const int MaximumTabs = 16;

    public int SchemaVersion { get; init; } = ContractSchema.CurrentVersion;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = string.Empty;

    public bool IsVisible { get; init; } = true;

    public Guid ActiveTabId { get; init; }

    public FolderPortalTab[] Tabs { get; init; } = [];

    public static FolderPortal Create(string name, string folderPath)
    {
        string normalizedName = ContainerDefinition.NormalizeRequiredText(
            name,
            MaximumNameLength,
            nameof(name));
        FolderPortalTab tab = FolderPortalTab.Create(folderPath);
        return new FolderPortal
        {
            Name = normalizedName,
            ActiveTabId = tab.Id,
            Tabs = [tab],
        }.EnsureValid();
    }

    public FolderPortal WithName(string name) => (this with
    {
        Name = ContainerDefinition.NormalizeRequiredText(name, MaximumNameLength, nameof(name)),
    }).EnsureValid();

    public FolderPortal WithVisibility(bool isVisible) =>
        (this with { IsVisible = isVisible }).EnsureValid();

    public FolderPortal AddTab(string folderPath)
    {
        if (Tabs.Length >= MaximumTabs)
        {
            throw new InvalidOperationException($"A folder portal supports at most {MaximumTabs} tabs.");
        }

        FolderPortalTab tab = FolderPortalTab.Create(folderPath);
        return (this with { Tabs = [.. Tabs, tab], ActiveTabId = tab.Id }).EnsureValid();
    }

    public FolderPortal CloseTab(Guid tabId)
    {
        if (Tabs.Length == 1)
        {
            throw new InvalidOperationException("A folder portal must keep at least one tab.");
        }

        int index = Array.FindIndex(Tabs, tab => tab.Id == tabId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Portal tab {tabId:D} was not found.");
        }

        FolderPortalTab[] remaining = [.. Tabs.Where(tab => tab.Id != tabId)];
        Guid activeId = ActiveTabId == tabId
            ? remaining[Math.Min(index, remaining.Length - 1)].Id
            : ActiveTabId;
        return (this with { Tabs = remaining, ActiveTabId = activeId }).EnsureValid();
    }

    public FolderPortal SelectTab(Guid tabId)
    {
        if (!Tabs.Any(tab => tab.Id == tabId))
        {
            throw new KeyNotFoundException($"Portal tab {tabId:D} was not found.");
        }

        return (this with { ActiveTabId = tabId }).EnsureValid();
    }

    public FolderPortal UpdateTab(FolderPortalTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        tab.EnsureValid();
        int index = Array.FindIndex(Tabs, candidate => candidate.Id == tab.Id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Portal tab {tab.Id:D} was not found.");
        }

        FolderPortalTab[] updated = [.. Tabs];
        updated[index] = tab;
        return (this with { Tabs = updated }).EnsureValid();
    }

    public FolderPortal EnsureValid()
    {
        ContractSchema.EnsureSupported(SchemaVersion);
        _ = ContainerDefinition.NormalizeRequiredText(Name, MaximumNameLength, nameof(Name));
        if (Tabs.Length is < 1 or > MaximumTabs)
        {
            throw new InvalidDataException($"A folder portal requires between 1 and {MaximumTabs} tabs.");
        }

        if (Tabs.Select(tab => tab.Id).Distinct().Count() != Tabs.Length)
        {
            throw new InvalidDataException("Portal tab identifiers must be unique.");
        }

        foreach (FolderPortalTab tab in Tabs)
        {
            tab.EnsureValid();
        }

        if (!Tabs.Any(tab => tab.Id == ActiveTabId))
        {
            throw new InvalidDataException("The active portal tab must belong to the portal.");
        }

        return this;
    }
}

public sealed record FolderPortalTab
{
    public const int MaximumSearchLength = 260;

    public const int MaximumHistoryEntries = 50;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string CurrentPath { get; init; } = string.Empty;

    public PortalViewMode ViewMode { get; init; } = PortalViewMode.Grid;

    public PortalSortMode SortMode { get; init; } = PortalSortMode.NameAscending;

    public string SearchQuery { get; init; } = string.Empty;

    public string[] BackHistory { get; init; } = [];

    public string[] ForwardHistory { get; init; } = [];

    public static FolderPortalTab Create(string folderPath) => new FolderPortalTab
    {
        CurrentPath = NormalizeFolderPath(folderPath),
    }.EnsureValid();

    public FolderPortalTab Navigate(string folderPath)
    {
        string target = NormalizeFolderPath(folderPath);
        if (string.Equals(target, CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            return this;
        }

        return (this with
        {
            CurrentPath = target,
            BackHistory = [.. BackHistory.Append(CurrentPath).TakeLast(MaximumHistoryEntries)],
            ForwardHistory = [],
        }).EnsureValid();
    }

    public FolderPortalTab GoBack()
    {
        if (BackHistory.Length == 0)
        {
            return this;
        }

        string target = BackHistory[^1];
        return (this with
        {
            CurrentPath = target,
            BackHistory = [.. BackHistory[..^1]],
            ForwardHistory = [.. ForwardHistory.Append(CurrentPath).TakeLast(MaximumHistoryEntries)],
        }).EnsureValid();
    }

    public FolderPortalTab GoForward()
    {
        if (ForwardHistory.Length == 0)
        {
            return this;
        }

        string target = ForwardHistory[^1];
        return (this with
        {
            CurrentPath = target,
            BackHistory = [.. BackHistory.Append(CurrentPath).TakeLast(MaximumHistoryEntries)],
            ForwardHistory = [.. ForwardHistory[..^1]],
        }).EnsureValid();
    }

    public FolderPortalTab GoUp()
    {
        DirectoryInfo? parent = Directory.GetParent(CurrentPath);
        return parent is null ? this : Navigate(parent.FullName);
    }

    public FolderPortalTab WithView(PortalViewMode viewMode) => (this with
    {
        ViewMode = Enum.IsDefined(viewMode) ? viewMode : throw new ArgumentOutOfRangeException(nameof(viewMode)),
    }).EnsureValid();

    public FolderPortalTab WithSort(PortalSortMode sortMode) => (this with
    {
        SortMode = Enum.IsDefined(sortMode) ? sortMode : throw new ArgumentOutOfRangeException(nameof(sortMode)),
    }).EnsureValid();

    public FolderPortalTab WithSearch(string? query)
    {
        string normalized = query?.Trim() ?? string.Empty;
        if (normalized.Length > MaximumSearchLength)
        {
            throw new ArgumentOutOfRangeException(nameof(query), $"Portal search cannot exceed {MaximumSearchLength} characters.");
        }

        return (this with { SearchQuery = normalized }).EnsureValid();
    }

    public FolderPortalTab EnsureValid()
    {
        _ = NormalizeFolderPath(CurrentPath);
        if (!Enum.IsDefined(ViewMode) || !Enum.IsDefined(SortMode))
        {
            throw new InvalidDataException("The portal view or sort mode is invalid.");
        }

        if (SearchQuery.Length > MaximumSearchLength || BackHistory.Length > MaximumHistoryEntries || ForwardHistory.Length > MaximumHistoryEntries)
        {
            throw new InvalidDataException("The portal tab state exceeds its supported limits.");
        }

        foreach (string path in BackHistory.Concat(ForwardHistory))
        {
            _ = NormalizeFolderPath(path);
        }

        return this;
    }

    private static string NormalizeFolderPath(string path)
    {
        string normalized = ContainerDefinition.NormalizeRequiredText(path, ItemReference.MaximumTargetLength, nameof(path));
        if (!Path.IsPathFullyQualified(normalized))
        {
            throw new ArgumentException("Portal targets must use a fully qualified folder path.", nameof(path));
        }

        string fullPath = Path.GetFullPath(normalized);
        string? root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

public sealed record PortalItem
{
    public string FullPath { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public PortalItemKind Kind { get; init; }

    public long SizeBytes { get; init; }

    public DateTimeOffset ModifiedAt { get; init; }

    public string TypeLabel { get; init; } = string.Empty;

    public bool IsHidden { get; init; }
}

public sealed record PortalLoadResult
{
    public PortalTargetState State { get; init; }

    public string Message { get; init; } = string.Empty;

    public PortalItem[] Items { get; init; } = [];

    public TimeSpan Elapsed { get; init; }
}

public sealed record PortalPreview
{
    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? TextContent { get; init; }

    public string? ImagePath { get; init; }
}

public sealed record RuleCondition
{
    public string Field { get; init; } = string.Empty;

    public RuleOperator Operator { get; init; }

    public string Value { get; init; } = string.Empty;
}

public sealed record RuleAction
{
    public RuleActionKind Kind { get; init; }

    public Guid? ContainerId { get; init; }

    public string? DestinationPath { get; init; }

    public bool RequiresConfirmation { get; init; } = true;
}

public sealed record RuleDefinition
{
    public int SchemaVersion { get; init; } = ContractSchema.CurrentVersion;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = string.Empty;

    public bool IsEnabled { get; init; }

    public int Priority { get; init; }

    public RuleCondition[] Conditions { get; init; } = [];

    public RuleAction Action { get; init; } = new();
}

public sealed record PlannedAction
{
    public ItemReference Item { get; init; } = new();

    public RuleAction Action { get; init; } = new();

    public string Reason { get; init; } = string.Empty;
}

public sealed record ActionPlan
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool RequiresUserConfirmation { get; init; } = true;

    public PlannedAction[] Actions { get; init; } = [];
}

public sealed record FileOperation
{
    public FileOperationKind Kind { get; init; }

    public string SourcePath { get; init; } = string.Empty;

    public string? DestinationPath { get; init; }

    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.RequireDecision;
}

public sealed record FileTransaction
{
    public int SchemaVersion { get; init; } = ContractSchema.CurrentVersion;

    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public FileTransactionState State { get; init; } = FileTransactionState.Planned;

    public FileOperation[] Operations { get; init; } = [];
}

public sealed record LayoutSnapshot
{
    public int SchemaVersion { get; init; } = ContractSchema.CurrentVersion;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string DisplayFingerprint { get; init; } = string.Empty;

    public ContainerDefinition[] Containers { get; init; } = [];
}

public sealed record WindowPlacement
{
    public string ApplicationId { get; init; } = string.Empty;

    public string DisplayId { get; init; } = string.Empty;

    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }
}

public sealed record WorkspaceProfile
{
    public int SchemaVersion { get; init; } = ContractSchema.CurrentVersion;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = string.Empty;

    public WindowPlacement[] Windows { get; init; } = [];
}

public sealed record WidgetInstance
{
    public int SchemaVersion { get; init; } = ContractSchema.CurrentVersion;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string WidgetType { get; init; } = string.Empty;

    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.Ordinal);
}

public sealed record ExportBundle
{
    public int SchemaVersion { get; init; } = ContractSchema.CurrentVersion;

    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.UtcNow;

    public ContainerDefinition[] Containers { get; init; } = [];

    public FolderPortal[] Portals { get; init; } = [];

    public RuleDefinition[] Rules { get; init; } = [];

    public LayoutSnapshot[] Layouts { get; init; } = [];

    public WorkspaceProfile[] Workspaces { get; init; } = [];

    public WidgetInstance[] Widgets { get; init; } = [];
}
