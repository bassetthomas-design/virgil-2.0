using System.Runtime.InteropServices;
using Microsoft.Win32;
using Virgil.Domain;

namespace Virgil.Core.Scanning;

internal static class WindowsInfoReader
{
    private const string NotAvailable = "N/A";

    public static ScanReaderResult<WindowsScanInfo> Read(DateTimeOffset scanDate)
    {
        var errors = new List<string>();

        var version = ReadVersion(errors);
        var edition = ReadRegistryValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
        var displayVersion = ReadRegistryValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
        var releaseId = ReadRegistryValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ReleaseId");
        var build = ReadBuild(version, errors);
        var versionText = BuildVersionText(version, displayVersion, releaseId);

        if (edition == NotAvailable)
        {
            errors.Add("Edition Windows indisponible.");
        }

        var info = new WindowsScanInfo(
            edition,
            versionText,
            build,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            ReadMachineName(errors),
            ReadUptime(errors),
            scanDate);

        return new ScanReaderResult<WindowsScanInfo>(info, errors);
    }

    private static OsVersionInfo ReadVersion(ICollection<string> errors)
    {
        var info = new OsVersionInfo();
        info.VersionInfoSize = Marshal.SizeOf<OsVersionInfo>();

        try
        {
            var status = RtlGetVersion(ref info);
            if (status == 0)
            {
                return info;
            }
        }
        catch
        {
        }

        errors.Add("Version Windows indisponible.");
        return new OsVersionInfo();
    }

    private static string ReadBuild(OsVersionInfo version, ICollection<string> errors)
    {
        if (version.BuildNumber == 0)
        {
            return NotAvailable;
        }

        var ubr = ReadRegistryValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR");
        if (ubr == NotAvailable)
        {
            return version.BuildNumber.ToString();
        }

        return $"{version.BuildNumber}.{ubr}";
    }

    private static string BuildVersionText(OsVersionInfo version, string displayVersion, string releaseId)
    {
        var coreVersion = version.MajorVersion == 0 && version.MinorVersion == 0
            ? NotAvailable
            : $"{version.MajorVersion}.{version.MinorVersion}";

        var marketingVersion = displayVersion != NotAvailable ? displayVersion : releaseId;
        return marketingVersion == NotAvailable ? coreVersion : $"{coreVersion} ({marketingVersion})";
    }

    private static string ReadMachineName(ICollection<string> errors)
    {
        try
        {
            return Environment.MachineName;
        }
        catch
        {
            errors.Add("Nom de machine indisponible.");
            return NotAvailable;
        }
    }

    private static TimeSpan ReadUptime(ICollection<string> errors)
    {
        try
        {
            return TimeSpan.FromMilliseconds(Environment.TickCount64);
        }
        catch
        {
            errors.Add("Duree depuis le demarrage indisponible.");
            return TimeSpan.Zero;
        }
    }

    private static string ReadRegistryValue(string keyName, string valueName)
    {
        try
        {
            return Registry.GetValue(keyName, valueName, null)?.ToString() ?? NotAvailable;
        }
        catch
        {
            return NotAvailable;
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int RtlGetVersion(ref OsVersionInfo versionInformation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersionInfo
    {
        public int VersionInfoSize;
        public int MajorVersion;
        public int MinorVersion;
        public int BuildNumber;
        public int PlatformId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string CsdVersion;
    }
}
