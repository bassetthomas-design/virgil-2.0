using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Virgil.Domain;

namespace Virgil.Core.Cleanup;

public interface ICleanupPreviewService
{
    IReadOnlyList<CleanupZoneDefinition> GetZones();

    Task<IReadOnlyList<CleanupZonePreview>> PreviewAsync(
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken);
}
