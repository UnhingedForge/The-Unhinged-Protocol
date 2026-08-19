using System.ComponentModel;
using System.Runtime.InteropServices;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.Infrastructure.Windows;

#pragma warning disable SYSLIB1054
public sealed class WindowsDisplayEnvironmentService : IDisplayEnvironmentService
{
    private const int MonitorDefaultToPrimary = 1;
    private const int SmRemoteSession = 0x1000;

    public Task<DisplayProfile> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<DisplayDescriptor> displays = [];
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            MonitorInfoEx info = new() { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return true;
            }

            uint dpiX = 96;
            _ = GetDpiForMonitor(monitor, 0, out dpiX, out _);
            double scale = Math.Clamp(dpiX / 96d, 1, 3);
            displays.Add(new DisplayDescriptor
            {
                Id = info.DeviceName.TrimEnd('\0'),
                Name = info.DeviceName.TrimEnd('\0'),
                Bounds = ToRectangle(info.Monitor),
                WorkArea = ToRectangle(info.WorkArea),
                Scale = scale,
                IsPrimary = (info.Flags & 1) != 0,
            });
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (displays.Count == 0)
        {
            nint monitor = MonitorFromWindow(0, MonitorDefaultToPrimary);
            MonitorInfoEx info = new() { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            displays.Add(new DisplayDescriptor
            {
                Id = "primary",
                Name = "Primary display",
                Bounds = ToRectangle(info.Monitor),
                WorkArea = ToRectangle(info.WorkArea),
                IsPrimary = true,
            });
        }

        return Task.FromResult(DisplayProfile.Create(displays, GetSystemMetrics(SmRemoteSession) != 0));
    }

    private static DisplayRectangle ToRectangle(NativeRectangle rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rectangle, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, int flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
#pragma warning restore SYSLIB1054
