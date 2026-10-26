using System;
using System.Collections.Generic;
using System.Linq;

namespace Virgil.Contracts;

public enum CleanupGroup { Sur, AVerifier, RestesLogiciels }

public enum CleanupRiskLevel
{
    Low,
    Medium,
    High
}

public enum CleanupClassification
{
    Cleanable,
    AdvancedCleanable,
    ReviewOnly,
    Protected,
    InformationOnly
}

public enum CleanupZoneId
{
    UserTemporaryFiles,
    UserCrashDumps,
    DirectXShaderCache,
    WindowsTemporaryFiles,
    WindowsThumbnailCache,
    WindowsErrorReports,
    TechnicalLogs,
    BattleNetCache,
    VisualStudioCache,
    InternetCache,
    BrowserEdgeCache,
    BrowserChromeCache,
    BrowserFirefoxCache,
    BrowserBraveCache,
    BrowserOperaCache,
    RecycleBin,
    WindowsUpdateCache,
    DeliveryOptimizationCache,
    MicrosoftStoreCache,
    InstallerTemporaryFiles,
    WindowsOld,
    PrefetchInformation,
    OldDownloads,
    SoftwareLeftovers
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
    int DisplayOrder)
{
    public CleanupClassification Classification { get; init; } = CleanupClassification.Cleanable;
    public CleanupGroup Group => Id == CleanupZoneId.SoftwareLeftovers ? CleanupGroup.RestesLogiciels
        : Id == CleanupZoneId.RecycleBin || Classification == CleanupClassification.Cleanable ? CleanupGroup.Sur : CleanupGroup.AVerifier;
    public bool IsPersonalFolder { get; init; }
    public bool NeedsReview { get; init; }
    public string? SoftwareName { get; init; }
    public string? ValidatedRemnantRoot { get; init; }
    public string? RemnantAllowedRoot { get; init; }
    // Un logiciel peut laisser plusieurs racines : chacune conserve ses propres gardes.
    public IReadOnlyList<CleanupZoneDefinition> RemnantZones { get; init; } = Array.Empty<CleanupZoneDefinition>();
    public IReadOnlyList<string> RegistryKeys { get; init; } = Array.Empty<string>();
    public DateTimeOffset? LastUsedUtc { get; init; }


    public bool RequiresReinforcedConfirmation { get; init; }

    public bool IsExecutable { get; init; } = true;

    public bool RequiresElevation { get; init; }

    public IReadOnlyList<string> AllowedExtensions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RequiredPathFragments { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExcludedPathFragments { get; init; } = Array.Empty<string>();
}

public sealed record CleanupStorageReviewItem(
    string FullPath,
    string Name,
    string ItemType,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    CleanupClassification Classification,
    string Reason);

public sealed record CleanupStorageAnalysis(
    DateTimeOffset CapturedAt,
    IReadOnlyList<CleanupStorageReviewItem> Items,
    IReadOnlyList<string> SkippedRoots,
    IReadOnlyList<string> Errors)
{
    public int ReviewItemCount => Items.Count(item => item.Classification == CleanupClassification.ReviewOnly);
    public int ProtectedItemCount => Items.Count(item => item.Classification == CleanupClassification.Protected);
    public long ReviewBytes => Items.Where(item => item.Classification == CleanupClassification.ReviewOnly).Sum(item => item.SizeBytes);
}

public sealed record CleanupAnalysisReport(
    DateTimeOffset CapturedAt,
    IReadOnlyList<CleanupZonePreview> Zones,
    CleanupStorageAnalysis Storage,
    IReadOnlyList<string> TechnicalBlocks,
    IReadOnlyList<string> Errors)
{
    public long SafeBytes => Zones.Where(zone => zone.Definition.Classification == CleanupClassification.Cleanable).Sum(zone => zone.EligibleBytes);
    public long AdvancedBytes => Zones.Where(zone => zone.Definition.Classification == CleanupClassification.AdvancedCleanable).Sum(zone => zone.EligibleBytes);
}

public sealed record CleanupPermissionRepairAssessment(
    string ExactPath,
    bool IsAllowed,
    string Reason,
    bool RequiresCriticalConfirmation = true);

public sealed record CleanupCandidate(
    CleanupZoneId ZoneId,
    string FullPath,
    string LogicalPath,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    bool IsEligible,
    string? ExclusionReason)
{
    public CleanupGroup Group { get; init; }
    public bool IsPersonalFolder { get; init; }
    public bool NeedsReview { get; init; }
}

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
    public int QuarantineRetentionDays { get; init; } = 7;
    public bool HasEligibleCandidates => EligibleFileCount > 0;
}

public sealed record CleanupProgress(
    CleanupZoneId? ZoneId,
    string Step,
    int? Percent,
    string Message,
    int ProcessedFiles = 0,
    int TotalFiles = 0,
    int DeletedFiles = 0,
    long DeletedBytes = 0,
    int SkippedFiles = 0,
    int ErrorFiles = 0);

public sealed record CleanupStepResult(
    CleanupZoneDefinition Zone,
    CleanupStepStatus Status,
    int DeletedFiles,
    long DeletedBytes,
    int SkippedFiles,
    int ErrorFiles,
    TimeSpan Duration,
    IReadOnlyList<string> Errors)
{
    public int QuarantinedFiles { get; init; }
    public long QuarantinedBytes { get; init; }
    public int QuarantineRetentionDays { get; init; } = 7;
    public string QuarantineSummary => $"{QuarantinedFiles} fichiers mis en quarantaine ({QuarantinedBytes} octets), conservé {QuarantineRetentionDays} jours en quarantaine, puis effacement définitif proposé avec confirmation";
    /// <summary>Renseigné uniquement pour un refus avant effet, jamais pour une erreur d'exécution.</summary>
    public string? RefusalReason { get; init; }
}

public sealed record CleanupSessionReport(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    TimeSpan Duration,
    IReadOnlyList<CleanupStepResult> Results,
    IReadOnlyList<string> Errors)
{
    public int QuarantinedFiles => Results.Sum(result => result.QuarantinedFiles);
    public long QuarantinedBytes => Results.Sum(result => result.QuarantinedBytes);
    public int DeletedFiles => Results.Sum(result => result.DeletedFiles);
    public long DeletedBytes => Results.Sum(result => result.DeletedBytes);
    public int SkippedZones => Results.Count(result => result.Status == CleanupStepStatus.Skipped);
    public int CancelledZones => Results.Count(result => result.Status == CleanupStepStatus.Cancelled);
    public int ErrorFiles => Results.Sum(result => result.ErrorFiles);
    public long EstimatedBytes { get; init; }
    public int ReviewItems { get; init; }
    public long ReviewBytes { get; init; }
    public int ProtectedItems { get; init; }
    public int AdvancedRefused { get; init; }
    public int LockedFilesIgnored { get; init; }
    public int ReparsePointsIgnored { get; init; }
    public IReadOnlyList<string> RefusedPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PermissionRepairsProposed { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PermissionRepairsExecuted { get; init; } = Array.Empty<string>();
    public bool RestartRecommended { get; init; }
    public bool PersonalFilesDeletedAutomatically => false;
}
