using System.Diagnostics;
using Virgil.Domain;

namespace Virgil.Core.Updates;

public sealed class WingetUpdateScanService : IUpdateScanService
{
    private readonly WingetAvailabilityService _availabilityService;
    private readonly IProcessRunner _processRunner;
    private readonly UpdateRiskClassifier _riskClassifier;
    private readonly WindowsUpdateStatusService _windowsUpdateStatusService;
    private readonly DriverInformationService _driverInformationService;

    public WingetUpdateScanService()
        : this(CreateDefaultProcessRunner())
    {
    }

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

        progress?.Report("Detection WinGet.");
        var winget = await _availabilityService.DetectAsync(cancellationToken).ConfigureAwait(false);
        errors.AddRange(winget.Errors);

        progress?.Report("Lecture Windows Update.");
        var windowsUpdate = _windowsUpdateStatusService.ReadStatus();
        errors.AddRange(windowsUpdate.Errors);

        var drivers = new DriverInventoryReport();

        if (request.IncludeApplicationUpdates && winget.IsAvailable && !string.IsNullOrWhiteSpace(winget.ExecutablePath))
        {
            progress?.Report("Previsualisation des mises a jour applicatives.");
            items = await ScanWingetUpdatesAsync(winget, errors, cancellationToken).ConfigureAwait(false);
        }
        else if (request.IncludeApplicationUpdates)
        {
            recommendations.Add("Installer ou reparer App Installer/WinGet pour analyser les mises a jour applicatives.");
        }

        if (request.IncludeDriverInventory)
        {
            progress?.Report("Inventaire pilotes lecture seule.");
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
            WindowsUpdate = windowsUpdate,
            Drivers = drivers,
            Recommendations = recommendations.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Errors = errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public static IReadOnlyList<string> BuildScanArguments(WingetCapabilities capabilities)
    {
        var arguments = new List<string> { "upgrade" };

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
            .RunAsync(new ProcessRunRequest(winget.ExecutablePath!, arguments, TimeSpan.FromSeconds(55)), cancellationToken)
            .ConfigureAwait(false);

        if (result.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.TimedOut)
        {
            errors.Add("Scan WinGet interrompu par timeout.");
            return Array.Empty<UpdateItem>();
        }

        if (!string.IsNullOrWhiteSpace(result.LaunchError))
        {
            errors.Add("Scan WinGet impossible.");
            return Array.Empty<UpdateItem>();
        }

        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            errors.Add("Scan WinGet retourne un statut non nul.");
            return Array.Empty<UpdateItem>();
        }

        var parseResult = WingetUpgradeParser.Parse(result.StandardOutput);
        foreach (var warning in parseResult.Warnings)
        {
            errors.Add(warning);
        }

        return parseResult.Items
            .Select(item => PrepareItem(item, winget, capabilities))
            .ToList();
    }

    private UpdateItem PrepareItem(UpdateItem item, WingetAvailability winget, WingetCapabilities capabilities)
    {
        var risk = _riskClassifier.Classify(item);
        return item with
        {
            RiskLevel = risk.Level,
            RiskReason = risk.Reason,
            RequiresExplicitConfirmation = true,
            CommandPreview = new UpdateCommandPreview
            {
                ExecutablePath = winget.ExecutablePath ?? "winget.exe",
                Arguments = WingetUpdateExecutionService.BuildUpgradeArguments(item.Id, capabilities)
            },
            Status = risk.Level == UpdateRiskLevel.CriticalInformationOnly
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
                ? "WinGet disponible. L'analyse approfondie peut previsualiser les mises a jour."
                : "WinGet absent ou inaccessible. Verifier App Installer depuis Microsoft Store.");
        }

        if (items.Count > 0)
        {
            recommendations.Add("Installer uniquement les applications validees individuellement.");
        }

        if (items.Any(item => item.RiskLevel == UpdateRiskLevel.Sensitive))
        {
            recommendations.Add("Verifier les composants sensibles avant execution.");
        }

        if (windowsUpdate.PendingRebootDetected)
        {
            recommendations.Add("Redemarrage en attente detecte : verifier Windows Update.");
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
            return "Aucune mise a jour applicative detectee";
        }

        return items.Any(item => item.RiskLevel == UpdateRiskLevel.Sensitive)
            ? "Validation requise"
            : "Mises a jour disponibles";
    }

    private static IProcessRunner CreateDefaultProcessRunner()
    {
        return new ProcessRunner();
    }
}
