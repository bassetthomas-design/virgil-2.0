using Virgil.Domain;

namespace Virgil.Core.Resources;

public interface IResourceMonitoringService
{
    Task<ResourceAnalysisReport> AnalyzeAsync(
        ResourceAnalysisRequest request,
        IProgress<ResourceProgress>? progress,
        CancellationToken cancellationToken);
}
