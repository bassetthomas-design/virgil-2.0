namespace Virgil.Core.Releases;

public sealed record VirgilRelease(SemanticVersion Version, DateTimeOffset PublishedAt, Uri Page);
public enum ReleaseFailure { OfflineOrTimeout, ApiLimit, InvalidResponse, NoPublishedRelease, Cancelled, SettingsUnavailable }

public abstract record ReleaseSourceResult
{
    private ReleaseSourceResult() { }
    public sealed record Published(IReadOnlyList<VirgilRelease> Releases) : ReleaseSourceResult;
    public sealed record Unavailable(ReleaseFailure Reason) : ReleaseSourceResult;
}

public abstract record ReleaseCheckResult
{
    private ReleaseCheckResult() { }
    public sealed record UpToDate : ReleaseCheckResult;
    public sealed record NewVersion(VirgilRelease Release) : ReleaseCheckResult;
    public sealed record Impossible(ReleaseFailure Reason) : ReleaseCheckResult;
}

public interface IVirgilReleaseSource
{
    Task<ReleaseSourceResult> ReadAsync(CancellationToken cancellationToken);
}
public interface IApplicationVersionSource
{
    string? ReadInformationalVersion();
}
public sealed class ApplicationVersionService(IApplicationVersionSource source)
{
    public SemanticVersion? Current => SemanticVersion.Parse(source.ReadInformationalVersion());
}
