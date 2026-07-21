using Virgil.Domain.Applications;

namespace Virgil.Core.Applications;

public sealed class ApplicationUninstallConfirmationPolicy
{
    public ApplicationUninstallConfirmationDecision Validate(
        ApplicationUninstallPlan plan,
        ApplicationUninstallConfirmation confirmation)
    {
        if (!plan.CanLaunch)
        {
            return new ApplicationUninstallConfirmationDecision
            {
                CanProceed = false,
                WasCancelled = false,
                RequiredLevel = ApplicationUninstallConfirmationLevel.None,
                Reason = plan.Validation.Reason
            };
        }

        if (!confirmation.ExplicitlyConfirmed)
        {
            return new ApplicationUninstallConfirmationDecision
            {
                CanProceed = false,
                WasCancelled = true,
                RequiredLevel = plan.RequiredConfirmationLevel,
                Reason = "Confirmation explicite absente."
            };
        }

        if (plan.RequiresReinforcedConfirmation && !confirmation.ReinforcedConfirmed)
        {
            return new ApplicationUninstallConfirmationDecision
            {
                CanProceed = false,
                WasCancelled = true,
                RequiredLevel = plan.RequiredConfirmationLevel,
                Reason = "Confirmation renforcee absente pour une application classee attention."
            };
        }

        return new ApplicationUninstallConfirmationDecision
        {
            CanProceed = true,
            WasCancelled = false,
            RequiredLevel = plan.RequiredConfirmationLevel,
            Reason = "Confirmation utilisateur validee."
        };
    }
}
