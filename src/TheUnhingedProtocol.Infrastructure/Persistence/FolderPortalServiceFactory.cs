using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Infrastructure.Shell;

namespace TheUnhingedProtocol.Infrastructure.Persistence;

public static class FolderPortalServiceFactory
{
    public static IFolderPortalService CreatePersistenceForCurrentUser() =>
        new SqliteFolderPortalService(RuntimePaths.Database);

    public static IFolderBrowserService CreateBrowser() => new FileSystemFolderBrowser();

}
