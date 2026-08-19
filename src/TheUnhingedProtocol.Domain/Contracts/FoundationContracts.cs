namespace TheUnhingedProtocol.Domain.Contracts;

public static class ContractSchema
{
    public const int CurrentVersion = 1;

    public static void EnsureSupported(int version)
    {
        if (version != CurrentVersion)
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

public enum PortalViewMode
{
    Grid,
    List,
    Details,
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
    public int SchemaVersion { get; init; } = ContractSchema.CurrentVersion;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = string.Empty;

    public ContainerKind Kind { get; init; } = ContainerKind.ReferenceGroup;

    public bool IsLocked { get; init; }

    public double Opacity { get; init; } = 1.0;
}

public sealed record ItemReference
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string CanonicalPath { get; init; } = string.Empty;

    public ItemKind Kind { get; init; }

    public bool AllowPhysicalMove { get; init; }
}

public sealed record FolderPortal
{
    public int SchemaVersion { get; init; } = ContractSchema.CurrentVersion;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string FolderPath { get; init; } = string.Empty;

    public PortalViewMode ViewMode { get; init; } = PortalViewMode.Grid;

    public string[] TabPaths { get; init; } = [];
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
