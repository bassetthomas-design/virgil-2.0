using System.Globalization;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Cleanup;

public sealed class QuarantineMaintenanceAction : IMaintenanceAction
{
    private readonly IQuarantineStore _store;
    private readonly IConfirmedPlanGate _gate;
    private readonly QuarantineEntry[] _entries;
    private readonly bool _purge;
    public QuarantineMaintenanceAction(IQuarantineStore store, IConfirmedPlanGate gate,
        IEnumerable<QuarantineEntry> entries, bool purge)
    {
        _store = store; _gate = gate; _purge = purge;
        _entries = entries.OrderBy(e => e.Id).Select(e => e with { Zone = e.Zone with
        {
            AllowedExtensions = Array.AsReadOnly(e.Zone.AllowedExtensions.ToArray()),
            RequiredPathFragments = Array.AsReadOnly(e.Zone.RequiredPathFragments.ToArray()),
            ExcludedPathFragments = Array.AsReadOnly(e.Zone.ExcludedPathFragments.ToArray())
        }}).ToArray();
    }
    public string Id => _purge ? "quarantine.purge" : "quarantine.restore";
    public RiskLevel RiskLevel => RiskLevel.Sensitive;
    public bool RequiresAdmin => false;
    public bool RebootRequired => false;
    public Reversibility Reversibility => _purge ? Reversibility.None : Reversibility.Full;
    public Task<ActionPreview> PreviewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreatePreview());
    }
    private ActionPreview CreatePreview()
    {
        var parameters = new Dictionary<string, string>();
        foreach (var e in _entries)
            parameters[e.Id.ToString()] = System.Text.Json.JsonSerializer.Serialize(e);
        parameters["Count"] = _entries.Length.ToString(CultureInfo.InvariantCulture);
        parameters["Bytes"] = _entries.Sum(e => e.SizeBytes).ToString(CultureInfo.InvariantCulture);
        return new(Id, _purge ? "Vider la quarantaine maintenant" : "Restaurer", RiskLevel,
            _entries.Select(e => e.OriginalPath).ToArray(), parameters,
            [_purge ? "Effacement définitif irréversible des éléments sélectionnés." : "Restauration vérifiée, sans écrasement du fichier d'origine."],
            false, false, Reversibility, []);
    }
    public Task<ActionResult> ExecuteAsync(ConfirmedPlan? plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var preview = CreatePreview();
        var validation = _gate.TryConsume(plan, preview);
        if (!validation.IsAccepted) return Task.FromResult(Refuse(validation.Message));
        // Prévalidation de toute la sélection avant le premier déplacement ou effacement.
        try
        {
            var current = _store.List();
            foreach (var entry in _entries)
            {
                var actual = current.SingleOrDefault(e => e.Id == entry.Id);
                if (actual is null || System.Text.Json.JsonSerializer.Serialize(actual) != System.Text.Json.JsonSerializer.Serialize(entry))
                    return Task.FromResult(Refuse("Le manifeste a changé. Relancer l'aperçu."));
                if (!_purge && _store.ValidateRestore(entry) is { } reason)
                    return Task.FromResult(Refuse(reason));
            }
        }
        catch { return Task.FromResult(Refuse("Vérification de la quarantaine impossible.")); }
        var effects = new List<string>();
        var errors = new List<string>();
        foreach (var entry in _entries)
        {
            if (cancellationToken.IsCancellationRequested) { errors.Add("Opération annulée."); break; }
            try
            {
                if (_purge) _store.Purge(entry); else _store.Restore(entry);
                effects.Add(_purge ? "Élément effacé définitivement." : "Élément restauré et vérifié.");
            }
            catch { errors.Add(_purge ? "Effacement définitif impossible." : "Restauration ou vérification impossible ; entrée conservée."); }
        }
        return Task.FromResult(new ActionResult(Id, errors.Count == 0, false, null, errors, effects, []));
    }
    private ActionResult Refuse(string reason) => new(Id, false, true, reason, [], [], []);
}

public sealed class QuarantineService(IQuarantineStore store, IConfirmedPlanGate gate, TimeProvider clock)
{
    public IReadOnlyList<QuarantineEntry> List() => store.List();
    public IMaintenanceAction Restore(IEnumerable<QuarantineEntry> entries) => new QuarantineMaintenanceAction(store, gate, entries, false);
    public IMaintenanceAction Purge(IEnumerable<QuarantineEntry> entries) => new QuarantineMaintenanceAction(store, gate, entries, true);
    public DateTimeOffset UtcNow => clock.GetUtcNow();

    /// <summary>Proposition figée ; aucune suppression avant exécution avec ConfirmedPlan.</summary>
    public QuarantineMaintenanceAction? GetExpiredPurgeAction()
    {
        var now = clock.GetUtcNow();
        var expired = store.List().Where(entry => entry.ExpiresAtUtc <= now).ToArray();
        return expired.Length == 0 ? null : new QuarantineMaintenanceAction(store, gate, expired, true);
    }
}
