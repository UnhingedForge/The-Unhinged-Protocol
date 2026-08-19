using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TheUnhingedProtocol.App;

public static class RuntimePerformanceManager
{
    public static void TrimAfterStartup()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: true, compacting: false);
        using Process process = Process.GetCurrentProcess();
        _ = SetProcessWorkingSetSize(process.Handle, new nint(-1), new nint(-1));
    }

#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(nint process, nint minimum, nint maximum);
#pragma warning restore SYSLIB1054
}
