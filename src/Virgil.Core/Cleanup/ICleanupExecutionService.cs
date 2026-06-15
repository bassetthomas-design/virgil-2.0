using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Virgil.Domain;

namespace Virgil.Core.Cleanup;

public interface ICleanupExecutionService
{
    Task<CleanupStepResult> ExecuteZoneAsync(
        CleanupZonePreview preview,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken);

    CleanupStepResult SkipZone(CleanupZonePreview preview);

    CleanupStepResult CancelZone(CleanupZonePreview preview);

    CleanupSessionReport CreateReport(
        DateTimeOffset startedAt,
        IReadOnlyList<CleanupStepResult> results,
        IReadOnlyList<string> errors);
}
