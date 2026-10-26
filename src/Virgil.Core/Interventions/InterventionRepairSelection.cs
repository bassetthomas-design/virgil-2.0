using System.Globalization;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Interventions;

public sealed record InterventionRepairSelection(IReadOnlyList<InterventionDiagnostic> Diagnostics,
    bool AllowWithoutRestorePoint = false);

public static class InterventionRepairPreviewFactory
{
    public static ActionPreview Create(InterventionRepairSelection selection)
    {
        var targets = selection.Diagnostics.Select(item => item.Definition.Id.ToString()).ToArray();
        if (selection.AllowWithoutRestorePoint) targets = ["aucun point de restauration", .. targets];
        return new ActionPreview("interventions.repair-selection", "Réparer les éléments détectés",
            selection.AllowWithoutRestorePoint ? RiskLevel.Critical : RiskLevel.Sensitive, targets,
            selection.Diagnostics.Select((item, index) => new KeyValuePair<string, string>(index.ToString(CultureInfo.InvariantCulture),
                ActionPreviewHasher.Hash(InterventionActionPreviewFactory.Create(item.Definition))))
                .Append(new("AllowWithoutRestorePoint", selection.AllowWithoutRestorePoint.ToString()))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new[] { selection.AllowWithoutRestorePoint ? "Aucun point de restauration : changements sans cette protection."
                    : "Créer et vérifier un point de restauration avant les réparations." }
                .Concat(selection.Diagnostics.Select(item => item.Definition.Title + " : " + item.Definition.ExpectedEffect))
                .Append("Exécuter une nouvelle vérification en lecture seule et produire le rapport.").ToArray(),
            true, selection.Diagnostics.Any(item => item.Definition.RebootPossible), Reversibility.Partial,
            selection.AllowWithoutRestorePoint ? [] : ["Point de restauration créé et vérifié"]);
    }
}
