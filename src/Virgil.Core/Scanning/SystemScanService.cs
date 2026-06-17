using System.Diagnostics;
using Virgil.Core.Cleanup;
using Virgil.Core.Monitoring;
using Virgil.Core.Updates;
using Virgil.Domain;

namespace Virgil.Core.Scanning;

public sealed class SystemScanService : ISystemScanService
{
    private readonly ICleanupService _cleanupService;
    private readonly IUpdateScanService _updateScanService;

    public SystemScanService()
        : this(new CleanupPreviewService(), new WingetUpdateScanService())
    {
    }

    public SystemScanService(ICleanupService cleanupService)
        : this(cleanupService, new WingetUpdateScanService())
    {
    }

    public SystemScanService(ICleanupService cleanupService, IUpdateScanService updateScanService)
    {
        _cleanupService = cleanupService;
        _updateScanService = updateScanService;
    }

    public async Task<SystemScanReport> RunAsync(
        ScanMode mode,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var scanDate = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var findings = new List<ScanFinding>();

        Report(progress, "Initialisation", "Scan initialise.", "Systeme", 0);

        var windows = ReadWindows(scanDate, progress, errors);
        var processor = await ReadProcessorAsync(progress, errors, findings, cancellationToken).ConfigureAwait(false);
        var memory = ReadMemory(progress, errors, findings);
        var allDisks = ReadDisks(progress, errors, findings);
        var reportDisks = SelectReportDisks(mode, allDisks);
        var network = ReadNetwork(progress, errors, findings);
        var processes = mode == ScanMode.Deep
            ? ReadProcesses(progress, errors, findings)
            : Array.Empty<ProcessScanInfo>();
        var cleanup = mode == ScanMode.Deep
            ? ReadCleanup(progress, errors, findings)
            : new CleanupScanInfo(false, 0, 0, Array.Empty<string>(), Array.Empty<string>());
        var updates = await ReadUpdatesAsync(mode, progress, errors, findings, cancellationToken).ConfigureAwait(false);

        foreach (var error in errors.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new ScanFinding(
                $"partial-error-{findings.Count + 1}",
                "Robustesse",
                "Erreur partielle",
                error,
                null,
                ScanSeverity.Attention,
                "Certaines donnees n'ont pas pu etre lues. Le scan a conserve les resultats disponibles."));
        }

        Report(progress, "Synthese", "Synthese du rapport.", "Systeme", 92);

        var recommendations = ScanRules.BuildRecommendations(findings)
            .Concat(updates.Recommendations.Take(3))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var overallStatus = ScanRules.OverallStatus(findings.Select(finding => finding.Severity));

        stopwatch.Stop();
        Report(progress, "Termine", "Scan termine.", "Systeme", 100);

        return new SystemScanReport(
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
            Updates = updates
        };
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
        Report(progress, "Processeur", "Mesure instantanee du processeur.", "CPU", 24);
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
                "Mesure CPU instantanee : si elle reste elevee, identifier les applications actives les plus consommatrices."));
        }

        return result.Value;
    }

    private static MemoryScanInfo ReadMemory(
        IProgress<ScanProgress>? progress,
        ICollection<string> errors,
        ICollection<ScanFinding> findings)
    {
        Report(progress, "Memoire", "Lecture de la memoire physique.", "RAM", 36);

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
            errors.Add("Lecture memoire indisponible.");
        }
        else if (memoryStatus.AvailableBytes > memoryStatus.TotalBytes)
        {
            errors.Add("Valeurs memoire incoherentes.");
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
                "Memoire",
                "Utilisation RAM elevee",
                "La memoire physique utilisee depasse le seuil de surveillance.",
                $"{memory.UsedPercent:0.0} %",
                severity,
                "Comparer avec les processus actifs. Un ecart avec le Gestionnaire des taches peut venir du moment d'echantillonnage, de la memoire compressee, du cache ou de la presentation Windows."));
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
                disk.IsSystemDrive ? "Disque systeme presque plein" : "Disque presque plein",
                $"Le disque {disk.Name} utilise une part importante de sa capacite.",
                $"{disk.UsedPercent:0.0} %",
                disk.Severity,
                disk.IsSystemDrive
                    ? "Liberer de l'espace sur le disque systeme ou deplacer des donnees non essentielles."
                    : "Surveiller l'espace disponible et archiver les donnees non essentielles."));
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
        Report(progress, "Reseau", "Lecture de l'interface active.", "Reseau", 64);
        var result = NetworkReader.ReadPrimaryInterface();
        AddErrors(errors, result.Errors);

        if (result.Errors.Count > 0)
        {
            findings.Add(new ScanFinding(
                "network-active-interface",
                "Reseau",
                "Aucun reseau actif detecte",
                "Virgil n'a pas detecte d'interface reseau active avec une adresse IPv4 locale.",
                null,
                ScanSeverity.Attention,
                "Verifier la connexion reseau si une connectivite est attendue."));
        }

        return result.Value;
    }

    private static IReadOnlyList<ProcessScanInfo> ReadProcesses(
        IProgress<ScanProgress>? progress,
        ICollection<string> errors,
        ICollection<ScanFinding> findings)
    {
        Report(progress, "Processus", "Lecture des principaux processus en memoire.", "Processus", 76);
        var result = ProcessReader.ReadTopMemoryProcesses(10);
        AddErrors(errors, result.Errors);

        var heavyProcess = result.Value.FirstOrDefault(process => process.WorkingSetBytes >= 1_073_741_824);
        if (heavyProcess is not null)
        {
            findings.Add(new ScanFinding(
                "process-memory",
                "Processus",
                "Processus consommant beaucoup de memoire",
                $"Le processus {heavyProcess.Name} utilise une quantite importante de memoire physique.",
                ScanRules.FormatBytes(heavyProcess.WorkingSetBytes),
                ScanSeverity.Attention,
                "Verifier si cette consommation correspond a une application attendue. Aucun processus n'a ete modifie."));
        }

        return result.Value;
    }

    private CleanupScanInfo ReadCleanup(
        IProgress<ScanProgress>? progress,
        ICollection<string> errors,
        ICollection<ScanFinding> findings)
    {
        Report(progress, "Nettoyage potentiel", "Estimation lecture seule des fichiers temporaires.", "Nettoyage", 84);

        try
        {
            var preview = _cleanupService.PreviewTemporaryFiles();
            var zones = preview.Targets
                .Select(target => target.DisplayName)
                .Where(zone => !string.IsNullOrWhiteSpace(zone))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var cleanup = new CleanupScanInfo(true, preview.TotalBytes, preview.TotalFiles, zones, Array.Empty<string>());

            if (cleanup.PotentialBytes >= 5L * 1024 * 1024 * 1024)
            {
                findings.Add(new ScanFinding(
                    "cleanup-potential-warning",
                    "Nettoyage",
                    "Nettoyage potentiel important",
                    "Un volume important de fichiers temporaires a ete detecte en lecture seule.",
                    ScanRules.FormatBytes(cleanup.PotentialBytes),
                    ScanSeverity.Warning,
                    "Verifier les zones temporaires avant toute action de nettoyage."));
            }
            else if (cleanup.PotentialBytes >= 1024L * 1024 * 1024)
            {
                findings.Add(new ScanFinding(
                    "cleanup-potential-attention",
                    "Nettoyage",
                    "Nettoyage potentiel notable",
                    "Des fichiers temporaires recuperables ont ete detectes en lecture seule.",
                    ScanRules.FormatBytes(cleanup.PotentialBytes),
                    ScanSeverity.Attention,
                    "Un nettoyage pourra etre envisage apres validation explicite."));
            }

            return cleanup;
        }
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
            "Mises a jour",
            mode == ScanMode.Deep
                ? "Previsualisation lecture seule des mises a jour."
                : "Detection lecture seule de WinGet.",
            "Mises a jour",
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
                    "Mises a jour",
                    "Mises a jour applicatives disponibles",
                    "Des mises a jour applicatives sont detectees en previsualisation lecture seule.",
                    report.Items.Count.ToString(),
                    report.SensitiveCount > 0 ? ScanSeverity.Attention : ScanSeverity.Information,
                    "Valider chaque application individuellement dans le module Mises a jour."));
            }

            if (report.SensitiveCount > 0)
            {
                findings.Add(new ScanFinding(
                    "updates-sensitive-preview",
                    "Mises a jour",
                    "Composants sensibles a verifier",
                    "Certaines mises a jour touchent a des composants sensibles.",
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
            var updateErrors = new[] { "Analyse mises a jour indisponible." };
            AddErrors(errors, updateErrors);
            return new UpdateScanSummary
            {
                WasAnalyzed = true,
                Status = "Indisponible",
                Errors = updateErrors
            };
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
