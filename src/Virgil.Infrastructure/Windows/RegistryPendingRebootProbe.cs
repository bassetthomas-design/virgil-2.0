using Microsoft.Win32;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

public sealed class RegistryPendingRebootProbe : IPendingRebootProbe
{
    public PendingRebootStatus Read()
    {
        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv");
        using var cbs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
        using var update = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        var sources = new List<string>();
        if (cbs is not null) sources.Add("Maintenance des composants");
        if (update is not null) sources.Add("Windows Update");
        return new(service is not null, sources);
    }
}
