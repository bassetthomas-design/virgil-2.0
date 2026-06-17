using System.Text;
using Virgil.Domain;

namespace Virgil.Core.Interventions;

public sealed class InterventionReportBuilder
{
    public string Build(InterventionSessionReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Date : {report.StartedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Duree : {report.Duration.TotalSeconds:0.0} s");
        builder.AppendLine($"Actions proposees : {report.ProposedActions.Count}");
        builder.AppendLine($"Actions executees : {report.ExecutedActions}");
        builder.AppendLine($"Actions passees : {report.SkippedActions}");
        builder.AppendLine($"Actions annulees : {report.CancelledActions}");
        builder.AppendLine($"Reussites : {report.Successes}");
        builder.AppendLine($"Echecs : {report.Failures}");
        builder.AppendLine($"Redemarrage requis : {(report.RebootRequired ? "oui" : "non")}");
        builder.AppendLine();

        foreach (var result in report.Results)
        {
            builder.AppendLine($"{result.Action.Title} : {StatusLabel(result.Status)}");
            builder.AppendLine($"  Confirmation : {(result.WasConfirmed ? "oui" : "non")}");
            builder.AppendLine($"  Admin : {(result.WasElevated ? "oui" : "non")}");
            builder.AppendLine($"  Code sortie : {result.ExitCode}");
            builder.AppendLine($"  Avant : {result.StateBefore}");
            builder.AppendLine($"  Apres : {result.StateAfter}");
            builder.AppendLine($"  Redemarrage : {(result.RebootRequired ? "oui" : "non")}");

            if (!string.IsNullOrWhiteSpace(result.SummaryOutput))
            {
                builder.AppendLine($"  Synthese : {result.SummaryOutput}");
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
            InterventionStatus.Completed => "terminee",
            InterventionStatus.PartialFailure => "partielle",
            InterventionStatus.Failed => "echec",
            InterventionStatus.Skipped => "passee",
            InterventionStatus.Cancelled => "annulee",
            InterventionStatus.RebootRequired => "terminee, redemarrage requis",
            InterventionStatus.Unavailable => "indisponible",
            _ => status.ToString()
        };
    }
}
