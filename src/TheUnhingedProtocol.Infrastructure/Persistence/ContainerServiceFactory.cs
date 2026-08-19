using TheUnhingedProtocol.Application.Contracts;

namespace TheUnhingedProtocol.Infrastructure.Persistence;

/// <summary>
/// Creates the local container catalog using the product runtime-data contract.
/// </summary>
public static class ContainerServiceFactory
{
    public static IContainerService CreateForCurrentUser()
    {
        return new SqliteContainerService(RuntimePaths.Database);
    }
}
