using System;
using System.Collections.Generic;
using System.Linq;

namespace Virgil.Domain;

public enum CleanupRiskLevel
{
    Low,
    Medium,
    High
}

public enum CleanupZoneId
{
    UserTemporaryFiles,
    UserCrashDumps,
    DirectXShaderCache
}

public enum CleanupStepStatus
{
    Completed,
    Skipped,
    Cancelled,
    Expired,
    PartialFailure,
    Failed
}

public sealed record CleanupZoneDefinition(
    CleanupZoneId Id,
    string DisplayName,
    string Description,
    string RootPath,
    TimeSpan MinimumAge,
    CleanupRiskLevel RiskLevel,
    string Warning,
    string Effect,
    string NotTouched,
    int DisplayOrder);

public sealed record CleanupCandidate(
    CleanupZoneId ZoneId,
    string FullPath,
    string LogicalPath,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    bool IsEligible,
    string? ExclusionReason);

public sealed record CleanupZonePreview(
    CleanupZoneDefinition Definition,
    DateTimeOffset GeneratedAt,
    int ExaminedFileCount,
    int EligibleFileCount,
    long EligibleBytes,
    int ExcludedFileCount,
    IReadOnlyList<CleanupCandidate> Candidates,
    IReadOnlyList<string> Errors)
{
    public bool HasEligibleCandidates => EligibleFileCount > 0;
}

public sealed record CleanupProgress(
    CleanupZoneId? ZoneId,
    string Step,
    int? Percent,
    string Message);

public sealed record CleanupStepResult(
    CleanupZoneDefinition Zone,
    CleanupStepStatus Status,
    int DeletedFiles,
    long DeletedBytes,
    int SkippedFiles,
    int ErrorFiles,
    TimeSpan Duration,
    IReadOnlyList<string> Errors);

public sealed record CleanupSessionReport(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    TimeSpan Duration,
    IReadOnlyList<CleanupStepResult> Results,
    IReadOnlyList<string> Errors)
{
    public int DeletedFiles => Results.Sum(result => result.DeletedFiles);
    public long DeletedBytes => Results.Sum(result => result.DeletedBytes);
    public int SkippedZones => Results.Count(result => result.Status == CleanupStepStatus.Skipped);
    public int CancelledZones => Results.Count(result => result.Status == CleanupStepStatus.Cancelled);
    public int ErrorFiles => Results.Sum(result => result.ErrorFiles);
}
