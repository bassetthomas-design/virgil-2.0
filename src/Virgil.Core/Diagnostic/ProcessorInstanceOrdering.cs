using System.Globalization;
using Virgil.Contracts.Diagnostic;

namespace Virgil.Core.Diagnostic;

/// <summary>Ordre des processeurs logiques PDH, sans accès Windows ni numéro déduit d'un nom invalide.</summary>
public static class ProcessorInstanceOrdering
{
    public static DiagnosticValue<IReadOnlyList<string>> Sort(IEnumerable<string> instances)
    {
        var parsed = new List<(int Group, int Index, string Instance)>();
        var identities = new HashSet<(int Group, int Index)>();
        foreach (var instance in instances)
        {
            if (string.Equals(instance, "_Total", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = instance.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var group))
                return Unavailable();
            if (string.Equals(parts[1], "_Total", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || !identities.Add((group, index))) return Unavailable();
            parsed.Add((group, index, instance));
        }
        return DiagnosticValue<IReadOnlyList<string>>.Available(Array.AsReadOnly(parsed
            .OrderBy(p => p.Group).ThenBy(p => p.Index).Select(p => p.Instance).ToArray()));
    }

    private static DiagnosticValue<IReadOnlyList<string>> Unavailable() =>
        DiagnosticValue<IReadOnlyList<string>>.Unavailable("Indisponible : identité des cœurs PDH non interprétable ou ambiguë");
}
