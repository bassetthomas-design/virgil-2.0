using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public sealed class InterventionRiskClassifier
{
    public string Describe(InterventionDefinition definition)
    {
        return definition.RiskLevel switch
        {
            InterventionRiskLevel.Low => "Risque faible : action courte et limitee.",
            InterventionRiskLevel.Moderate => "Risque modere : vérifier l'impact avant exécution.",
            _ => "Action sensible : confirmation renforcee requise."
        };
    }
}
