using System.Text;
using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public sealed class InterventionReportBuilder
{
    public string Build(InterventionSessionReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Date : {report.StartedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Durée : {report.Duration.TotalSeconds:0.0} s");
        builder.AppendLine($"Actions proposées : {report.ProposedActions.Count}");
        builder.AppendLine($"Actions exécutées : {report.ExecutedActions}");
        builder.AppendLine($"Actions passées : {report.SkippedActions}");
        builder.AppendLine($"Actions annulees : {report.CancelledActions}");
        builder.AppendLine($"Reussites : {report.Successes}");
        builder.AppendLine($"Échecs : {report.Failures}");
        builder.AppendLine($"Redémarrage requis : {(report.RebootRequired ? "oui" : "non")}");
        builder.AppendLine();

        foreach (var result in report.Results)
        {
            builder.AppendLine($"{result.Action.Title} : {StatusLabel(result.Status)}");
            builder.AppendLine($"  Confirmation : {(result.WasConfirmed ? "oui" : "non")}");
            builder.AppendLine($"  Admin : {(result.WasElevated ? "oui" : "non")}");
            builder.AppendLine($"  Code sortie : {result.ExitCode}");
            builder.AppendLine($"  Avant : {result.StateBefore}");
            builder.AppendLine($"  Après : {result.StateAfter}");
            builder.AppendLine($"  Redémarrage : {(result.RebootRequired ? "oui" : "non")}");

            if (!string.IsNullOrWhiteSpace(result.SummaryOutput))
            {
                builder.AppendLine($"  Synthèse : {result.SummaryOutput}");
            }

            if (!string.IsNullOrWhiteSpace(result.ReadableError))
            {
                builder.AppendLine($"  Erreur : {result.ReadableError}");
            }
        }

        if (report.Errors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Erreurs session :");
            foreach (var error in report.Errors.Take(8))
            {
                builder.AppendLine("- " + error);
            }
        }

        return builder.ToString();
    }

    private static string StatusLabel(InterventionStatus status)
    {
        return status switch
        {
            InterventionStatus.Completed => "terminée",
            InterventionStatus.PartialFailure => "partielle",
            InterventionStatus.Failed => "échec",
            InterventionStatus.Skipped => "passée",
            InterventionStatus.Cancelled => "annulée",
            InterventionStatus.RebootRequired => "terminée, redémarrage requis",
            InterventionStatus.Unavailable => "indisponible",
            _ => status.ToString()
        };
    }
}
