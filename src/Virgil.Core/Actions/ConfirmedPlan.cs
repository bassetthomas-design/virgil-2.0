using Virgil.Contracts.Actions;

namespace Virgil.Core.Actions;

public sealed class ConfirmedPlan
{
    internal ConfirmedPlan(Guid planId, string actionId, string previewHash,
        ConfirmationLevel confirmationLevel, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        PlanId = planId;
        ActionId = actionId;
        PreviewHash = previewHash;
        ConfirmationLevel = confirmationLevel;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public Guid PlanId { get; }
    public string ActionId { get; }
    public string PreviewHash { get; }
    public ConfirmationLevel ConfirmationLevel { get; }
    public DateTimeOffset IssuedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
}
