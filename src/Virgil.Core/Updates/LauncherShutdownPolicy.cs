namespace Virgil.Core.Updates;

public sealed record LauncherProcess(int Id, int ParentId, string ExecutablePath);
public sealed record LauncherSnapshot(bool IsComplete, IReadOnlyList<LauncherProcess> Processes, IReadOnlyList<string> LibraryRoots);
public sealed record LauncherShutdownResult(bool AnyClosed, string Message);
public interface ILauncherShutdownBackend
{
    Task<LauncherSnapshot> ReadAsync(string launcherName, CancellationToken token);
    Task CloseAsync(string executable, bool force, CancellationToken token);
}

// Cette exception produit est accessible seulement à la séquence ayant consommé son plan.
public sealed class LauncherShutdownPolicy(ILauncherShutdownBackend backend, TimeProvider clock)
{
    internal async Task<LauncherShutdownResult> ExecuteAsync(IReadOnlyList<string> names, CancellationToken token)
    {
        var lines = new List<string>();
        var anyClosed = false;
        foreach (var name in names)
        {
            token.ThrowIfCancellationRequested();
            if (!LauncherCatalog.Executables.TryGetValue(name, out var executable))
            { lines.Add(name + " : fermeture refusée, hors catalogue."); continue; }
            var state = await backend.ReadAsync(name, token).ConfigureAwait(false);
            var launchers = state.Processes.Where(p => FileName(p.ExecutablePath).Equals(executable, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (!state.IsComplete) { lines.Add(name + " : mis à jour au prochain lancement."); continue; }
            if (launchers.Length == 0) { lines.Add(name + " : déjà fermé."); continue; }
            if (GameOrUnknown(state, launchers)) { lines.Add(name + " : mis à jour au prochain lancement."); continue; }
            token.ThrowIfCancellationRequested();
            await backend.CloseAsync(executable, false, token).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(15), clock, token).ConfigureAwait(false);
            state = await backend.ReadAsync(name, token).ConfigureAwait(false);
            launchers = state.Processes.Where(p => FileName(p.ExecutablePath).Equals(executable, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (launchers.Length > 0)
            {
                if (GameOrUnknown(state, launchers)) { lines.Add(name + " : mis à jour au prochain lancement."); continue; }
                token.ThrowIfCancellationRequested();
                await backend.CloseAsync(executable, true, token).ConfigureAwait(false);
                anyClosed = true;
                lines.Add(name + " : fermé de force ; relancez-le quand vous voulez.");
            }
            else { anyClosed = true; lines.Add(name + " : fermé ; relancez-le quand vous voulez."); }
        }
        return new(anyClosed, string.Join("\n", lines));
    }
    private static string FileName(string? path) => (path ?? "").Replace('\\', '/').Split('/').Last();
    private static bool GameOrUnknown(LauncherSnapshot state, LauncherProcess[] launchers) => !state.IsComplete || state.LibraryRoots.Count == 0 ||
        state.Processes.Any(p => launchers.Any(l => p.ParentId == l.Id) || state.LibraryRoots.Any(root => p.ExecutablePath.Replace('/', '\\').StartsWith(root.Replace('/', '\\').TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)));
}
