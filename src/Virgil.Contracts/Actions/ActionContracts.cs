using System.Collections.ObjectModel;

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
    IReadOnlyList<string> RequiredBackups)
{
    private IReadOnlyList<string> _targets = Snapshot(Targets);
    private IReadOnlyDictionary<string, string> _parameters = Snapshot(Parameters);
    private IReadOnlyList<string> _effects = Snapshot(Effects);
    private IReadOnlyList<string> _requiredBackups = Snapshot(RequiredBackups);

    public IReadOnlyList<string> Targets
    {
        get => _targets;
        init => _targets = Snapshot(value);
    }

    public IReadOnlyDictionary<string, string> Parameters
    {
        get => _parameters;
        init => _parameters = Snapshot(value);
    }

    public IReadOnlyList<string> Effects
    {
        get => _effects;
        init => _effects = Snapshot(value);
    }

    public IReadOnlyList<string> RequiredBackups
    {
        get => _requiredBackups;
        init => _requiredBackups = Snapshot(value);
    }

    private static ReadOnlyCollection<string> Snapshot(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }

    private static ReadOnlyDictionary<string, string> Snapshot(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(values, StringComparer.Ordinal));
    }
}

public record ActionResult(
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
