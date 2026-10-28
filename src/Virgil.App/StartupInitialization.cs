namespace Virgil.App;

public static class StartupInitialization
{
    public static async Task StartAsync(Func<Task> refreshHistory, Func<Task> checkRelease)
    {
        await checkRelease();
        await refreshHistory();
    }
}
