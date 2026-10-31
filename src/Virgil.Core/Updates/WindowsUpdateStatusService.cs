using Virgil.Contracts;

namespace Virgil.Core.Updates;

public sealed class WindowsUpdateStatusService(IPendingRebootProbe probe)
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
}
