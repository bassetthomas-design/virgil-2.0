using Virgil.Core.Processes;
using System.Diagnostics;
using System.Text.Json;
using Virgil.Contracts;

namespace Virgil.Core.Updates;

public sealed class WingetUpdateScanService : IUpdateScanService
{
    private readonly WingetAvailabilityService _availabilityService;
    private readonly IProcessRunner _processRunner;
    private readonly UpdateRiskClassifier _riskClassifier;
    private readonly WindowsUpdateStatusService _windowsUpdateStatusService;
    private readonly DriverInformationService _driverInformationService;

    public WingetUpdateScanService(IProcessRunner processRunner)
        : this(
            new WingetAvailabilityService(processRunner),
            processRunner,
            new UpdateRiskClassifier(),
            new WindowsUpdateStatusService(),
            new DriverInformationService(processRunner))
    {
    }

    public WingetUpdateScanService(
        WingetAvailabilityService availabilityService,
        IProcessRunner processRunner,
        UpdateRiskClassifier riskClassifier,
        WindowsUpdateStatusService windowsUpdateStatusService,
        DriverInformationService driverInformationService)
    {
        _availabilityService = availabilityService;
        _processRunner = processRunner;
        _riskClassifier = riskClassifier;
        _windowsUpdateStatusService = windowsUpdateStatusService;
        _driverInformationService = driverInformationService;
    }

    public async Task<UpdateScanReport> ScanAsync(
        UpdateScanRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var recommendations = new List<string>();
        IReadOnlyList<UpdateItem> items = Array.Empty<UpdateItem>();

        progress?.Report("Détection WinGet.");
        var winget = await _availabilityService.DetectAsync(cancellationToken).ConfigureAwait(false);
        errors.AddRange(winget.Errors);

        progress?.Report("Lecture Windows Update.");
        var windowsUpdate = _windowsUpdateStatusService.ReadStatus();
        errors.AddRange(windowsUpdate.Errors);

        var drivers = new DriverInventoryReport();
        var moduleMissing = false;

        if (request.IncludeApplicationUpdates && winget.IsAvailable && !string.IsNullOrWhiteSpace(winget.ExecutablePath))
        {
            progress?.Report("Recherche des mises à jour applicatives (jusqu’à 120 secondes).");
            var json = await ReadClientModuleAsync(cancellationToken).ConfigureAwait(false);
            moduleMissing = json.Missing;
            items = json.Items is not null
                ? json.Items.Select(item => PrepareItem(item, winget, WingetAvailabilityService.GetCapabilities(winget.Version))).ToList()
                : await ScanWingetUpdatesAsync(winget, errors, cancellationToken).ConfigureAwait(false);
            if (moduleMissing) recommendations.Add("Le module Microsoft.WinGet.Client peut être installé après confirmation pour une analyse indépendante de la langue.");
        }
        else if (request.IncludeApplicationUpdates)
        {
            recommendations.Add("Installer ou réparer App Installer/WinGet pour analyser les mises à jour applicatives.");
        }

        if (request.IncludeDriverInventory)
        {
            progress?.Report("Inventaire des pilotes en lecture seule.");
            drivers = await _driverInformationService.InspectAsync(cancellationToken).ConfigureAwait(false);
            errors.AddRange(drivers.Errors);
        }

        recommendations.AddRange(BuildRecommendations(items, drivers, windowsUpdate, winget, request));

        stopwatch.Stop();
        return new UpdateScanReport
        {
            CapturedAt = DateTimeOffset.Now,
            Scope = request.Scope,
            Duration = stopwatch.Elapsed,
            OverallStatus = BuildOverallStatus(items, errors),
            Winget = winget,
            Items = items,
            IsWingetClientModuleMissing = moduleMissing,
            WindowsUpdate = windowsUpdate,
            Drivers = drivers,
            Recommendations = recommendations.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Errors = errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public static IReadOnlyList<string> BuildScanArguments(WingetCapabilities capabilities)
    {
        var arguments = new List<string> { "upgrade" };

        if (capabilities.SupportsIncludeUnknown) arguments.Add("--include-unknown");
        if (capabilities.SupportsIncludePinned) arguments.Add("--include-pinned");

        if (capabilities.SupportsAcceptSourceAgreements)
        {
            arguments.Add("--accept-source-agreements");
        }

        if (capabilities.SupportsDisableInteractivity)
        {
            arguments.Add("--disable-interactivity");
        }

        return arguments;
    }

    private async Task<IReadOnlyList<UpdateItem>> ScanWingetUpdatesAsync(
        WingetAvailability winget,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        var capabilities = WingetAvailabilityService.GetCapabilities(winget.Version);
        var arguments = BuildScanArguments(capabilities);
        var result = await _processRunner
            .RunAsync(new ProcessRunRequest(winget.ExecutablePath!, arguments, TimeSpan.FromSeconds(120)), cancellationToken)
            .ConfigureAwait(false);

        if (result.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.TimedOut)
        {
            errors.Add("Recherche WinGet interrompue : délai de 120 secondes dépassé.");
            return Array.Empty<UpdateItem>();
        }

        if (!string.IsNullOrWhiteSpace(result.LaunchError))
        {
            errors.Add("Recherche WinGet impossible.");
            return Array.Empty<UpdateItem>();
        }

        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            errors.Add("La recherche WinGet a retourné un statut non nul.");
            return Array.Empty<UpdateItem>();
        }

        var parseResult = WingetUpgradeParser.Parse(result.StandardOutput);
        foreach (var warning in parseResult.Warnings)
        {
            errors.Add(warning);
        }

        var items = new List<UpdateItem>();
        foreach (var parsed in parseResult.Items)
        {
            var item = parsed;
            if (item.IsIdTruncated)
            {
                var lookup = await _processRunner.RunAsync(new ProcessRunRequest(winget.ExecutablePath!,
                    BuildLookupArguments(item.Id.TrimEnd('…'), capabilities), TimeSpan.FromSeconds(120)), cancellationToken).ConfigureAwait(false);
                if (lookup.Cancelled) throw new OperationCanceledException(cancellationToken);
                var ids = ReadListedIds(lookup.StandardOutput).Where(id => !id.Contains('…') && id.StartsWith(item.Id.TrimEnd('…'), StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (lookup.ExitCode == 0 && !lookup.TimedOut && ids.Count == 1) item = item with { Id = ids[0], IsIdTruncated = false };
            }
            items.Add(PrepareItem(item, winget, capabilities));
        }
        return items;
    }

    private async Task<(IReadOnlyList<UpdateItem>? Items, bool Missing)> ReadClientModuleAsync(CancellationToken cancellationToken)
    {
        const string script = "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new(); $ErrorActionPreference='Stop'; if (!(Get-Module -ListAvailable Microsoft.WinGet.Client)) { Write-Output 'VIRGIL_MODULE_MISSING'; exit 0 }; Import-Module Microsoft.WinGet.Client; ConvertTo-Json -Compress -InputObject @(Get-WinGetPackage | Where-Object IsUpdateAvailable | Select-Object Id,Name,InstalledVersion,@{n='Source';e={if($_.Source.Name){$_.Source.Name}else{[string]$_.Source}}})";
        var result = await _processRunner.RunAsync(new ProcessRunRequest("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-Command", script }, TimeSpan.FromSeconds(120)), cancellationToken).ConfigureAwait(false);
        if (result.Cancelled) throw new OperationCanceledException(cancellationToken);
        if (result.StandardOutput.Trim() == "VIRGIL_MODULE_MISSING") return (null, true);
        if (result.ExitCode != 0 || result.TimedOut || !string.IsNullOrWhiteSpace(result.LaunchError)) return (null, false);
        try
        {
            var items = WingetUpgradeParser.ParseJson(result.StandardOutput).Items;
            // Get-WinGetPackage exposes catalog versions, not the applicable upgrade version.
            // Never pin the catalog maximum: ask the CLI to resolve applicability for nonempty inventories.
            return (items.Count == 0 ? items : null, false);
        }
        catch (JsonException) { return (null, false); }
    }

    private static IReadOnlyList<string> BuildLookupArguments(string prefix, WingetCapabilities capabilities)
    {
        var arguments = new List<string> { "list", "--id", prefix };
        if (capabilities.SupportsDisableInteractivity) arguments.Add("--disable-interactivity");
        if (capabilities.SupportsAcceptSourceAgreements) arguments.Add("--accept-source-agreements");
        return arguments;
    }

    private static IEnumerable<string> ReadListedIds(string output)
    {
        var lines = output.Replace("\r", string.Empty).Split('\n');
        for (var i = 1; i + 1 < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0 || !lines[i].Trim().All(c => c == '-')) continue;
            var tokens = System.Text.RegularExpressions.Regex.Matches(lines[i - 1], @"\S+");
            if (tokens.Count < 3) continue;
            for (var row = i + 1; row < lines.Length && lines[row].Length > tokens[2].Index; row++)
                yield return lines[row][tokens[1].Index..tokens[2].Index].Trim();
        }
    }

    private UpdateItem PrepareItem(UpdateItem item, WingetAvailability winget, WingetCapabilities capabilities)
    {
        var risk = _riskClassifier.Classify(item);
        return item with
        {
            RiskReason = risk.Reason,
            RequiresExplicitConfirmation = true,
            CommandPreview = new UpdateCommandPreview
            {
                ExecutablePath = winget.ExecutablePath ?? "winget.exe",
                Arguments = WingetUpdateExecutionService.BuildUpgradeArguments(item.Id, capabilities)
            },
            Message = item.IsIdTruncated ? "Identifiant tronqué : cible exacte non résolue, mise à jour indisponible." : item.IsInstalledVersionUnknown ? "Version installée inconnue." : item.Message,
            RiskLevel = item.IsIdTruncated ? UpdateRiskLevel.CriticalInformationOnly : risk.Level,
            Status = item.IsIdTruncated || risk.Level == UpdateRiskLevel.CriticalInformationOnly
                ? UpdateItemStatus.InformationOnly
                : UpdateItemStatus.Available
        };
    }

    private static IReadOnlyList<string> BuildRecommendations(
        IReadOnlyList<UpdateItem> items,
        DriverInventoryReport drivers,
        WindowsUpdateInformation windowsUpdate,
        WingetAvailability winget,
        UpdateScanRequest request)
    {
        var recommendations = new List<string>();

        if (request.Scope == UpdateScanScope.AvailabilityOnly)
        {
            recommendations.Add(winget.IsAvailable
                ? "WinGet disponible. L'analyse approfondie peut prévisualiser les mises à jour."
                : "WinGet absent ou inaccessible. Vérifier App Installer depuis Microsoft Store.");
        }

        if (items.Count > 0)
        {
            recommendations.Add("Vérifier la sélection avant de lancer les mises à jour.");
        }

        if (items.Any(item => item.RiskLevel == UpdateRiskLevel.Sensitive))
        {
            recommendations.Add("Vérifier les composants sensibles avant exécution.");
        }

        if (windowsUpdate.PendingRebootDetected)
        {
            recommendations.Add("Redémarrage en attente détecté : vérifier Windows Update.");
        }

        recommendations.AddRange(drivers.Recommendations);
        return recommendations;
    }

    private static string BuildOverallStatus(IReadOnlyList<UpdateItem> items, IReadOnlyList<string> errors)
    {
        if (errors.Count > 0)
        {
            return "Partiel";
        }

        if (items.Count == 0)
        {
            return "Aucune mise à jour applicative détectée";
        }

        return items.Any(item => item.RiskLevel == UpdateRiskLevel.Sensitive)
            ? "Validation requise"
            : "Mises à jour disponibles";
    }
}
