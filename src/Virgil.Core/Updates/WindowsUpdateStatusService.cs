using Virgil.Contracts;

namespace Virgil.Core.Updates;

public sealed record WindowsUpdateProbeResult(string AvailabilityState, int AvailableCount, string Status,
    string? UnavailableReason = null, IReadOnlyList<string>? Notes = null);

public interface IWindowsUpdateReader
{
    Task<WindowsUpdateProbeResult> ReadAsync(CancellationToken cancellationToken);
}

public sealed class WindowsUpdateStatusService(IPendingRebootProbe probe, IWindowsUpdateReader? reader = null)
{
    public WindowsUpdateInformation ReadStatus()
    {
        var notes = new List<string>
        {
            "Ouverture contrôlée de la page Windows Update uniquement.",
            "« Tout mettre à jour » peut rechercher et installer des mises à jour après confirmation, sans jamais redémarrer automatiquement."
        };
        var errors = new List<string>();
        PendingRebootStatus status;
        try { status = probe.Read(); }
        catch
        {
            status = new(false, []);
            errors.Add("Lecture de Windows Update et du redémarrage en attente indisponible.");
        }
        foreach (var source in status.Sources) notes.Add("Redémarrage en attente : " + source + ".");
        return new WindowsUpdateInformation
        {
            Status = status.ServicePresent ? "Windows Update accessible via les Paramètres Windows." : "Windows Update non confirmé localement.",
            SettingsUri = "ms-settings:windowsupdate",
            ServiceRegistryKeyPresent = status.ServicePresent,
            PendingRebootDetected = status.Sources.Count > 0,
            Notes = notes,
            Errors = errors
        };
    }

    public async Task<WindowsUpdateInformation> ScanAsync(CancellationToken cancellationToken)
    {
        var local = ReadStatus();
        if (reader is null) return local;

        WindowsUpdateProbeResult result;
        try { result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            const string reason = "Lecture Windows Update indisponible ; ouvrez Windows Update pour vérifier.";
            result = new("inconnu", 0, reason, reason);
        }

        return local with
        {
            AvailabilityState = result.AvailabilityState,
            AvailableCount = result.AvailableCount,
            Status = result.Status,
            UnavailableReason = result.UnavailableReason,
            Notes = result.Notes is null ? local.Notes : local.Notes.Concat(result.Notes).ToArray(),
            Errors = result.AvailabilityState == "inconnu" && !string.IsNullOrWhiteSpace(result.UnavailableReason)
                ? local.Errors.Append(result.UnavailableReason).ToArray() : local.Errors
        };
    }
}
