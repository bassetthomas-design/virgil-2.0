using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Virgil.Domain.Applications;

namespace Virgil.Core.Applications;

public sealed class ApplicationRegistryReader : IApplicationInventorySourceReader
{
    private readonly IReadOnlyList<ApplicationRegistryEntry>? _testEntries;

    public ApplicationRegistryReader()
    {
    }

    public ApplicationRegistryReader(IEnumerable<ApplicationRegistryEntry> testEntries)
    {
        _testEntries = testEntries.ToList();
    }

    public string SourceName => "Registre";

    public Task<ApplicationInventorySourceResult> ReadAsync(CancellationToken cancellationToken)
    {
        if (_testEntries is not null)
        {
            return Task.FromResult(new ApplicationInventorySourceResult(
                _testEntries.Select(ConvertEntry).Where(app => !string.IsNullOrWhiteSpace(app.DisplayName)).ToList(),
                Array.Empty<string>()));
        }

        var entries = new List<InstalledApplication>();
        var errors = new List<string>();

        ReadRegistryView(
            RegistryHive.LocalMachine,
            RegistryView.Registry64,
            ApplicationArchitecture.X64,
            entries,
            errors,
            cancellationToken);
        ReadRegistryView(
            RegistryHive.LocalMachine,
            RegistryView.Registry32,
            ApplicationArchitecture.X86,
            entries,
            errors,
            cancellationToken);
        ReadRegistryView(
            RegistryHive.CurrentUser,
            RegistryView.Default,
            ApplicationArchitecture.Unknown,
            entries,
            errors,
            cancellationToken);

        return Task.FromResult(new ApplicationInventorySourceResult(entries, errors));
    }

    public static InstalledApplication ConvertEntry(ApplicationRegistryEntry entry)
    {
        if (entry.SystemComponent || string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            return new InstalledApplication();
        }

        var productCode = TryExtractProductCode(entry);
        var uninstallKind = !string.IsNullOrWhiteSpace(productCode)
            ? ApplicationUninstallKind.Msi
            : !string.IsNullOrWhiteSpace(entry.UninstallString)
                ? ApplicationUninstallKind.RegistryUninstallString
                : !string.IsNullOrWhiteSpace(entry.QuietUninstallString)
                    ? ApplicationUninstallKind.RegistryQuietUninstallString
                    : ApplicationUninstallKind.None;
        var command = !string.IsNullOrWhiteSpace(productCode)
            ? $"msiexec.exe /x {productCode}"
            : entry.UninstallString;

        return new InstalledApplication
        {
            Id = StableId("registry", entry.KeyName, entry.DisplayName, entry.Publisher, entry.DisplayVersion),
            DisplayName = entry.DisplayName.Trim(),
            Publisher = entry.Publisher.Trim(),
            Version = entry.DisplayVersion.Trim(),
            InstallDate = ParseInstallDate(entry.InstallDateRaw),
            EstimatedSizeBytes = entry.EstimatedSizeKilobytes is > 0
                ? entry.EstimatedSizeKilobytes.Value * 1024
                : null,
            Source = entry.WindowsInstaller ? ApplicationInventorySource.Msi : ApplicationInventorySource.Registry,
            Sources = entry.WindowsInstaller
                ? [ApplicationInventorySource.Registry, ApplicationInventorySource.Msi]
                : [ApplicationInventorySource.Registry],
            Architecture = entry.Architecture,
            InstallLocation = CleanPath(entry.InstallLocation),
            IconPath = ApplicationIconExtractor.CleanIconPath(entry.DisplayIcon),
            UninstallCommand = command,
            QuietUninstallCommand = entry.QuietUninstallString,
            MsiProductCode = productCode,
            UninstallKind = uninstallKind,
            Status = uninstallKind == ApplicationUninstallKind.None
                ? ApplicationStatus.Unknown
                : ApplicationStatus.UninstallAvailable
        };
    }

    private static void ReadRegistryView(
        RegistryHive hive,
        RegistryView view,
        ApplicationArchitecture architecture,
        ICollection<InstalledApplication> entries,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        const string uninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(uninstallPath);
            if (uninstallKey is null)
            {
                return;
            }

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var subKey = uninstallKey.OpenSubKey(subKeyName);
                    if (subKey is null)
                    {
                        continue;
                    }

                    var entry = new ApplicationRegistryEntry
                    {
                        RegistryView = $"{hive}/{view}",
                        KeyName = subKeyName,
                        DisplayName = ReadString(subKey, "DisplayName"),
                        Publisher = ReadString(subKey, "Publisher"),
                        DisplayVersion = ReadString(subKey, "DisplayVersion"),
                        InstallDateRaw = ReadString(subKey, "InstallDate"),
                        EstimatedSizeKilobytes = ReadLong(subKey, "EstimatedSize"),
                        InstallLocation = ReadString(subKey, "InstallLocation"),
                        DisplayIcon = ReadString(subKey, "DisplayIcon"),
                        UninstallString = ReadString(subKey, "UninstallString"),
                        QuietUninstallString = ReadString(subKey, "QuietUninstallString"),
                        WindowsInstaller = ReadInt(subKey, "WindowsInstaller") == 1,
                        SystemComponent = ReadInt(subKey, "SystemComponent") == 1,
                        Architecture = architecture
                    };
                    var converted = ConvertEntry(entry);
                    if (!string.IsNullOrWhiteSpace(converted.DisplayName))
                    {
                        entries.Add(converted);
                    }
                }
                catch
                {
                    errors.Add($"Entree registre ignoree : {subKeyName}.");
                }
            }
        }
        catch
        {
            errors.Add($"Source registre inaccessible : {hive}/{view}.");
        }
    }

    private static string ReadString(RegistryKey key, string name)
    {
        return key.GetValue(name)?.ToString() ?? string.Empty;
    }

    private static int ReadInt(RegistryKey key, string name)
    {
        return int.TryParse(key.GetValue(name)?.ToString(), out var value) ? value : 0;
    }

    private static long? ReadLong(RegistryKey key, string name)
    {
        return long.TryParse(key.GetValue(name)?.ToString(), out var value) ? value : null;
    }

    private static string? TryExtractProductCode(ApplicationRegistryEntry entry)
    {
        if (IsGuidProductCode(entry.KeyName))
        {
            return entry.KeyName;
        }

        var command = entry.UninstallString ?? string.Empty;
        var start = command.IndexOf('{');
        var end = command.IndexOf('}', start >= 0 ? start : 0);
        if (start >= 0 && end > start)
        {
            var candidate = command[start..(end + 1)];
            if (IsGuidProductCode(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsGuidProductCode(string value)
    {
        return value.Length == 38 &&
            value[0] == '{' &&
            value[^1] == '}' &&
            Guid.TryParse(value[1..^1], out _);
    }

    private static DateTimeOffset? ParseInstallDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 8)
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(
            value,
            "yyyyMMdd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeLocal,
            out var date)
            ? date
            : null;
    }

    private static string? CleanPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    internal static string StableId(params string[] values)
    {
        var joined = string.Join("|", values.Select(value => value.Trim().ToLowerInvariant()));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}

