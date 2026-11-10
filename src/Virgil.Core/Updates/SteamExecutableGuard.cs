namespace Virgil.Core.Updates;

public sealed class SteamExecutableGuard(Func<string, bool> exists, Func<string, bool> signedByValve)
{
    public SteamExecutableCheck Check(string? path)
    {
        _ = exists;
        _ = signedByValve;
        _ = path;
        return new(false, "Steam : relance bloquée, vérification indisponible.");
    }
}
