using CommunityToolkit.Mvvm.ComponentModel;

namespace TheUnhingedProtocol.App.ViewModels;

/// <summary>
/// Read-only Phase 0 status exposed by the foundation shell.
/// </summary>
public sealed class MainPageViewModel : ObservableObject
{
    public MainPageViewModel()
    {
        ProductName = "The Unhinged Protocol";
        CurrentPhase = "Phase 0 — Foundation and Specification";
        Summary = "This shell validates the approved Windows architecture without implementing later-phase product behavior.";
    }

    public string ProductName { get; }

    public string CurrentPhase { get; }

    public string Summary { get; }

    public IReadOnlyList<string> StatusItems { get; } =
    [
        ".NET 10 and WinUI 3 application boundary",
        "Versioned domain and persistence contracts",
        "Safety-first architecture and strict phase governance",
        "x64 and ARM64 build targets",
    ];
}
