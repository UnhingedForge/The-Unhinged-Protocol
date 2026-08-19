using Microsoft.UI.Xaml;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App;

/// <summary>
/// Hosts the active Phase 1 desktop-organizer surface.
/// </summary>
public sealed partial class MainWindow : Window, IDisposable
{
    private readonly CancellationTokenSource shellMonitorCancellation = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;
    private nint lastShellWindow;
    private DateTimeOffset shellLossDetectedAt;

    public MainWindow()
    {
        InitializeComponent();
        dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        FocusController = new WindowsFocusController(
            WinRT.Interop.WindowNative.GetWindowHandle(this),
            dispatcherQueue);
        lastShellWindow = GetShellWindow();
        _ = MonitorShellAsync(shellMonitorCancellation.Token);
        Closed += OnClosed;
        RootFrame.Navigate(typeof(MainPage));
    }

    public WindowsFocusController FocusController { get; }

    public event EventHandler<TimeSpan>? ExplorerRecovered;

    public string ApplyFocusPreferences(OrganizerPreferences preferences) =>
        FocusController.ApplyPreferences(preferences);

    public bool TogglePeek() => FocusController.TogglePeek();

    private async Task MonitorShellAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                nint current = GetShellWindow();
                if (lastShellWindow != 0 && current == 0)
                {
                    shellLossDetectedAt = DateTimeOffset.UtcNow;
                }
                else if (lastShellWindow == 0 && current != 0 && shellLossDetectedAt != default)
                {
                    TimeSpan elapsed = DateTimeOffset.UtcNow - shellLossDetectedAt;
                    shellLossDetectedAt = default;
                    dispatcherQueue.TryEnqueue(() => ExplorerRecovered?.Invoke(this, elapsed));
                }

                lastShellWindow = current;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Dispose();
    }

    public void Dispose()
    {
        shellMonitorCancellation.Cancel();
        shellMonitorCancellation.Dispose();
        FocusController.Dispose();
        GC.SuppressFinalize(this);
    }

#pragma warning disable SYSLIB1054
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetShellWindow();
#pragma warning restore SYSLIB1054
}
