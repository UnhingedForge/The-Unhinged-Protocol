using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App;

#pragma warning disable SYSLIB1054, CA1838
public sealed class WindowsFocusController : IDisposable
{
    private const int VisibilityHotKeyId = 0x5510;
    private const int PeekHotKeyId = 0x5511;
    private const uint WmHotKey = 0x0312;
    private const int WhMouseLowLevel = 14;
    private const int WmLeftButtonDown = 0x0201;
    private const int GaParent = 1;
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008L;

    private readonly nint windowHandle;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly SubclassProc subclassProc;
    private readonly LowLevelMouseProc mouseProc;
    private nint mouseHook;
    private OrganizerPreferences preferences = new();
    private DateTimeOffset lastDesktopClick;
    private NativePoint lastDesktopPoint;
    private bool isPeekActive;
    private bool previousTopmost;
    private bool previousVisible;
    private bool previousMinimized;

    public WindowsFocusController(nint windowHandle, DispatcherQueue dispatcherQueue)
    {
        this.windowHandle = windowHandle;
        this.dispatcherQueue = dispatcherQueue;
        subclassProc = WindowSubclass;
        mouseProc = MouseHook;
        if (!SetWindowSubclass(windowHandle, subclassProc, 1, 0))
        {
            throw new InvalidOperationException("The Phase 1 hotkey message route could not be installed.");
        }
    }

    public event EventHandler? ToggleVisibilityRequested;

    public event EventHandler? TogglePeekRequested;

    public string ApplyPreferences(OrganizerPreferences updated)
    {
        preferences = updated.EnsureValid();
        UnregisterHotKey(windowHandle, VisibilityHotKeyId);
        UnregisterHotKey(windowHandle, PeekHotKeyId);
        List<string> conflicts = [];
        if (preferences.VisibilityHotKey.IsEnabled &&
            !RegisterHotKey(windowHandle, VisibilityHotKeyId, ToNativeModifiers(preferences.VisibilityHotKey.Modifiers), (uint)preferences.VisibilityHotKey.VirtualKey))
        {
            conflicts.Add($"Visibility shortcut {preferences.VisibilityHotKey}");
        }

        if (preferences.PeekHotKey.IsEnabled &&
            !RegisterHotKey(windowHandle, PeekHotKeyId, ToNativeModifiers(preferences.PeekHotKey.Modifiers), (uint)preferences.PeekHotKey.VirtualKey))
        {
            conflicts.Add($"Peek shortcut {preferences.PeekHotKey}");
        }

        ConfigureDesktopGesture(preferences.DesktopGesture != DesktopGestureAction.Disabled);
        return conflicts.Count == 0
            ? "Global shortcuts are active."
            : $"Conflict detected: {string.Join("; ", conflicts)}. Reassign or disable the conflicting shortcut.";
    }

    public bool TogglePeek()
    {
        if (!isPeekActive)
        {
            previousVisible = IsWindowVisible(windowHandle);
            previousMinimized = IsIconic(windowHandle);
            previousTopmost = (GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64() & WsExTopmost) != 0;
            ShowWindow(windowHandle, SwRestore);
            _ = SetWindowPos(windowHandle, new nint(-1), 0, 0, 0, 0, SwpNoMove | SwpNoSize);
            _ = SetForegroundWindow(windowHandle);
            isPeekActive = true;
            return true;
        }

        _ = SetWindowPos(windowHandle, previousTopmost ? new nint(-1) : new nint(-2), 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
        if (previousMinimized) ShowWindow(windowHandle, SwMinimize);
        else if (!previousVisible) ShowWindow(windowHandle, SwHide);
        else ShowWindow(windowHandle, SwShow);
        isPeekActive = false;
        return false;
    }

    public void Dispose()
    {
        UnregisterHotKey(windowHandle, VisibilityHotKeyId);
        UnregisterHotKey(windowHandle, PeekHotKeyId);
        ConfigureDesktopGesture(false);
        _ = RemoveWindowSubclass(windowHandle, subclassProc, 1);
    }

    private nint WindowSubclass(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WmHotKey)
        {
            if (wParam == VisibilityHotKeyId) ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);
            if (wParam == PeekHotKeyId) TogglePeekRequested?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void ConfigureDesktopGesture(bool enabled)
    {
        if (enabled && mouseHook == 0)
        {
            mouseHook = SetWindowsHookEx(WhMouseLowLevel, mouseProc, 0, 0);
        }
        else if (!enabled && mouseHook != 0)
        {
            _ = UnhookWindowsHookEx(mouseHook);
            mouseHook = 0;
        }
    }

    private nint MouseHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && wParam == WmLeftButtonDown)
        {
            MouseHookData data = Marshal.PtrToStructure<MouseHookData>(lParam);
            if (IsEmptyDesktop(data.Point))
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                int dx = Math.Abs(data.Point.X - lastDesktopPoint.X);
                int dy = Math.Abs(data.Point.Y - lastDesktopPoint.Y);
                if ((now - lastDesktopClick).TotalMilliseconds <= GetDoubleClickTime() && dx <= 4 && dy <= 4)
                {
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        if (preferences.DesktopGesture == DesktopGestureAction.ToggleOrganizerVisibility)
                            ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);
                        else if (preferences.DesktopGesture == DesktopGestureAction.TogglePeek)
                            TogglePeekRequested?.Invoke(this, EventArgs.Empty);
                    });
                    lastDesktopClick = DateTimeOffset.MinValue;
                }
                else
                {
                    lastDesktopClick = now;
                    lastDesktopPoint = data.Point;
                }
            }
        }

        return CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    private static bool IsEmptyDesktop(NativePoint point)
    {
        nint window = WindowFromPoint(point);
        for (int depth = 0; depth < 5 && window != 0; depth++)
        {
            StringBuilder className = new(128);
            int length = GetClassName(window, className, className.Capacity);
            string name = length > 0 ? className.ToString() : string.Empty;
            if (name is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32") return true;
            window = GetAncestor(window, GaParent);
        }

        return false;
    }

    private static uint ToNativeModifiers(HotKeyModifiers modifiers)
    {
        const uint noRepeat = 0x4000;
        uint native = noRepeat;
        if (modifiers.HasFlag(HotKeyModifiers.Alt)) native |= 0x0001;
        if (modifiers.HasFlag(HotKeyModifiers.Control)) native |= 0x0002;
        if (modifiers.HasFlag(HotKeyModifiers.Shift)) native |= 0x0004;
        if (modifiers.HasFlag(HotKeyModifiers.Windows)) native |= 0x0008;
        return native;
    }

    private delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data);
    private delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hook, LowLevelMouseProc callback, nint module, uint threadId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int count);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
#pragma warning restore SYSLIB1054, CA1838
