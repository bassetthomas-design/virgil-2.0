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

    public static void ObserveForbiddenWingetCommands(ConcurrentQueue<string> violations)
    {
        foreach (var process in Process.GetProcessesByName("winget"))
        using (process)
        {
            try
            {
                using var handle = OpenProcess(0x1000, false, process.Id);
                if (handle.IsInvalid) { if (!process.HasExited) violations.Enqueue("Lecture de la commande WinGet refusée."); continue; }
                _ = NtQueryInformationProcess(handle, 60, IntPtr.Zero, 0, out var required);
                if (required <= 0 || required > 131072) { if (!process.HasExited) violations.Enqueue("Taille de la commande WinGet indisponible."); continue; }
                var buffer = Marshal.AllocHGlobal(required);
                try
                {
                    var status = NtQueryInformationProcess(handle, 60, buffer, required, out _);
                    if (status != 0) { if (!process.HasExited) violations.Enqueue("Lecture de la commande WinGet impossible."); continue; }
                    var value = Marshal.PtrToStructure<UnicodeCommandLine>(buffer);
                    var offset = value.Buffer.ToInt64() - buffer.ToInt64();
                    if (offset < Marshal.SizeOf<UnicodeCommandLine>() || offset + value.Length > required || value.Length % 2 != 0)
                    { violations.Enqueue("Commande WinGet invalide."); continue; }
                    var command = Marshal.PtrToStringUni(value.Buffer, value.Length / 2) ?? "";
                    if (!WingetSmokeSafety.IsReadOnly(command))
                        violations.Enqueue("Commande WinGet interdite observée : " + command);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            catch (InvalidOperationException) { /* Le processus peut se terminer entre les deux lectures. */ }
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeCommandLine
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(Microsoft.Win32.SafeHandles.SafeProcessHandle process, int informationClass,
        IntPtr information, int length, out int returnLength);

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
