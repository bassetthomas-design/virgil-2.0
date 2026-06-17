using Microsoft.Win32;
using Virgil.Domain;

namespace Virgil.Core.Updates;

public sealed class WindowsUpdateStatusService
{
    private const string WindowsUpdateSettingsUri = "ms-settings:windowsupdate";

    public WindowsUpdateInformation ReadStatus()
    {
        var errors = new List<string>();
        var notes = new List<string>
        {
            "Ouverture controlee de la page Windows Update uniquement.",
            "Aucune recherche, installation ou redemarrage n'est declenche par Virgil."
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
            errors.Add("Lecture du redemarrage en attente indisponible.");
        }

        var status = servicePresent
            ? "Windows Update accessible via les Parametres Windows."
            : "Windows Update non confirme localement.";

        if (pendingReboot)
        {
            notes.Add("Un redemarrage en attente semble present.");
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
