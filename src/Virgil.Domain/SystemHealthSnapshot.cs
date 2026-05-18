namespace Virgil.Domain;

public sealed record SystemHealthSnapshot(
    DateTimeOffset CapturedAt,
    double CpuUsagePercent,
    MemoryStatus Memory,
    IReadOnlyList<DriveStatus> Drives,
    string OverallStatus,
    IReadOnlyList<string> Recommendations);
