namespace Virgil.App;

/// <summary>L’historique local ne dépend jamais du réseau.</summary>
public static class StartupInitialization
{
    public static Task StartAsync(Func<Task> refreshHistory, Func<Task> checkRelease)
    {
        var local = refreshHistory();
        _ = ObserveReleaseAsync(checkRelease);
        return local;
    }
    private static async Task ObserveReleaseAsync(Func<Task> checkRelease)
    {
        try { await checkRelease(); }
        catch { /* La vérification réseau ne bloque pas l’état local. */ }
    }
}
