using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Diagnostic;

/// <summary>Proposition de navigation seulement : aucun service de nettoyage ou mise à jour.</summary>
public sealed class DiagnosticNavigationAction(IConfirmedPlanGate gate, DiagnosticSuggestion destination) : IMaintenanceAction
{
    public DiagnosticSuggestion Destination => destination;
    public string Id => destination == DiagnosticSuggestion.Cleanup ? "diagnostic.open.cleanup" : "diagnostic.open.updates";
    public RiskLevel RiskLevel => RiskLevel.Low;
    public bool RequiresAdmin => false;
    public bool RebootRequired => false;
    public Reversibility Reversibility => Reversibility.None;
    public Task<ActionPreview> PreviewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var screen = destination == DiagnosticSuggestion.Cleanup ? "Nettoyage" : "Mises à jour";
        return Task.FromResult(new ActionPreview(Id, "Ouvrir " + screen, RiskLevel, [screen],
            new Dictionary<string, string>(), ["Ouvre l'écran existant. Aucune sélection imposée, aucun nettoyage ni mise à jour exécuté."], false, false, Reversibility, []));
    }
    public async Task<ActionResult> ExecuteAsync(ConfirmedPlan? plan, CancellationToken cancellationToken)
    {
        var preview = await PreviewAsync(cancellationToken).ConfigureAwait(false);
        var validation = gate.TryConsume(plan, preview);
        return validation.IsAccepted ? new(Id, true, false, null, [], preview.Effects, [])
            : new(Id, false, true, validation.Message, [], [], []);
    }
}
