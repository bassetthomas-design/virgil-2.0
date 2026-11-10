namespace Virgil.Core.Updates;

public sealed record LauncherProcess(int Id, int ParentId, string ExecutablePath, string? Name = null);
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
            var launchers = state.Processes.Where(p => ProcessName(p).Equals(executable, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (!state.IsComplete) { lines.Add(name + " : mis à jour au prochain lancement."); continue; }
            if (launchers.Length == 0) { lines.Add(name + " : déjà fermé."); continue; }
            if (GameOrUnknown(name, state, launchers)) { lines.Add(name + " : mis à jour au prochain lancement."); continue; }
            token.ThrowIfCancellationRequested();
            try { await backend.CloseAsync(executable, false, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { /* La tentative douce peut échouer ; la vérification et le délai restent obligatoires. */ }
            var started = clock.GetTimestamp();
            do
            {
                var remaining = TimeSpan.FromSeconds(15) - clock.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero) break;
                await Task.Delay(remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), clock, token).ConfigureAwait(false);
                state = await backend.ReadAsync(name, token).ConfigureAwait(false);
                launchers = state.Processes.Where(p => ProcessName(p).Equals(executable, StringComparison.OrdinalIgnoreCase)).ToArray();
            } while (launchers.Length > 0);
            state = await backend.ReadAsync(name, token).ConfigureAwait(false);
            launchers = state.Processes.Where(p => ProcessName(p).Equals(executable, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (!state.IsComplete) { lines.Add(name + " : mis à jour au prochain lancement."); continue; }
            if (launchers.Length > 0)
            {
                if (GameOrUnknown(name, state, launchers)) { lines.Add(name + " : mis à jour au prochain lancement."); continue; }
                token.ThrowIfCancellationRequested();
                await backend.CloseAsync(executable, true, token).ConfigureAwait(false);
                anyClosed = true;
                lines.Add(name + " : fermé de force ; relancez-le quand vous voulez.");
            }
            else { anyClosed = true; lines.Add(name + " : fermé ; relancez-le quand vous voulez."); }
        }
        return new(anyClosed, string.Join("\n", lines));
    }
    internal async Task<bool> CanRestartSteamAsync(CancellationToken token)
    {
        var state = await backend.ReadAsync("Steam", token).ConfigureAwait(false);
        var launchers = state.Processes.Where(p => ProcessName(p).Equals("steam.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        return state.IsComplete && launchers.Length == 0 && !GameOrUnknown("Steam", state, launchers);
    }
    private static string FileName(string? path) => (path ?? "").Replace('\\', '/').Split('/').Last();
    private static string ProcessName(LauncherProcess process) => string.IsNullOrWhiteSpace(process.Name) ? FileName(process.ExecutablePath) : process.Name;
    private static bool GameOrUnknown(string name, LauncherSnapshot state, LauncherProcess[] launchers)
    {
        if (!state.IsComplete || state.LibraryRoots.Count == 0 || launchers.Any(p => string.IsNullOrWhiteSpace(p.ExecutablePath))) return true;
        var tree = launchers.Select(p => p.Id).ToHashSet();
        bool added;
        do
        {
            added = false;
            foreach (var process in state.Processes)
                if (tree.Contains(process.ParentId)) added |= tree.Add(process.Id);
        } while (added);
        return state.Processes.Any(p =>
            state.LibraryRoots.Any(root => !string.IsNullOrWhiteSpace(p.ExecutablePath) && p.ExecutablePath.Replace('/', '\\').StartsWith(root.Replace('/', '\\').TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) ||
            (tree.Contains(p.Id) && !launchers.Any(l => l.Id == p.Id) && !LauncherCatalog.IsKnownHelper(name, ProcessName(p))));
    }
}
