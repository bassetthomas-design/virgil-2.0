using Microsoft.Win32;
using Virgil.Contracts;

namespace Virgil.Core.Updates;

public sealed class WindowsUpdateStatusService
{
    public WindowsUpdateStatusService() { }
    public WindowsUpdateStatusService(IPendingRebootProbe probe) { }

    private const string WindowsUpdateSettingsUri = "ms-settings:windowsupdate";

    public WindowsUpdateInformation ReadStatus()
    {
        var errors = new List<string>();
        var notes = new List<string>
        {
            "Ouverture contrôlée de la page Windows Update uniquement.",
            "Aucune recherche, installation ou redémarrage n'est déclenché par Virgil."
        };

        bool servicePresent;
        bool pendingReboot;

        try
        {
            servicePresent = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv") is not null;
        }
        catch
        {
            servicePresent = false;
            errors.Add("Statut du service Windows Update indisponible.");
        }

        try
        {
            pendingReboot = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") is not null ||
                Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") is not null;
        }
        catch
        {
            pendingReboot = false;
            errors.Add("Lecture du redémarrage en attente indisponible.");
        }

        var status = servicePresent
            ? "Windows Update accessible via les Paramètres Windows."
            : "Windows Update non confirmé localement.";

        if (pendingReboot)
        {
            notes.Add("Un redémarrage en attente semble présent.");
        }

        return new WindowsUpdateInformation
        {
            Status = status,
            SettingsUri = WindowsUpdateSettingsUri,
            ServiceRegistryKeyPresent = servicePresent,
            PendingRebootDetected = pendingReboot,
            Notes = notes,
            Errors = errors
        };
    }
}
