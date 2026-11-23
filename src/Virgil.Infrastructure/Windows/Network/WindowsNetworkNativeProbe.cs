using System.Runtime.InteropServices;
using Virgil.Core.Network;

namespace Virgil.Infrastructure.Windows.Network;

public static class WindowsNetworkNativeProbe
{
    public static (bool Available, IReadOnlySet<int> DefaultRouteInterfaceIndices) ReadDefaultRouteInterfaces()
    {
        var indices = new HashSet<int>();
        if (!OperatingSystem.IsWindows()) return (false, indices);

        IntPtr table = IntPtr.Zero;
        try
        {
            if (GetIpForwardTable2(0, out table) != 0 || table == IntPtr.Zero) return (false, indices);

            // MIB_IPFORWARD_TABLE2 aligns its first MIB_IPFORWARD_ROW2 to eight bytes.
            var count = Marshal.ReadInt32(table);
            for (var index = 0; index < count; index++)
            {
                var row = IntPtr.Add(table, 8 + index * 104);
                // DestinationPrefix starts at byte 12 and contains a 28-byte SOCKADDR_INET.
                if (Marshal.ReadByte(row, 40) == 0) indices.Add(Marshal.ReadInt32(row, 8));
            }
            return (true, indices);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return (false, new HashSet<int>());
        }
        finally
        {
            if (table != IntPtr.Zero) FreeMibTable(table);
        }
    }

    public static WifiProtection ReadWifiProtection()
    {
        if (!OperatingSystem.IsWindows()) return WifiProtection.Unknown;

        IntPtr handle = IntPtr.Zero;
        IntPtr interfaces = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0 ||
                WlanEnumInterfaces(handle, IntPtr.Zero, out interfaces) != 0 || interfaces == IntPtr.Zero)
                return WifiProtection.Unknown;

            var weakest = WifiProtection.NotApplicable;
            var count = Marshal.ReadInt32(interfaces);
            for (var index = 0; index < count; index++)
            {
                // WLAN_INTERFACE_INFO contains a GUID, WCHAR[256], then the interface state.
                var info = IntPtr.Add(interfaces, 8 + index * 532);
                if (Marshal.ReadInt32(info, 528) != 1) continue;

                var id = Marshal.PtrToStructure<Guid>(info);
                IntPtr data = IntPtr.Zero;
                try
                {
                    var protection = WifiProtection.Unknown;
                    if (WlanQueryInterface(handle, ref id, 7, IntPtr.Zero, out var size, out data, IntPtr.Zero) == 0 &&
                        data != IntPtr.Zero && size >= 604)
                    {
                        // WLAN_SECURITY_ATTRIBUTES follows the profile name and association attributes.
                        protection = MapWifiSecurity(Marshal.ReadInt32(data, 588) != 0,
                            unchecked((uint)Marshal.ReadInt32(data, 596)), unchecked((uint)Marshal.ReadInt32(data, 600)));
                    }
                    if (weakest == WifiProtection.NotApplicable || Rank(protection) < Rank(weakest)) weakest = protection;
                }
                finally
                {
                    if (data != IntPtr.Zero) WlanFreeMemory(data);
                }
            }
            return weakest == WifiProtection.NotApplicable ? WifiProtection.Unknown : weakest;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return WifiProtection.Unknown;
        }
        finally
        {
            if (interfaces != IntPtr.Zero) WlanFreeMemory(interfaces);
            if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    private static int Rank(WifiProtection protection) => protection switch
    {
        WifiProtection.Open => 0,
        WifiProtection.Wep => 1,
        WifiProtection.Wpa => 2,
        WifiProtection.Unknown => 3,
        WifiProtection.Wpa2 => 4,
        _ => 5
    };

    public static WifiProtection MapWifiSecurity(bool securityEnabled, uint authAlgorithm, uint cipherAlgorithm) =>
        (securityEnabled, authAlgorithm, cipherAlgorithm) switch
        {
            (false, _, _) => WifiProtection.Open,
            (_, _, 1 or 5 or 0x101) => WifiProtection.Wep,
            (_, 3 or 4, 2 or 4) => WifiProtection.Wpa,
            (_, 6 or 7, 2 or 4 or 8 or 9 or 10) => WifiProtection.Wpa2,
            (_, 8 or 9 or 11, 4 or 8 or 9 or 10) => WifiProtection.Wpa3,
            _ => WifiProtection.Unknown
        };

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetIpForwardTable2(ushort family, out IntPtr table);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern void FreeMibTable(IntPtr table);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiatedVersion, out IntPtr handle);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr interfaces);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanQueryInterface(IntPtr handle, ref Guid id, uint opcode, IntPtr reserved,
        out uint size, out IntPtr data, IntPtr opcodeValueType);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern void WlanFreeMemory(IntPtr memory);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
}
