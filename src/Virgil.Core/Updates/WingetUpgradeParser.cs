using System.Text.RegularExpressions;
using Virgil.Domain;

namespace Virgil.Core.Updates;

public static class WingetUpgradeParser
{
    private static readonly Regex ColumnSplitter = new(@"\s{2,}|\t+", RegexOptions.Compiled);

    public static WingetParseResult Parse(string output)
    {
        var items = new List<UpdateItem>();
        var warnings = new List<string>();
        var headerSeen = false;

        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || IsSummaryLine(line))
            {
                continue;
            }

            if (IsHeaderLine(line))
            {
                headerSeen = true;
                continue;
            }

            if (IsSeparator(line))
            {
                continue;
            }

            if (!headerSeen && !LooksLikePackageLine(line))
            {
                continue;
            }

            var parts = ColumnSplitter.Split(line).Where(part => !string.IsNullOrWhiteSpace(part)).ToList();
            if (parts.Count < 5)
            {
                warnings.Add($"Ligne WinGet ignoree : {line}");
                continue;
            }

            var name = string.Join(" ", parts.Take(parts.Count - 4)).Trim();
            var id = parts[^4].Trim();
            var installedVersion = parts[^3].Trim();
            var availableVersion = parts[^2].Trim();
            var source = parts[^1].Trim();

            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(id) ||
                string.Equals(id, "-", StringComparison.OrdinalIgnoreCase) ||
                !LooksLikePackageId(id))
            {
                warnings.Add($"Ligne WinGet ambigue ignoree : {line}");
                continue;
            }

            items.Add(new UpdateItem
            {
                Id = id,
                Name = name,
                InstalledVersion = installedVersion,
                AvailableVersion = availableVersion,
                Source = IsMicrosoftStore(source) ? UpdateSource.MicrosoftStore : UpdateSource.Winget,
                Scope = "Utilisateur ou machine",
                RequiresExplicitConfirmation = true
            });
        }

        return new WingetParseResult(items, warnings);
    }

    private static IEnumerable<string> SplitLines(string output)
    {
        return output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private static bool IsHeaderLine(string line)
    {
        return ContainsAny(line, "Name", "Nom") &&
            ContainsAny(line, "Id", "ID") &&
            ContainsAny(line, "Version") &&
            ContainsAny(line, "Available", "Disponible", "Dispo");
    }

    private static bool IsSeparator(string line)
    {
        return line.All(character => character is '-' or ' ' or '\t');
    }

    private static bool IsSummaryLine(string line)
    {
        return line.Contains("upgrades available", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("mise", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("jour", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("disponible", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("No installed package", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Aucun package", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePackageLine(string line)
    {
        return ColumnSplitter.Split(line).Count(part => !string.IsNullOrWhiteSpace(part)) >= 5;
    }

    private static bool LooksLikePackageId(string id)
    {
        return id.Contains('.') &&
            id.Any(char.IsLetter) &&
            id.All(character => char.IsLetterOrDigit(character) ||
                character is '.' or '-' or '_' or '+');
    }

    private static bool IsMicrosoftStore(string source)
    {
        return source.Contains("msstore", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("store", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record WingetParseResult(
    IReadOnlyList<UpdateItem> Items,
    IReadOnlyList<string> Warnings);
