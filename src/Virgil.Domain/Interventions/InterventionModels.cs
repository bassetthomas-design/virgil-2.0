namespace Virgil.Domain;

public enum InterventionId
{
    RestartExplorer,
    FlushDns,
    RenewIp,
    ResetWinsock,
    ResetTcpIp,
    SfcScan,
    DismScanHealth,
    DismRestoreHealth,
    ChkdskOnlineScan
}

public enum InterventionCategory
{
    System,
    Network,
    Storage,
    Interface
}

public enum InterventionRiskLevel
{
    Low,
    Moderate,
    Sensitive
}

public enum InterventionStatus
{
    Available,
    Recommended,
    Running,
    Completed,
    PartialFailure,
    Failed,
    Skipped,
    Cancelled,
    RebootRequired,
    Unavailable
}

public sealed record InterventionCommandPreview
{
    public string Executable { get; init; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public bool RunsElevated { get; init; }
}

public sealed record InterventionDefinition
{
    public InterventionId Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public InterventionCategory Category { get; init; }

    public string Description { get; init; } = string.Empty;

    public string ExpectedEffect { get; init; } = string.Empty;

    public string NotTouched { get; init; } = string.Empty;

    public InterventionRiskLevel RiskLevel { get; init; }

    public bool RequiresAdministrator { get; init; }

    public bool RebootPossible { get; init; }

    public string EstimatedDuration { get; init; } = string.Empty;

    public string AvailabilityCondition { get; init; } = string.Empty;

    public int DisplayOrder { get; init; }

    public bool CanBeInterruptedAfterStart { get; init; }

    public IReadOnlyList<InterventionCommandPreview> CommandPreviews { get; init; } =
        Array.Empty<InterventionCommandPreview>();
}

public sealed record InterventionDiagnostic
{
    public InterventionDefinition Definition { get; init; } = new();

    public bool IsAvailable { get; init; }

    public InterventionStatus Status { get; init; } = InterventionStatus.Unavailable;

    public string StateBefore { get; init; } = "Non analyse";

    public string Recommendation { get; init; } = "Disponible apres validation explicite.";

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> TechnicalData { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record InterventionExecutionResult
{
    public InterventionDefinition Action { get; init; } = new();

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset FinishedAt { get; init; } = DateTimeOffset.Now;

    public int ExitCode { get; init; }

    public InterventionStatus Status { get; init; }

    public string SummaryOutput { get; init; } = string.Empty;

    public string? ReadableError { get; init; }

    public bool RebootRequired { get; init; }

    public string StateBefore { get; init; } = string.Empty;

    public string StateAfter { get; init; } = string.Empty;

    public bool WasConfirmed { get; init; }

    public bool WasElevated { get; init; }

    public TimeSpan Duration => FinishedAt - StartedAt;
}

public sealed record InterventionSessionReport
{
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;

    public TimeSpan Duration { get; init; }

    public IReadOnlyList<InterventionDiagnostic> ProposedActions { get; init; } =
        Array.Empty<InterventionDiagnostic>();

    public IReadOnlyList<InterventionExecutionResult> Results { get; init; } =
        Array.Empty<InterventionExecutionResult>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public int ExecutedActions => Results.Count(result => result.WasConfirmed &&
        result.Status is not InterventionStatus.Skipped and not InterventionStatus.Cancelled);

    public int SkippedActions => Results.Count(result => result.Status == InterventionStatus.Skipped);

    public int CancelledActions => Results.Count(result => result.Status == InterventionStatus.Cancelled);

    public int Successes => Results.Count(result => result.Status is
        InterventionStatus.Completed or InterventionStatus.RebootRequired);

    public int Failures => Results.Count(result => result.Status is
        InterventionStatus.Failed or InterventionStatus.PartialFailure);

    public bool RebootRequired => Results.Any(result => result.RebootRequired ||
        result.Status == InterventionStatus.RebootRequired);
}

public sealed record InterventionScanSummary
{
    public bool WasAnalyzed { get; init; }

    public int AvailableActions { get; init; }

    public int RecommendedActions { get; init; }

    public bool RebootPotentiallyRequired { get; init; }

    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static InterventionScanSummary NotAnalyzed { get; } = new();
}

public sealed record ElevatedInterventionRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public InterventionId ActionId { get; init; }

    public string Nonce { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string ResultPath { get; init; } = string.Empty;
}

public sealed record ElevatedInterventionResult
{
    public int ProtocolVersion { get; init; } = 1;

    public InterventionId ActionId { get; init; }

    public string Nonce { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset FinishedAt { get; init; } = DateTimeOffset.UtcNow;

    public int ExitCode { get; init; }

    public InterventionStatus Status { get; init; }

    public string SummaryOutput { get; init; } = string.Empty;

    public string? ReadableError { get; init; }

    public bool RebootRequired { get; init; }
}
