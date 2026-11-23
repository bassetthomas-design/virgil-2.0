using System.Globalization;

namespace Virgil.Core.Updates;

// Exécuté exclusivement après la consommation du plan par la séquence.
internal sealed class SteamUpdateService(ISteamUpdateBackend backend)
{
    internal async Task<IReadOnlyList<UpdateSequenceStepResult>> ExecuteAsync(UpdateSequenceStep steam, IReadOnlyList<SteamGameState> initial,
        Func<Task<bool>> canRestart, bool finalPassSucceeded, Action waiting, CancellationToken token, bool singleStepSucceeded = false)
    {
        var results = new List<UpdateSequenceStepResult>();
        UpdateSequenceStepResult Launcher(string status, string message) => new(steam, false, false, message) { VerifiedStatus = status, SafetyBlocked = status == "bloquée", WasSkipped = status != "bloquée" && status != "en erreur" };
        void NotStarted(string reason)
        {
            results.Add(Launcher("non lancée", reason));
            results.AddRange(initial.Select(game => GameResult(game, "non lancée", reason)));
        }
        if (token.IsCancellationRequested) { NotStarted("Non lancée : annulation avant la relance de Steam."); return results; }
        if (!finalPassSucceeded && !singleStepSucceeded) { NotStarted("Non lancée : la passe finale des applications n’a pas été terminée."); return results; }
        if (!await canRestart().ConfigureAwait(false))
        { NotStarted("Non lancée : un jeu tourne, Steam est ouvert ou la fermeture n’est pas vérifiée."); return results; }
        var check = await backend.SteamClient.CheckExecutableAsync(steam.SteamSession?.ExecutablePath, token).ConfigureAwait(false);
        if (!check.IsValid)
        {
            results.Add(Launcher("bloquée", check.Reason));
            results.AddRange(initial.Select(game => GameResult(game, "non lancée", "Non lancée : relance de Steam bloquée.")));
            return results;
        }
        // Relecture des processus après la vérification du fichier et juste avant l’effet.
        if (token.IsCancellationRequested || !await canRestart().ConfigureAwait(false))
        { NotStarted("Non lancée : annulation ou jeu détecté avant la relance."); return results; }
        token.ThrowIfCancellationRequested();
        if (!await backend.SteamClient.LaunchSilentAsync(steam.SteamSession!.ExecutablePath!, token).ConfigureAwait(false))
        {
            results.Add(Launcher("bloquée", "Steam : relance bloquée, le lancement silencieux n’a pas été confirmé."));
            results.AddRange(initial.Select(game => GameResult(game, "non lancée", "Non lancée : Steam n’a pas pu être ouvert.")));
            return results;
        }
        waiting();
        var states = initial.ToDictionary(game => game.Id, game => game, StringComparer.Ordinal);
        var requested = initial.Where(game => (game.StateFlags & 2) != 0).Select(game => game.Id).ToHashSet(StringComparer.Ordinal);
        var alwaysInstalled = initial.Where(game => game.StateFlags == 4).Select(game => game.Id).ToHashSet(StringComparer.Ordinal);
        var start = backend.SteamClock.GetTimestamp();
        var polls = 0;
        var waitLimit = backend.SteamWaitLimit;
        try
        {
            while (backend.SteamClock.GetElapsedTime(start) < waitLimit)
            {
                var remaining = waitLimit - backend.SteamClock.GetElapsedTime(start);
                await Task.Delay(remaining < TimeSpan.FromSeconds(15) ? remaining : TimeSpan.FromSeconds(15), backend.SteamClock, token).ConfigureAwait(false);
                var current = await backend.SteamClient.ReadGamesAsync(token).ConfigureAwait(false);
                polls++;
                foreach (var original in initial)
                {
                    var state = current.FirstOrDefault(game => game.Id == original.Id)
                        ?? original with { Error = "Le jeu n’est plus lisible dans Steam." };
                    // Un nom connu n’est jamais remplacé par un identifiant ou un message technique.
                    states[original.Id] = state with { Name = original.Name };
                    if ((state.StateFlags & 2) != 0) requested.Add(state.Id);
                    if (state.StateFlags != 4 || state.Error is not null) alwaysInstalled.Remove(state.Id);
                }
                if (initial.All(game => Finished(game, states[game.Id], requested, alwaysInstalled, polls, waitLimit))) break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            foreach (var game in initial.Where(game => !Finished(game, states[game.Id], requested, alwaysInstalled, polls, waitLimit)))
                states[game.Id] = states[game.Id] with { Error = "Lecture de l’état du jeu impossible dans Steam." };
        }
        foreach (var original in initial)
        {
            var state = states[original.Id];
            var error = state.Error ?? ((state.StateFlags & (32u | 128u)) != 0 || state.StateFlags == 0 ? "Steam indique des fichiers de jeu absents ou endommagés." : null);
            var status = error is not null ? "en erreur"
                : polls > 0 && state.StateFlags == 4 && requested.Contains(original.Id) && state.BuildId != original.BuildId ? "mis à jour"
                : polls > 0 && HasEnoughPolls(polls, waitLimit) && alwaysInstalled.Contains(original.Id) && !requested.Contains(original.Id) ? "à jour selon Steam" : "reste à installer";
            results.Add(GameResult(original, status, error ?? (status == "reste à installer" ? "Reste à installer — Steam continue en arrière-plan." : status)));
        }
        var steamStatus = results.Any(game => game.VerifiedStatus == "en erreur") ? "en erreur"
            : results.Any(game => game.VerifiedStatus == "reste à installer") ? "reste à installer"
            : results.Any(game => game.VerifiedStatus == "mis à jour") ? "mis à jour" : "à jour selon Steam";
        results.Insert(0, new(steam, steamStatus is "mis à jour" or "à jour selon Steam", false,
            "Steam ouvert en silencieux ; jeux contrôlés en lecture seule.")
        { VerifiedStatus = steamStatus, WasSkipped = steamStatus == "reste à installer" });
        return results;
    }
    private static bool Finished(SteamGameState original, SteamGameState state, HashSet<string> requested, HashSet<string> alwaysInstalled, int polls, TimeSpan waitLimit) =>
        state.Error is not null || (state.StateFlags & (32u | 128u)) != 0 || state.StateFlags == 0 || (state.StateFlags == 4
            && (requested.Contains(original.Id) ? state.BuildId != original.BuildId : alwaysInstalled.Contains(original.Id) && HasEnoughPolls(polls, waitLimit)));
    private static bool HasEnoughPolls(int polls, TimeSpan waitLimit) =>
        polls >= 2 || waitLimit < TimeSpan.FromSeconds(30);

    internal static (string Status, string Detail) ClassifyScannedGame(SteamGameState game, TimeProvider? clock = null)
    {
        var flags = game.StateFlags;
        var (status, detail) = (game.Error, flags) switch
        {
            (not null, _) or (_, 0) => ("en erreur", game.Error ?? "Steam indique des fichiers manquants ou endommagés."),
            _ when (flags & (32u | 128u)) != 0 => ("en erreur", "Steam indique des fichiers manquants ou endommagés."),
            _ when (flags & (512u | 8388608u)) != 0 => ("téléchargement arrêté", "Mise à jour en pause ou arrêtée dans Steam."),
            _ when (flags & (256u | 1048576u | 131072u | 262144u | 524288u | 2097152u | 4194304u)) != 0 =>
                ("en téléchargement", "Téléchargement ou application de mise à jour en cours dans Steam."),
            _ when (flags & (2u | 1024u)) != 0 => ("en file d’attente", "Mise à jour en attente ou en file dans Steam."),
            (_, 4) => ("à jour localement", string.Empty),
            _ => ("à vérifier", "État local incomplet dans Steam.")
        };
        var freshness = game.LastUpdatedUtc is { } updated
            ? "Dernière mise à jour locale : " + TimeZoneInfo.ConvertTime(updated, (clock ?? TimeProvider.System).LocalTimeZone)
                .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) + "."
            : "Date locale inconnue.";
        return (status, (detail.Length == 0 ? "" : detail + " ") + freshness
            + " Donnée locale Steam uniquement (peut retarder si Steam est fermé ou hors ligne).");
    }

    internal static (string VerifiedStatus, string Detail) SummarizeScannedLauncher(SteamSession session,
        IReadOnlyList<SteamGameState> games, string? readError = null, TimeProvider? clock = null)
    {
        if (readError is not null) return ("indisponible", "Lecture des fichiers locaux Steam impossible.");
        var statuses = games.ToLookup(game => ClassifyScannedGame(game, clock).Status, StringComparer.Ordinal);
        var counts = new List<string>();
        int Count(string status, string singular, string plural)
        {
            var count = statuses[status].Count();
            if (count > 0) counts.Add(count == 1 ? $"1 jeu {singular}" : $"{count} jeux {plural}");
            return count;
        }
        var pending = Count("en téléchargement", "en téléchargement", "en téléchargement")
            + Count("en file d’attente", "en file d’attente", "en file d’attente")
            + Count("téléchargement arrêté", "arrêté", "arrêtés");
        var errors = Count("en erreur", "en erreur", "en erreur");
        Count("à jour localement", "à jour localement", "à jour localement");
        Count("à vérifier", "à vérifier", "à vérifier");
        var summary = string.Join(", ", counts);
        if (!session.IsOpen)
        {
            var status = errors > 0 ? "en erreur" : pending > 0 ? DriverCardSummary.Unsupported : "à vérifier";
            var inventory = games.Count == 0 ? "aucun jeu détecté dans les bibliothèques locales Steam. " : summary + ". ";
            return session.IsProcessRunning
                ? (status, "Steam est hors ligne ou sans session active : " + inventory + "Connectez Steam puis vérifiez « Téléchargements ».")
                : (status, "Steam est fermé : " + inventory + "Ouvrez Steam pour vérifier « Téléchargements ».");
        }
        if (games.Count == 0)
            return ("à vérifier", "Aucun jeu détecté dans les bibliothèques locales Steam ; vérifiez « Téléchargements » dans Steam.");
        if (errors > 0 || pending > 0)
            return (errors > 0 ? "en erreur" : "reste à installer", summary + ". Ouvrez Steam pour vérifier « Téléchargements ».");
        return ("à vérifier", "Aucune mise à jour en attente dans les fichiers locaux Steam (données locales uniquement ; ouvrez « Téléchargements » dans Steam pour confirmer).");
    }

    private static UpdateSequenceStepResult GameResult(SteamGameState game, string status, string message) =>
        new(new("SteamGame:" + game.Id, game.Name, UpdateComponent.Launcher), status is "mis à jour" or "à jour selon Steam", false, message)
        { VerifiedStatus = status, WasSkipped = status is "non lancée" or "reste à installer" };
}
