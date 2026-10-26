using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Interventions;

public static class InterventionActionPreviewFactory
{
    public static ActionPreview Create(InterventionDefinition definition) => new(
        "interventions." + definition.Id.ToString().ToLowerInvariant(), definition.Title,
        definition.AllowWithoutRestorePoint ? RiskLevel.Critical : definition.RiskLevel.ToRiskLevel(),
        definition.AllowWithoutRestorePoint ? ["aucun point de restauration", definition.Id.ToString()] : [definition.Id.ToString()],
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AllowWithoutRestorePoint"] = definition.AllowWithoutRestorePoint.ToString(),
            ["RestorePointRequired"] = InterventionSafety.RequiresRestorePoint(definition.Id).ToString(),
            ["Category"] = definition.Category.ToString(),
            ["RequiresAdministrator"] = definition.RequiresAdministrator.ToString(),
            ["RiskLevel"] = definition.RiskLevel.ToString()
        }, [definition.ExpectedEffect, definition.AllowWithoutRestorePoint ? "Aucun point de restauration : changement sans cette protection." : InterventionSafety.RequiresRestorePoint(definition.Id) ? "Créer et vérifier un point de restauration avant toute réparation." : "Aucune réparation."], definition.RequiresAdministrator,
        definition.RebootPossible, Reversibility.None, []);
}
