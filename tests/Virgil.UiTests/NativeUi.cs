using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Virgil.UiTests;

internal static class NativeUi
{
    public static double MeasureLatency(IntPtr handle)
    {
        var timer = Stopwatch.StartNew();
        // WM_NULL = 0, SMTO_ABORTIFHUNG = 2. A zero return is a failure even
        // if the OS returns early; GetLastWin32Error is not required to be set.
        var result = SendMessageTimeout(handle, 0, UIntPtr.Zero, IntPtr.Zero, 2, 500, out _);
        timer.Stop();
        if (result == IntPtr.Zero)
            throw new InvalidOperationException($"SendMessageTimeout WM_NULL a échoué après {timer.Elapsed.TotalMilliseconds:F0} ms.");
        return timer.Elapsed.TotalMilliseconds;
    }

    public static void ObserveForbiddenProcesses(ConcurrentQueue<string> violations)
    {
        foreach (var name in new[] { "Virgil.ElevatedHelper", "consent" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process) violations.Enqueue($"Processus interdit observé : {name} (PID {process.Id})");
        }
    }

    public static void ObserveErrorDialogs(int processId, ConcurrentQueue<string> violations)
    {
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var owner);
            if (owner != processId || !IsWindowVisible(handle)) return true;
            var title = new StringBuilder(256);
            var className = new StringBuilder(256);
            GetWindowText(handle, title, title.Capacity);
            GetClassName(handle, className, className.Capacity);
            if (title.ToString() == "Virgil" && className.ToString() == "#32770")
                violations.Enqueue("MessageBox Virgil observée pendant la session.");
            return true;
        }, IntPtr.Zero);
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr handle, uint message, UIntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out int processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder text, int maxCount);
}
