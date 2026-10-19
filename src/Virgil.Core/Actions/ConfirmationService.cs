using Virgil.Contracts.Actions;

namespace Virgil.Core.Actions;

/// <summary>In-memory consent authority and the single gate for future destructive actions.</summary>
public sealed class ConfirmationService : IConfirmationService, IConfirmedPlanGate
{
    public static TimeSpan MaximumLifetime { get; } = TimeSpan.FromMinutes(15);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;
    private readonly object _sync = new();
    // Object identity establishes provenance. Retain refusal reasons for a bounded window.
    private readonly Dictionary<ConfirmedPlan, IssuedPlan> _issued = new(ReferenceEqualityComparer.Instance);

    private readonly record struct IssuedPlan(long Timestamp, bool Used);
    private readonly Queue<ConfirmedPlan> _issuanceOrder = new();
    private const int MaximumEntries = 1024;

    public ConfirmationService(TimeProvider timeProvider, TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _lifetime = lifetime ?? TimeSpan.FromMinutes(10);
        if (_lifetime <= TimeSpan.Zero || _lifetime > MaximumLifetime)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "La validité doit être positive et ne pas dépasser 15 minutes.");
    }

    public static string ExpectedCriticalText(ActionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return $"JE COMPRENDS ET JE CONFIRME : {preview.ActionId} : {preview.Targets.FirstOrDefault() ?? "(aucune cible)"}";
    }

    public ConfirmedPlan Issue(ActionPreview preview, ConfirmationConsent consent)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(consent);
        lock (_sync) PurgeLocked();
        var required = RiskConfirmationPolicy.RequiredLevel(preview.RiskLevel);
        if (!Enum.IsDefined(consent.Level) || consent.Level < required ||
            (consent.Level == ConfirmationLevel.Critical &&
             !string.Equals(consent.TypedConfirmation, ExpectedCriticalText(preview), StringComparison.Ordinal)))
            throw new ConfirmationRefusedException();

        var hash = ActionPreviewHasher.Hash(preview);
        lock (_sync)
        {
            PurgeLocked();
            var timestamp = _timeProvider.GetTimestamp();
            var now = _timeProvider.GetUtcNow();
            var plan = new ConfirmedPlan(Guid.NewGuid(), preview.ActionId, hash, consent.Level, now, now + _lifetime);
            _issued.Add(plan, new(timestamp, false));
            _issuanceOrder.Enqueue(plan);
            while (_issued.Count > MaximumEntries) _issued.Remove(_issuanceOrder.Dequeue());
            return plan;
        }
    }

    public PlanValidationResult Validate(ConfirmedPlan? plan, ActionPreview currentPreview)
    {
        ArgumentNullException.ThrowIfNull(currentPreview);
        lock (_sync)
        {
            PurgeLocked();
            return ValidateLocked(plan, currentPreview);
        }
    }

    public PlanValidationResult TryConsume(ConfirmedPlan? plan, ActionPreview currentPreview)
    {
        ArgumentNullException.ThrowIfNull(currentPreview);
        lock (_sync)
        {
            PurgeLocked();
            var result = ValidateLocked(plan, currentPreview);
            if (result.IsAccepted) _issued[plan!] = _issued[plan!] with { Used = true };
            return result;
        }
    }

    private void PurgeLocked()
    {
        while (_issuanceOrder.TryPeek(out var oldest) &&
               _timeProvider.GetElapsedTime(_issued[oldest].Timestamp) > _lifetime + MaximumLifetime)
        {
            _issuanceOrder.Dequeue();
            _issued.Remove(oldest);
        }
    }

    private PlanValidationResult ValidateLocked(ConfirmedPlan? plan, ActionPreview preview)
    {
        if (plan is null) return Refuse(PlanRejectionReason.Missing, "Plan de confirmation absent.");
        if (!_issued.TryGetValue(plan, out var issued))
            return Refuse(PlanRejectionReason.UnknownProvenance, "Provenance du plan inconnue.");
        if (!string.Equals(plan.ActionId, preview.ActionId, StringComparison.Ordinal))
            return Refuse(PlanRejectionReason.ActionMismatch, "Le plan concerne une autre action.");
        if (!string.Equals(plan.PreviewHash, ActionPreviewHasher.Hash(preview), StringComparison.Ordinal))
            return Refuse(PlanRejectionReason.PreviewHashMismatch, "L’aperçu a changé ; une nouvelle confirmation est nécessaire.");
        if (_timeProvider.GetElapsedTime(issued.Timestamp) >= _lifetime || plan.ExpiresAt <= _timeProvider.GetUtcNow())
            return Refuse(PlanRejectionReason.Expired, "Le plan de confirmation a expiré.");
        if (issued.Used) return Refuse(PlanRejectionReason.AlreadyUsed, "Le plan de confirmation a déjà été utilisé.");
        return new(true, null, "Plan de confirmation accepté.");
    }

    private static PlanValidationResult Refuse(PlanRejectionReason reason, string message) => new(false, reason, message);
}
