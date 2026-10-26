using Virgil.Contracts;
using Virgil.Core.Interventions;
using Virgil.Core.Startup;

namespace Virgil.Infrastructure.Windows.Startup;

/// <summary>Helper élévé, script fixe et vérification du point créé ; tout refus bloque le changement.</summary>
public sealed class WindowsStartupRestorePointService(IInterventionElevatedHelperClient helper) : IStartupRestorePointService
{
    public async Task<bool> CreateAndVerifyAsync(CancellationToken token)
    {
        var result = await helper.ExecuteAsync(new InterventionDefinition
        {
            Id = InterventionId.CreateStartupRestorePoint,
            Title = "Protéger le changement du démarrage",
            RequiresAdministrator = true,
            RiskLevel = InterventionRiskLevel.Sensitive
        }, token).ConfigureAwait(false);
        return result.Status == InterventionStatus.Completed && result.ExitCode == 0 &&
            result.SummaryOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.Trim() == "VIRGIL_RESTORE_POINT_VERIFIED");
    }
}
