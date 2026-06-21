using Virgil.Domain;

namespace Virgil.Core.Cleanup;

public sealed class CleanupAnalyzer
{
    private readonly ICleanupPreviewService _previewService;
    private readonly CleanupStorageAnalyzer _storageAnalyzer;

    public CleanupAnalyzer(ICleanupPreviewService? previewService = null, CleanupStorageAnalyzer? storageAnalyzer = null)
    {
        _previewService = previewService ?? new CleanupPreviewService();
        _storageAnalyzer = storageAnalyzer ?? new CleanupStorageAnalyzer();
    }

    public async Task<CleanupAnalysisReport> AnalyzeAsync(IProgress<CleanupProgress>? progress, CancellationToken cancellationToken)
    {
        var zones = await _previewService.PreviewAsync(progress, cancellationToken).ConfigureAwait(false);
        var storage = await _storageAnalyzer.AnalyzeAsync(cancellationToken).ConfigureAwait(false);
        var technicalBlocks = zones
            .Where(zone => zone.Errors.Count > 0 || (zone.Definition.RequiresElevation && zone.EligibleFileCount == 0))
            .Select(zone => $"{zone.Definition.DisplayName} : acces limite ou action elevee non executee.")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var errors = zones.SelectMany(zone => zone.Errors).Concat(storage.Errors).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return new CleanupAnalysisReport(DateTimeOffset.Now, zones, storage, technicalBlocks, errors);
    }
}
