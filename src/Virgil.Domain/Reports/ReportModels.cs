namespace Virgil.Domain;

public enum ReportKind
{
    QuickScan,
    DeepScan,
    Cleanup,
    Updates,
    Interventions,
    Resources,
    Startup,
    Network,
    WindowsRepair,
    ApplicationManagement,
    Unknown
}

public enum ReportSeverity
{
    Info,
    Success,
    Warning,
    Error,
    Critical
}

public enum ReportActionStatus
{
    Proposed,
    Executed,
    Skipped,
    Cancelled,
    Failed,
    Partial,
    InformationOnly
}

public sealed record ReportAction
{
    public string Name { get; init; } = string.Empty;

    public ReportActionStatus Status { get; init; }

    public string Risk { get; init; } = "Information";

    public string Result { get; init; } = string.Empty;

    public string? ReadableError { get; init; }

    public bool RestartRequired { get; init; }

    public string TechnicalDetails { get; init; } = string.Empty;
}

public sealed record ReportEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset Date { get; init; } = DateTimeOffset.Now;

    public ReportKind Kind { get; init; } = ReportKind.Unknown;

    public string Title { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Status { get; init; } = "Information";

    public ReportSeverity Severity { get; init; } = ReportSeverity.Info;

    public string Module { get; init; } = string.Empty;

    public IReadOnlyList<ReportAction> ProposedActions { get; init; } = Array.Empty<ReportAction>();

    public IReadOnlyList<ReportAction> ExecutedActions { get; init; } = Array.Empty<ReportAction>();

    public IReadOnlyList<ReportAction> SkippedActions { get; init; } = Array.Empty<ReportAction>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public bool RestartRequired { get; init; }

    public TimeSpan Duration { get; init; }

    public string SimpleView { get; init; } = string.Empty;

    public string TechnicalDetails { get; init; } = string.Empty;

    public string VirgilVersion { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;
}

public sealed record ReportHistoryIndex
{
    public IReadOnlyList<ReportEntry> Reports { get; init; } = Array.Empty<ReportEntry>();

    public int TotalCount { get; init; }

    public DateTimeOffset? LastReportDate { get; init; }

    public int AppliedLimit { get; init; } = 30;
}

public sealed record ReportSaveResult
{
    public bool Success { get; init; }

    public ReportEntry? Report { get; init; }

    public string? ReadableError { get; init; }
}

public sealed record ReportHistoryLoadResult
{
    public ReportHistoryIndex Index { get; init; } = new();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed record ReportExportResult
{
    public bool Success { get; init; }

    public string? ExportedPath { get; init; }

    public string? ReadableError { get; init; }
}
