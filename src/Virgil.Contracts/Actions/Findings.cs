namespace Virgil.Contracts.Actions;

/// <summary>Constats et propositions uniquement : aucun consentement ni capacité d'exécution.</summary>
public class Findings
{
    public Findings(IEnumerable<MaintenanceFinding> observations, IEnumerable<ProposedMaintenanceAction> proposedActions)
    {
        Observations = Array.AsReadOnly(observations.ToArray());
        ProposedActions = Array.AsReadOnly(proposedActions.ToArray());
    }

    public IReadOnlyList<MaintenanceFinding> Observations { get; }
    public IReadOnlyList<ProposedMaintenanceAction> ProposedActions { get; }
}

public sealed record MaintenanceFinding(string Id, string Title, string Description);

public sealed record ProposedMaintenanceAction(
    string Id, string Title, RiskLevel RiskLevel, bool RequiresAdmin,
    bool RebootRequired, Reversibility Reversibility);
