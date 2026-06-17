namespace Virgil.Domain;

public enum UpdateScanScope
{
    AvailabilityOnly,
    DeepPreview
}

public enum UpdateSource
{
    Winget,
    MicrosoftStore,
    WindowsUpdate,
    Driver,
    FirmwareInformation
}

public enum UpdateRiskLevel
{
    Safe,
    ValidationRequired,
    Sensitive,
    CriticalInformationOnly
}

public enum UpdateItemStatus
{
    Available,
    Completed,
    Skipped,
    Failed,
    Cancelled,
    InformationOnly
}

public sealed record UpdateScanRequest
{
    public static UpdateScanRequest QuickAvailability { get; } = new()
    {
        Scope = UpdateScanScope.AvailabilityOnly,
        IncludeApplicationUpdates = false,
        IncludeDriverInventory = false
    };

    public static UpdateScanRequest DeepPreview { get; } = new()
    {
        Scope = UpdateScanScope.DeepPreview,
        IncludeApplicationUpdates = true,
        IncludeDriverInventory = true
    };

    public UpdateScanScope Scope { get; init; }

    public bool IncludeApplicationUpdates { get; init; }

    public bool IncludeDriverInventory { get; init; }
}

public sealed record WingetAvailability
{
    public bool IsAvailable { get; init; }

    public string? ExecutablePath { get; init; }

    public string? Version { get; init; }

    public string Message { get; init; } = "WinGet non detecte.";

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static WingetAvailability Unavailable(string message, IReadOnlyList<string>? errors = null)
    {
        return new WingetAvailability
        {
            IsAvailable = false,
            Message = message,
            Errors = errors ?? Array.Empty<string>()
        };
    }
}

public sealed record WingetCapabilities
{
    public bool SupportsAcceptSourceAgreements { get; init; }

    public bool SupportsAcceptPackageAgreements { get; init; }

    public bool SupportsDisableInteractivity { get; init; }

    public static WingetCapabilities Conservative { get; } = new()
    {
        SupportsAcceptSourceAgreements = false,
        SupportsAcceptPackageAgreements = false,
        SupportsDisableInteractivity = false
    };
}

public sealed record UpdateCommandPreview
{
    public string ExecutablePath { get; init; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
}

public sealed record UpdateItem
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Publisher { get; init; } = string.Empty;

    public string InstalledVersion { get; init; } = string.Empty;

    public string AvailableVersion { get; init; } = string.Empty;

    public UpdateSource Source { get; init; }

    public string Scope { get; init; } = "Inconnu";

    public UpdateRiskLevel RiskLevel { get; init; } = UpdateRiskLevel.ValidationRequired;

    public string RiskReason { get; init; } = "Validation utilisateur requise.";

    public bool RequiresExplicitConfirmation { get; init; } = true;

    public UpdateItemStatus Status { get; init; } = UpdateItemStatus.Available;

    public UpdateCommandPreview? CommandPreview { get; init; }

    public string? Message { get; init; }

    public string? TechnicalDetails { get; init; }
}

public sealed record WindowsUpdateInformation
{
    public string Status { get; init; } = "A examiner dans Windows Update.";

    public string SettingsUri { get; init; } = "ms-settings:windowsupdate";

    public bool ServiceRegistryKeyPresent { get; init; }

    public bool PendingRebootDetected { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed record DriverInformation
{
    public string PublishedName { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string ClassName { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Date { get; init; } = string.Empty;

    public string Signer { get; init; } = string.Empty;

    public string Status { get; init; } = "Inventorie";
}

public sealed record DriverInventoryReport
{
    public bool WasAnalyzed { get; init; }

    public bool CanInstallDrivers { get; init; }

    public string InstallButtonVisibilityReason { get; init; } =
        "Masque en V1 : aucune source fiable d'installation pilote n'est executee.";

    public IReadOnlyList<DriverInformation> Drivers { get; init; } = Array.Empty<DriverInformation>();

    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed record UpdateScanReport
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    public UpdateScanScope Scope { get; init; } = UpdateScanScope.AvailabilityOnly;

    public TimeSpan Duration { get; init; }

    public string OverallStatus { get; init; } = "Non analyse";

    public WingetAvailability Winget { get; init; } =
        WingetAvailability.Unavailable("WinGet non detecte.");

    public IReadOnlyList<UpdateItem> Items { get; init; } = Array.Empty<UpdateItem>();

    public WindowsUpdateInformation WindowsUpdate { get; init; } = new();

    public DriverInventoryReport Drivers { get; init; } = new();

    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public int SafeCount => Items.Count(item => item.RiskLevel == UpdateRiskLevel.Safe);

    public int ValidationRequiredCount => Items.Count(item => item.RiskLevel == UpdateRiskLevel.ValidationRequired);

    public int SensitiveCount => Items.Count(item => item.RiskLevel == UpdateRiskLevel.Sensitive);

    public int InformationOnlyCount => Items.Count(item => item.RiskLevel == UpdateRiskLevel.CriticalInformationOnly);
}

public sealed record UpdateExecutionResult
{
    public UpdateItem Item { get; init; } = new();

    public UpdateItemStatus Status { get; init; }

    public string UserMessage { get; init; } = string.Empty;

    public string TechnicalDetails { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;

    public TimeSpan Duration { get; init; }
}

public sealed record UpdateSessionReport
{
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;

    public TimeSpan Duration { get; init; }

    public IReadOnlyList<UpdateExecutionResult> Results { get; init; } = Array.Empty<UpdateExecutionResult>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public int CompletedCount => Results.Count(result => result.Status == UpdateItemStatus.Completed);

    public int SkippedCount => Results.Count(result => result.Status == UpdateItemStatus.Skipped);

    public int FailedCount => Results.Count(result => result.Status == UpdateItemStatus.Failed);

    public bool WasCancelled => Results.Any(result => result.Status == UpdateItemStatus.Cancelled) ||
        Errors.Any(error => error.Contains("annul", StringComparison.OrdinalIgnoreCase));
}

public sealed record UpdateScanSummary
{
    public bool WasAnalyzed { get; init; }

    public bool WingetAvailable { get; init; }

    public int ApplicationUpdates { get; init; }

    public int SensitiveUpdates { get; init; }

    public int DriverCount { get; init; }

    public string Status { get; init; } = "Non analyse";

    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static UpdateScanSummary NotAnalyzed { get; } = new();
}
