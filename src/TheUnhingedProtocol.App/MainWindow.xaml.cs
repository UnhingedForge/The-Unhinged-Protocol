using Microsoft.UI.Xaml;

namespace TheUnhingedProtocol.App;

/// <summary>
/// Hosts the Phase 0 foundation status page.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RootFrame.Navigate(typeof(MainPage));
    }
}
