namespace Virgil.Contracts.Actions;

public enum RiskLevel { Info, Low, Sensitive, Critical }
public enum ConfirmationLevel { Simple, Reinforced, Critical }
public enum Reversibility { Full, Partial, None }

public sealed record ActionPreview(
    string ActionId,
    string Title,
    RiskLevel RiskLevel,
    IReadOnlyList<string> Targets,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<string> Effects,
    bool RequiresAdmin,
    bool RebootRequired,
    Reversibility Reversibility,
    IReadOnlyList<string> RequiredBackups);

public sealed record ActionResult(
    string ActionId,
    bool Succeeded,
    bool WasRefused,
    string? RefusalReason,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Effects,
    IReadOnlyList<string> RestorationItems);

public enum PlanRejectionReason
{
    Missing, UnknownProvenance, ActionMismatch, PreviewHashMismatch,
    Expired, AlreadyUsed, InsufficientConsent
}

public sealed record PlanValidationResult(bool IsAccepted, PlanRejectionReason? Reason, string Message);
