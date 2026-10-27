using System.Globalization;
using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

public static class CleanupConfirmationSummary
{
    public static void Bind(IReadOnlyList<CleanupZonePreview> selection, IDictionary<string, string> parameters)
    {
        foreach (var nature in Enum.GetValues<CleanupNature>())
            parameters["NatureCount:" + nature] = selection.Count(p => CleanupItemNature.For(p) == nature).ToString(CultureInfo.InvariantCulture);
        parameters["NatureSummary"] = Create(selection);
    }

    public static string Create(IReadOnlyList<CleanupZonePreview> selection)
    {
        var personal = selection.Where(p => CleanupItemNature.For(p) == CleanupNature.Personal).ToArray();
        var system = selection.Where(p => CleanupItemNature.For(p) == CleanupNature.System).ToArray();
        if (personal.Length == 0 && system.Length == 0)
            return "Aucun fichier système ni personnel n'est concerné.";
        static string Names(CleanupZonePreview[] rows) => string.Join(", ", rows.Select(p => p.Definition.DisplayName));
        return $"Concernés : {personal.Length} élément(s) personnel(s) ({Names(personal)}) et {system.Length} élément(s) système ({Names(system)}).";
    }
}
