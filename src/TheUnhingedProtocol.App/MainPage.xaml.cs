using Microsoft.UI.Xaml.Controls;
using TheUnhingedProtocol.App.ViewModels;

namespace TheUnhingedProtocol.App;

/// <summary>
/// The main content page displayed inside the application window.
/// </summary>
public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
    }
}
