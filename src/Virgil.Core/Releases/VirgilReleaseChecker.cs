using Virgil.Core.Settings;

namespace Virgil.Core.Releases;

public sealed class VirgilReleaseChecker(IVirgilReleaseSource source, ApplicationVersionService version,
    ISettingsService settings, TimeProvider time)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public static bool StartupDue(bool enabled, DateTimeOffset? last, DateTimeOffset now) =>
        enabled && (last is null || now - last.Value >= TimeSpan.FromHours(24));

    /// <summary>Null signifie uniquement : contrôle de démarrage désactivé ou déjà effectué.</summary>
    public async Task<ReleaseCheckResult?> CheckAsync(bool startup, CancellationToken ct)
    {
        var entered = false;
        try
        {
            await _gate.WaitAsync(ct);
            entered = true;
            if (startup && !StartupDue(settings.Current.CheckVirgilUpdatesAtStartup,
                settings.Current.LastVirgilUpdateCheckUtc, time.GetUtcNow())) return null;
            // Persister avant l'accès réseau : les échecs comptent aussi dans les 24 h.
            try { await settings.SaveAsync(settings.Current with { LastVirgilUpdateCheckUtc = time.GetUtcNow() }, ct); }
            catch (OperationCanceledException) { return new ReleaseCheckResult.Impossible(ReleaseFailure.Cancelled); }
            catch (Exception) { return new ReleaseCheckResult.Impossible(ReleaseFailure.SettingsUnavailable); }
            var current = version.Current;
            if (current is null) return new ReleaseCheckResult.Impossible(ReleaseFailure.InvalidResponse);
            var result = await source.ReadAsync(ct);
            if (result is ReleaseSourceResult.Unavailable failure) return new ReleaseCheckResult.Impossible(failure.Reason);
            var latest = ((ReleaseSourceResult.Published)result).Releases.OrderByDescending(r => r.Version).FirstOrDefault();
            if (latest is null) return new ReleaseCheckResult.Impossible(ReleaseFailure.NoPublishedRelease);
            return latest.Version.CompareTo(current) > 0 ? new ReleaseCheckResult.NewVersion(latest) : new ReleaseCheckResult.UpToDate();
        }
        catch (OperationCanceledException) { return new ReleaseCheckResult.Impossible(ReleaseFailure.Cancelled); }
        catch (Exception) { return new ReleaseCheckResult.Impossible(ReleaseFailure.InvalidResponse); }
        finally { if (entered) _gate.Release(); }
    }
}
