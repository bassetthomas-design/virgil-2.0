using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Contracts.Applications;

namespace Virgil.Core.Actions;

/// <summary>Pure category mappings. Module-specific guards and confirmation flags remain authoritative.</summary>
public static class RiskLevelMapping
{
    public static RiskLevel ToRiskLevel(this ApplicationRiskLevel value) => value switch
    {
        ApplicationRiskLevel.SafeToUninstall => RiskLevel.Low,
        ApplicationRiskLevel.Caution => RiskLevel.Sensitive,
        ApplicationRiskLevel.Protected or ApplicationRiskLevel.Unknown => RiskLevel.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static RiskLevel ToRiskLevel(this UpdateRiskLevel value) => value switch
    {
        UpdateRiskLevel.Safe or UpdateRiskLevel.ValidationRequired => RiskLevel.Low,
        UpdateRiskLevel.Sensitive => RiskLevel.Sensitive,
        UpdateRiskLevel.CriticalInformationOnly => RiskLevel.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static RiskLevel ToRiskLevel(this InterventionRiskLevel value) => value switch
    {
        InterventionRiskLevel.Low or InterventionRiskLevel.Moderate => RiskLevel.Low,
        InterventionRiskLevel.Sensitive => RiskLevel.Sensitive,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    // Cleanup also has RequiresReinforcedConfirmation, independently of its enum
    // (including advanced Medium zones). Preserve that flag when wiring cleanup in 0e-4.
    public static RiskLevel ToRiskLevel(this CleanupRiskLevel value) => value switch
    {
        CleanupRiskLevel.Low or CleanupRiskLevel.Medium => RiskLevel.Low,
        CleanupRiskLevel.High => RiskLevel.Sensitive,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
}
