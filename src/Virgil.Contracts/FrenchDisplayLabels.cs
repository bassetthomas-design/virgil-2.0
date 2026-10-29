using System.Globalization;
using System.Text;

namespace Virgil.Contracts;

/// <summary>Libellés affichés partagés ; aucune clé de protocole ou JSON.</summary>
public static class FrenchDisplayLabels
{
    public const string Reanalysis = "réanalyse";
    public const string NotAnalyzed = "Non analysé";
    public const string Inventoried = "Inventorié";
    public const string High = "Élevé";
    public const string Moderate = "modéré";
    public const string PreviewExpired = "preview expirée";

    /// <summary>Accepte les anciens statuts de rapports sans accent sans changer les valeurs inconnues.</summary>
    public static string NormalizeLegacy(string value)
    {
        foreach (var canonical in new[] { NotAnalyzed, Inventoried, High, Moderate, PreviewExpired })
            if (string.Equals(WithoutAccents(value), WithoutAccents(canonical), StringComparison.OrdinalIgnoreCase))
                return canonical;
        return value;
    }

    public static bool ContainsReanalysis(string value) => CultureInfo.InvariantCulture.CompareInfo.IndexOf(
        value, Reanalysis, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private static string WithoutAccents(string value) => string.Concat(value.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));
}
