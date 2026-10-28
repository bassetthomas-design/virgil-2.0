using System.Text;
using Virgil.Core.Scanning;
using Virgil.Contracts;

namespace Virgil.Core.Reports;

public static class ReportMapper
{
    public static ReportEntry FromSystemScan(SystemScanReport report)
    {
        var kind = report.Mode == ScanMode.Deep ? ReportKind.DeepScan : ReportKind.QuickScan;
        var severity = MapScanSeverity(report.Findings.Select(finding => finding.Severity).DefaultIfEmpty(ScanSeverity.Healthy).Max());
        var simple = new StringBuilder()
            .AppendLine($"État général : {report.OverallStatus}")
            .AppendLine($"CPU : {report.Processor.UsagePercent:0.0} %")
            .AppendLine($"RAM : {report.Memory.UsedPercent:0.0} %")
            .AppendLine($"Disques analyses : {report.Disks.Count}")
            .AppendLine($"Nettoyage potentiel : {(report.Cleanup.WasAnalyzed ? ScanRules.FormatBytes(report.Cleanup.PotentialBytes) : "Non analysé")}")
            .AppendLine($"Nettoyage sur : {ScanRules.FormatBytes(report.Cleanup.SafePotentialBytes)}")
            .AppendLine($"Nettoyage avance : {ScanRules.FormatBytes(report.Cleanup.AdvancedPotentialBytes)}")
            .AppendLine($"Elements personnels a revoir : {report.Cleanup.ReviewItemCount} ({ScanRules.FormatBytes(report.Cleanup.ReviewItemBytes)})")
            .AppendLine("Protection personnelle : aucune suppression depuis l'analyse approfondie.")
            .AppendLine($"Mises à jour disponibles : {report.Updates.ApplicationUpdates}")
            .AppendLine($"Interventions recommandées : {report.Interventions.RecommendedActions}")
            .AppendLine($"Processus lourds : {report.Resources.HeavyProcessCount}")
            .AppendLine($"Applications détectées : {report.Applications.DetectedCount}")
            .AppendLine($"Applications désinstallables : {report.Applications.UninstallableCount}")
            .AppendLine($"Applications protégées : {report.Applications.ProtectedCount}")
            .AppendLine("Applications : inventaire lecture seule depuis le scan.")
            .AppendLine("Aucune action exécutée depuis le scan.")
            .ToString();
        var technical = new StringBuilder()
            .AppendLine($"Windows : {report.Windows.Edition} {report.Windows.Version} build {report.Windows.Build}")
            .AppendLine($"Architecture : {report.Windows.SystemArchitecture} / {report.Windows.ProcessArchitecture}")
            .AppendLine($"CPU logique : {report.Processor.LogicalProcessorCount}")
            .AppendLine($"Réseau : {report.Network.Name} - {report.Network.Type} - {report.Network.Status}")
            .AppendLine($"Duree : {report.Duration}")
            .AppendLine($"Source mises à jour : {(report.Updates.WingetAvailable ? "WinGet disponible" : "WinGet indisponible")}")
            .AppendLine($"Inventaire applications : {(report.Applications.WasAnalyzed ? $"{report.Applications.DetectedCount} détectées, {report.Applications.UnknownCount} inconnues" : "Non analysé")}")
            .ToString();
        return Base(
            kind,
            report.Mode == ScanMode.Deep ? "Analyse approfondie" : "Scan rapide",
            $"{report.OverallStatus}. {report.Findings.Count} constat(s), {report.Errors.Count} erreur(s).",
            report.OverallStatus,
            severity,
            "Scan",
            report.CapturedAt,
            report.Duration,
            simple,
            technical,
            "SystemScanService") with
        {
            ProposedActions = report.Recommendations.Select(Proposed).ToList(),
            Errors = report.Errors.ToList(),
            RestartRequired = report.Interventions.RebootPotentiallyRequired || report.Resources.Uptime >= TimeSpan.FromDays(7)
        };
    }

    public static ReportEntry FromCleanup(CleanupSessionReport report, int analyzedZones)
    {
        var proposed = report.Results.Select(result => new ReportAction
        {
            Name = result.Zone.DisplayName,
            Status = ReportActionStatus.Proposed,
            Risk = result.Zone.RiskLevel.ToString(),
            Result = "Zone proposée dans le parcours guide.",
            TechnicalDetails = result.Zone.RootPath
        }).ToList();
        var executed = report.Results
            .Where(result => result.Status != CleanupStepStatus.Skipped)
            .Select(result => new ReportAction
            {
                Name = result.Zone.DisplayName,
                Status = MapCleanupStatus(result.Status),
                Risk = result.Zone.RiskLevel.ToString(),
                Result = result.Zone.Id == CleanupZoneId.RecycleBin ? $"{result.DeletedFiles} fichier(s), {ScanRules.FormatBytes(result.DeletedBytes)} liberes, {result.SkippedFiles} ignores." : result.QuarantineSummary,
                ReadableError = result.Errors.FirstOrDefault(),
                TechnicalDetails = $"Duree : {result.Duration}. Fichiers en erreur : {result.ErrorFiles}. Racine : {result.Zone.RootPath}"
            })
            .ToList();
        var skipped = report.Results
            .Where(result => result.Status == CleanupStepStatus.Skipped)
            .Select(result => new ReportAction
            {
                Name = result.Zone.DisplayName,
                Status = ReportActionStatus.Skipped,
                Risk = result.Zone.RiskLevel.ToString(),
                Result = "Zone passée. Aucune suppression."
            })
            .ToList();
        var severity = report.Errors.Count > 0 || report.ErrorFiles > 0
            ? ReportSeverity.Warning
            : report.CancelledZones > 0 ? ReportSeverity.Warning : ReportSeverity.Success;
        return Base(
            ReportKind.Cleanup,
            "Rapport nettoyage",
            $"{report.QuarantinedFiles} fichiers mis en quarantaine ({ScanRules.FormatBytes(report.QuarantinedBytes)}). Corbeille : {ScanRules.FormatBytes(report.DeletedBytes)} libérés.",
            severity == ReportSeverity.Success ? "Terminé" : "Terminé avec avertissements",
            severity,
            "Nettoyage",
            report.FinishedAt,
            report.Duration,
            $"Zones analysees : {analyzedZones}\nZones exécutées : {executed.Count}\nZones passées : {skipped.Count}\nZones avancees refusees : {report.AdvancedRefused}\nEspace estime : {ScanRules.FormatBytes(report.EstimatedBytes)}\nQuarantaine : {ScanRules.FormatBytes(report.QuarantinedBytes)}\n{string.Join("; ", report.Results.Where(r => r.QuarantinedFiles > 0).Select(r => r.QuarantineSummary))}\nCorbeille — espace libéré : {ScanRules.FormatBytes(report.DeletedBytes)}\nElements a revoir : {report.ReviewItems} ({ScanRules.FormatBytes(report.ReviewBytes)})\nElements protégés : {report.ProtectedItems}\nFichiers ignores ou verrouilles : {report.LockedFilesIgnored}\nAucun fichier personnel supprime automatiquement.",
            $"Debut : {report.StartedAt:O}\nFin : {report.FinishedAt:O}\nDuree : {report.Duration}\nPoints de reanalyse ignores : {report.ReparsePointsIgnored}\nChemins refuses : {string.Join(" | ", report.RefusedPaths)}\nRéparations proposées : {string.Join(" | ", report.PermissionRepairsProposed)}\nRéparations exécutées : {string.Join(" | ", report.PermissionRepairsExecuted)}",
            "CleanupExecutionService") with
        {
            ProposedActions = proposed,
            ExecutedActions = executed,
            SkippedActions = skipped,
            Errors = report.Errors.Concat(report.Results.SelectMany(result => result.Errors)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            RestartRequired = report.RestartRecommended
        };
    }

    public static ReportEntry FromUpdateScan(UpdateScanReport report)
    {
        var severity = report.Errors.Count > 0
            ? ReportSeverity.Warning
            : report.Items.Count > 0 ? ReportSeverity.Info : ReportSeverity.Success;
        var proposed = report.Items.Select(item => new ReportAction
        {
            Name = item.Name,
            Status = ReportActionStatus.Proposed,
            Risk = item.RiskLevel.ToString(),
            Result = $"{item.InstalledVersion} -> {item.AvailableVersion}",
            TechnicalDetails = BuildUpdateTechnicalDetails(item)
        }).ToList();
        return Base(
            ReportKind.Updates,
            "Scan mises à jour",
            $"{report.Items.Count} mise(s) à jour détectée(s). Aucune installation automatique.",
            report.OverallStatus,
            severity,
            "Mises à jour",
            report.CapturedAt,
            report.Duration,
            $"Applications : {report.Items.Count}\nComposants sensibles : {report.SensitiveCount}\nPilotes inventories : {report.Drivers.Drivers.Count}\nAucune installation exécutée.",
            $"Portee : {report.Scope}\nWinGet : {report.Winget.Message}\nDuree : {report.Duration}",
            "WingetUpdateScanService") with
        {
            ProposedActions = proposed,
            Errors = report.Errors.Concat(report.Winget.Errors).Concat(report.Drivers.Errors).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            RestartRequired = report.WindowsUpdate.PendingRebootDetected
        };
    }

    public static ReportEntry FromUpdateSession(UpdateSessionReport report)
    {
        var proposed = report.Results.Select(result => new ReportAction
        {
            Name = result.Item.Name,
            Status = ReportActionStatus.Proposed,
            Risk = result.Item.RiskLevel.ToString(),
            Result = $"{result.Item.InstalledVersion} -> {result.Item.AvailableVersion}",
            TechnicalDetails = BuildUpdateTechnicalDetails(result.Item)
        }).ToList();
        var executed = report.Results
            .Where(result => result.Status != UpdateItemStatus.Skipped)
            .Select(result => new ReportAction
            {
                Name = result.Item.Name,
                Status = MapUpdateStatus(result.Status),
                Risk = result.Item.RiskLevel.ToString(),
                Result = result.UserMessage,
                ReadableError = result.Status == UpdateItemStatus.Failed ? result.UserMessage : null,
                TechnicalDetails = $"{BuildUpdateTechnicalDetails(result.Item)}\n{result.TechnicalDetails}\nDuree : {result.Duration}"
            })
            .ToList();
        var skipped = report.Results
            .Where(result => result.Status == UpdateItemStatus.Skipped)
            .Select(result => new ReportAction
            {
                Name = result.Item.Name,
                Status = ReportActionStatus.Skipped,
                Risk = result.Item.RiskLevel.ToString(),
                Result = "Mise à jour passée."
            })
            .ToList();
        var severity = report.FailedCount > 0
            ? ReportSeverity.Error
            : report.WasCancelled ? ReportSeverity.Warning : ReportSeverity.Success;
        return Base(
            ReportKind.Updates,
            "Rapport mises à jour",
            $"{report.CompletedCount} terminée(s), {report.SkippedCount} passée(s), {report.FailedCount} échec(s).",
            severity == ReportSeverity.Success ? "Terminé" : "Terminé avec incidents",
            severity,
            "Mises à jour",
            report.StartedAt + report.Duration,
            report.Duration,
            $"Mises à jour effectuées : {report.CompletedCount}\nActions passées : {report.SkippedCount}\nÉchecs : {report.FailedCount}",
            $"Debut : {report.StartedAt:O}\nDuree : {report.Duration}",
            "WingetUpdateExecutionService") with
        {
            ProposedActions = proposed,
            ExecutedActions = executed,
            SkippedActions = skipped,
            Errors = report.Errors.ToList()
        };
    }

    public static ReportEntry FromInterventions(InterventionSessionReport report)
    {
        var proposed = report.ProposedActions.Select(diagnostic => new ReportAction
        {
            Name = diagnostic.Definition.Title,
            Status = ReportActionStatus.Proposed,
            Risk = diagnostic.Definition.RiskLevel.ToString(),
            Result = diagnostic.Recommendation,
            RestartRequired = diagnostic.Definition.RebootPossible,
            TechnicalDetails = string.Join("\n", diagnostic.TechnicalData.Select(pair => $"{pair.Key} : {pair.Value}"))
        }).ToList();
        var executed = report.Results
            .Where(result => result.Status is not InterventionStatus.Skipped and not InterventionStatus.Cancelled)
            .Select(result => new ReportAction
            {
                Name = result.Action.Title,
                Status = MapInterventionStatus(result.Status),
                Risk = result.Action.RiskLevel.ToString(),
                Result = result.SummaryOutput,
                ReadableError = result.ReadableError,
                RestartRequired = result.RebootRequired,
                TechnicalDetails = $"Code sortie : {result.ExitCode}\nAdmin : {(result.WasElevated ? "oui" : "non")}\nDuree : {result.Duration}\nÉtat avant : {result.StateBefore}\nÉtat après : {result.StateAfter}"
            })
            .ToList();
        var skipped = report.Results
            .Where(result => result.Status is InterventionStatus.Skipped or InterventionStatus.Cancelled)
            .Select(result => new ReportAction
            {
                Name = result.Action.Title,
                Status = result.Status == InterventionStatus.Cancelled
                    ? ReportActionStatus.Cancelled
                    : ReportActionStatus.Skipped,
                Risk = result.Action.RiskLevel.ToString(),
                Result = result.SummaryOutput,
                ReadableError = result.ReadableError
            })
            .ToList();
        var severity = report.Failures > 0
            ? ReportSeverity.Error
            : report.CancelledActions > 0 ? ReportSeverity.Warning : ReportSeverity.Success;
        return Base(
            ReportKind.Interventions,
            "Rapport interventions",
            $"{report.Successes} reussie(s), {report.Failures} échec(s), {report.SkippedActions} passée(s).",
            severity == ReportSeverity.Success ? "Terminé" : "Terminé avec incidents",
            severity,
            "Interventions",
            report.StartedAt + report.Duration,
            report.Duration,
            $"Actions exécutées : {report.ExecutedActions}\nActions passées : {report.SkippedActions}\nErreurs : {report.Errors.Count}\nRedémarrage requis : {(report.RebootRequired ? "oui" : "non")}",
            $"Debut : {report.StartedAt:O}\nDuree : {report.Duration}",
            "InterventionExecutionService") with
        {
            ProposedActions = proposed,
            ExecutedActions = executed,
            SkippedActions = skipped,
            Errors = report.Errors.ToList(),
            RestartRequired = report.RebootRequired
        };
    }

    public static ReportEntry FromResources(ResourceSessionReport report)
    {
        var analysis = report.Analyses.LastOrDefault();
        var proposed = report.ProposedActions.Select(Proposed).ToList();
        var executed = report.ExecutedActions.Select(result => new ReportAction
        {
            Name = $"{result.Action} - {result.Target}",
            Status = MapProcessActionStatus(result.Status),
            Risk = result.Action == ProcessActionKind.KillProcess ? "Moyen" : "Faible",
            Result = result.Summary,
            ReadableError = result.ReadableError,
            TechnicalDetails = $"Horodatage : {result.Timestamp:O}"
        }).ToList();
        var skipped = report.SkippedActions.Select(value => new ReportAction
        {
            Name = value,
            Status = ReportActionStatus.Skipped,
            Risk = "Information",
            Result = "Action passée."
        }).ToList();
        var severity = report.Errors.Count > 0
            ? ReportSeverity.Warning
            : analysis?.OverallHealth >= ResourceHealthLevel.InterventionRecommended
                ? ReportSeverity.Warning
                : ReportSeverity.Success;
        var simple = analysis is null
            ? "Aucune analyse CPU/RAM dans cette session."
            : $"CPU moyen : {analysis.AverageCpuPercent:0.0} %\nCPU max : {analysis.MaximumCpuPercent:0.0} %\nRAM moyenne : {analysis.AverageMemoryPercent:0.0} %\nRAM max : {analysis.MaximumMemoryPercent:0.0} %\nProcessus lourds : {CountHeavyProcesses(analysis)}\nRedémarrage conseillé : {(report.RestartRecommended ? "oui" : "non")}";
        var technical = analysis is null
            ? string.Empty
            : string.Join("\n", analysis.TopMemoryProcesses.Take(5).Select(process =>
                $"{process.Name} PID {process.ProcessId} RAM {ScanRules.FormatBytes(process.WorkingSetBytes)} CPU {process.CpuPercent:0.0} % chemin {process.Path}"));
        return Base(
            ReportKind.Resources,
            "Rapport ressources",
            analysis is null
                ? "Session ressources sans analyse."
                : $"CPU {analysis.AverageCpuPercent:0.0} %, RAM {analysis.AverageMemoryPercent:0.0} %, {CountHeavyProcesses(analysis)} processus lourd(s).",
            severity == ReportSeverity.Success ? "Stable" : "A surveiller",
            severity,
            "Ressources",
            report.CapturedAt,
            analysis?.Duration ?? TimeSpan.Zero,
            simple,
            technical,
            "ResourceMonitoringService") with
        {
            ProposedActions = proposed,
            ExecutedActions = executed,
            SkippedActions = skipped,
            Errors = report.Errors.Concat(analysis?.Errors ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            RestartRequired = report.RestartRecommended
        };
    }

    private static ReportEntry Base(
        ReportKind kind,
        string title,
        string summary,
        string status,
        ReportSeverity severity,
        string module,
        DateTimeOffset date,
        TimeSpan duration,
        string simpleView,
        string technicalDetails,
        string source)
    {
        return new ReportEntry
        {
            Date = date,
            Kind = kind,
            Title = title,
            Summary = summary,
            Status = status,
            Severity = severity,
            Module = module,
            Duration = duration,
            SimpleView = simpleView,
            TechnicalDetails = technicalDetails,
            VirgilVersion = typeof(ReportMapper).Assembly.GetName().Version?.ToString() ?? string.Empty,
            Source = source
        };
    }

    private static ReportAction Proposed(string value)
    {
        return new ReportAction
        {
            Name = value,
            Status = ReportActionStatus.Proposed,
            Risk = "Information",
            Result = "Proposition uniquement."
        };
    }

    private static string BuildUpdateTechnicalDetails(UpdateItem item)
    {
        var command = item.CommandPreview is null
            ? "Aucune commande"
            : item.CommandPreview.ExecutablePath + " " + string.Join(" ", item.CommandPreview.Arguments);
        return $"Id : {item.Id}\nEditeur : {item.Publisher}\nSource : {item.Source}\nCommande : {command}\n{item.TechnicalDetails}";
    }

    private static ReportSeverity MapScanSeverity(ScanSeverity severity)
    {
        return severity switch
        {
            ScanSeverity.Critical => ReportSeverity.Critical,
            ScanSeverity.Warning => ReportSeverity.Error,
            ScanSeverity.Attention => ReportSeverity.Warning,
            ScanSeverity.Healthy => ReportSeverity.Success,
            _ => ReportSeverity.Info
        };
    }

    private static ReportActionStatus MapCleanupStatus(CleanupStepStatus status)
    {
        return status switch
        {
            CleanupStepStatus.Completed => ReportActionStatus.Executed,
            CleanupStepStatus.Skipped => ReportActionStatus.Skipped,
            CleanupStepStatus.Cancelled => ReportActionStatus.Cancelled,
            CleanupStepStatus.PartialFailure => ReportActionStatus.Partial,
            CleanupStepStatus.Failed or CleanupStepStatus.Expired => ReportActionStatus.Failed,
            _ => ReportActionStatus.InformationOnly
        };
    }

    private static ReportActionStatus MapUpdateStatus(UpdateItemStatus status)
    {
        return status switch
        {
            UpdateItemStatus.Completed => ReportActionStatus.Executed,
            UpdateItemStatus.Skipped => ReportActionStatus.Skipped,
            UpdateItemStatus.Cancelled => ReportActionStatus.Cancelled,
            UpdateItemStatus.Failed => ReportActionStatus.Failed,
            UpdateItemStatus.InformationOnly => ReportActionStatus.InformationOnly,
            _ => ReportActionStatus.Proposed
        };
    }

    private static ReportActionStatus MapInterventionStatus(InterventionStatus status)
    {
        return status switch
        {
            InterventionStatus.Completed or InterventionStatus.RebootRequired => ReportActionStatus.Executed,
            InterventionStatus.Skipped => ReportActionStatus.Skipped,
            InterventionStatus.Cancelled => ReportActionStatus.Cancelled,
            InterventionStatus.PartialFailure => ReportActionStatus.Partial,
            InterventionStatus.Failed => ReportActionStatus.Failed,
            _ => ReportActionStatus.InformationOnly
        };
    }

    private static ReportActionStatus MapProcessActionStatus(ProcessActionStatus status)
    {
        return status switch
        {
            ProcessActionStatus.Completed => ReportActionStatus.Executed,
            ProcessActionStatus.Skipped => ReportActionStatus.Skipped,
            ProcessActionStatus.Cancelled => ReportActionStatus.Cancelled,
            ProcessActionStatus.PartialFailure => ReportActionStatus.Partial,
            ProcessActionStatus.Failed => ReportActionStatus.Failed,
            ProcessActionStatus.InformationOnly => ReportActionStatus.InformationOnly,
            _ => ReportActionStatus.InformationOnly
        };
    }

    private static int CountHeavyProcesses(ResourceAnalysisReport report)
    {
        return report.TopMemoryProcesses
            .Concat(report.TopCpuProcesses)
            .GroupBy(process => process.ProcessId)
            .Count(group => group.First().Status == ProcessResourceStatus.Heavy);
    }
}
