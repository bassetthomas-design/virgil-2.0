using System.Text.Json;
using System.Text.RegularExpressions;

namespace Virgil.Infrastructure.Windows;

/// <summary>Lecture pure des formats des launchers ; les erreurs remontent à l'inventaire.</summary>
public static class LauncherManifestParser
{
    public static IReadOnlyList<string> SteamLibraries(string text)
    {
        var pairs = ParseValvePairs(text);
        return pairs.Where(p => p.Key.Equals("path", StringComparison.OrdinalIgnoreCase) || int.TryParse(p.Key, out _))
            .Select(p => p.Value).Where(p => p.Contains(':') || p.StartsWith('/')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static (string Name, string InstallDirectory) SteamApplication(string text)
    {
        var pairs = ParseValvePairs(text);
        var name = pairs.FirstOrDefault(p => p.Key.Equals("name", StringComparison.OrdinalIgnoreCase)).Value;
        var directory = pairs.FirstOrDefault(p => p.Key.Equals("installdir", StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(directory)) throw new FormatException("Manifeste Steam incomplet.");
        return (name, directory);
    }

    public static (string Name, string InstallDirectory) EpicApplication(string text)
    {
        using var json = JsonDocument.Parse(text);
        return (json.RootElement.GetProperty("DisplayName").GetString() ?? throw new FormatException("Nom Epic absent."),
            json.RootElement.GetProperty("InstallLocation").GetString() ?? throw new FormatException("Chemin Epic absent."));
    }

    private static IReadOnlyList<KeyValuePair<string, string>> ParseValvePairs(string text)
    {
        // Accepte VDF/ACF récents et anciens, sans confondre les blocs avec des valeurs.
        var tokens = Regex.Matches(text, "\\\"(?:\\\\.|[^\\\"\\\\])*\\\"|[{}]|//[^\\r\\n]*")
            .Select(m => m.Value).Where(v => !v.StartsWith("//", StringComparison.Ordinal)).ToArray();
        var depth = 0; var result = new List<KeyValuePair<string, string>>();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "{") { depth++; continue; }
            if (tokens[i] == "}") { if (--depth < 0) throw new FormatException("Bloc Valve invalide."); continue; }
            if (i + 1 >= tokens.Length) throw new FormatException("Valeur Valve absente.");
            if (tokens[i + 1] == "{") continue;
            if (!tokens[i + 1].StartsWith('"')) throw new FormatException("Valeur Valve invalide.");
            result.Add(new(Unescape(tokens[i]), Unescape(tokens[++i])));
        }
        if (depth != 0 || tokens.Length == 0) throw new FormatException("Manifeste Valve invalide.");
        return result;
    }
    private static string Unescape(string text) => text[1..^1].Replace("\\\\", "\\", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal);
}
