using Virgil.Domain;

namespace Virgil.Core.Interventions;

public sealed class InterventionRiskClassifier
{
    public string Describe(InterventionDefinition definition)
    {
        return definition.RiskLevel switch
        {
            InterventionRiskLevel.Low => "Risque faible : action courte et limitee.",
            InterventionRiskLevel.Moderate => "Risque modere : verifier l'impact avant execution.",
            _ => "Action sensible : confirmation renforcee requise."
        };
    }
}
