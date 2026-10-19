using Virgil.Contracts.Actions;

namespace Virgil.Core.Actions;

public static class RiskConfirmationPolicy
{
    public static ConfirmationLevel RequiredLevel(RiskLevel riskLevel) => riskLevel switch
    {
        RiskLevel.Info or RiskLevel.Low => ConfirmationLevel.Simple,
        RiskLevel.Sensitive => ConfirmationLevel.Reinforced,
        RiskLevel.Critical => ConfirmationLevel.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(riskLevel))
    };
}
