namespace Virgil.Core.Updates;

public static class DriverCardSummary
{
    public const string Unsupported = "non pris en charge automatiquement";
    public static string Status(UpdateSequenceStepResult result) => result.VerifiedStatus
        ?? (UpdateSequenceService.IsSafetyBlocked(result) ? "bloquée" : result.Message.StartsWith("Non lancée", StringComparison.Ordinal) ? "non lancée"
            : result.Succeeded ? "mis à jour" : "en erreur");
    public static UpdateSourceVerification Summarize(IReadOnlyList<UpdateSequenceStepResult> results)
    {
        var statuses = results.Select(Status).ToArray();
        var counts = new List<string>();
        void Add(string status, string singular, string plural)
        {
            var count = statuses.Count(s => s == status);
            if (count > 0) counts.Add($"{count} " + (count == 1 ? singular : plural));
        }
        Add("mis à jour", "mis à jour", "mis à jour"); Add("à jour", "à jour", "à jour");
        Add("reste à installer", "reste à installer", "restent à installer"); Add("en erreur", "en erreur", "en erreur");
        Add("bloquée", "bloquée", "bloquées"); Add("non lancée", "non lancée", "non lancées");
        Add(Unsupported, Unsupported, Unsupported);
        var status = new[] { "bloquée", "en erreur", "reste à installer", "non lancée", "mis à jour", "à jour", Unsupported }
            .FirstOrDefault(statuses.Contains) ?? "en erreur";
        return new(UpdateComponent.Driver, status, string.Join(", ", counts)) { HasRemaining = statuses.Contains("reste à installer") };
    }
}
