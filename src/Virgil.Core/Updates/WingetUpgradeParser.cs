using System.Text.Json;
using System.Text.RegularExpressions;
using Virgil.Contracts;

namespace Virgil.Core.Updates;

public static class WingetUpgradeParser
{
    public static WingetParseResult Parse(string output)
    {
        var items = new List<UpdateItem>();
        var lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int[]? starts = null;
        var table = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i + 1 < lines.Length && IsSeparator(lines[i + 1]))
            {
                var tokens = Regex.Matches(line, @"\S+");
                starts = tokens.Count == 5 ? tokens.Select(token => token.Index).ToArray() : null;
                table++;
                i++;
                continue;
            }
            if (starts is null) continue;
            if (string.IsNullOrWhiteSpace(line) || line.Length <= starts[4])
            {
                starts = null;
                continue;
            }
            var parts = starts.Select((start, column) => line[start..(column + 1 < starts.Length ? Math.Min(starts[column + 1], line.Length) : line.Length)].Trim()).ToArray();
            // A row must occupy every column and identify a package source. Prose is never a warning.
            if (parts.Any(string.IsNullOrWhiteSpace) || parts[1].Any(char.IsWhiteSpace) || parts[3].Any(char.IsWhiteSpace) || parts[4].Any(char.IsWhiteSpace))
            {
                starts = null;
                continue;
            }
            items.Add(Create(parts[1], parts[0], parts[2], parts[3], parts[4]) with { RequiresExplicitTarget = table > 1 });
        }
        return new(items, Array.Empty<string>());
    }

    public static WingetParseResult ParseJson(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Null) return new(Array.Empty<UpdateItem>(), Array.Empty<string>());
        var rows = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : new[] { root };
        var items = new List<UpdateItem>();
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Object) throw new JsonException("Objet WinGet attendu.");
            var id = Read(row, "Id");
            var name = Read(row, "Name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
            items.Add(Create(id, name, Read(row, "InstalledVersion"), Read(row, "Available"), Read(row, "Source")));
        }
        return new(items, Array.Empty<string>());
    }

    private static string Read(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static bool IsSeparator(string line) => line.Trim().Length > 0 && line.Trim().All(character => character == '-');
    private static UpdateItem Create(string id, string name, string installed, string available, string source) => new()
    {
        Id = id,
        Name = name,
        InstalledVersion = installed,
        AvailableVersion = available,
        IsInstalledVersionUnknown = installed.StartsWith('<') || installed.StartsWith('>') || string.IsNullOrWhiteSpace(installed) || installed.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || installed.Equals("Inconnu", StringComparison.OrdinalIgnoreCase),
        IsIdTruncated = id.Contains('…'),
        Source = source.Contains("store", StringComparison.OrdinalIgnoreCase) ? UpdateSource.MicrosoftStore : UpdateSource.Winget,
        Scope = "Utilisateur ou machine",
        RequiresExplicitConfirmation = true
    };
}

public sealed record WingetParseResult(IReadOnlyList<UpdateItem> Items, IReadOnlyList<string> Warnings);
