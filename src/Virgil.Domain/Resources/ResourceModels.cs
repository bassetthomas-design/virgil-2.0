namespace Virgil.Domain;

public enum ResourceHealthLevel
{
    Unknown = -1,
    Stable = 0,
    Watch = 1,
    InterventionRecommended = 2,
    Critical = 3
}

public enum ProcessResourceStatus
{
    Normal,
    Heavy,
    Review,
    Protected,
    System
}

public enum ProcessActionKind
{
    CloseMainWindow,
    KillProcess,
    OpenLocation,
    RestartExplorer,
    ReleaseInactiveMemory
}

public enum ProcessActionStatus
{
    Completed,
    PartialFailure,
    Failed,
    Skipped,
    Cancelled,
    InformationOnly
}

public sealed record ResourceAnalysisRequest
{
    public TimeSpan ObservationDuration { get; init; } = TimeSpan.FromSeconds(5);

    public int SampleCount { get; init; } = 5;

    public int MaximumProcesses { get; init; } = 12;

    public static ResourceAnalysisRequest Interactive { get; } = new();

    public static ResourceAnalysisRequest DeepScanPreview { get; } = new()
    {
        ObservationDuration = TimeSpan.FromSeconds(1.2),
        SampleCount = 3,
        MaximumProcesses = 5
    };
}

public sealed record ResourceProgress(
    string Step,
    int Percent,
    string Message);

public sealed record ResourceSample
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public double InstantCpuPercent { get; init; }

    public double ShortAverageCpuPercent { get; init; }

    public ulong TotalMemoryBytes { get; init; }

    public ulong UsedMemoryBytes { get; init; }

    public ulong AvailableMemoryBytes { get; init; }

    public double UsedMemoryPercent { get; init; }

    public TimeSpan Uptime { get; init; }

    public int ProcessCount { get; init; }

    public ResourceHealthLevel OverallHealth { get; init; } = ResourceHealthLevel.Unknown;
}

public sealed record ProcessResourceInfo
{
    public int ProcessId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? MainWindowTitle { get; init; }

    public string? Path { get; init; }

    public string? Publisher { get; init; }

    public long WorkingSetBytes { get; init; }

    public double CpuPercent { get; init; }

    public ProcessResourceStatus Status { get; init; }

    public bool CanCloseGracefully { get; init; }

    public bool CanForceClose { get; init; }

    public bool IsCriticalSystemProcess { get; init; }

    public string UserMessage { get; init; } = string.Empty;

    public DateTimeOffset? StartedAt { get; init; }
}

public sealed record ProcessInspectionResult
{
    public IReadOnlyList<ProcessResourceInfo> Processes { get; init; } =
        Array.Empty<ProcessResourceInfo>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed record ResourceAnalysisReport
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    public TimeSpan Duration { get; init; }

    public IReadOnlyList<ResourceSample> Samples { get; init; } = Array.Empty<ResourceSample>();

    public double AverageCpuPercent { get; init; }

    public double MaximumCpuPercent { get; init; }

    public double AverageMemoryPercent { get; init; }

    public double MaximumMemoryPercent { get; init; }

    public ResourceHealthLevel OverallHealth { get; init; } = ResourceHealthLevel.Unknown;

    public IReadOnlyList<ProcessResourceInfo> TopMemoryProcesses { get; init; } =
        Array.Empty<ProcessResourceInfo>();

    public IReadOnlyList<ProcessResourceInfo> TopCpuProcesses { get; init; } =
        Array.Empty<ProcessResourceInfo>();

    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public TimeSpan Uptime => Samples.LastOrDefault()?.Uptime ?? TimeSpan.Zero;

    public int ProcessCount => Samples.LastOrDefault()?.ProcessCount ?? 0;

    public bool RestartRecommended => Uptime >= TimeSpan.FromDays(7);
}

public sealed record ProcessActionResult
{
    public ProcessActionKind Action { get; init; }

    public string Target { get; init; } = string.Empty;

    public ProcessActionStatus Status { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string? ReadableError { get; init; }

    public string Summary { get; init; } = string.Empty;
}

public sealed record ResourceSessionReport
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    public IReadOnlyList<ResourceAnalysisReport> Analyses { get; init; } =
        Array.Empty<ResourceAnalysisReport>();

    public IReadOnlyList<string> ProposedActions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ProcessActionResult> ExecutedActions { get; init; } =
        Array.Empty<ProcessActionResult>();

    public IReadOnlyList<string> SkippedActions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public bool RestartRecommended { get; init; }
}

public sealed record ResourceScanSummary
{
    public bool WasAnalyzed { get; init; }

    public double AverageCpuPercent { get; init; }

    public double MemoryPercent { get; init; }

    public TimeSpan Uptime { get; init; }

    public int HeavyProcessCount { get; init; }

    public IReadOnlyList<string> TopMemoryProcesses { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static ResourceScanSummary NotAnalyzed { get; } = new();
}
