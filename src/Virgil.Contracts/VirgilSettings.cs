namespace Virgil.Contracts;

public sealed record VirgilSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string MinimumLogLevel { get; init; } = "Information";
}
