using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

public sealed class SteamInventoryReader(ISteamReadAccess access)
{
    public IReadOnlyList<SteamGameState> Read()
    {
        var executable = access.ReadExecutablePath();
        if (string.IsNullOrWhiteSpace(executable)) throw new IOException("Dossier de Steam introuvable.");
        var directory = Path.GetDirectoryName(executable.Replace('\\', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(directory)) throw new IOException("Dossier de Steam incomplet.");
        var libraries = LauncherManifestParser.SteamLibraries(access.ReadText(Path.Combine(directory, "steamapps", "libraryfolders.vdf")))
            .Append(directory).Distinct(StringComparer.OrdinalIgnoreCase);
        var games = new Dictionary<string, SteamGameState>(StringComparer.Ordinal);
        foreach (var library in libraries)
        {
            var root = Path.Combine(library, "steamapps");
            foreach (var file in access.EnumerateManifests(root))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (!fileName.StartsWith("appmanifest_", StringComparison.Ordinal)) continue;
                var id = fileName["appmanifest_".Length..];
                if (id.Length == 0 || !id.All(char.IsAsciiDigit)) continue;
                var name = "Jeu Steam";
                try
                {
                    var text = access.ReadText(file);
                    name = LauncherManifestParser.SteamGameName(text);
                    var state = LauncherManifestParser.SteamUpdateState(text);
                    games[id] = new(id, state.Name, state.StateFlags, state.BuildId);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
                { games[id] = new(id, name, 0, "", "Impossible de lire l’état de ce jeu dans Steam."); }
            }
        }
        return games.Values.ToArray();
    }
}
