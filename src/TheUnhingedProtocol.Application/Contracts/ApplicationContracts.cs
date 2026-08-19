using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.Application.Contracts;

public interface IContainerService
{
    public Task<IReadOnlyList<ContainerDefinition>> GetAllAsync(CancellationToken cancellationToken);
}

public interface ILayoutService
{
    public Task<IReadOnlyList<LayoutSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken);
}

public interface IShellCatalogService
{
    public Task<IReadOnlyList<ItemReference>> ScanAsync(CancellationToken cancellationToken);
}

public interface IFileTransactionService
{
    public Task<FileTransaction> ExecuteAsync(ActionPlan approvedPlan, CancellationToken cancellationToken);

    public Task<FileTransaction> RollBackAsync(Guid transactionId, CancellationToken cancellationToken);
}

public interface IRuleEngine
{
    public Task<ActionPlan> PreviewAsync(
        RuleDefinition rule,
        IReadOnlyList<ItemReference> items,
        CancellationToken cancellationToken);
}

public interface ISearchService
{
    public Task<IReadOnlyList<ItemReference>> SearchAsync(string query, CancellationToken cancellationToken);
}

public interface IWorkspaceService
{
    public Task<IReadOnlyList<WorkspaceProfile>> GetAllAsync(CancellationToken cancellationToken);
}

public interface IWidgetHost
{
    public Task<IReadOnlyList<WidgetInstance>> GetEnabledAsync(CancellationToken cancellationToken);
}

public interface IAiClassificationService
{
    public Task<ActionPlan> SuggestAsync(
        IReadOnlyList<ItemReference> items,
        CancellationToken cancellationToken);
}

public interface ISynchronizationService
{
    public Task<ExportBundle> ExportAsync(CancellationToken cancellationToken);
}

public interface IDiagnosticsService
{
    public void RecordLocalEvent(string eventName, IReadOnlyDictionary<string, string> redactedProperties);
}

public interface IUpdateService
{
    public Task<bool> IsUpdateAvailableAsync(CancellationToken cancellationToken);
}
