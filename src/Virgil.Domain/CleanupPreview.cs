namespace Virgil.Domain;

public sealed record CleanupPreview(
    DateTimeOffset CapturedAt,
    IReadOnlyList<CleanupTarget> Targets)
{
    public long TotalBytes => Targets.Sum(target => target.Bytes);
    public int TotalFiles => Targets.Sum(target => target.FileCount);
}

public sealed record CleanupTarget(
    string DisplayName,
    string Path,
    long Bytes,
    int FileCount,
    string RiskLevel);
