namespace Virgil.Core.Updates;

public static class LauncherCardSummary
{
    public const string Waiting = "Steam met ses jeux à jour ; vous pouvez arrêter l’attente.";
    public const string Unsupported = "non pris en charge automatiquement";
    public const string NoSession = "Steam n’était pas connecté ; ouvrez Steam, il mettra vos jeux à jour.";
    public const string OtherGames = "Jeux : non pris en charge automatiquement — le lanceur met ses jeux à jour lui-même à son ouverture.";
    public static string Confirmation(UpdateSequenceStep step) => step.Id == "Steam"
        ? step.SteamSession?.IsOpen == true ? "Steam : fermer, mettre à jour, relancer en silencieux pour les jeux"
            : "Steam : fermer ; jeux non pris en charge automatiquement — " + NoSession
        : step.Title + " : fermer ; jeux non pris en charge automatiquement";
    public static UpdateSourceVerification Summarize(IReadOnlyList<UpdateSequenceStepResult> outcomes)
    {
        var games = outcomes.Where(r => r.Step.Id.StartsWith("SteamGame:", StringComparison.Ordinal)).ToArray();
        var launchers = outcomes.Where(r => !r.Step.Id.StartsWith("SteamGame:", StringComparison.Ordinal)).ToArray();
        int Count(string status) => games.Count(r => r.VerifiedStatus == status);
        var parts = new List<string>();
        var counts = new List<string>();
        void AddCount(string status, string singular, string plural)
        {
            var count = Count(status);
            if (count > 0) counts.Add($"{count} " + (count == 1 ? singular : plural));
        }
        AddCount("mis à jour", "mis à jour", "mis à jour");
        AddCount("à jour selon Steam", "à jour selon Steam", "à jour selon Steam");
        AddCount("reste à installer", "reste à installer", "restent à installer");
        AddCount("en erreur", "en erreur", "en erreur");
        AddCount("non lancée", "non lancé", "non lancés");
        if (counts.Count > 0) parts.Add("Steam : " + string.Join(", ", counts));
        var unsupported = launchers.Count(r => r.VerifiedStatus == Unsupported);
        if (unsupported > 0) parts.Add($"{unsupported} lanceur" + (unsupported > 1 ? "s" : "") + " non pris en charge automatiquement");
        parts.AddRange(launchers.Where(r => r.VerifiedStatus is "bloquée" or "non lancée" or "en erreur" || (r.Step.Id == "Steam" && r.VerifiedStatus == Unsupported)).Select(r => r.Message));
        var blocked = outcomes.Any(UpdateSequenceService.IsSafetyBlocked);
        var failed = outcomes.Any(r => r.VerifiedStatus == "en erreur" || (!r.Succeeded && !r.WasSkipped && !r.WasUnavailable
            && r.VerifiedStatus != "reste à installer" && !r.Message.StartsWith("Non lancée", StringComparison.Ordinal)));
        var remaining = Count("reste à installer") > 0;
        var notStarted = outcomes.Any(r => r.VerifiedStatus == "non lancée" || r.Message.StartsWith("Non lancée", StringComparison.Ordinal));
        if (remaining) parts.Add("Steam continue en arrière-plan.");
        if (failed) parts.Add("Un échec est détaillé sur la carte concernée.");
        var status = blocked ? "bloquée" : failed ? "en erreur" : remaining ? "reste à installer" : notStarted ? "non lancée" : unsupported > 0 ? Unsupported
            : games.Length > 0 ? "à jour selon Steam" : "à vérifier";
        return new(UpdateComponent.Launcher, status, string.Join(" ; ", parts)) { HasRemaining = remaining };
    }
}
