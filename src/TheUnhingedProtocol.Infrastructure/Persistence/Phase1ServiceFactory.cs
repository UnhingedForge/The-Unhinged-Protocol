using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Infrastructure.Search;
using TheUnhingedProtocol.Infrastructure.Shell;
using TheUnhingedProtocol.Infrastructure.Windows;

namespace TheUnhingedProtocol.Infrastructure.Persistence;

public static class Phase1ServiceFactory
{
    public static IOrganizerPreferencesService CreatePreferences() =>
        new JsonOrganizerPreferencesService(RuntimePaths.Preferences);

    public static ILayoutSnapshotService CreateLayouts() =>
        new SqliteLayoutSnapshotService(RuntimePaths.Database);

    public static IDisplayEnvironmentService CreateDisplayEnvironment() =>
        new WindowsDisplayEnvironmentService();

    public static IUnifiedSearchService CreateSearch(IWindowsSearchAdapter windowsSearchAdapter) =>
        new UnifiedSearchService(windowsSearchAdapter);

    public static IOnboardingService CreateOnboarding() =>
        new NonDestructiveOnboardingService(RuntimePaths.Database);
}
