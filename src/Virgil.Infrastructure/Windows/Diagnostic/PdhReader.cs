using System.Runtime.InteropServices;
using System.Text;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Diagnostic;

namespace Virgil.Infrastructure.Windows.Diagnostic;

/// <summary>Une requête de lecture PDH, noms anglais indépendants de la langue de Windows.</summary>
internal sealed class PdhReader : IDisposable
{
    private IntPtr _query;
    private readonly Dictionary<string, IntPtr> _counters = new(StringComparer.OrdinalIgnoreCase);
    internal PdhReader()
    {
        if (PdhOpenQuery(null, UIntPtr.Zero, out _query) != 0) _query = IntPtr.Zero;
    }
    internal IReadOnlyList<string> Expand(string path)
    {
        if (_query == IntPtr.Zero) return Array.Empty<string>();
        // PdhExpandWildCardPath attend des noms localisés. Localiser les objets anglais via leurs index.
        var localized = LocalizePath(path);
        uint length = 0;
        var status = PdhExpandWildCardPath(null, localized, null, ref length, 0);
        if (length == 0 || status != 0x800007D2) return Array.Empty<string>();
        var buffer = new char[length];
        if (PdhExpandWildCardPath(null, localized, buffer, ref length, 0) != 0) return Array.Empty<string>();
        var paths = new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in paths) Add(item, false);
        return paths;
    }
    internal void Add(string path, bool english = true)
    {
        if (_query == IntPtr.Zero || _counters.ContainsKey(path)) return;
        var status = english ? PdhAddEnglishCounter(_query, path, UIntPtr.Zero, out var handle)
            : PdhAddCounter(_query, path, UIntPtr.Zero, out handle);
        if (status == 0) _counters[path] = handle;
    }
    internal void Collect()
    {
        if (_query != IntPtr.Zero) PdhCollectQueryData(_query);
    }
    internal DiagnosticValue<double> Gauge(string path)
    {
        if (!_counters.TryGetValue(path, out var handle)) return Missing();
        var status = PdhGetFormattedCounterValue(handle, 0x00000200, out _, out var value);
        return status == 0 && value.Status is 0 or 1 && double.IsFinite(value.Value)
            ? DiagnosticValue<double>.Available(value.Value) : DiagnosticValue<double>.Unavailable("Lecture du compteur Windows en échec");
    }
    internal DiagnosticCounter Counter(string path, DiagnosticCounterKind kind)
    {
        if (!_counters.TryGetValue(path, out var handle)) return new(Missing(), Missing(), kind);
        if (PdhGetRawCounterValue(handle, out _, out var raw) != 0 || raw.Status > 1)
            return new(DiagnosticValue<double>.Unavailable("Lecture du compteur Windows en échec"), Missing(), kind);
        PdhGetCounterTimeBase(handle, out var frequency);
        return new(DiagnosticValue<double>.Available(raw.First), DiagnosticValue<double>.Available(raw.Second), kind,
            frequency > 0 ? frequency : 1);
    }
    internal static string Instance(string path)
    {
        var start = path.IndexOf('(');
        var end = path.LastIndexOf(')');
        return start >= 0 && end > start ? path[(start + 1)..end] : path;
    }
    private static DiagnosticValue<double> Missing() => DiagnosticValue<double>.Unmeasured("Indisponible : compteur non exposé par Windows");
    private static string LocalizePath(string path)
    {
        // Résoudre un compteur anglais en handle puis demander son chemin localisé à PDH.
        if (PdhOpenQuery(null, UIntPtr.Zero, out var query) != 0) return path;
        try
        {
            if (PdhAddEnglishCounter(query, path, UIntPtr.Zero, out var counter) != 0) return path;
            uint size = 0;
            PdhGetCounterInfo(counter, false, ref size, IntPtr.Zero);
            if (size == 0) return path;
            var memory = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetCounterInfo(counter, false, ref size, memory) != 0) return path;
                return Marshal.PtrToStringUni(Marshal.PtrToStructure<CounterInfo>(memory).FullPath) ?? path;
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        finally { PdhCloseQuery(query); }
    }
    public void Dispose()
    {
        if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterInfo
    {
        public uint Length, Type, Version, Status;
        public int Scale, DefaultScale;
        public UIntPtr UserData, QueryUserData;
        public IntPtr FullPath;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct RawValue
    {
        public uint Status;
        public System.Runtime.InteropServices.ComTypes.FILETIME Timestamp;
        public long First, Second;
        public uint Multiple;
    }
    [StructLayout(LayoutKind.Explicit)]
    private struct FormattedValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, UIntPtr data, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, UIntPtr data, out IntPtr counter);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddCounter(IntPtr query, string path, UIntPtr data, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetRawCounterValue(IntPtr counter, out uint type, out RawValue value);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out FormattedValue value);
    [DllImport("pdh.dll")] private static extern uint PdhGetCounterTimeBase(IntPtr counter, out long frequency);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhExpandWildCardPath(string? source, string path, [Out] char[]? buffer, ref uint length, uint flags);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetCounterInfo(IntPtr counter, [MarshalAs(UnmanagedType.Bool)] bool explain, ref uint size, IntPtr info);
}
