using System.Collections.Specialized;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using TheUnhingedProtocol.App.ViewModels;
using TheUnhingedProtocol.Domain.Contracts;
using TheUnhingedProtocol.Infrastructure.Persistence;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace TheUnhingedProtocol.App;

/// <summary>
/// The main content page displayed inside the application window.
/// </summary>
public sealed partial class MainPage : Page, IDisposable
{
    private Point? drawStart;
    private CancellationTokenSource? searchCancellation;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? startupTrimTimer;

    public MainPageViewModel ViewModel { get; } = new(
        ContainerServiceFactory.CreateForCurrentUser(),
        FolderPortalServiceFactory.CreatePersistenceForCurrentUser(),
        FolderPortalServiceFactory.CreateBrowser());

    public Phase1ToolsViewModel Tools { get; } = new(
        Phase1ServiceFactory.CreatePreferences(),
        Phase1ServiceFactory.CreateDisplayEnvironment(),
        Phase1ServiceFactory.CreateLayouts(),
        Phase1ServiceFactory.CreateSearch(new WindowsSearchAdapter()),
        Phase1ServiceFactory.CreateOnboarding());

    public MainPage()
    {
        InitializeComponent();
        ViewModel.Containers.CollectionChanged += OnContainersChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void Dispose()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = null;
        if (App.Window is MainWindow window)
        {
            window.FocusController.ToggleVisibilityRequested -= OnToggleVisibilityRequested;
            window.FocusController.TogglePeekRequested -= OnTogglePeekRequested;
            window.ExplorerRecovered -= OnExplorerRecovered;
        }
        GC.SuppressFinalize(this);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Dispose();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        await Tools.InitializeAsync();
        if (App.Window is MainWindow window)
        {
            window.FocusController.ToggleVisibilityRequested += OnToggleVisibilityRequested;
            window.FocusController.TogglePeekRequested += OnTogglePeekRequested;
            window.ExplorerRecovered += OnExplorerRecovered;
            string hotKeyStatus = window.ApplyFocusPreferences(Tools.Preferences);
            Tools.ReportRecovery($"{Tools.DisplaySummary}. {hotKeyStatus}");
        }
        QueueCanvasPositionRefresh(ViewModel.Containers);
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => GlobalVisibilityToggle.Focus(FocusState.Programmatic));
        startupTrimTimer ??= DispatcherQueue.CreateTimer();
        startupTrimTimer.Interval = TimeSpan.FromSeconds(5);
        startupTrimTimer.IsRepeating = false;
        startupTrimTimer.Tick += OnStartupTrimTimerTick;
        startupTrimTimer.Start();
    }

    private void OnStartupTrimTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        sender.Tick -= OnStartupTrimTimerTick;
        RuntimePerformanceManager.TrimAfterStartup();
    }

    private async void OnToggleVisibilityRequested(object? sender, EventArgs e) =>
        await SetGlobalVisibilityAsync(!Tools.IsOrganizerVisible);

    private void OnTogglePeekRequested(object? sender, EventArgs e)
    {
        if (App.Window is MainWindow window)
        {
            bool active = window.TogglePeek();
            Tools.ReportRecovery(active
                ? "Peek is active above open windows where Windows permits. Toggle Peek again to restore the exact prior window state."
                : "Peek ended and the organizer window returned to its prior visibility, minimized, and topmost state.");
        }
    }

    private async void OnExplorerRecovered(object? sender, TimeSpan elapsed)
    {
        await Tools.RefreshDisplayAsync();
        Tools.ReportRecovery($"Explorer recovery completed in {elapsed.TotalSeconds:0.0} seconds. Organizer state remained intact.");
    }

    private async void GlobalVisibility_Click(object sender, RoutedEventArgs e)
    {
        bool desired = (sender as ToggleButton)?.IsChecked == true;
        await SetGlobalVisibilityAsync(desired);
    }

    private async Task SetGlobalVisibilityAsync(bool isVisible)
    {
        try
        {
            await Tools.SetGlobalVisibilityAsync(isVisible);
            GlobalVisibilityToggle.IsChecked = isVisible;
        }
        catch (Exception ex)
        {
            Tools.ReportRecovery($"Visibility could not be changed: {ex.Message}");
        }
    }

    private async void FocusControls_Click(object sender, RoutedEventArgs e)
    {
        CheckBox globalVisibility = new()
        {
            Content = "Show organizer surfaces globally",
            IsChecked = Tools.IsOrganizerVisible,
        };
        ComboBox gesture = CreateEnumPicker("Empty-desktop double-click (opt-in)", Tools.Preferences.DesktopGesture);
        TextBlock gestureWarning = new()
        {
            Text = "The gesture activates only on an empty Explorer desktop with a plain left-button double-click. It may conflict with another desktop utility; leave it disabled if that occurs.",
            TextWrapping = TextWrapping.Wrap,
        };
        ComboBox visibilityHotKey = CreateHotKeyPicker("Visibility shortcut", Tools.Preferences.VisibilityHotKey, isPeek: false);
        ComboBox peekHotKey = CreateHotKeyPicker("Peek shortcut", Tools.Preferences.PeekHotKey, isPeek: true);
        StackPanel containerChoices = new() { Spacing = 4 };
        List<(ContainerCardViewModel Container, CheckBox Choice)> containerVisibility = [];
        foreach (ContainerCardViewModel container in ViewModel.Containers)
        {
            CheckBox choice = new() { Content = container.Name, IsChecked = container.IsVisible };
            containerChoices.Children.Add(choice);
            containerVisibility.Add((container, choice));
        }

        List<(FolderPortalViewModel Portal, CheckBox Choice)> portalVisibility = [];
        foreach (FolderPortalViewModel portal in ViewModel.Portals)
        {
            CheckBox choice = new() { Content = $"{portal.Name} (portal)", IsChecked = portal.IsVisible };
            containerChoices.Children.Add(choice);
            portalVisibility.Add((portal, choice));
        }

        StackPanel content = new() { MinWidth = 480, MaxWidth = 640, Spacing = 10 };
        content.Children.Add(globalVisibility);
        content.Children.Add(gesture);
        content.Children.Add(gestureWarning);
        content.Children.Add(visibilityHotKey);
        content.Children.Add(peekHotKey);
        content.Children.Add(new TextBlock { Text = "Individual surfaces", Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["BodyStrongTextBlockStyle"] });
        content.Children.Add(containerChoices);
        ContentDialog dialog = CreateEditDialog("Focus controls", new ScrollViewer { MaxHeight = 600, Content = content });
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            OrganizerPreferences updated = Tools.Preferences with
            {
                IsOrganizerVisible = globalVisibility.IsChecked == true,
                DesktopGesture = (DesktopGestureAction)((ComboBoxItem)gesture.SelectedItem).Tag,
                VisibilityHotKey = (HotKeyGesture)((ComboBoxItem)visibilityHotKey.SelectedItem).Tag,
                PeekHotKey = (HotKeyGesture)((ComboBoxItem)peekHotKey.SelectedItem).Tag,
            };
            await Tools.SavePreferencesAsync(updated);
            foreach ((ContainerCardViewModel container, CheckBox choice) in containerVisibility)
            {
                if (container.IsVisible != (choice.IsChecked == true))
                    await ViewModel.SetContainerVisibilityAsync(container, choice.IsChecked == true);
            }
            foreach ((FolderPortalViewModel portal, CheckBox choice) in portalVisibility)
            {
                if (portal.IsVisible != (choice.IsChecked == true))
                    await portal.SetVisibilityAsync(choice.IsChecked == true);
            }
            string hotKeyStatus = App.Window is MainWindow window
                ? window.ApplyFocusPreferences(Tools.Preferences)
                : "The shortcuts will activate on next launch.";
            Tools.ReportRecovery(hotKeyStatus);
            GlobalVisibilityToggle.IsChecked = Tools.IsOrganizerVisible;
        }
        catch (Exception ex)
        {
            Tools.ReportRecovery($"Focus controls were not applied: {ex.Message}");
        }
    }

    private async void Layouts_Click(object sender, RoutedEventArgs e)
    {
        TextBox name = new() { Header = "New snapshot name", Text = $"Layout {DateTimeOffset.Now:g}", MaxLength = 80 };
        ListView snapshots = new()
        {
            Header = "Snapshot history",
            Height = 240,
            ItemsSource = Tools.Snapshots,
            DisplayMemberPath = "Name",
            SelectionMode = ListViewSelectionMode.Single,
        };
        if (Tools.Snapshots.Count > 0) snapshots.SelectedIndex = 0;
        TextBlock result = new() { Text = "Select a snapshot to compare, restore, or export.", TextWrapping = TextWrapping.Wrap };
        Button compare = new() { Content = "Compare" };
        Button restore = new() { Content = "Preview and restore" };
        Button export = new() { Content = "Export" };
        Button import = new() { Content = "Import" };
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(compare); actions.Children.Add(restore); actions.Children.Add(export); actions.Children.Add(import);
        StackPanel content = new() { MinWidth = 560, Spacing = 10 };
        content.Children.Add(name); content.Children.Add(snapshots); content.Children.Add(actions); content.Children.Add(result);
        ContentDialog dialog = CreateEditDialog("Layout snapshots", content);
        dialog.PrimaryButtonText = "Create snapshot";

        compare.Click += async (_, _) =>
        {
            if (snapshots.SelectedItem is LayoutArchive selected)
            {
                LayoutDifference difference = await Tools.CompareSnapshotAsync(selected.Id);
                result.Text = difference.Summary;
            }
        };
        restore.Click += async (_, _) =>
        {
            if (snapshots.SelectedItem is not LayoutArchive selected) return;
            if (restore.Tag is not Guid confirmedId || confirmedId != selected.Id)
            {
                LayoutDifference difference = await Tools.CompareSnapshotAsync(selected.Id);
                result.Text = $"PREVIEW — {difference.Summary} A recovery snapshot will be created first. No underlying file will change. Select Confirm restore to proceed.";
                restore.Content = "Confirm restore";
                restore.Tag = selected.Id;
                return;
            }
            await Tools.RestoreSnapshotAsync(selected.Id);
            await ViewModel.ReloadAsync();
            QueueCanvasPositionRefresh(ViewModel.Containers);
            result.Text = Tools.StatusMessage;
            restore.Content = "Preview and restore";
            restore.Tag = null;
        };
        export.Click += async (_, _) =>
        {
            if (snapshots.SelectedItem is not LayoutArchive selected) return;
            FileSavePicker picker = new() { SuggestedFileName = $"{SanitizeFileName(selected.Name)}.tup-layout.json" };
            picker.FileTypeChoices.Add("The Unhinged Protocol layout", [".json"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is not null)
            {
                await Tools.ExportSnapshotAsync(selected.Id, file.Path);
                result.Text = "Export completed with the snapshot checksum intact.";
            }
        };
        import.Click += async (_, _) =>
        {
            FileOpenPicker picker = CreateFilePicker(".json");
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                await Tools.ImportSnapshotAsync(file.Path);
                snapshots.ItemsSource = null;
                snapshots.ItemsSource = Tools.Snapshots;
                result.Text = Tools.StatusMessage;
            }
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await Tools.CreateSnapshotAsync(name.Text, LayoutSnapshotKind.Manual);
        }
    }

    private async void UnifiedSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = new CancellationTokenSource();
        try
        {
            await Tools.SearchAsync(
                args.QueryText,
                ViewModel.Containers.Select(container => container.CaptureDefinition()).ToArray(),
                ViewModel.Portals.Select(portal => portal.CaptureDefinition()).ToArray(),
                searchCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        ListView results = new()
        {
            Height = 420,
            ItemsSource = Tools.SearchResults,
            DisplayMemberPath = "Title",
            SelectionMode = ListViewSelectionMode.Single,
        };
        if (Tools.SearchResults.Count > 0) results.SelectedIndex = 0;
        StackPanel content = new() { MinWidth = 600, Spacing = 8 };
        content.Children.Add(new TextBlock { Text = Tools.WindowsSearchStatus, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(results);
        ContentDialog dialog = CreateEditDialog($"Search results for “{args.QueryText}”", content);
        dialog.PrimaryButtonText = "Open selected";
        dialog.IsPrimaryButtonEnabled = Tools.SearchResults.Count > 0;
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && results.SelectedItem is SearchResult selected)
        {
            await OpenSearchResultAsync(selected);
        }
    }

    private async Task OpenSearchResultAsync(SearchResult result)
    {
        if (result.Target?.StartsWith("settings:", StringComparison.Ordinal) == true)
        {
            if (result.Target == "settings:layouts") Layouts_Click(this, new RoutedEventArgs());
            else if (result.Target == "settings:onboarding") Onboarding_Click(this, new RoutedEventArgs());
            else FocusControls_Click(this, new RoutedEventArgs());
            return;
        }
        if (!string.IsNullOrWhiteSpace(result.Target))
        {
            try { Process.Start(new ProcessStartInfo(result.Target) { UseShellExecute = true }); }
            catch (Exception ex) { Tools.ReportRecovery($"Search result could not be opened: {ex.Message}"); }
            return;
        }
        Tools.ReportRecovery($"{result.Title} is available in the current organizer surface.");
        await Task.CompletedTask;
    }

    private async void Onboarding_Click(object sender, RoutedEventArgs e)
    {
        using CancellationTokenSource cancellation = new();
        CheckBox consent = new()
        {
            Content = "I consent to a one-time metadata-only scan of the selected Desktop location.",
            IsChecked = false,
        };
        TextBlock disclosure = new()
        {
            Text = "The scan reads only visible top-level names, paths, item type, size, and modified date to prepare suggestions. It does not read file contents, move, rename, overwrite, delete, upload, or keep scanning in the background.",
            TextWrapping = TextWrapping.Wrap,
        };
        TextBox location = new()
        {
            Header = "Desktop location",
            Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        Button scan = new() { Content = "Scan and preview" };
        StackPanel suggestions = new() { Spacing = 4 };
        TextBlock state = new() { Text = "Consent is required before scanning.", TextWrapping = TextWrapping.Wrap };
        StackPanel content = new() { MinWidth = 600, Spacing = 10 };
        content.Children.Add(disclosure); content.Children.Add(consent); content.Children.Add(location); content.Children.Add(scan); content.Children.Add(state); content.Children.Add(suggestions);
        ContentDialog dialog = CreateEditDialog("Guided organization", new ScrollViewer { MaxHeight = 640, Content = content });
        dialog.PrimaryButtonText = "Create accepted references";
        dialog.IsPrimaryButtonEnabled = false;
        dialog.Closed += (_, _) => cancellation.Cancel();
        scan.Click += async (_, _) =>
        {
            OnboardingScanResult result = await Tools.ScanDesktopAsync(location.Text, consent.IsChecked == true, cancellation.Token);
            state.Text = result.Message;
            suggestions.Children.Clear();
            foreach (OnboardingSuggestionViewModel suggestion in Tools.OnboardingSuggestions)
            {
                CheckBox choice = new() { Content = suggestion.Summary, IsChecked = suggestion.IsAccepted, DataContext = suggestion };
                choice.Checked += (_, _) => suggestion.IsAccepted = true;
                choice.Unchecked += (_, _) => suggestion.IsAccepted = false;
                suggestions.Children.Add(choice);
            }
            dialog.IsPrimaryButtonEnabled = result.State is OnboardingScanState.Ready or OnboardingScanState.OneDriveRedirected or OnboardingScanState.LargeDesktop;
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            IReadOnlyList<ContainerDefinition> created = await Tools.ApplyOnboardingAsync();
            if (created.Count > 0)
            {
                await ViewModel.ReloadAsync();
                QueueCanvasPositionRefresh(ViewModel.Containers);
            }
        }
    }

    private async void RefreshDisplay_Click(object sender, RoutedEventArgs e)
    {
        await Tools.RefreshDisplayAsync();
        Tools.ReportRecovery($"{Tools.DisplaySummary} {Tools.VirtualDesktopStatus}");
    }

    private void OnContainersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            QueueCanvasPositionRefresh(e.NewItems.OfType<ContainerCardViewModel>());
        }
    }

    private void QueueCanvasPositionRefresh(IEnumerable<ContainerCardViewModel> containers)
    {
        ContainerCardViewModel[] pendingContainers = containers.ToArray();
        DispatcherQueue.TryEnqueue(() =>
        {
            WorkspaceItems.UpdateLayout();
            foreach (ContainerCardViewModel container in pendingContainers)
            {
                ApplyCanvasPosition(container);
            }
        });
    }

    private async void NewPortal_Click(object sender, RoutedEventArgs e)
    {
        StorageFolder? folder = await PickFolderAsync();
        if (folder is not null)
        {
            await ViewModel.CreatePortalAsync($"Folder portal {ViewModel.Portals.Count + 1}", folder.Path);
        }
    }

    private async void AddPortalTab_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPortal(sender, out FolderPortalViewModel portal))
        {
            return;
        }

        StorageFolder? folder = await PickFolderAsync();
        if (folder is not null)
        {
            await RunPortalActionAsync(() => portal.AddTabAsync(folder.Path));
        }
    }

    private async void ChangePortalTarget_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPortal(sender, out FolderPortalViewModel portal))
        {
            return;
        }

        StorageFolder? folder = await PickFolderAsync();
        if (folder is not null)
        {
            await RunPortalActionAsync(() => portal.ChangeTargetAsync(folder.Path));
        }
    }

    private async void PortalTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PortalTabViewModel tab &&
            TryFindPortal(tab.Id, out FolderPortalViewModel portal) &&
            portal.ActiveTab.Id != tab.Id)
        {
            await RunPortalActionAsync(() => portal.SelectTabAsync(tab.Id));
        }
    }

    private async void ClosePortalTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PortalTabViewModel tab &&
            TryFindPortal(tab.Id, out FolderPortalViewModel portal))
        {
            await RunPortalActionAsync(() => portal.CloseTabAsync(tab.Id));
        }
    }

    private async void PortalBack_Click(object sender, RoutedEventArgs e) =>
        await RunPortalSenderActionAsync(sender, portal => portal.GoBackAsync());

    private async void PortalForward_Click(object sender, RoutedEventArgs e) =>
        await RunPortalSenderActionAsync(sender, portal => portal.GoForwardAsync());

    private async void PortalUp_Click(object sender, RoutedEventArgs e) =>
        await RunPortalSenderActionAsync(sender, portal => portal.GoUpAsync());

    private async void PortalRefresh_Click(object sender, RoutedEventArgs e) =>
        await RunPortalSenderActionAsync(sender, portal => portal.RefreshAsync());

    private async void PortalAddress_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && sender is TextBox textBox && TryGetPortal(sender, out FolderPortalViewModel portal))
        {
            e.Handled = true;
            await RunPortalActionAsync(() => portal.NavigateAsync(textBox.Text));
        }
    }

    private async void PortalSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (TryGetPortal(sender, out FolderPortalViewModel portal))
        {
            await RunPortalActionAsync(() => portal.SetSearchAsync(args.QueryText));
        }
    }

    private async void PortalView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox &&
            TryGetPortal(sender, out FolderPortalViewModel portal) &&
            comboBox.SelectedItem is ComboBoxItem selected &&
            Enum.TryParse(selected.Tag?.ToString(), out PortalViewMode viewMode) &&
            viewMode != portal.ActiveTab.ViewMode)
        {
            await RunPortalActionAsync(() => portal.SetViewModeAsync(viewMode));
        }
    }

    private async void PortalSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox &&
            TryGetPortal(sender, out FolderPortalViewModel portal) &&
            comboBox.SelectedItem is ComboBoxItem selected &&
            Enum.TryParse(selected.Tag?.ToString(), out PortalSortMode sortMode) &&
            sortMode != portal.ActiveTab.SortMode)
        {
            await RunPortalActionAsync(() => portal.SetSortModeAsync(sortMode));
        }
    }

    private async void PortalItem_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PortalItemViewModel item || !TryGetPortal(sender, out FolderPortalViewModel portal))
        {
            return;
        }

        if (item.Kind == PortalItemKind.Folder)
        {
            await RunPortalActionAsync(() => portal.NavigateAsync(item.FullPath));
        }
        else
        {
            OpenPortalItem(item);
        }
    }

    private void OpenPortalItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetPortalItem(sender, out PortalItemViewModel item))
        {
            if (item.Kind == PortalItemKind.Folder && TryFindPortalForItem(item, out FolderPortalViewModel portal))
            {
                _ = RunPortalActionAsync(() => portal.NavigateAsync(item.FullPath));
            }
            else
            {
                OpenPortalItem(item);
            }
        }
    }

    private async void PreviewPortalItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPortalItem(sender, out PortalItemViewModel item) || !TryFindPortalForItem(item, out FolderPortalViewModel portal))
        {
            return;
        }

        try
        {
            PortalPreview preview = await portal.GetPreviewAsync(item);
            StackPanel content = new() { MinWidth = 520, MaxWidth = 720, MaxHeight = 520, Spacing = 10 };
            content.Children.Add(new TextBlock { Text = preview.Description, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(preview.TextContent))
            {
                content.Children.Add(new ScrollViewer
                {
                    MaxHeight = 400,
                    Content = new TextBlock
                    {
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        IsTextSelectionEnabled = true,
                        Text = preview.TextContent,
                        TextWrapping = TextWrapping.Wrap,
                    },
                });
            }
            else if (!string.IsNullOrWhiteSpace(preview.ImagePath))
            {
                content.Children.Add(new Image
                {
                    MaxHeight = 400,
                    Source = new BitmapImage(new Uri(preview.ImagePath)),
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                });
            }

            ContentDialog dialog = new()
            {
                XamlRoot = XamlRoot,
                Title = preview.Title,
                Content = content,
                CloseButtonText = "Close",
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            ViewModel.ReportNonDestructiveError(ex.Message);
        }
    }

    private void ShowPortalItemLocation_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetPortalItem(sender, out PortalItemViewModel item))
        {
            try
            {
                ProcessStartInfo explorer = new("explorer.exe") { UseShellExecute = true };
                explorer.ArgumentList.Add("/select,");
                explorer.ArgumentList.Add(item.FullPath);
                Process.Start(explorer);
            }
            catch (Exception ex)
            {
                ViewModel.ReportNonDestructiveError(ex.Message);
            }
        }
    }

    private async void CopyPortalItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPortalItem(sender, out PortalItemViewModel item))
        {
            return;
        }

        try
        {
            IStorageItem storageItem = item.Kind == PortalItemKind.Folder
                ? await StorageFolder.GetFolderFromPathAsync(item.FullPath)
                : await StorageFile.GetFileFromPathAsync(item.FullPath);
            DataPackage package = new() { RequestedOperation = DataPackageOperation.Copy };
            package.SetStorageItems([storageItem], readOnly: true);
            package.SetText(item.FullPath);
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            ViewModel.ReportNonDestructiveError(ex.Message);
        }
    }

    private void CopyPortalPath_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetPortalItem(sender, out PortalItemViewModel item))
        {
            DataPackage package = new();
            package.SetText(item.FullPath);
            Clipboard.SetContent(package);
        }
    }

    private void OpenPortalItem(PortalItemViewModel item)
    {
        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ViewModel.ReportNonDestructiveError(ex.Message);
        }
    }

    private async Task RunPortalSenderActionAsync(object sender, Func<FolderPortalViewModel, Task> action)
    {
        if (TryGetPortal(sender, out FolderPortalViewModel portal))
        {
            await RunPortalActionAsync(() => action(portal));
        }
    }

    private async Task RunPortalActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportNonDestructiveError(ex.Message);
        }
    }

    private static bool TryGetPortal(object sender, out FolderPortalViewModel portal)
    {
        portal = (sender as FrameworkElement)?.DataContext as FolderPortalViewModel ?? null!;
        return portal is not null;
    }

    private bool TryFindPortal(Guid tabId, out FolderPortalViewModel portal)
    {
        portal = ViewModel.Portals.FirstOrDefault(candidate => candidate.Tabs.Any(tab => tab.Id == tabId))!;
        return portal is not null;
    }

    private bool TryFindPortalForItem(PortalItemViewModel item, out FolderPortalViewModel portal)
    {
        portal = ViewModel.Portals.FirstOrDefault(candidate =>
            candidate.ActiveTab.Items.Any(existing => string.Equals(existing.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase)))!;
        return portal is not null;
    }

    private static bool TryGetPortalItem(object sender, out PortalItemViewModel item)
    {
        item = (sender as FrameworkElement)?.DataContext as PortalItemViewModel ?? null!;
        return item is not null;
    }

    private static async Task<StorageFolder?> PickFolderAsync()
    {
        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        return await picker.PickSingleFolderAsync();
    }

    private async void CompactTemplate_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.CreateContainerAsync(ContainerTemplateKind.Compact);

    private async void StandardTemplate_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.CreateContainerAsync(ContainerTemplateKind.Standard);

    private async void WideTemplate_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.CreateContainerAsync(ContainerTemplateKind.Wide);

    private void WorkspaceBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (DrawContainerToggle.IsChecked != true)
        {
            return;
        }

        drawStart = e.GetCurrentPoint(WorkspaceItems).Position;
        DrawPreview.Visibility = Visibility.Visible;
        Canvas.SetLeft(DrawPreview, drawStart.Value.X);
        Canvas.SetTop(DrawPreview, drawStart.Value.Y);
        DrawPreview.Width = 1;
        DrawPreview.Height = 1;
        WorkspaceBorder.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void WorkspaceBorder_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (drawStart is null || DrawContainerToggle.IsChecked != true)
        {
            return;
        }

        Point current = e.GetCurrentPoint(WorkspaceItems).Position;
        UpdateDrawPreview(drawStart.Value, current);
        e.Handled = true;
    }

    private async void WorkspaceBorder_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (drawStart is null)
        {
            return;
        }

        Point current = e.GetCurrentPoint(WorkspaceItems).Position;
        Point start = drawStart.Value;
        drawStart = null;
        WorkspaceBorder.ReleasePointerCapture(e.Pointer);
        DrawPreview.Visibility = Visibility.Collapsed;
        DrawContainerToggle.IsChecked = false;
        e.Handled = true;

        double x = Math.Max(0, Math.Min(start.X, current.X));
        double y = Math.Max(0, Math.Min(start.Y, current.Y));
        double width = Math.Max(ContainerBounds.MinimumWidth, Math.Abs(current.X - start.X));
        double height = Math.Max(ContainerBounds.MinimumHeight, Math.Abs(current.Y - start.Y));
        width = Math.Min(width, Math.Max(ContainerBounds.MinimumWidth, WorkspaceItems.ActualWidth - x));
        height = Math.Min(height, Math.Max(ContainerBounds.MinimumHeight, WorkspaceItems.ActualHeight - y));

        try
        {
            await ViewModel.CreateContainerAsync(
                ContainerTemplateKind.Standard,
                ContainerBounds.Create(x, y, width, height));
        }
        catch (Exception ex)
        {
            ViewModel.ReportNonDestructiveError(ex.Message);
        }
    }

    private void UpdateDrawPreview(Point start, Point current)
    {
        Canvas.SetLeft(DrawPreview, Math.Min(start.X, current.X));
        Canvas.SetTop(DrawPreview, Math.Min(start.Y, current.Y));
        DrawPreview.Width = Math.Abs(current.X - start.X);
        DrawPreview.Height = Math.Abs(current.Y - start.Y);
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetContainer(sender, out ContainerCardViewModel container))
        {
            return;
        }

        FileOpenPicker picker = CreateFilePicker("*");
        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        foreach (StorageFile file in files)
        {
            await TryAddReferenceAsync(container, file.Path, ClassifyFile(file.Path));
        }
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetContainer(sender, out ContainerCardViewModel container))
        {
            return;
        }

        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            await TryAddReferenceAsync(container, folder.Path, ItemKind.Folder);
        }
    }

    private async void AddShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetContainer(sender, out ContainerCardViewModel container))
        {
            return;
        }

        FileOpenPicker picker = CreateFilePicker(".lnk", ".url");
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await TryAddReferenceAsync(container, file.Path, ItemKind.Shortcut);
        }
    }

    private async void AddApplication_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetContainer(sender, out ContainerCardViewModel container))
        {
            return;
        }

        FileOpenPicker picker = CreateFilePicker(".exe", ".com", ".bat");
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await TryAddReferenceAsync(container, file.Path, ItemKind.Application);
        }
    }

    private async void AddUrl_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetContainer(sender, out ContainerCardViewModel container))
        {
            return;
        }

        TextBox urlBox = new() { Header = "HTTP or HTTPS address", PlaceholderText = "https://example.com" };
        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = "Add URL reference",
            Content = urlBox,
            PrimaryButtonText = "Add reference",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await TryAddReferenceAsync(container, urlBox.Text, ItemKind.Url);
        }
    }

    private async void Container_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetContainer(sender, out ContainerCardViewModel container))
        {
            return;
        }

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            foreach (IStorageItem item in items)
            {
                ItemKind kind = item is StorageFolder ? ItemKind.Folder : ClassifyFile(item.Path);
                await TryAddReferenceAsync(container, item.Path, kind);
            }
        }
        else if (e.DataView.Contains(StandardDataFormats.WebLink))
        {
            Uri uri = await e.DataView.GetWebLinkAsync();
            await TryAddReferenceAsync(container, uri.AbsoluteUri, ItemKind.Url);
        }
    }

    private void Container_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems) ||
            e.DataView.Contains(StandardDataFormats.WebLink))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add reference only — originals stay in place";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private async Task TryAddReferenceAsync(ContainerCardViewModel container, string target, ItemKind kind)
    {
        try
        {
            await ViewModel.AddReferenceAsync(container, target, kind);
        }
        catch (Exception ex)
        {
            ViewModel.ReportNonDestructiveError(ex.Message);
        }
    }

    private async void EditContainer_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetContainer(sender, out ContainerCardViewModel container))
        {
            return;
        }

        TextBox nameBox = new() { Header = "Name", Text = container.Name, MaxLength = ContainerDefinition.MaximumNameLength };
        TextBox labelBox = new() { Header = "Label", Text = container.Label ?? string.Empty, MaxLength = ContainerDefinition.MaximumLabelLength };
        TextBox tagsBox = new() { Header = "Tags (comma separated)", Text = container.TagsText };
        ComboBox iconBox = CreateIconPicker(container.IconGlyph);
        StackPanel content = new() { Spacing = 10 };
        content.Children.Add(nameBox);
        content.Children.Add(labelBox);
        content.Children.Add(tagsBox);
        content.Children.Add(iconBox);
        ContentDialog dialog = CreateEditDialog("Edit container", content);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            string glyph = ((ComboBoxItem)iconBox.SelectedItem).Tag?.ToString() ?? container.IconGlyph;
            await ViewModel.UpdatePresentationAsync(container, nameBox.Text, labelBox.Text, tagsBox.Text, glyph);
        }
        catch (Exception ex)
        {
            ViewModel.ReportNonDestructiveError(ex.Message);
        }
    }

    private async void CompositionMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox ||
            !TryGetContainer(sender, out ContainerCardViewModel container) ||
            comboBox.SelectedItem is not ComboBoxItem selected ||
            !Enum.TryParse(selected.Tag?.ToString(), out ContainerCompositionMode mode) ||
            mode == container.CompositionMode)
        {
            return;
        }

        await RunContainerActionAsync(container, () => ViewModel.SetCompositionModeAsync(container, mode));
    }

    private async void AddSection_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetContainer(sender, out ContainerCardViewModel container) || !container.CanChangeLayout)
        {
            return;
        }

        TextBox nameBox = new()
        {
            Header = "Section name",
            Text = $"Section {container.Sections.Count + 1}",
            MaxLength = ContainerSectionDefinition.MaximumNameLength,
        };
        ContentDialog dialog = CreateEditDialog("Add container section", nameBox);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await RunContainerActionAsync(container, () => ViewModel.AddSectionAsync(container, nameBox.Text));
        }
    }

    private async void SelectSection_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSectionContext(sender, out ContainerCardViewModel container, out ContainerSectionViewModel section) &&
            container.ActiveSection.Id != section.Id)
        {
            await RunContainerActionAsync(container, () => ViewModel.SelectSectionAsync(container, section.Id));
        }
    }

    private async void ToggleStackSection_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSectionContext(sender, out ContainerCardViewModel container, out ContainerSectionViewModel section))
        {
            await RunContainerActionAsync(
                container,
                () => ViewModel.SetSectionExpandedAsync(container, section.Id, !section.IsExpanded));
        }
    }

    private async void PreviousPage_Click(object sender, RoutedEventArgs e) =>
        await MoveContainerPageAsync(sender, -1);

    private async void NextPage_Click(object sender, RoutedEventArgs e) =>
        await MoveContainerPageAsync(sender, 1);

    private async Task MoveContainerPageAsync(object sender, int direction)
    {
        if (!TryGetContainer(sender, out ContainerCardViewModel container))
        {
            return;
        }

        int currentIndex = container.Sections.IndexOf(container.ActiveSection);
        int nextIndex = Math.Clamp(currentIndex + direction, 0, container.Sections.Count - 1);
        if (nextIndex != currentIndex)
        {
            await RunContainerActionAsync(
                container,
                () => ViewModel.SelectSectionAsync(container, container.Sections[nextIndex].Id));
        }
    }

    private async void RenameSection_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSectionContext(sender, out ContainerCardViewModel container, out ContainerSectionViewModel section) ||
            !container.CanChangeLayout)
        {
            return;
        }

        TextBox nameBox = new()
        {
            Header = "Section name",
            Text = section.Name,
            MaxLength = ContainerSectionDefinition.MaximumNameLength,
        };
        ContentDialog dialog = CreateEditDialog("Rename container section", nameBox);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await RunContainerActionAsync(
                container,
                () => ViewModel.RenameSectionAsync(container, section.Id, nameBox.Text));
        }
    }

    private async void RemoveSection_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSectionContext(sender, out ContainerCardViewModel container, out ContainerSectionViewModel section) ||
            !container.CanChangeLayout)
        {
            return;
        }

        await RunContainerActionAsync(container, () => ViewModel.RemoveSectionAsync(container, section.Id));
    }

    private async void ExpandedState_Click(object sender, RoutedEventArgs e) =>
        await SetDisplayStateAsync(sender, ContainerDisplayState.Expanded);

    private async void RolledUpState_Click(object sender, RoutedEventArgs e) =>
        await SetDisplayStateAsync(sender, ContainerDisplayState.RolledUp);

    private async void CollapsedState_Click(object sender, RoutedEventArgs e) =>
        await SetDisplayStateAsync(sender, ContainerDisplayState.Collapsed);

    private async void CapsuleState_Click(object sender, RoutedEventArgs e) =>
        await SetDisplayStateAsync(sender, ContainerDisplayState.Capsule);

    private async Task SetDisplayStateAsync(object sender, ContainerDisplayState state)
    {
        if (TryGetContainer(sender, out ContainerCardViewModel container))
        {
            await RunContainerActionAsync(container, () => ViewModel.SetDisplayStateAsync(container, state));
        }
    }

    private async void PinContainer_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetContainer(sender, out ContainerCardViewModel container) && container.CanChangeLayout)
        {
            await RunContainerActionAsync(
                container,
                () => ViewModel.SetLayoutOptionsAsync(container, !container.IsPinned, container.IsLocked, container.IsAutoSize));
        }
    }

    private async void AutoSizeContainer_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetContainer(sender, out ContainerCardViewModel container) && container.CanChangeLayout)
        {
            await RunContainerActionAsync(
                container,
                () => ViewModel.SetLayoutOptionsAsync(container, container.IsPinned, container.IsLocked, !container.IsAutoSize));
        }
    }

    private async void LockContainer_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetContainer(sender, out ContainerCardViewModel container))
        {
            await RunContainerActionAsync(
                container,
                () => ViewModel.SetLayoutOptionsAsync(container, container.IsPinned, !container.IsLocked, container.IsAutoSize));
        }
    }

    private async void ContainerVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetContainer(sender, out ContainerCardViewModel container))
        {
            await RunContainerActionAsync(
                container,
                () => ViewModel.SetContainerVisibilityAsync(container, !container.IsVisible));
        }
    }

    private async void SnapGrid8_Click(object sender, RoutedEventArgs e) => await SetSnapGridAsync(sender, 8);

    private async void SnapGrid16_Click(object sender, RoutedEventArgs e) => await SetSnapGridAsync(sender, 16);

    private async void SnapGrid24_Click(object sender, RoutedEventArgs e) => await SetSnapGridAsync(sender, 24);

    private async void SnapGrid32_Click(object sender, RoutedEventArgs e) => await SetSnapGridAsync(sender, 32);

    private async Task SetSnapGridAsync(object sender, double gridSize)
    {
        if (TryGetContainer(sender, out ContainerCardViewModel container) && container.CanChangeLayout)
        {
            await RunContainerActionAsync(container, () => ViewModel.SetSnapGridAsync(container, gridSize));
        }
    }

    private async void Appearance_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetContainer(sender, out ContainerCardViewModel container))
        {
            return;
        }

        Slider opacitySlider = new()
        {
            Header = "Opacity",
            Minimum = 60,
            Maximum = 100,
            StepFrequency = 5,
            Value = container.OpacityPercent,
        };
        ComboBox colorBox = CreateEnumPicker("Approved color", container.Color);
        ComboBox iconTreatmentBox = CreateEnumPicker("Icon treatment", container.IconTreatment);
        ComboBox backgroundBox = CreateEnumPicker("Background", container.BackgroundStyle);
        TextBlock backgroundNotice = new()
        {
            Text = "Only built-in system and subtle color treatments are available. Artwork backgrounds require separate owner approval.",
            TextWrapping = TextWrapping.Wrap,
        };
        StackPanel content = new() { MinWidth = 360, Spacing = 10 };
        content.Children.Add(opacitySlider);
        content.Children.Add(colorBox);
        content.Children.Add(iconTreatmentBox);
        content.Children.Add(backgroundBox);
        content.Children.Add(backgroundNotice);
        ContentDialog dialog = CreateEditDialog("Container appearance", content);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ContainerColor color = (ContainerColor)((ComboBoxItem)colorBox.SelectedItem).Tag;
        ContainerIconTreatment iconTreatment = (ContainerIconTreatment)((ComboBoxItem)iconTreatmentBox.SelectedItem).Tag;
        ContainerBackgroundStyle background = (ContainerBackgroundStyle)((ComboBoxItem)backgroundBox.SelectedItem).Tag;
        await RunContainerActionAsync(
            container,
            () => ViewModel.UpdateAppearanceAsync(container, opacitySlider.Value / 100, color, iconTreatment, background));
    }

    private async Task RunContainerActionAsync(ContainerCardViewModel container, Func<Task> action)
    {
        try
        {
            await action();
            ApplyCanvasPosition(container);
        }
        catch (Exception ex)
        {
            ViewModel.ReportNonDestructiveError(ex.Message);
        }
    }

    private async void EditReference_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetReferenceContext(sender, out ContainerCardViewModel container, out ItemCardViewModel item))
        {
            return;
        }

        TextBox nameBox = new() { Header = "Display name", Text = item.DisplayName, MaxLength = ItemReference.MaximumDisplayNameLength };
        TextBox labelBox = new() { Header = "Label", Text = item.Label ?? string.Empty, MaxLength = ContainerDefinition.MaximumLabelLength };
        TextBox tagsBox = new() { Header = "Tags (comma separated)", Text = item.TagsText };
        ComboBox iconBox = CreateIconPicker(item.Reference.IconGlyph);
        CheckBox thumbnailBox = new() { Content = "Show image thumbnail when available", IsChecked = item.Reference.ShowThumbnail };
        StackPanel content = new() { Spacing = 10 };
        content.Children.Add(nameBox);
        content.Children.Add(labelBox);
        content.Children.Add(tagsBox);
        content.Children.Add(iconBox);
        content.Children.Add(thumbnailBox);
        ContentDialog dialog = CreateEditDialog("Edit reference", content);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            string? glyph = ((ComboBoxItem)iconBox.SelectedItem).Tag?.ToString();
            string[] tags = tagsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            ItemReference updated = item.Reference.WithMetadata(
                nameBox.Text,
                labelBox.Text,
                tags,
                glyph,
                thumbnailBox.IsChecked == true);
            await ViewModel.UpdateReferenceAsync(container, updated);
        }
        catch (Exception ex)
        {
            ViewModel.ReportNonDestructiveError(ex.Message);
        }
    }

    private async void RemoveReference_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetReferenceContext(sender, out ContainerCardViewModel container, out ItemCardViewModel item))
        {
            await ViewModel.RemoveReferenceAsync(container, item.Id);
        }
    }

    private async void MoveReferenceUp_Click(object sender, RoutedEventArgs e)
    {
        await MoveReferenceAsync(sender, -1);
    }

    private async void MoveReferenceDown_Click(object sender, RoutedEventArgs e)
    {
        await MoveReferenceAsync(sender, 1);
    }

    private async Task MoveReferenceAsync(object sender, int direction)
    {
        if (!TryGetReferenceContext(sender, out ContainerCardViewModel container, out ItemCardViewModel item))
        {
            return;
        }

        try
        {
            await ViewModel.MoveReferenceAsync(container, item.Id, direction);
        }
        catch (Exception ex)
        {
            ViewModel.ReportNonDestructiveError(ex.Message);
        }
    }

    private async void SortMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox ||
            comboBox.DataContext is not ContainerCardViewModel container ||
            comboBox.SelectedItem is not ComboBoxItem selected ||
            !Enum.TryParse(selected.Tag?.ToString(), out ContainerSortMode sortMode) ||
            sortMode == container.SortMode)
        {
            return;
        }

        await ViewModel.SetSortModeAsync(container, sortMode);
    }

    private async void Reference_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ItemCardViewModel item)
        {
            await OpenReferenceAsync(item);
        }
    }

    private async void OpenReference_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetReference(sender, out ItemCardViewModel item))
        {
            await OpenReferenceAsync(item);
        }
    }

    private Task OpenReferenceAsync(ItemCardViewModel item)
    {
        try
        {
            if (!item.IsAvailable)
            {
                throw new FileNotFoundException("The referenced item is currently unavailable.", item.Target);
            }

            Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ViewModel.ReportNonDestructiveError(ex.Message);
        }

        return Task.CompletedTask;
    }

    private void ShowReferenceLocation_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetReference(sender, out ItemCardViewModel item))
        {
            return;
        }

        try
        {
            if (!item.IsAvailable || item.Reference.Kind == ItemKind.Url)
            {
                throw new InvalidOperationException("This reference does not have an available Windows folder location.");
            }

            if (item.Reference.Kind == ItemKind.Folder)
            {
                Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
            }
            else
            {
                ProcessStartInfo explorer = new("explorer.exe") { UseShellExecute = true };
                explorer.ArgumentList.Add("/select,");
                explorer.ArgumentList.Add(item.Target);
                Process.Start(explorer);
            }
        }
        catch (Exception ex)
        {
            ViewModel.ReportNonDestructiveError(ex.Message);
        }
    }

    private void CopyReferenceTarget_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetReference(sender, out ItemCardViewModel item))
        {
            DataPackage package = new();
            package.SetText(item.Target);
            Clipboard.SetContent(package);
        }
    }

    private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (TryGetContainer(sender, out ContainerCardViewModel container))
        {
            MoveContainer(container, e.HorizontalChange, e.VerticalChange);
        }
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (TryGetContainer(sender, out ContainerCardViewModel container))
        {
            ResizeContainer(container, e.HorizontalChange, e.VerticalChange);
        }
    }

    private async void GeometryThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (TryGetContainer(sender, out ContainerCardViewModel container))
        {
            await SaveContainerGeometryAsync(container);
            ApplyCanvasPosition(container);
        }
    }

    private async void MoveKeyboardButton_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (TryGetContainer(sender, out ContainerCardViewModel container) && TryGetArrowDelta(e, out double horizontal, out double vertical))
        {
            MoveContainer(container, horizontal, vertical);
            await SaveContainerGeometryAsync(container);
            ApplyCanvasPosition(container);
        }
    }

    private async void ResizeKeyboardButton_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (TryGetContainer(sender, out ContainerCardViewModel container) && TryGetArrowDelta(e, out double horizontal, out double vertical))
        {
            ResizeContainer(container, horizontal, vertical);
            await SaveContainerGeometryAsync(container);
            ApplyCanvasPosition(container);
        }
    }

    private void MoveKeyboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetContainer(sender, out ContainerCardViewModel container))
        {
            ViewModel.ShowGeometryKeyboardHelp(container, isResize: false);
        }
    }

    private void ResizeKeyboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetContainer(sender, out ContainerCardViewModel container))
        {
            ViewModel.ShowGeometryKeyboardHelp(container, isResize: true);
        }
    }

    private static bool TryGetArrowDelta(KeyRoutedEventArgs e, out double horizontal, out double vertical)
    {
        const double keyboardStep = 8;
        (horizontal, vertical) = e.Key switch
        {
            VirtualKey.Left => (-keyboardStep, 0d),
            VirtualKey.Right => (keyboardStep, 0d),
            VirtualKey.Up => (0d, -keyboardStep),
            VirtualKey.Down => (0d, keyboardStep),
            _ => (0d, 0d),
        };
        e.Handled = horizontal != 0 || vertical != 0;
        return e.Handled;
    }

    private void MoveContainer(ContainerCardViewModel container, double horizontal, double vertical)
    {
        if (!container.CanChangeLayout)
        {
            return;
        }

        double maximumX = Math.Max(0, WorkspaceItems.ActualWidth - container.Width);
        double maximumY = Math.Max(0, WorkspaceItems.ActualHeight - container.Height);
        double x = Math.Clamp(container.X + horizontal, 0, maximumX);
        double y = Math.Clamp(container.Y + vertical, 0, maximumY);
        container.SetInteractiveBounds(ContainerBounds.Create(x, y, container.Width, container.BoundsHeight));
        ApplyCanvasPosition(container);
    }

    private void ResizeContainer(ContainerCardViewModel container, double horizontal, double vertical)
    {
        if (!container.CanResizeLayout)
        {
            return;
        }

        double maximumWidth = Math.Min(ContainerBounds.MaximumDimension, Math.Max(ContainerBounds.MinimumWidth, WorkspaceItems.ActualWidth - container.X));
        double maximumHeight = Math.Min(ContainerBounds.MaximumDimension, Math.Max(ContainerBounds.MinimumHeight, WorkspaceItems.ActualHeight - container.Y));
        double width = Math.Clamp(container.Width + horizontal, ContainerBounds.MinimumWidth, maximumWidth);
        double height = Math.Clamp(container.BoundsHeight + vertical, ContainerBounds.MinimumHeight, maximumHeight);
        container.SetInteractiveBounds(ContainerBounds.Create(container.X, container.Y, width, height));
    }

    private Task SaveContainerGeometryAsync(ContainerCardViewModel container) =>
        ViewModel.SaveContainerBoundsAsync(
            container,
            XamlRoot?.RasterizationScale ?? 1,
            Math.Max(ContainerBounds.MinimumWidth, WorkspaceItems.ActualWidth),
            Math.Max(ContainerBounds.MinimumHeight, WorkspaceItems.ActualHeight));

    private void ApplyCanvasPosition(ContainerCardViewModel container)
    {
        if (WorkspaceItems.ContainerFromItem(container) is ContentPresenter presenter)
        {
            Canvas.SetLeft(presenter, container.X);
            Canvas.SetTop(presenter, container.Y);
            Canvas.SetZIndex(presenter, container.ZIndex);
        }
    }

    private static FileOpenPicker CreateFilePicker(params string[] extensions)
    {
        FileOpenPicker picker = new();
        foreach (string extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        return picker;
    }

    private static ItemKind ClassifyFile(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            return ItemKind.Shortcut;
        }
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".com", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return ItemKind.Application;
        }
        return ItemKind.File;
    }

    private ContentDialog CreateEditDialog(string title, UIElement content) => new()
    {
        XamlRoot = XamlRoot,
        Title = title,
        Content = content,
        PrimaryButtonText = "Save",
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Primary,
    };

    private static ComboBox CreateIconPicker(string? selectedGlyph)
    {
        ComboBox comboBox = new() { Header = "Icon", HorizontalAlignment = HorizontalAlignment.Stretch };
        ComboBoxItem automatic = new() { Content = "Automatic", Tag = null };
        comboBox.Items.Add(automatic);
        string[] names = ["Folder", "Collection", "Workspace", "Favorite"];
        for (int index = 0; index < ContainerDefinition.ApprovedIconGlyphs.Length; index++)
        {
            string glyph = ContainerDefinition.ApprovedIconGlyphs[index];
            comboBox.Items.Add(new ComboBoxItem { Content = names[index], Tag = glyph });
        }
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selectedGlyph, StringComparison.Ordinal))
            ?? automatic;
        return comboBox;
    }

    private static ComboBox CreateEnumPicker<TEnum>(string header, TEnum selectedValue)
        where TEnum : struct, Enum
    {
        ComboBox comboBox = new()
        {
            Header = header,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        foreach (TEnum value in Enum.GetValues<TEnum>())
        {
            comboBox.Items.Add(new ComboBoxItem
            {
                Content = value.ToString(),
                Tag = value,
            });
        }

        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .Single(item => Equals(item.Tag, selectedValue));
        return comboBox;
    }

    private static ComboBox CreateHotKeyPicker(string header, HotKeyGesture selected, bool isPeek)
    {
        char key = isPeek ? 'P' : 'U';
        HotKeyGesture[] choices =
        [
            new() { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Alt, VirtualKey = key },
            new() { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, VirtualKey = key },
            new() { Modifiers = HotKeyModifiers.Alt | HotKeyModifiers.Shift, VirtualKey = key },
            new() { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Alt, VirtualKey = key, IsEnabled = false },
        ];
        ComboBox comboBox = new() { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (HotKeyGesture choice in choices)
        {
            comboBox.Items.Add(new ComboBoxItem { Content = choice.ToString(), Tag = choice });
        }
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, selected)) ?? comboBox.Items[0];
        return comboBox;
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '-' : character)).Trim();
    }

    private bool TryGetReferenceContext(
        object sender,
        out ContainerCardViewModel container,
        out ItemCardViewModel item)
    {
        if (TryGetReference(sender, out item))
        {
            Guid itemId = item.Id;
            ContainerCardViewModel? match = ViewModel.Containers.FirstOrDefault(candidate =>
                candidate.Items.Any(existing => existing.Id == itemId));
            if (match is not null)
            {
                container = match;
                return true;
            }
        }

        container = null!;
        return false;
    }

    private bool TryGetSectionContext(
        object sender,
        out ContainerCardViewModel container,
        out ContainerSectionViewModel section)
    {
        section = (sender as FrameworkElement)?.DataContext as ContainerSectionViewModel ?? null!;
        if (section is not null)
        {
            Guid sectionId = section.Id;
            ContainerCardViewModel? match = ViewModel.Containers.FirstOrDefault(candidate =>
                candidate.Sections.Any(existing => existing.Id == sectionId));
            if (match is not null)
            {
                container = match;
                return true;
            }
        }

        container = null!;
        return false;
    }

    private static bool TryGetReference(object sender, out ItemCardViewModel item)
    {
        item = (sender as FrameworkElement)?.DataContext as ItemCardViewModel ?? null!;
        return item is not null;
    }

    private static bool TryGetContainer(object sender, out ContainerCardViewModel container)
    {
        container = (sender as FrameworkElement)?.DataContext as ContainerCardViewModel ?? null!;
        return container is not null;
    }
}
