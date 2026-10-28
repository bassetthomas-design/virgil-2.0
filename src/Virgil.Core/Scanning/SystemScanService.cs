using System.Diagnostics;
using Virgil.Core.Applications;
using Virgil.Core.Status;
using Virgil.Core.Cleanup;
using Virgil.Core.Interventions;
using Virgil.Core.Monitoring;
using Virgil.Core.Resources;
using Virgil.Core.Updates;
using Virgil.Contracts;
using Virgil.Contracts.Applications;

namespace Virgil.Core.Scanning;

public sealed class SystemScanService : ISystemScanService
{
    private readonly ICleanupService _cleanupService;
    private readonly ICleanupEstimateService? _cleanupEstimator;
    private readonly ISystemStatusStore? _statusStore;
    private readonly IUpdateScanService _updateScanService;
    private readonly IInterventionDiagnosticService _interventionDiagnosticService;
    private readonly IResourceMonitoringService _resourceMonitoringService;
    private readonly IApplicationInventoryService _applicationInventoryService;

    public SystemScanService(
        ICleanupService cleanupService,
        IUpdateScanService updateScanService,
        IInterventionDiagnosticService interventionDiagnosticService,
        IResourceMonitoringService resourceMonitoringService,
        IApplicationInventoryService applicationInventoryService,
        ICleanupEstimateService? cleanupEstimator = null, ISystemStatusStore? statusStore = null)
    {
        _cleanupService = cleanupService;
        _cleanupEstimator = cleanupEstimator;
        _statusStore = statusStore;
        _updateScanService = updateScanService;
        _interventionDiagnosticService = interventionDiagnosticService;
        _resourceMonitoringService = resourceMonitoringService;
        _applicationInventoryService = applicationInventoryService;
    }

    public Task<SystemScanReport> RunAsync(ScanMode mode, IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => RunCoreAsync(mode, progress, cancellationToken), cancellationToken).WaitAsync(cancellationToken);

    private async Task<SystemScanReport> RunCoreAsync(
        ScanMode mode, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var scanDate = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var findings = new List<ScanFinding>();

        Report(progress, "Initialisation", "Scan initialisé.", "Système", 0);

        cancellationToken.ThrowIfCancellationRequested();
        var windows = ReadWindows(scanDate, progress, errors);
        cancellationToken.ThrowIfCancellationRequested();
        var processor = await ReadProcessorAsync(progress, errors, findings, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var memory = ReadMemory(progress, errors, findings);
        cancellationToken.ThrowIfCancellationRequested();
        var allDisks = ReadDisks(progress, errors, findings);
        cancellationToken.ThrowIfCancellationRequested();
        var reportDisks = SelectReportDisks(mode, allDisks);
        cancellationToken.ThrowIfCancellationRequested();
        var network = ReadNetwork(progress, errors, findings);
        cancellationToken.ThrowIfCancellationRequested();
        var processes = mode == ScanMode.Deep
            ? ReadProcesses(progress, errors, findings)
            : Array.Empty<ProcessScanInfo>();
        cancellationToken.ThrowIfCancellationRequested();
        var cleanup = mode == ScanMode.Deep
            ? await ReadCleanupAsync(progress, errors, findings, cancellationToken).ConfigureAwait(false)
            : new CleanupScanInfo(false, 0, 0, Array.Empty<string>(), Array.Empty<string>());
        cancellationToken.ThrowIfCancellationRequested();
        var resources = await ReadResourcesAsync(mode, progress, errors, findings, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var updates = await ReadUpdatesAsync(mode, progress, errors, findings, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var interventions = await ReadInterventionsAsync(mode, progress, errors, findings, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var applications = await ReadApplicationsAsync(mode, progress, errors, findings, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var error in errors.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new ScanFinding(
                $"partial-error-{findings.Count + 1}",
                "Robustesse",
                "Erreur partielle",
                error,
                null,
                ScanSeverity.Attention,
                "Certaines données n'ont pas pu être lues. Le scan a conservé les résultats disponibles."));
        }

        Report(progress, "Synthèse", "Synthèse du rapport.", "Système", 92);

        var recommendations = ScanRules.BuildRecommendations(findings)
            .Concat(updates.Recommendations.Take(3))
            .Concat(resources.Recommendations.Take(3))
            .Concat(interventions.Diagnostics.Take(3))
            .Concat(applications.Recommendations.Take(3))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var overallStatus = ScanRules.OverallStatus(findings.Select(finding => finding.Severity));

        stopwatch.Stop();
        Report(progress, "Terminé", "Scan terminé.", "Système", 100);

        var report = new SystemScanReport(
            scanDate,
            mode,
            stopwatch.Elapsed,
            overallStatus,
            windows,
            processor,
            memory,
            reportDisks,
            processes,
            network,
            cleanup,
            findings,
            recommendations,
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            Updates = updates,
            Interventions = interventions,
            Resources = resources,
            Applications = applications
        };
        cancellationToken.ThrowIfCancellationRequested();
        _statusStore?.PublishScan(report);
        return report;
    }

    private async Task<ApplicationScanSummary> ReadApplicationsAsync(
        ScanMode mode,
        IProgress<ScanProgress>? progress,
        ICollection<string> errors,
        ICollection<ScanFinding> findings,
        CancellationToken cancellationToken)
    {
        if (mode != ScanMode.Deep)
        {
            return ApplicationScanSummary.NotAnalyzed;
        }

        Report(
            progress,
            "Applications",
            "Inventaire lecture seule des applications installées.",
            "Applications",
            91);

        try
        {
            var report = await _applicationInventoryService
                .InventoryAsync(null, cancellationToken)
                .ConfigureAwait(false);
            AddErrors(errors, report.Errors);

            var largeCount = report.Applications.Count(app => app.EstimatedSizeBytes >= 1L * 1024 * 1024 * 1024);
            if (report.ProtectedCount > 0)
            {
                findings.Add(new ScanFinding(
                    "applications-protected",
                    "Applications",
                    "Applications protégées détectées",
                    "Des composants pilotes, sécurité, runtime ou système sont exclus de la désinstallation Virgil V1.",
                    report.ProtectedCount.ToString(),
                    ScanSeverity.Information,
                    "Conserver ces composants. Virgil bloque leur désinstallation depuis le module Applications."));
            }

            if (report.UnknownCount > 0)
            {
                findings.Add(new ScanFinding(
                    "applications-unknown",
                    "Applications",
                    "Applications a classification incertaine",
                    "Certaines applications ne disposent pas d'une source ou d'une commande de désinstallation suffisamment fiable.",
                    report.UnknownCount.ToString(),
                    ScanSeverity.Attention,
                    "Vérifier manuellement chaque application inconnue avant toute action."));
            }

            if (largeCount > 0)
            {
                findings.Add(new ScanFinding(
                    "applications-large",
                    "Applications",
                    "Applications volumineuses",
                    "Des applications representent un volume disque significatif.",
                    largeCount.ToString(),
                    ScanSeverity.Information,
                    "Utiliser le module Applications pour analyser individuellement. Aucune désinstallation n'est exécutée depuis le scan."));
            }

            var recommendations = new List<string>();
            if (report.UninstallableCount > 0)
            {
                recommendations.Add("Ouvrir le module Applications pour examiner les programmes désinstallables individuellement.");
            }

            if (report.CautionCount > 0)
            {
                recommendations.Add("Valider explicitement les applications marquees attention avant de lancer leur désinstalleur officiel.");
            }

            if (report.UnknownCount > 0)
            {
                recommendations.Add("Ne pas supprimer manuellement les dossiers d'applications inconnues.");
            }

            return new ApplicationScanSummary
            {
                WasAnalyzed = true,
                DetectedCount = report.Applications.Count,
                UninstallableCount = report.UninstallableCount,
                ProtectedCount = report.ProtectedCount,
                UnknownCount = report.UnknownCount,
                LargeApplicationCount = largeCount,
                Recommendations = recommendations,
                Errors = report.Errors
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            var applicationErrors = new[] { "Inventaire applications indisponible." };
            AddErrors(errors, applicationErrors);
            return new ApplicationScanSummary
            {
                WasAnalyzed = true,
                Errors = applicationErrors
            };
        }
    }

    private async Task<ResourceScanSummary> ReadResourcesAsync(
        ScanMode mode,
        IProgress<ScanProgress>? progress,
        ICollection<string> errors,
        ICollection<ScanFinding> findings,
        CancellationToken cancellationToken)
    {
        if (mode != ScanMode.Deep)
        {
            return ResourceScanSummary.NotAnalyzed;
        }

        Report(
            progress,
            "Ressources système",
            "Observation courte CPU/RAM et processus en lecture seule.",
            "Ressources",
            86);

        try
        {
            var report = await _resourceMonitoringService
                .AnalyzeAsync(ResourceAnalysisRequest.DeepScanPreview, null, cancellationToken)
                .ConfigureAwait(false);
            AddErrors(errors, report.Errors);

            var mainProcesses = report.TopMemoryProcesses
                .Concat(report.TopCpuProcesses)
                .GroupBy(process => process.ProcessId)
                .Select(group => group.First())
                .ToList();
            var heavyCount = mainProcesses.Count(process => process.Status == ProcessResourceStatus.Heavy);

            if (report.AverageCpuPercent >= 85)
            {
                findings.Add(new ScanFinding(
                    "resources-cpu-sustained",
                    "Ressources",
                    "Charge CPU elevee sur observation courte",
                    "La moyenne CPU est restee elevee pendant plusieurs echantillons.",
                    $"{report.AverageCpuPercent:0.0} %",
                    report.AverageCpuPercent >= 95 ? ScanSeverity.Critical : ScanSeverity.Warning,
                    "Examiner les principaux processus CPU dans le module Ressources. Aucune fermeture n'a été exécutée."));
            }

            if (report.AverageMemoryPercent >= 85)
            {
                findings.Add(new ScanFinding(
                    "resources-memory-high",
                    "Ressources",
                    "Utilisation RAM elevee",
                    "La RAM utilisee depasse le seuil d'intervention conseillee.",
                    $"{report.AverageMemoryPercent:0.0} %",
                    report.AverageMemoryPercent >= 95 ? ScanSeverity.Critical : ScanSeverity.Warning,
                    "Examiner les applications lourdes avant toute fermeture."));
            }

            if (heavyCount > 0)
            {
                findings.Add(new ScanFinding(
                    "resources-heavy-processes",
                    "Ressources",
                    "Processus lourds détectés",
                    "Des processus depassent les seuils CPU ou RAM du module Ressources.",
                    heavyCount.ToString(),
                    ScanSeverity.Attention,
                    "Ouvrir le module Ressources pour vérifier chaque processus. Aucune action n'est exécutée depuis le scan."));
            }

            return new ResourceScanSummary
            {
                WasAnalyzed = true,
                AverageCpuPercent = report.AverageCpuPercent,
                MemoryPercent = report.AverageMemoryPercent,
                Uptime = report.Uptime,
                HeavyProcessCount = heavyCount,
                TopMemoryProcesses = report.TopMemoryProcesses
                    .Take(5)
                    .Select(process => $"{process.Name} - {ScanRules.FormatBytes(process.WorkingSetBytes)}")
                    .ToList(),
                Recommendations = report.Recommendations,
                Errors = report.Errors
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            var resourceErrors = new[] { "Analyse ressources indisponible." };
            AddErrors(errors, resourceErrors);
            return new ResourceScanSummary
            {
                WasAnalyzed = true,
                Errors = resourceErrors
            };
        }
    }

    private static WindowsScanInfo ReadWindows(
        DateTimeOffset scanDate,
        IProgress<ScanProgress>? progress,
        ICollection<string> errors)
    {
        Report(progress, "Informations Windows", "Lecture des informations Windows.", "Windows", 10);
        var result = WindowsInfoReader.Read(scanDate);
        AddErrors(errors, result.Errors);
        return result.Value;
    }

    private static async Task<ProcessorScanInfo> ReadProcessorAsync(
        IProgress<ScanProgress>? progress,
        ICollection<string> errors,
        ICollection<ScanFinding> findings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, "Processeur", "Mesure instantanée du processeur.", "CPU", 24);
        var result = await ProcessorReader.ReadAsync(cancellationToken).ConfigureAwait(false);
        AddErrors(errors, result.Errors);

        if (result.Value.Severity >= ScanSeverity.Attention)
        {
            findings.Add(new ScanFinding(
                "cpu-usage",
                "Processeur",
                "Utilisation CPU elevee",
                "La charge CPU est elevee au moment du scan.",
                $"{result.Value.UsagePercent:0.0} %",
                result.Value.Severity,
                "Mesure CPU instantanée : si elle reste elevee, identifier les applications actives les plus consommatrices."));
        }

        return result.Value;
    }

    private static MemoryScanInfo ReadMemory(
        IProgress<ScanProgress>? progress,
        ICollection<string> errors,
        ICollection<ScanFinding> findings)
    {
        Report(progress, "Mémoire", "Lecture de la mémoire physique.", "RAM", 36);

        MemoryStatus memoryStatus;
        try
        {
            memoryStatus = MemoryReader.Read();
        }
        catch
        {
            memoryStatus = new MemoryStatus(0, 0);
        }

        if (memoryStatus.TotalBytes == 0)
        {
            errors.Add("Lecture mémoire indisponible.");
        }
        else if (memoryStatus.AvailableBytes > memoryStatus.TotalBytes)
        {
            errors.Add("Valeurs mémoire incoherentes.");
        }

        var severity = memoryStatus.TotalBytes == 0
            ? ScanSeverity.Information
            : ScanRules.CalculateMemorySeverity(memoryStatus.UsedPercent);

        var memory = new MemoryScanInfo(
            memoryStatus.TotalBytes,
            memoryStatus.AvailableBytes,
            memoryStatus.UsedBytes,
            memoryStatus.UsedPercent,
            severity,
            memoryStatus.TotalBytes == 0 ? "N/A" : ScanRules.StatusForSeverity(severity));

        if (severity >= ScanSeverity.Attention)
        {
            findings.Add(new ScanFinding(
                "memory-usage",
                "Mémoire",
                "Utilisation RAM elevee",
                "La mémoire physique utilisee depasse le seuil de surveillance.",
                $"{memory.UsedPercent:0.0} %",
                severity,
                "Comparer avec les processus actifs. Un ecart avec le Gestionnaire des taches peut venir du moment d'echantillonnage, de la mémoire compressee, du cache ou de la presentation Windows."));
        }

        return memory;
    }

    private static IReadOnlyList<DiskScanInfo> ReadDisks(
        IProgress<ScanProgress>? progress,
        ICollection<string> errors,
        ICollection<ScanFinding> findings)
    {
        Report(progress, "Stockage", "Analyse des disques fixes prets.", "Disques", 50);
        var result = DiskReader.ReadFixedDrives();
        AddErrors(errors, result.Errors);

        if (result.Value.Count == 0)
        {
            errors.Add("Aucun disque fixe accessible.");
        }

        foreach (var disk in result.Value.Where(disk => disk.Severity >= ScanSeverity.Attention))
        {
            findings.Add(new ScanFinding(
                $"disk-{disk.Name.TrimEnd('\\')}",
                "Stockage",
                disk.IsSystemDrive ? "Disque système presque plein" : "Disque presque plein",
                $"Le disque {disk.Name} utilise une part importante de sa capacité.",
                $"{disk.UsedPercent:0.0} %",
                disk.Severity,
                disk.IsSystemDrive
                    ? "Liberer de l'espace sur le disque système ou déplacer des données non essentielles."
                    : "Surveiller l'espace disponible et archiver les données non essentielles."));
        }

        return result.Value;
    }

    private static IReadOnlyList<DiskScanInfo> SelectReportDisks(ScanMode mode, IReadOnlyList<DiskScanInfo> disks)
    {
        if (mode == ScanMode.Deep)
        {
            return disks;
        }

        var systemDisk = ScanRules.SelectSystemDisk(disks, Environment.SystemDirectory);
        return systemDisk is null ? Array.Empty<DiskScanInfo>() : new[] { systemDisk };
    }

    private static NetworkScanInfo ReadNetwork(
        IProgress<ScanProgress>? progress,
        ICollection<string> errors,
        ICollection<ScanFinding> findings)
    {
        Report(progress, "Réseau", "Lecture de l'interface active.", "Réseau", 64);
        var result = NetworkReader.ReadPrimaryInterface();
        AddErrors(errors, result.Errors);

        if (result.Errors.Count > 0)
        {
            findings.Add(new ScanFinding(
                "network-active-interface",
                "Réseau",
                "Aucun réseau actif détecté",
                "Virgil n'a pas détecté d'interface réseau active avec une adresse IPv4 locale.",
                null,
                ScanSeverity.Attention,
                "Vérifier la connexion réseau si une connectivite est attendue."));
        }

        return result.Value;
    }

    private static IReadOnlyList<ProcessScanInfo> ReadProcesses(
        IProgress<ScanProgress>? progress,
        ICollection<string> errors,
        ICollection<ScanFinding> findings)
    {
        Report(progress, "Processus", "Lecture des principaux processus en mémoire.", "Processus", 76);
        var result = ProcessReader.ReadTopMemoryProcesses(10);
        AddErrors(errors, result.Errors);

        var heavyProcess = result.Value.FirstOrDefault(process => process.WorkingSetBytes >= 1_073_741_824);
        if (heavyProcess is not null)
        {
            findings.Add(new ScanFinding(
                "process-memory",
                "Processus",
                "Processus consommant beaucoup de mémoire",
                $"Le processus {heavyProcess.Name} utilise une quantite importante de mémoire physique.",
                ScanRules.FormatBytes(heavyProcess.WorkingSetBytes),
                ScanSeverity.Attention,
                "Vérifier si cette consommation correspond a une application attendue. Aucun processus n'a été modifie."));
        }

        return result.Value;
    }

    private async Task<CleanupScanInfo> ReadCleanupAsync(
        IProgress<ScanProgress>? progress,
        ICollection<string> errors,
        ICollection<ScanFinding> findings,
        CancellationToken cancellationToken)
    {
        Report(progress, "Nettoyage potentiel", "Estimation lecture seule des fichiers temporaires.", "Nettoyage", 84);

        try
        {
            CleanupScanInfo cleanup;
            if (_cleanupEstimator is not null)
            {
                var estimate = await _cleanupEstimator.EstimateAsync(null, cancellationToken).ConfigureAwait(false);
                cleanup = CleanupEstimateService.Summarize(estimate.Analysis);
                AddErrors(errors, cleanup.Errors);
                _statusStore?.PublishCleanup(estimate.Analysis);
            }
            else
            {
                var preview = _cleanupService.PreviewTemporaryFiles();
                var zones = preview.Targets
                    .Select(target => target.DisplayName)
                    .Where(zone => !string.IsNullOrWhiteSpace(zone))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                cleanup = new CleanupScanInfo(true, preview.TotalBytes, preview.TotalFiles, zones, Array.Empty<string>())
                {
                    SafePotentialBytes = preview.TotalBytes,
                    PersonalDataProtected = true,
                    ExecutedAnyAction = false
                };
            }

            if (cleanup.PotentialBytes >= 5L * 1024 * 1024 * 1024)
            {
                findings.Add(new ScanFinding(
                    "cleanup-potential-warning",
                    "Nettoyage",
                    "Nettoyage potentiel important",
                    "Un volume important de fichiers temporaires a été détecté en lecture seule.",
                    ScanRules.FormatBytes(cleanup.PotentialBytes),
                    ScanSeverity.Warning,
                    "Vérifier les zones temporaires avant toute action de nettoyage."));
            }
            else if (cleanup.PotentialBytes >= 1024L * 1024 * 1024)
            {
                findings.Add(new ScanFinding(
                    "cleanup-potential-attention",
                    "Nettoyage",
                    "Nettoyage potentiel notable",
                    "Des fichiers temporaires récupérables ont été détectés en lecture seule.",
                    ScanRules.FormatBytes(cleanup.PotentialBytes),
                    ScanSeverity.Attention,
                    "Un nettoyage pourra être envisage après validation explicite."));
            }

            return cleanup;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            var cleanupErrors = new[] { "Analyse nettoyage indisponible." };
            AddErrors(errors, cleanupErrors);
            return new CleanupScanInfo(true, 0, 0, Array.Empty<string>(), cleanupErrors);
        }
    }

    private async Task<UpdateScanSummary> ReadUpdatesAsync(
        ScanMode mode,
        IProgress<ScanProgress>? progress,
        ICollection<string> errors,
        ICollection<ScanFinding> findings,
        CancellationToken cancellationToken)
    {
        var request = mode == ScanMode.Deep
            ? UpdateScanRequest.DeepPreview
            : UpdateScanRequest.QuickAvailability;

        Report(
            progress,
            "Mises à jour",
            mode == ScanMode.Deep
                ? "Prévisualisation lecture seule des mises à jour."
                : "Detection lecture seule de WinGet.",
            "Mises à jour",
            88);

        try
        {
            var report = await _updateScanService
                .ScanAsync(request, null, cancellationToken)
                .ConfigureAwait(false);

            AddErrors(errors, report.Errors);

            if (mode == ScanMode.Deep && report.Items.Count > 0)
            {
                findings.Add(new ScanFinding(
                    "updates-preview",
                    "Mises à jour",
                    "Mises à jour applicatives disponibles",
                    "Des mises à jour applicatives sont détectées en prévisualisation lecture seule.",
                    report.Items.Count.ToString(),
                    report.SensitiveCount > 0 ? ScanSeverity.Attention : ScanSeverity.Information,
                    "Valider chaque application individuellement dans le module Mises à jour."));
            }

            if (report.SensitiveCount > 0)
            {
                findings.Add(new ScanFinding(
                    "updates-sensitive-preview",
                    "Mises à jour",
                    "Composants sensibles à vérifier",
                    "Certaines mises à jour touchent a des composants sensibles.",
                    report.SensitiveCount.ToString(),
                    ScanSeverity.Attention,
                    "Consulter le detail avant toute installation."));
            }

            return new UpdateScanSummary
            {
                WasAnalyzed = true,
                WingetAvailable = report.Winget.IsAvailable,
                ApplicationUpdates = report.Items.Count,
                SensitiveUpdates = report.SensitiveCount,
                DriverCount = report.Drivers.Drivers.Count,
                Status = report.OverallStatus,
                Recommendations = report.Recommendations,
                Errors = report.Errors
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            var updateErrors = new[] { "Analyse mises à jour indisponible." };
            AddErrors(errors, updateErrors);
            return new UpdateScanSummary
            {
                WasAnalyzed = true,
                Status = "Indisponible",
                Errors = updateErrors
            };
        }
    }

    private async Task<InterventionScanSummary> ReadInterventionsAsync(
        ScanMode mode,
        IProgress<ScanProgress>? progress,
        ICollection<string> errors,
        ICollection<ScanFinding> findings,
        CancellationToken cancellationToken)
    {
        if (mode != ScanMode.Deep)
        {
            return InterventionScanSummary.NotAnalyzed;
        }

        Report(
            progress,
            "Interventions ciblées",
            "Diagnostics lecture seule des interventions disponibles.",
            "Interventions",
            90);

        try
        {
            var diagnostics = await _interventionDiagnosticService
                .DiagnoseAllAsync(cancellationToken)
                .ConfigureAwait(false);
            var available = diagnostics.Count(diagnostic => diagnostic.IsAvailable);
            var recommended = diagnostics.Count(diagnostic => diagnostic.Status == InterventionStatus.Recommended);
            var diagnosticsMessages = BuildInterventionMessages(diagnostics).ToList();
            var interventionErrors = diagnostics
                .SelectMany(diagnostic => diagnostic.Errors)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            AddErrors(errors, interventionErrors);

            if (recommended > 0)
            {
                findings.Add(new ScanFinding(
                    "interventions-recommended",
                    "Interventions",
                    "Interventions ciblées recommandées",
                    "Des diagnostics lecture seule suggerent une ou plusieurs interventions.",
                    recommended.ToString(),
                    ScanSeverity.Attention,
                    "Ouvrir le module Interventions ciblées. Aucune action n'est exécutée depuis le rapport de scan."));
            }

            return new InterventionScanSummary
            {
                WasAnalyzed = true,
                AvailableActions = available,
                RecommendedActions = recommended,
                RebootPotentiallyRequired = diagnostics.Any(diagnostic =>
                    diagnostic.IsAvailable && diagnostic.Definition.RebootPossible),
                Diagnostics = diagnosticsMessages,
                Errors = interventionErrors
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            var interventionErrors = new[] { "Diagnostic interventions indisponible." };
            AddErrors(errors, interventionErrors);
            return new InterventionScanSummary
            {
                WasAnalyzed = true,
                Errors = interventionErrors
            };
        }
    }

    private static IEnumerable<string> BuildInterventionMessages(
        IEnumerable<InterventionDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics.Where(diagnostic => diagnostic.Status == InterventionStatus.Recommended))
        {
            yield return $"{diagnostic.Definition.Title} : {diagnostic.Recommendation}";
        }
    }

    private static void AddErrors(ICollection<string> target, IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                target.Add(error);
            }
        }
    }

    private static void Report(
        IProgress<ScanProgress>? progress,
        string step,
        string message,
        string category,
        int? percent)
    {
        progress?.Report(new ScanProgress(step, percent, message, category));
    }
}
