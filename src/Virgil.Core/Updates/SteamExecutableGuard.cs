namespace Virgil.Core.Updates;

public sealed class SteamExecutableGuard(Func<string, bool> exists, Func<string, bool> signedByValve)
{
    public SteamExecutableCheck Check(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !(path.StartsWith('/') || (path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\')))
            return new(false, "Steam : relance bloquée, chemin absent ou incomplet.");
        if (!path.Replace('\\', '/').Split('/').Last().Equals("steam.exe", StringComparison.OrdinalIgnoreCase))
            return new(false, "Steam : relance bloquée, nom du fichier incorrect.");
        try
        {
            if (!exists(path)) return new(false, "Steam : relance bloquée, fichier absent.");
            if (!signedByValve(path)) return new(false, "Steam : relance bloquée, signature de Valve non vérifiée.");
            return new(true, "Signature de Valve vérifiée.");
        }
        catch { return new(false, "Steam : relance bloquée, vérification du fichier impossible."); }
    }
}
