using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public sealed class InterventionDiagnosticService : IInterventionDiagnosticService
{
    private static readonly Regex SystemDrivePattern = new("^[A-Z]:$", RegexOptions.Compiled);
    private readonly IInterventionCatalog _catalog;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string> _systemDirectory;
    private readonly Func<string, Process[]> _processProvider;
    private readonly Func<IEnumerable<NetworkInterface>> _networkProvider;

    public InterventionDiagnosticService()
        : this(
            new InterventionCatalog(),
            File.Exists,
            () => Environment.SystemDirectory,
            Process.GetProcessesByName,
            NetworkInterface.GetAllNetworkInterfaces)
    {
    }

    public InterventionDiagnosticService(
        IInterventionCatalog catalog,
        Func<string, bool> fileExists,
        Func<string> systemDirectory,
        Func<string, Process[]> processProvider,
        Func<IEnumerable<NetworkInterface>> networkProvider)
    {
        _catalog = catalog;
        _fileExists = fileExists;
        _systemDirectory = systemDirectory;
        _processProvider = processProvider;
        _networkProvider = networkProvider;
    }

    public async Task<IReadOnlyList<InterventionDiagnostic>> DiagnoseAllAsync(CancellationToken cancellationToken)
    {
        var diagnostics = new List<InterventionDiagnostic>();
        foreach (var definition in _catalog.GetAll().OrderBy(definition => definition.DisplayOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(await DiagnoseAsync(definition.Id, cancellationToken).ConfigureAwait(false));
        }

        return diagnostics;
    }

    public Task<InterventionDiagnostic> DiagnoseAsync(InterventionId id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var definition = _catalog.Get(id);
        var warnings = BuildWarnings(definition).ToList();
        var errors = new List<string>();
        var technicalData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var available = AreExecutablesAvailable(definition, errors);
        var state = "Action disponible après validation explicite.";
        var status = available ? InterventionStatus.Available : InterventionStatus.Unavailable;
        var recommendation = available
            ? "Disponible. Aucune panne n'est inventée par le diagnostic."
            : "Indisponible dans l'état actuel.";

        if (definition.Id == InterventionId.RestartExplorer)
        {
            var explorerCount = CountExplorerProcesses();
            technicalData["ExplorerProcesses"] = explorerCount.ToString();
            if (explorerCount == 0)
            {
                status = InterventionStatus.Recommended;
                recommendation = "Explorer semble absent. Relance possible après confirmation.";
                state = "Aucun processus explorer.exe détecté.";
            }
            else
            {
                state = $"{explorerCount} processus explorer.exe détecté(s).";
            }
        }

        if (definition.Id == InterventionId.RenewIp)
        {
            var dhcp = DetectActiveDhcpInterface();
            technicalData["ActiveDhcpInterface"] = dhcp.InterfaceName ?? "N/A";
            technicalData["IgnoredVpn"] = dhcp.IgnoredVpnCount.ToString();
            available = available && dhcp.HasActiveDhcpInterface;
            status = available ? InterventionStatus.Available : InterventionStatus.Unavailable;
            state = dhcp.HasActiveDhcpInterface
                ? $"Interface DHCP active : {dhcp.InterfaceName}."
                : "Aucune interface DHCP active non VPN détectée.";
            recommendation = dhcp.HasActiveDhcpInterface
                ? "Disponible. La connexion peut être interrompue temporairement."
                : "Exécution non proposée sans interface DHCP active non VPN.";
        }

        if (definition.Id == InterventionId.ChkdskOnlineScan)
        {
            var drive = TryGetSystemDrive();
            available = available && drive is not null;
            status = available ? InterventionStatus.Available : InterventionStatus.Unavailable;
            technicalData["SystemDrive"] = drive ?? "Invalide";
            if (drive is null)
            {
                errors.Add("Lecteur système invalide pour CHKDSK /scan.");
            }
        }

        return Task.FromResult(new InterventionDiagnostic
        {
            Definition = definition,
            IsAvailable = available,
            Status = status,
            StateBefore = state,
            Recommendation = recommendation,
            Warnings = warnings,
            Errors = errors,
            TechnicalData = technicalData
        });
    }

    private bool AreExecutablesAvailable(InterventionDefinition definition, ICollection<string> errors)
    {
        foreach (var command in definition.CommandPreviews)
        {
            if (command.Executable.StartsWith("virgil-", StringComparison.Ordinal)) continue;
            if (string.Equals(command.Executable, "<system-drive>", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (FindWindowsExecutable(command.Executable) is null)
            {
                errors.Add($"{command.Executable} indisponible.");
                return false;
            }
        }

        return true;
    }

    private string? FindWindowsExecutable(string executable)
    {
        var systemDirectory = _systemDirectory();
        var windowsDirectory = Directory.GetParent(systemDirectory)?.FullName ?? systemDirectory;
        var candidates = new[]
        {
            Path.Combine(systemDirectory, executable),
            Path.Combine(windowsDirectory, executable),
            executable
        };

        return candidates.FirstOrDefault(_fileExists);
    }

    private int CountExplorerProcesses()
    {
        try
        {
            return _processProvider("explorer").Length;
        }
        catch
        {
            return 0;
        }
    }

    private DhcpDetection DetectActiveDhcpInterface()
    {
        var ignoredVpn = 0;
        try
        {
            foreach (var network in _networkProvider())
            {
                if (network.OperationalStatus != OperationalStatus.Up ||
                    network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                if (LooksLikeVpn(network))
                {
                    ignoredVpn++;
                    continue;
                }

                var ipv4 = network.GetIPProperties().GetIPv4Properties();
                if (ipv4?.IsDhcpEnabled == true)
                {
                    return new DhcpDetection(true, network.Name, ignoredVpn);
                }
            }
        }
        catch
        {
            return new DhcpDetection(false, null, ignoredVpn);
        }

        return new DhcpDetection(false, null, ignoredVpn);
    }

    private static bool LooksLikeVpn(NetworkInterface network)
    {
        var text = $"{network.Name} {network.Description}".ToLowerInvariant();
        return text.Contains("vpn", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("tap", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("tun", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("wireguard", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("virtual", StringComparison.OrdinalIgnoreCase);
    }

    private string? TryGetSystemDrive()
    {
        var root = Path.GetPathRoot(_systemDirectory())?.TrimEnd('\\');
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var normalized = root.ToUpperInvariant();
        return SystemDrivePattern.IsMatch(normalized) ? normalized : null;
    }

    private static IEnumerable<string> BuildWarnings(InterventionDefinition definition)
    {
        if (definition.RequiresAdministrator)
        {
            yield return "Droits administrateur demandés uniquement après confirmation.";
        }

        if (definition.RebootPossible)
        {
            yield return "Redémarrage manuel potentiellement requis. Virgil ne redémarre pas le PC.";
        }

        if (!definition.CanBeInterruptedAfterStart)
        {
            yield return "Ne pas interrompre brutalement cette action après démarrage.";
        }
    }

    private sealed record DhcpDetection(
        bool HasActiveDhcpInterface,
        string? InterfaceName,
        int IgnoredVpnCount);
}
