using System.Text;
using Virgil.Core.Scanning;
using Virgil.Domain;

namespace Virgil.Core.Resources;

public sealed class ResourceReportBuilder
{
    public string Build(ResourceSessionReport report, bool includeTechnicalDetails = false)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Date : {report.CapturedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Analyses : {report.Analyses.Count}");

        var analysis = report.Analyses.LastOrDefault();
        if (analysis is not null)
        {
            builder.AppendLine($"Duree : {analysis.Duration.TotalSeconds:0.0} s");
            builder.AppendLine($"CPU moyen : {analysis.AverageCpuPercent:0.0} %");
            builder.AppendLine($"CPU max : {analysis.MaximumCpuPercent:0.0} %");
            builder.AppendLine($"RAM moyenne : {analysis.AverageMemoryPercent:0.0} %");
            builder.AppendLine($"RAM max : {analysis.MaximumMemoryPercent:0.0} %");
            builder.AppendLine($"Etat CPU : {analysis.CpuHealth}");
            builder.AppendLine($"Etat RAM : {analysis.MemoryHealth}");
            builder.AppendLine($"Etat global : {analysis.OverallHealth}");
            builder.AppendLine($"Uptime : {FormatUptime(analysis.Uptime)}");
            AppendProcesses(builder, "Processus RAM principaux", analysis.TopMemoryProcesses, includeTechnicalDetails);
            AppendProcesses(builder, "Processus CPU principaux", analysis.TopCpuProcesses, includeTechnicalDetails);
        }

        builder.AppendLine($"Actions proposees : {report.ProposedActions.Count}");
        builder.AppendLine($"Actions executees : {report.ExecutedActions.Count}");
        builder.AppendLine($"Actions passees : {report.SkippedActions.Count}");
        builder.AppendLine($"Redemarrage conseille : {(report.RestartRecommended ? "oui" : "non")}");

        AppendList(builder, "Actions proposees", report.ProposedActions);
        AppendList(builder, "Actions passees", report.SkippedActions);

        foreach (var result in report.ExecutedActions)
        {
            builder.AppendLine($"- {result.Action} / {result.Target} : {result.Status} - {result.Summary}");
        }

        AppendList(builder, "Erreurs", report.Errors);
        return builder.ToString();
    }

    private static void AppendProcesses(
        StringBuilder builder,
        string title,
        IEnumerable<ProcessResourceInfo> processes,
        bool includeTechnicalDetails)
    {
        builder.AppendLine(title + " :");
        foreach (var process in processes.Take(5))
        {
            var details = $"{process.Name} (PID {process.ProcessId}) - " +
                $"RAM {ScanRules.FormatBytes(process.WorkingSetBytes)} - CPU {process.CpuPercent:0.0} %";
            if (includeTechnicalDetails && !string.IsNullOrWhiteSpace(process.Path))
            {
                details += $" - {process.Path}";
            }

            builder.AppendLine("- " + details);
        }
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        builder.AppendLine(title + " :");
        foreach (var value in values.Take(8))
        {
            builder.AppendLine("- " + value);
        }
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        return uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays} j {uptime.Hours} h"
            : $"{uptime.Hours} h {uptime.Minutes} min";
    }
}
