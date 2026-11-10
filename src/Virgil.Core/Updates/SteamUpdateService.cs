namespace Virgil.Core.Updates;

// Exécuté exclusivement après la consommation du plan par la séquence.
internal sealed class SteamUpdateService(ISteamUpdateBackend backend)
{
    internal async Task<IReadOnlyList<UpdateSequenceStepResult>> ExecuteAsync(UpdateSequenceStep steam, IReadOnlyList<SteamGameState> initial,
        Func<Task<bool>> canRestart, bool finalPassSucceeded, string closure, Action waiting, CancellationToken token)
    {
        var results = new List<UpdateSequenceStepResult>();
        UpdateSequenceStepResult Launcher(string status, string message) => new(steam, false, false, message) { VerifiedStatus = status, WasSkipped = status != "bloquée" && status != "en erreur" };
        void NotStarted(string reason)
        {
            results.Add(Launcher("non lancée", reason));
            results.AddRange(initial.Select(game => GameResult(game, "non lancée", reason)));
        }
        if (token.IsCancellationRequested) { NotStarted("Non lancée : annulation avant la relance de Steam."); return results; }
        if (!finalPassSucceeded) { NotStarted("Non lancée : la passe finale des applications n’a pas été terminée."); return results; }
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
        var polled = false;
        try
        {
            while (backend.SteamClock.GetElapsedTime(start) < backend.SteamWaitLimit)
            {
                var remaining = backend.SteamWaitLimit - backend.SteamClock.GetElapsedTime(start);
                await Task.Delay(remaining < TimeSpan.FromSeconds(15) ? remaining : TimeSpan.FromSeconds(15), backend.SteamClock, token).ConfigureAwait(false);
                var current = await backend.SteamClient.ReadGamesAsync(token).ConfigureAwait(false);
                polled = true;
                foreach (var original in initial)
                {
                    var state = current.FirstOrDefault(game => game.Id == original.Id)
                        ?? original with { Error = "Le jeu n’est plus lisible dans Steam." };
                    // Un nom connu n’est jamais remplacé par un identifiant ou un message technique.
                    states[original.Id] = state with { Name = original.Name };
                    if ((state.StateFlags & 2) != 0) requested.Add(state.Id);
                    if (state.StateFlags != 4 || state.Error is not null) alwaysInstalled.Remove(state.Id);
                }
                if (initial.All(game => Finished(game, states[game.Id], requested, alwaysInstalled))) break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            foreach (var game in initial.Where(game => !Finished(game, states[game.Id], requested, alwaysInstalled)))
                states[game.Id] = states[game.Id] with { Error = "Lecture de l’état du jeu impossible dans Steam." };
        }
        results.Add(new(steam, true, false, closure + "\nSteam ouvert en silencieux ; jeux contrôlés en lecture seule.") { VerifiedStatus = "à vérifier" });
        foreach (var original in initial)
        {
            var state = states[original.Id];
            var error = state.Error ?? ((state.StateFlags & (32u | 128u)) != 0 || state.StateFlags == 0 ? "Steam indique des fichiers de jeu absents ou endommagés." : null);
            var status = error is not null ? "en erreur"
                : polled && state.StateFlags == 4 && requested.Contains(original.Id) && state.BuildId != original.BuildId ? "mis à jour"
                : polled && alwaysInstalled.Contains(original.Id) && !requested.Contains(original.Id) ? "à jour selon Steam" : "reste à installer";
            results.Add(GameResult(original, status, error ?? (status == "reste à installer" ? "Reste à installer — Steam continue en arrière-plan." : status)));
        }
        return results;
    }
    private static bool Finished(SteamGameState original, SteamGameState state, HashSet<string> requested, HashSet<string> alwaysInstalled) =>
        state.Error is not null || (state.StateFlags & (32u | 128u)) != 0 || state.StateFlags == 0 || (state.StateFlags == 4
            && (requested.Contains(original.Id) ? state.BuildId != original.BuildId : alwaysInstalled.Contains(original.Id)));
    private static UpdateSequenceStepResult GameResult(SteamGameState game, string status, string message) =>
        new(new("SteamGame:" + game.Id, game.Name, UpdateComponent.Launcher), status is "mis à jour" or "à jour selon Steam", false, message)
        { VerifiedStatus = status, WasSkipped = status is "non lancée" or "reste à installer" };
}
