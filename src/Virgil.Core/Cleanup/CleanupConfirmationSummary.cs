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
        // Distinction de présentation uniquement : la nature, les gardes et la sélection restent inchangées.
        var system = selection.Where(p => CleanupItemNature.For(p) == CleanupNature.System &&
            p.Definition.Id is not (CleanupZoneId.WindowsTemporaryFiles or CleanupZoneId.WindowsUpdateCache or
                CleanupZoneId.DeliveryOptimizationCache or CleanupZoneId.WindowsErrorReports or
                CleanupZoneId.TechnicalLogs or CleanupZoneId.InstallerTemporaryFiles or
                CleanupZoneId.PrefetchInformation)).ToArray();
        if (personal.Length == 0 && system.Length == 0)
            return "Aucun fichier système ni personnel n'est concerné.";
        static string Names(CleanupZonePreview[] rows) => string.Join(", ", rows.Select(p => p.Definition.DisplayName));
        var categories = new List<string>();
        if (personal.Length > 0)
            categories.Add($"{personal.Length} élément(s) personnel(s) ({Names(personal)})");
        if (system.Length > 0)
            categories.Add($"{system.Length} élément(s) système sensible ({Names(system)})");
        return $"Concernés : {string.Join(" et ", categories)}.";
    }
}
