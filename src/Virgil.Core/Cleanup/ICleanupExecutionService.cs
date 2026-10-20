using Virgil.Core.Actions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

public interface ICleanupExecutionService
{
    Task<CleanupStepResult> ExecuteZoneAsync(
        CleanupZonePreview preview,
        ConfirmedPlan? plan,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken);

    Task<CleanupSelectionResult> ExecuteSelectionAsync(IReadOnlyList<CleanupZonePreview> selection, ConfirmedPlan? plan,
        IProgress<CleanupProgress>? progress, CancellationToken cancellationToken) =>
        Task.FromResult(CleanupSelectionResult.Refused("Exécution de sélection indisponible."));

    CleanupStepResult SkipZone(CleanupZonePreview preview);

    CleanupStepResult CancelZone(CleanupZonePreview preview);

    CleanupSessionReport CreateReport(
        DateTimeOffset startedAt,
        IReadOnlyList<CleanupStepResult> results,
        IReadOnlyList<string> errors);
}
