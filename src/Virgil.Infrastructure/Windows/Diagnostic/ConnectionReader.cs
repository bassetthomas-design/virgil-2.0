using System.Runtime.InteropServices;
using Virgil.Contracts.Diagnostic;

namespace Virgil.Infrastructure.Windows.Diagnostic;

internal static class ConnectionReader
{
    // TCP établi et sockets UDP liés, IPv4 et IPv6. UDP n'a pas d'état « connecté ».
    internal static DiagnosticValue<int> ForPid(int pid, IReadOnlyDictionary<int, int>? table) => table is null
        ? DiagnosticValue<int>.Unavailable("Indisponible : tables réseau Windows inaccessibles")
        : DiagnosticValue<int>.Available(table.GetValueOrDefault(pid));
    internal static IReadOnlyDictionary<int, int>? Read(CancellationToken token)
    {
        var counts = new Dictionary<int, int>();
        foreach (var family in new uint[] { 2, 23 })
        foreach (var tcp in new[] { true, false })
        {
            token.ThrowIfCancellationRequested();
            uint size = 0;
            uint ReadTable(IntPtr data) => tcp
                ? GetExtendedTcpTable(data, ref size, false, family, 5, 0)
                : GetExtendedUdpTable(data, ref size, false, family, 1, 0);
            var status = ReadTable(IntPtr.Zero);
            if (status != 122 || size < 4) return null;
            var memory = Marshal.AllocHGlobal((int)size);
            try
            {
                if (ReadTable(memory) != 0) return null;
                var length = Marshal.ReadInt32(memory);
                var stride = (tcp, family) switch { (true, 2) => 24, (true, 23) => 56, (false, 2) => 12, _ => 28 };
                if (length < 0 || (long)length * stride + 4 > size) return null;
                for (var i = 0; i < length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var row = IntPtr.Add(memory, 4 + i * stride);
                    if (tcp && Marshal.ReadInt32(row, family == 2 ? 0 : 48) != 5) continue;
                    var pid = Marshal.ReadInt32(row, stride - 4);
                    counts[pid] = counts.GetValueOrDefault(pid) + 1;
                }
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        return counts;
    }
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order, uint family, uint tableClass, uint reserved);
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedUdpTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order, uint family, uint tableClass, uint reserved);
}
