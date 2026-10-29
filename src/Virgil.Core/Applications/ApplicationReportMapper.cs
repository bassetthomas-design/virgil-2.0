using System.Text;
using Virgil.Contracts;
using Virgil.Contracts.Applications;

namespace Virgil.Core.Applications;

public static class ApplicationReportMapper
{
    public static ReportEntry FromInventory(ApplicationInventoryReport report)
    {
        var simple = new StringBuilder()
            .AppendLine($"Applications détectées : {report.Applications.Count}")
            .AppendLine($"Désinstallables : {report.UninstallableCount}")
            .AppendLine($"Protégées : {report.ProtectedCount}")
            .AppendLine($"Inconnues : {report.UnknownCount}")
            .AppendLine($"Attention : {report.CautionCount}")
            .AppendLine("Aucune donnée personnelle supprimée automatiquement.")
            .AppendLine("Aucune désinstallation exécutée depuis l'inventaire.")
            .ToString();
        var technical = string.Join("\n", report.Applications.Take(80).Select(app =>
            $"{app.DisplayName} | {app.Publisher} | {app.Version} | {app.Source} | {app.RiskLevel} | {app.UninstallKind}"));

        return new ReportEntry
        {
            Date = report.CapturedAt,
            Kind = ReportKind.ApplicationManagement,
            Title = "Inventaire applications",
            Summary = $"{report.Applications.Count} application(s), {report.UninstallableCount} désinstallable(s), {report.ProtectedCount} protégée(s).",
            Status = report.Errors.Count == 0 ? "Terminé" : "Terminé avec avertissements",
            Severity = report.Errors.Count == 0 ? ReportSeverity.Success : ReportSeverity.Warning,
            Module = "Applications",
            Duration = report.Duration,
            SimpleView = simple,
            TechnicalDetails = technical,
            Source = "ApplicationInventoryService",
            Errors = report.Errors
        };
    }

    public static ReportEntry FromUninstall(ApplicationUninstallResult result)
    {
        var remnants = result.Remnants;
        var simple = new StringBuilder()
            .AppendLine($"Application : {result.Application.DisplayName}")
            .AppendLine($"Méthode : {result.Method}")
            .AppendLine($"Résultat : {result.Result}")
            .AppendLine($"Confirmation explicite : {(result.WasExplicitlyConfirmed ? "oui" : "non")}")
            .AppendLine($"Confirmation renforcée : {(result.WasReinforcedConfirmed ? "oui" : "non")}")
            .AppendLine($"Code sortie : {(result.ExitCode.HasValue ? result.ExitCode.Value.ToString() : "N/A")}")
            .AppendLine($"Restes détectés : {remnants.Remnants.Count}")
            .AppendLine($"Restes techniques : {remnants.TechnicalCount}")
            .AppendLine($"Restes personnels protégés : {remnants.UserDataCount + remnants.ProtectedCount}")
            .AppendLine("Aucune donnée personnelle supprimée automatiquement.")
            .ToString();

        return new ReportEntry
        {
            Date = DateTimeOffset.Now,
            Kind = ReportKind.ApplicationManagement,
            Title = "Rapport applications",
            Summary = $"{result.Application.DisplayName} : {result.Result}",
            Status = result.WasCancelled ? "Annulé" : result.WasLaunched ? "Désinstalleur lancé" : "Non lancé",
            Severity = result.Errors.Count == 0 ? ReportSeverity.Info : ReportSeverity.Warning,
            Module = "Applications",
            SimpleView = simple,
            TechnicalDetails = string.Join("\n", remnants.Remnants.Select(item => $"{item.Kind} | {item.Path} | {item.Reason}")),
            Source = "ApplicationUninstallService",
            ProposedActions =
            [
                new ReportAction
                {
                    Name = result.Application.DisplayName,
                    Status = ReportActionStatus.Proposed,
                    Risk = result.Application.RiskLevel.ToString(),
                    Result = "Désinstallation officielle uniquement."
                }
            ],
            ExecutedActions = result.WasLaunched
                ? [new ReportAction
                {
                    Name = result.Application.DisplayName,
                    Status = ReportActionStatus.Executed,
                    Risk = result.Application.RiskLevel.ToString(),
                    Result = result.Result,
                    TechnicalDetails = result.Application.UninstallCommand ?? result.Application.WingetId ?? string.Empty
                }]
                : Array.Empty<ReportAction>(),
            Errors = result.Errors.Concat(remnants.Errors).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }
}
