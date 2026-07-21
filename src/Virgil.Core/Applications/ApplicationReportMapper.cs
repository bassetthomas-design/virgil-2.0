using System.Text;
using Virgil.Domain;
using Virgil.Domain.Applications;

namespace Virgil.Core.Applications;

public static class ApplicationReportMapper
{
    public static ReportEntry FromInventory(ApplicationInventoryReport report)
    {
        var simple = new StringBuilder()
            .AppendLine($"Applications detectees : {report.Applications.Count}")
            .AppendLine($"Desinstallables : {report.UninstallableCount}")
            .AppendLine($"Protegees : {report.ProtectedCount}")
            .AppendLine($"Inconnues : {report.UnknownCount}")
            .AppendLine($"Attention : {report.CautionCount}")
            .AppendLine("Aucune donnee personnelle supprimee automatiquement.")
            .AppendLine("Aucune desinstallation executee depuis l'inventaire.")
            .ToString();
        var technical = string.Join("\n", report.Applications.Take(80).Select(app =>
            $"{app.DisplayName} | {app.Publisher} | {app.Version} | {app.Source} | {app.RiskLevel} | {app.UninstallKind}"));

        return new ReportEntry
        {
            Date = report.CapturedAt,
            Kind = ReportKind.ApplicationManagement,
            Title = "Inventaire applications",
            Summary = $"{report.Applications.Count} application(s), {report.UninstallableCount} desinstallable(s), {report.ProtectedCount} protegee(s).",
            Status = report.Errors.Count == 0 ? "Termine" : "Termine avec avertissements",
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
            .AppendLine($"Methode : {result.Method}")
            .AppendLine($"Resultat : {result.Result}")
            .AppendLine($"Confirmation explicite : {(result.WasExplicitlyConfirmed ? "oui" : "non")}")
            .AppendLine($"Confirmation renforcee : {(result.WasReinforcedConfirmed ? "oui" : "non")}")
            .AppendLine($"Code sortie : {(result.ExitCode.HasValue ? result.ExitCode.Value.ToString() : "N/A")}")
            .AppendLine($"Restes detectes : {remnants.Remnants.Count}")
            .AppendLine($"Restes techniques : {remnants.TechnicalCount}")
            .AppendLine($"Restes personnels proteges : {remnants.UserDataCount + remnants.ProtectedCount}")
            .AppendLine("Aucune donnee personnelle supprimee automatiquement.")
            .ToString();

        return new ReportEntry
        {
            Date = DateTimeOffset.Now,
            Kind = ReportKind.ApplicationManagement,
            Title = "Rapport applications",
            Summary = $"{result.Application.DisplayName} : {result.Result}",
            Status = result.WasCancelled ? "Annule" : result.WasLaunched ? "Desinstalleur lance" : "Non lance",
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
                    Result = "Desinstallation officielle uniquement."
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
