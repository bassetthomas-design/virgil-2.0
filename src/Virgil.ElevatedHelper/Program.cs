namespace Virgil.ElevatedHelper;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await new ElevatedActionDispatcher().RunAsync(args).ConfigureAwait(false);
    }
}
