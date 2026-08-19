using System.Xml.Linq;

namespace TheUnhingedProtocol.Architecture.Tests;

public sealed class ProjectBoundaryTests
{
    [Fact]
    public void SourceProjectsFollowTheApprovedDependencyDirection()
    {
        string root = FindRepositoryRoot();

        AssertProjectReferences(root, "src/TheUnhingedProtocol.Domain/TheUnhingedProtocol.Domain.csproj", []);
        AssertProjectReferences(
            root,
            "src/TheUnhingedProtocol.Application/TheUnhingedProtocol.Application.csproj",
            ["src/TheUnhingedProtocol.Domain/TheUnhingedProtocol.Domain.csproj"]);
        AssertProjectReferences(
            root,
            "src/TheUnhingedProtocol.Infrastructure/TheUnhingedProtocol.Infrastructure.csproj",
            [
                "src/TheUnhingedProtocol.Application/TheUnhingedProtocol.Application.csproj",
                "src/TheUnhingedProtocol.Domain/TheUnhingedProtocol.Domain.csproj",
            ]);
        AssertProjectReferences(
            root,
            "src/TheUnhingedProtocol.Presentation/TheUnhingedProtocol.Presentation.csproj",
            [
                "src/TheUnhingedProtocol.Application/TheUnhingedProtocol.Application.csproj",
                "src/TheUnhingedProtocol.Domain/TheUnhingedProtocol.Domain.csproj",
            ]);
        AssertProjectReferences(
            root,
            "src/TheUnhingedProtocol.App/TheUnhingedProtocol.App.csproj",
            [
                "src/TheUnhingedProtocol.Domain/TheUnhingedProtocol.Domain.csproj",
                "src/TheUnhingedProtocol.Infrastructure/TheUnhingedProtocol.Infrastructure.csproj",
                "src/TheUnhingedProtocol.Presentation/TheUnhingedProtocol.Presentation.csproj",
            ]);
    }

    [Fact]
    public void DomainProjectHasNoPackageDependencies()
    {
        string root = FindRepositoryRoot();
        string projectPath = Path.Combine(
            root,
            "src",
            "TheUnhingedProtocol.Domain",
            "TheUnhingedProtocol.Domain.csproj");
        XDocument project = XDocument.Load(projectPath);

        Assert.Empty(project.Descendants("PackageReference"));
    }

    [Fact]
    public void ContainerSurfaceHonorsNativeAccessibilityAndThemeContracts()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "TheUnhingedProtocol.App", "MainPage.xaml"));
        string theme = File.ReadAllText(Path.Combine(
            root,
            "src",
            "TheUnhingedProtocol.App",
            "Styles",
            "FoundationTheme.xaml"));

        Assert.Contains("ThemeResource", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpText", xaml, StringComparison.Ordinal);
        Assert.Contains("TitleTextBlockStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("BodyTextBlockStyle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard", theme, StringComparison.Ordinal);
        Assert.Contains("Unified local search", xaml, StringComparison.Ordinal);
        Assert.Contains("Guided onboarding", xaml, StringComparison.Ordinal);
        Assert.Contains("Global organizer visibility", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PhaseOneOperationsRemainLocalAndDoNotIntroducePhaseTwoAuthority()
    {
        string root = FindRepositoryRoot();
        string search = File.ReadAllText(Path.Combine(
            root,
            "src",
            "TheUnhingedProtocol.Infrastructure",
            "Search",
            "UnifiedSearchService.cs"));
        string onboarding = File.ReadAllText(Path.Combine(
            root,
            "src",
            "TheUnhingedProtocol.Infrastructure",
            "Shell",
            "NonDestructiveOnboardingService.cs"));

        Assert.DoesNotContain("HttpClient", search, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", onboarding, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Move", onboarding, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Delete", onboarding, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Move", onboarding, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Delete", onboarding, StringComparison.Ordinal);
    }

    private static void AssertProjectReferences(
        string root,
        string projectRelativePath,
        IReadOnlyCollection<string> expectedRelativePaths)
    {
        string projectPath = Path.GetFullPath(Path.Combine(root, projectRelativePath));
        XDocument project = XDocument.Load(projectPath);
        string projectDirectory = Path.GetDirectoryName(projectPath)!;

        string[] actual = project.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(projectDirectory, value!))))
            .Select(NormalizePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected = expectedRelativePaths
            .Select(NormalizePath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TheUnhingedProtocol.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("The repository root could not be located.");
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
