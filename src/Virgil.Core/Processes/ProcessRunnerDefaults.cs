namespace Virgil.Core.Processes;

// TODO 0d : remplacer par l'injection via Microsoft.Extensions.Hosting
public static class ProcessRunnerDefaults
{
    private static IProcessRunner? _current;

    public static void Configure(IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _current = runner;
    }

    public static IProcessRunner Current =>
        _current ?? throw new InvalidOperationException("IProcessRunner non configure.");
}
