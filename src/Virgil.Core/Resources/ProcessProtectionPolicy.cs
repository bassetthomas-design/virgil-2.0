using Virgil.Domain;

namespace Virgil.Core.Resources;

public sealed record ProcessProtectionDecision(
    ProcessResourceStatus Status,
    bool IsCritical,
    bool CanCloseGracefully,
    bool CanForceClose,
    string Message);

public sealed class ProcessProtectionPolicy
{
    private static readonly HashSet<string> CriticalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system",
        "idle",
        "csrss",
        "wininit",
        "winlogon",
        "services",
        "lsass",
        "smss",
        "svchost",
        "dwm",
        "fontdrvhost",
        "registry",
        "memory compression",
        "audiodg",
        "conhost",
        "ctfmon",
        "logonui",
        "sihost",
        "taskhostw",
        "userinit",
        "securityhealthservice",
        "secure system"
    };

    private static readonly string[] ProtectedKeywords =
    {
        "antivirus", "defender", "msmpeng", "securityhealth", "sense", "vpn",
        "wireguard", "openvpn", "nordvpn", "protonvpn", "forticlient", "cisco",
        "crowdstrike", "sentinel", "malwarebytes", "bitdefender", "kaspersky",
        "nvcontainer", "radeon", "realtek", "driver", "endpoint"
    };

    private readonly string _system32Directory;
    private readonly string _windowsDirectory;
    private readonly int _currentProcessId;

    public ProcessProtectionPolicy()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"), Environment.ProcessId)
    {
    }

    public ProcessProtectionPolicy(string system32Directory, int currentProcessId)
    {
        _system32Directory = NormalizeDirectory(system32Directory);
        _windowsDirectory = NormalizeDirectory(
            Directory.GetParent(_system32Directory)?.FullName ?? _system32Directory);
        _currentProcessId = currentProcessId;
    }

    public ProcessProtectionDecision Evaluate(ProcessObservation process, double cpuPercent)
    {
        if (process.ProcessId <= 4 || process.ProcessId == _currentProcessId || CriticalNames.Contains(process.Name))
        {
            return Protected(ProcessResourceStatus.System, "Processus systeme critique protege.");
        }

        var searchable = $"{process.Name} {process.Path} {process.Publisher}";
        if (ProtectedKeywords.Any(keyword => searchable.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return Protected(ProcessResourceStatus.Protected, "Processus securite, VPN ou materiel protege.");
        }

        if (IsUnderWindows(process.Path))
        {
            return Protected(ProcessResourceStatus.System, "Processus installe dans Windows protege.");
        }

        if (process.AccessDenied || string.IsNullOrWhiteSpace(process.Path) || process.StartedAt is null)
        {
            return Protected(ProcessResourceStatus.Protected, "Identite insuffisante : fermeture non proposee.");
        }

        if (!process.HasMainWindow || string.IsNullOrWhiteSpace(process.MainWindowTitle))
        {
            return Protected(ProcessResourceStatus.Review, "Processus sans fenetre principale : a verifier.");
        }

        var isHeavy = process.WorkingSetBytes >= 512L * 1024 * 1024 || cpuPercent >= 20;
        return new ProcessProtectionDecision(
            isHeavy ? ProcessResourceStatus.Heavy : ProcessResourceStatus.Normal,
            IsCritical: false,
            CanCloseGracefully: true,
            CanForceClose: true,
            isHeavy ? "Application lourde. Examiner avant toute action." : "Application utilisateur.");
    }

    private bool IsUnderWindows(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            return string.Equals(fullPath, _system32Directory, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(
                    _windowsDirectory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static ProcessProtectionDecision Protected(ProcessResourceStatus status, string message)
    {
        return new ProcessProtectionDecision(status, true, false, false, message);
    }

    private static string NormalizeDirectory(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
