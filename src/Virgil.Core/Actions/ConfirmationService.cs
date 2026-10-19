using Virgil.Contracts.Actions;

namespace Virgil.Core.Actions;

/// <summary>In-memory consent authority and the single gate for future destructive actions.</summary>
public sealed class ConfirmationService : IConfirmationService, IConfirmedPlanGate
{
    public static TimeSpan MaximumLifetime { get; } = TimeSpan.FromMinutes(15);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;
    private readonly object _sync = new();
    // Object identity, not public values, establishes provenance. Retain used entries
    // to distinguish AlreadyUsed from UnknownProvenance until this authority is released.
    private readonly Dictionary<ConfirmedPlan, bool> _issued = new(ReferenceEqualityComparer.Instance);

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
        var required = RiskConfirmationPolicy.RequiredLevel(preview.RiskLevel);
        if (!Enum.IsDefined(consent.Level) || consent.Level < required ||
            (consent.Level == ConfirmationLevel.Critical &&
             !string.Equals(consent.TypedConfirmation, ExpectedCriticalText(preview), StringComparison.Ordinal)))
            throw new ConfirmationRefusedException();

        var hash = ActionPreviewHasher.Hash(preview);
        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            var plan = new ConfirmedPlan(Guid.NewGuid(), preview.ActionId, hash, consent.Level, now, now + _lifetime);
            _issued.Add(plan, false);
            return plan;
        }
    }

    public PlanValidationResult Validate(ConfirmedPlan? plan, ActionPreview currentPreview)
    {
        lock (_sync) return ValidateLocked(plan, currentPreview);
    }

    public PlanValidationResult TryConsume(ConfirmedPlan? plan, ActionPreview currentPreview)
    {
        lock (_sync)
        {
            var result = ValidateLocked(plan, currentPreview);
            if (result.IsAccepted) _issued[plan!] = true;
            return result;
        }
    }

    private PlanValidationResult ValidateLocked(ConfirmedPlan? plan, ActionPreview preview)
    {
        if (plan is null) return Refuse(PlanRejectionReason.Missing, "Plan de confirmation absent.");
        if (!_issued.TryGetValue(plan, out var used))
            return Refuse(PlanRejectionReason.UnknownProvenance, "Provenance du plan inconnue.");
        if (!string.Equals(plan.ActionId, preview.ActionId, StringComparison.Ordinal))
            return Refuse(PlanRejectionReason.ActionMismatch, "Le plan concerne une autre action.");
        if (!string.Equals(plan.PreviewHash, ActionPreviewHasher.Hash(preview), StringComparison.Ordinal))
            return Refuse(PlanRejectionReason.PreviewHashMismatch, "L’aperçu a changé ; une nouvelle confirmation est nécessaire.");
        if (plan.ExpiresAt <= _timeProvider.GetUtcNow())
            return Refuse(PlanRejectionReason.Expired, "Le plan de confirmation a expiré.");
        if (used) return Refuse(PlanRejectionReason.AlreadyUsed, "Le plan de confirmation a déjà été utilisé.");
        return new(true, null, "Plan de confirmation accepté.");
    }

    private static PlanValidationResult Refuse(PlanRejectionReason reason, string message) => new(false, reason, message);
}
