using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Cleanup;
using Virgil.Core.Interventions;

namespace Virgil.Tests;

// Existing behavioral tests now obtain genuine plans from the same test authority as execution.
internal static class TestPlans
{
    internal static ConfirmationService Authority { get; } = new(TimeProvider.System);
    internal static ConfirmedPlan Cleanup(CleanupZonePreview preview) => Issue(CleanupActionPreviewFactory.Create(preview));
    internal static ConfirmedPlan Intervention(InterventionDiagnostic diagnostic) => Issue(InterventionActionPreviewFactory.Create(diagnostic.Definition));
    private static ConfirmedPlan Issue(ActionPreview preview) => Authority.Issue(preview,
        new ConfirmationConsent(RiskConfirmationPolicy.RequiredLevel(preview.RiskLevel), null));
}
