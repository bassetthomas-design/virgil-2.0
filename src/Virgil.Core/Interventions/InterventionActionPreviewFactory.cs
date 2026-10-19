using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Interventions;

public static class InterventionActionPreviewFactory
{
    public static ActionPreview Create(InterventionDefinition definition) => new(
        "interventions." + definition.Id.ToString().ToLowerInvariant(), definition.Title,
        definition.RiskLevel.ToRiskLevel(), [definition.Id.ToString()],
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Category"] = definition.Category.ToString(),
            ["RequiresAdministrator"] = definition.RequiresAdministrator.ToString(),
            ["RiskLevel"] = definition.RiskLevel.ToString()
        }, [definition.ExpectedEffect], definition.RequiresAdministrator,
        definition.RebootPossible, Reversibility.None, []);
}
