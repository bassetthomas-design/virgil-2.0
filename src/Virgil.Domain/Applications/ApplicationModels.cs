namespace Virgil.Domain.Applications;

public enum ApplicationInventorySource
{
    Registry,
    Msi,
    Winget,
    Store,
    Unknown
}

public enum ApplicationArchitecture
{
    X64,
    X86,
    Unknown
}

public enum ApplicationUninstallKind
{
    None,
    Msi,
    RegistryUninstallString,
    RegistryQuietUninstallString,
    Winget,
    StoreSettings,
    Unknown
}

public enum ApplicationRiskLevel
{
    SafeToUninstall,
    Caution,
    Protected,
    Unknown
}

public enum ApplicationStatus
{
    Installed,
    UninstallAvailable,
    Protected,
    ReadOnly,
    Unknown,
    UninstallStarted,
    UninstallCompleted,
    UninstallFailed
}

public enum ApplicationCommandValidationStatus
{
    Allowed,
    NeedsCaution,
    Blocked
}

public enum ApplicationRemnantKind
{
    TechnicalRemnant,
    UserData,
    ProtectedRemnant,
    UnknownRemnant
}

public enum ApplicationRemnantAction
{
    OpenLocation,
    ExportList,
    Ignore,
    MarkReview,
    DeleteTechnicalOnly
}

public sealed record InstalledApplication
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Publisher { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public DateTimeOffset? InstallDate { get; init; }

    public long? EstimatedSizeBytes { get; init; }

    public ApplicationInventorySource Source { get; init; } = ApplicationInventorySource.Unknown;

    public IReadOnlyList<ApplicationInventorySource> Sources { get; init; } = Array.Empty<ApplicationInventorySource>();

    public ApplicationArchitecture Architecture { get; init; } = ApplicationArchitecture.Unknown;

    public string? InstallLocation { get; init; }

    public string? IconPath { get; init; }

    public string? ExtractedIconPath { get; init; }

    public string? UninstallCommand { get; init; }

    public string? QuietUninstallCommand { get; init; }

    public string? MsiProductCode { get; init; }

    public string? WingetId { get; init; }

    public string? StorePackageFullName { get; init; }

    public ApplicationUninstallKind UninstallKind { get; init; } = ApplicationUninstallKind.None;

    public ApplicationRiskLevel RiskLevel { get; init; } = ApplicationRiskLevel.Unknown;

    public ApplicationStatus Status { get; init; } = ApplicationStatus.Unknown;

    public string RiskReason { get; init; } = string.Empty;

    public bool CanUninstall { get; init; }

    public bool CanOpenLocation => !string.IsNullOrWhiteSpace(InstallLocation);

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record ApplicationRegistryEntry
{
    public string RegistryView { get; init; } = string.Empty;

    public string KeyName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Publisher { get; init; } = string.Empty;

    public string DisplayVersion { get; init; } = string.Empty;

    public string? InstallDateRaw { get; init; }

    public long? EstimatedSizeKilobytes { get; init; }

    public string? InstallLocation { get; init; }

    public string? DisplayIcon { get; init; }

    public string? UninstallString { get; init; }

    public string? QuietUninstallString { get; init; }

    public bool WindowsInstaller { get; init; }

    public bool SystemComponent { get; init; }

    public ApplicationArchitecture Architecture { get; init; } = ApplicationArchitecture.Unknown;
}

public sealed record ApplicationInventoryProgress
{
    public string Step { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public int ApplicationsFound { get; init; }

    public int? Percent { get; init; }

    public string Status { get; init; } = string.Empty;
}

public sealed record ApplicationInventorySourceResult(
    IReadOnlyList<InstalledApplication> Applications,
    IReadOnlyList<string> Errors);

public sealed record ApplicationInventoryReport
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    public TimeSpan Duration { get; init; }

    public IReadOnlyList<InstalledApplication> Applications { get; init; } = Array.Empty<InstalledApplication>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public int UninstallableCount => Applications.Count(app => app.CanUninstall);

    public int ProtectedCount => Applications.Count(app => app.RiskLevel == ApplicationRiskLevel.Protected);

    public int UnknownCount => Applications.Count(app => app.RiskLevel == ApplicationRiskLevel.Unknown);

    public int CautionCount => Applications.Count(app => app.RiskLevel == ApplicationRiskLevel.Caution);

    public int SafeCount => Applications.Count(app => app.RiskLevel == ApplicationRiskLevel.SafeToUninstall);
}

public sealed record ApplicationCommandValidationResult
{
    public ApplicationCommandValidationStatus Status { get; init; } = ApplicationCommandValidationStatus.Blocked;

    public string Reason { get; init; } = string.Empty;

    public string? Executable { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
}

public sealed record ApplicationUninstallPlan
{
    public InstalledApplication Application { get; init; } = new();

    public ApplicationUninstallKind Method { get; init; } = ApplicationUninstallKind.None;

    public ApplicationCommandValidationResult Validation { get; init; } = new();

    public bool RequiresCautionConfirmation { get; init; }

    public bool CanLaunch => Validation.Status is ApplicationCommandValidationStatus.Allowed or ApplicationCommandValidationStatus.NeedsCaution;
}

public sealed record ApplicationUninstallProgress
{
    public int StepNumber { get; init; }

    public int TotalSteps { get; init; } = 5;

    public int? Percent { get; init; }

    public string Step { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public IReadOnlyList<string> NonBlockingErrors { get; init; } = Array.Empty<string>();
}

public sealed record ApplicationUninstallResult
{
    public InstalledApplication Application { get; init; } = new();

    public ApplicationUninstallKind Method { get; init; } = ApplicationUninstallKind.None;

    public bool WasLaunched { get; init; }

    public bool WasCancelled { get; init; }

    public bool StatusUnknown { get; init; }

    public int? ExitCode { get; init; }

    public string Result { get; init; } = string.Empty;

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public ApplicationRemnantScanReport Remnants { get; init; } = new();
}

public sealed record ApplicationRemnantCandidate
{
    public string Path { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public long? SizeBytes { get; init; }

    public bool IsDirectory { get; init; }

    public ApplicationRemnantKind Kind { get; init; } = ApplicationRemnantKind.UnknownRemnant;

    public string Reason { get; init; } = string.Empty;

    public IReadOnlyList<ApplicationRemnantAction> AvailableActions { get; init; } = Array.Empty<ApplicationRemnantAction>();
}

public sealed record ApplicationRemnantScanReport
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    public InstalledApplication? Application { get; init; }

    public IReadOnlyList<ApplicationRemnantCandidate> Remnants { get; init; } = Array.Empty<ApplicationRemnantCandidate>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public int TechnicalCount => Remnants.Count(item => item.Kind == ApplicationRemnantKind.TechnicalRemnant);

    public int UserDataCount => Remnants.Count(item => item.Kind == ApplicationRemnantKind.UserData);

    public int ProtectedCount => Remnants.Count(item => item.Kind == ApplicationRemnantKind.ProtectedRemnant);

    public int UnknownCount => Remnants.Count(item => item.Kind == ApplicationRemnantKind.UnknownRemnant);
}

public sealed record ApplicationManagementSessionReport
{
    public DateTimeOffset Date { get; init; } = DateTimeOffset.Now;

    public ApplicationInventoryReport? Inventory { get; init; }

    public ApplicationUninstallResult? Uninstall { get; init; }

    public IReadOnlyList<ApplicationRemnantCandidate> TechnicalRemnantsDeleted { get; init; } = Array.Empty<ApplicationRemnantCandidate>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public bool PersonalDataAutoDeleted { get; init; }
}

public sealed record ApplicationScanSummary
{
    public static ApplicationScanSummary NotAnalyzed { get; } = new();

    public bool WasAnalyzed { get; init; }

    public int DetectedCount { get; init; }

    public int UninstallableCount { get; init; }

    public int ProtectedCount { get; init; }

    public int UnknownCount { get; init; }

    public int LargeApplicationCount { get; init; }

    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
