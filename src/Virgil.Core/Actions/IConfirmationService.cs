using Virgil.Contracts.Actions;

namespace Virgil.Core.Actions;

public sealed record ConfirmationConsent(ConfirmationLevel Level, string? TypedConfirmation);

public interface IConfirmationService
{
    /// <exception cref="ConfirmationRefusedException">Le consentement est insuffisant.</exception>
    ConfirmedPlan Issue(ActionPreview preview, ConfirmationConsent consent);
}

public interface IConfirmedPlanGate
{
    PlanValidationResult Validate(ConfirmedPlan? plan, ActionPreview currentPreview);
    PlanValidationResult TryConsume(ConfirmedPlan? plan, ActionPreview currentPreview);
}

public sealed class ConfirmationRefusedException : InvalidOperationException
{
    public ConfirmationRefusedException() : base("Consentement insuffisant pour cette action.") { }

    public PlanValidationResult Result => new(false, PlanRejectionReason.InsufficientConsent, Message);
}
