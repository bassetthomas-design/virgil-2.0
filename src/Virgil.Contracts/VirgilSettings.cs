namespace Virgil.Contracts;

public sealed record VirgilSettings
{
    private int _quarantineRetentionDays = 7;
    public int QuarantineRetentionDays
    {
        get => _quarantineRetentionDays;
        init => _quarantineRetentionDays = value is >= 1 and <= 30 ? value : 7;
    }

    public bool CheckVirgilUpdatesAtStartup { get; init; } = true;
    public DateTimeOffset? LastVirgilUpdateCheckUtc { get; init; }

    public int SchemaVersion { get; init; } = 1;
    public string MinimumLogLevel { get; init; } = "Information";
}
