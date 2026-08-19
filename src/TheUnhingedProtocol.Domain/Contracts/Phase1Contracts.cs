using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheUnhingedProtocol.Domain.Contracts;

public enum DesktopGestureAction
{
    Disabled,
    ToggleOrganizerVisibility,
    TogglePeek,
}

[Flags]
public enum HotKeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

public sealed record HotKeyGesture
{
    public HotKeyModifiers Modifiers { get; init; } = HotKeyModifiers.Control | HotKeyModifiers.Alt;

    public int VirtualKey { get; init; } = 0x55;

    public bool IsEnabled { get; init; } = true;

    public HotKeyGesture EnsureValid()
    {
        if ((Modifiers & ~(HotKeyModifiers.Alt | HotKeyModifiers.Control | HotKeyModifiers.Shift | HotKeyModifiers.Windows)) != 0 ||
            VirtualKey is < 0x30 or > 0x7A)
        {
            throw new InvalidDataException("The global hotkey is outside the supported keyboard range.");
        }

        return this;
    }

    public override string ToString()
    {
        if (!IsEnabled)
        {
            return "Disabled";
        }

        List<string> parts = [];
        if (Modifiers.HasFlag(HotKeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotKeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotKeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotKeyModifiers.Windows)) parts.Add("Windows");
        parts.Add(((char)VirtualKey).ToString());
        return string.Join('+', parts);
    }
}

public sealed record OrganizerPreferences
{
    public int SchemaVersion { get; init; } = ContractSchema.CurrentVersion;

    public bool IsOrganizerVisible { get; init; } = true;

    public DesktopGestureAction DesktopGesture { get; init; }

    public HotKeyGesture VisibilityHotKey { get; init; } = new();

    public HotKeyGesture PeekHotKey { get; init; } = new()
    {
        Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Alt,
        VirtualKey = 0x50,
    };

    public bool AutomaticSnapshotsEnabled { get; init; } = true;

    public int AutomaticSnapshotLimit { get; init; } = 20;

    public OrganizerPreferences EnsureValid()
    {
        ContractSchema.EnsureSupported(SchemaVersion);
        VisibilityHotKey.EnsureValid();
        PeekHotKey.EnsureValid();
        if (VisibilityHotKey.IsEnabled && PeekHotKey.IsEnabled && VisibilityHotKey == PeekHotKey)
        {
            throw new InvalidDataException("Visibility and Peek cannot use the same global hotkey.");
        }

        if (!Enum.IsDefined(DesktopGesture) || AutomaticSnapshotLimit is < 1 or > 100)
        {
            throw new InvalidDataException("The organizer preference state is invalid.");
        }

        return this;
    }
}

public sealed record DisplayRectangle(double X, double Y, double Width, double Height)
{
    public DisplayRectangle EnsureValid()
    {
        if (!new[] { X, Y, Width, Height }.All(double.IsFinite) || Width < 1 || Height < 1)
        {
            throw new InvalidDataException("The display rectangle is invalid.");
        }

        return this;
    }
}

public sealed record DisplayDescriptor
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public DisplayRectangle Bounds { get; init; } = new(0, 0, 1920, 1080);

    public DisplayRectangle WorkArea { get; init; } = new(0, 0, 1920, 1040);

    public double Scale { get; init; } = 1;

    public bool IsPrimary { get; init; }

    public DisplayDescriptor EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Id) || !double.IsFinite(Scale) || Scale is < 1 or > 3)
        {
            throw new InvalidDataException("The display descriptor is invalid.");
        }

        Bounds.EnsureValid();
        WorkArea.EnsureValid();
        return this;
    }
}

public sealed record DisplayProfile
{
    public string Fingerprint { get; init; } = string.Empty;

    public DisplayDescriptor[] Displays { get; init; } = [];

    public bool IsRemoteSession { get; init; }

    public bool VirtualDesktopPlacementAvailable { get; init; }

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public static DisplayProfile Create(IEnumerable<DisplayDescriptor> displays, bool isRemoteSession)
    {
        DisplayDescriptor[] normalized = displays
            .Select(display => display.EnsureValid())
            .OrderBy(display => display.Id, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0 || normalized.Count(display => display.IsPrimary) != 1)
        {
            throw new InvalidDataException("A display profile requires exactly one primary display.");
        }

        string fingerprintInput = string.Join('|', normalized.Select(display =>
            $"{display.Id}:{display.Bounds.Width:0}x{display.Bounds.Height:0}@{display.Scale:0.##}"));
        return new DisplayProfile
        {
            Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput)))[..16],
            Displays = normalized,
            IsRemoteSession = isRemoteSession,
            VirtualDesktopPlacementAvailable = false,
        };
    }
}

public static class DisplayRecoveryPolicy
{
    public static ContainerBounds Recover(
        ContainerBounds savedBounds,
        DisplayRectangle previousWorkArea,
        DisplayRectangle currentWorkArea,
        double previousScale,
        double currentScale)
    {
        ArgumentNullException.ThrowIfNull(savedBounds);
        savedBounds.EnsureValid();
        previousWorkArea.EnsureValid();
        currentWorkArea.EnsureValid();
        if (previousScale is < 1 or > 3 || currentScale is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(previousScale));
        }

        double relativeX = previousWorkArea.Width <= savedBounds.Width
            ? 0
            : (savedBounds.X - previousWorkArea.X) / (previousWorkArea.Width - savedBounds.Width);
        double relativeY = previousWorkArea.Height <= savedBounds.Height
            ? 0
            : (savedBounds.Y - previousWorkArea.Y) / (previousWorkArea.Height - savedBounds.Height);
        double scaleRatio = previousScale / currentScale;
        double width = Math.Clamp(savedBounds.Width * scaleRatio, ContainerBounds.MinimumWidth,
            Math.Min(ContainerBounds.MaximumDimension, currentWorkArea.Width));
        double height = Math.Clamp(savedBounds.Height * scaleRatio, ContainerBounds.MinimumHeight,
            Math.Min(ContainerBounds.MaximumDimension, currentWorkArea.Height));
        double x = currentWorkArea.X + (Math.Clamp(relativeX, 0, 1) * Math.Max(0, currentWorkArea.Width - width));
        double y = currentWorkArea.Y + (Math.Clamp(relativeY, 0, 1) * Math.Max(0, currentWorkArea.Height - height));
        return ContainerBounds.Create(Math.Max(0, x), Math.Max(0, y), width, height);
    }
}

public enum LayoutSnapshotKind
{
    Manual,
    Automatic,
    Recovery,
}

public sealed record LayoutArchive
{
    public int SchemaVersion { get; init; } = ContractSchema.CurrentVersion;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = string.Empty;

    public LayoutSnapshotKind Kind { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DisplayProfile DisplayProfile { get; init; } = DisplayProfile.Create(
        [new DisplayDescriptor { Id = "primary", Name = "Primary", IsPrimary = true }], false);

    public ContainerDefinition[] Containers { get; init; } = [];

    public FolderPortal[] Portals { get; init; } = [];

    public string Checksum { get; init; } = string.Empty;

    public LayoutArchive WithChecksum()
    {
        LayoutArchive unsigned = this with { Checksum = string.Empty };
        string json = JsonSerializer.Serialize(unsigned, Phase1Json.Options);
        return this with { Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))) };
    }

    public LayoutArchive EnsureValid()
    {
        ContractSchema.EnsureSupported(SchemaVersion);
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 80 || !Enum.IsDefined(Kind))
        {
            throw new InvalidDataException("The layout snapshot identity is invalid.");
        }

        _ = DisplayProfile.Displays.Select(display => display.EnsureValid()).ToArray();
        _ = Containers.Select(container => container.UpgradeToCurrent()).ToArray();
        _ = Portals.Select(portal => portal.EnsureValid()).ToArray();
        string expected = WithChecksum().Checksum;
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(expected),
            Convert.FromHexString(Checksum)))
        {
            throw new InvalidDataException("The layout snapshot checksum does not match its contents.");
        }

        return this;
    }
}

public sealed record LayoutDifference
{
    public int AddedContainers { get; init; }
    public int RemovedContainers { get; init; }
    public int ChangedContainers { get; init; }
    public int AddedItems { get; init; }
    public int RemovedItems { get; init; }
    public bool DisplayProfileChanged { get; init; }

    public string Summary =>
        $"Containers +{AddedContainers}/-{RemovedContainers}/{ChangedContainers} changed; items +{AddedItems}/-{RemovedItems}; display {(DisplayProfileChanged ? "changed" : "unchanged")}.";
}

public enum SearchResultSource
{
    Container,
    Portal,
    DesktopItem,
    Application,
    Setting,
    Tag,
    WindowsSearch,
}

public enum SearchAvailability
{
    Ready,
    IndexUnavailable,
    PermissionDenied,
    Offline,
    Stale,
}

public sealed record SearchResult
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Target { get; init; }
    public SearchResultSource Source { get; init; }
    public int Score { get; init; }
    public string ActionLabel { get; init; } = "Open";
}

public sealed record SearchResponse
{
    public SearchResult[] Results { get; init; } = [];
    public SearchAvailability WindowsSearchState { get; init; }
    public string? WindowsSearchMessage { get; init; }
}

public enum OnboardingScanState
{
    Ready,
    ConsentRequired,
    DesktopUnavailable,
    PermissionDenied,
    OneDriveRedirected,
    LargeDesktop,
    Canceled,
}

public sealed record OnboardingCandidate
{
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public ItemKind Kind { get; init; }
    public long? Size { get; init; }
    public DateTimeOffset? ModifiedAt { get; init; }
}

public sealed record OnboardingSuggestion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Category { get; init; } = string.Empty;
    public OnboardingCandidate[] Candidates { get; init; } = [];
    public bool IsAccepted { get; init; } = true;
}

public sealed record OnboardingScanResult
{
    public OnboardingScanState State { get; init; }
    public string Message { get; init; } = string.Empty;
    public OnboardingSuggestion[] Suggestions { get; init; } = [];
}

internal static class Phase1Json
{
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
