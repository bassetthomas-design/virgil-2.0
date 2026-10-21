using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Virgil.Contracts;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Diagnostic;
using Virgil.Core.Resources;

namespace Virgil.Infrastructure.Windows.Diagnostic;

/// <summary>Lectures ponctuelles, sans commande externe, écriture ou message brut du journal.</summary>
public sealed class WindowsDiagnosticSignals(ProcessProtectionPolicy policy) : IDiagnosticSignalSource
{
    public Task<DiagnosticSignals> ReadAsync(CancellationToken cancellationToken) => Task.Run(() => new DiagnosticSignals(
        ReadErrors(cancellationToken), ReadDevices(cancellationToken), ReadStartup(cancellationToken), ReadProcesses(cancellationToken)), cancellationToken);
    private static DiagnosticValue<T> Missing<T>(string reason) => DiagnosticValue<T>.Unavailable("Indisponible : " + reason);
    public static DiagnosticValue<WindowsErrorSummary> ReadErrors(CancellationToken token)
    {
        try
        {
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName,
                "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= 86400000]]]"));
            var count = 0;
            var sources = new Dictionary<string, int>(StringComparer.Ordinal);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                using var entry = reader.ReadEvent();
                if (entry is null) break;
                count++;
                var source = entry.ProviderName ?? "Source inconnue";
                sources[source] = sources.GetValueOrDefault(source) + 1;
            }
            return DiagnosticValue<WindowsErrorSummary>.Available(new(count, sources.OrderByDescending(s => s.Value)
                .ThenBy(s => s.Key, StringComparer.Ordinal).Take(3).Select(s => s.Key + " (" + s.Value + ")").ToArray()));
        }
        catch (OperationCanceledException) { throw; }
        catch { return Missing<WindowsErrorSummary>("journal System inaccessible. Aucun message brut collecté."); }
    }
    public static DiagnosticValue<IReadOnlyList<DeviceProblem>> ReadDevices(CancellationToken token)
    {
        var set = IntPtr.Zero;
        try
        {
            set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 2 | 4); // PRESENT | ALLCLASSES
            if (set == new IntPtr(-1)) return Missing<IReadOnlyList<DeviceProblem>>("inventaire des périphériques inaccessible.");
            var result = new List<DeviceProblem>();
            for (uint i = 0; ; i++)
            {
                token.ThrowIfCancellationRequested();
                var data = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref data))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    return Missing<IReadOnlyList<DeviceProblem>>("inventaire des périphériques incomplet.");
                }
                if (CM_Get_DevNode_Status(out var status, out var problem, data.DevInst, 0) != 0)
                    return Missing<IReadOnlyList<DeviceProblem>>("statut d'un périphérique illisible.");
                if (problem == 0 && (status & 0x400) == 0) continue; // DN_HAS_PROBLEM
                var buffer = new byte[2048];
                var name = SetupDiGetDeviceRegistryProperty(set, ref data, 12, out _, buffer, (uint)buffer.Length, out _)
                    || SetupDiGetDeviceRegistryProperty(set, ref data, 0, out _, buffer, (uint)buffer.Length, out _)
                    ? Encoding.Unicode.GetString(buffer).TrimEnd('\0') : "Périphérique sans nom disponible";
                result.Add(new(name, status, problem));
            }
            return DiagnosticValue<IReadOnlyList<DeviceProblem>>.Available(result.AsReadOnly());
        }
        catch (OperationCanceledException) { throw; }
        catch { return Missing<IReadOnlyList<DeviceProblem>>("lecture SetupAPI/CfgMgr32 impossible."); }
        finally { if (set != IntPtr.Zero && set != new IntPtr(-1)) SetupDiDestroyDeviceInfoList(set); }
    }
    public static DiagnosticValue<IReadOnlyList<StartupEntry>> ReadStartup(CancellationToken token) =>
        ReadStartup(token, ReadRunNames, ReadStartupFolderNames);

    // Lecteurs injectables pour vérifier les vues et les doublons sans modifier le registre.
    public static DiagnosticValue<IReadOnlyList<StartupEntry>> ReadStartup(CancellationToken token,
        Func<RegistryHive, RegistryView, CancellationToken, IReadOnlyList<string>> readRunNames,
        Func<Environment.SpecialFolder, CancellationToken, IReadOnlyList<string>> readFolderNames)
    {
        try
        {
            var entries = new List<StartupEntry>();
            var registryEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var impact = Missing<string>("impact au démarrage non mesuré");
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                // HKCU\Software est partagé entre les vues ; HKLM conserve ses deux vues.
                var views = hive == RegistryHive.CurrentUser
                    ? new[] { RegistryView.Default }
                    : new[] { RegistryView.Registry64, RegistryView.Registry32 };
                foreach (var view in views)
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var name in readRunNames(hive, view, token))
                    {
                        token.ThrowIfCancellationRequested();
                        if (registryEntries.Add(hive + "/" + name))
                            entries.Add(new(name, hive + "/" + view + "/Run", impact));
                    }
                }
            }
            foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
            {
                token.ThrowIfCancellationRequested();
                foreach (var name in readFolderNames(folder, token))
                    entries.Add(new(name, folder.ToString(), impact));
            }
            return DiagnosticValue<IReadOnlyList<StartupEntry>>.Available(entries.AsReadOnly());
        }
        catch (OperationCanceledException) { throw; }
        catch { return Missing<IReadOnlyList<StartupEntry>>("clés Run ou dossiers Démarrage inaccessibles."); }
    }
    private static IReadOnlyList<string> ReadRunNames(RegistryHive hive, RegistryView view, CancellationToken token)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var run = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
        if (run is null) return Array.Empty<string>();
        var names = run.GetValueNames();
        foreach (var name in names)
        {
            token.ThrowIfCancellationRequested();
            // Vérifier la lisibilité sans conserver la commande ou ses arguments personnels.
            if (run.GetValue(name) is null) throw new InvalidOperationException("Entrée Run illisible.");
        }
        return names;
    }
    private static IReadOnlyList<string> ReadStartupFolderNames(Environment.SpecialFolder folder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Dossier Démarrage non résolu.");
        if (!Directory.Exists(path)) return Array.Empty<string>();
        return Directory.EnumerateFiles(path).Where(f => !string.Equals(Path.GetFileName(f), "desktop.ini", StringComparison.OrdinalIgnoreCase))
            .Select(file => { token.ThrowIfCancellationRequested(); return Path.GetFileName(file); }).ToArray();
    }
    private DiagnosticValue<IReadOnlyList<ProcessResourceInfo>> ReadProcesses(CancellationToken token)
    {
        try
        {
            var result = new List<ProcessResourceInfo>();
            var incomplete = false;
            foreach (var process in Process.GetProcesses())
            using (process)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var name = process.ProcessName;
                    if (!DiagnosticAnalyzer.IsLauncher(name)) continue;
                    var path = process.MainModule?.FileName;
                    var publisher = path is null ? null : FileVersionInfo.GetVersionInfo(path).CompanyName;
                    var observation = new ProcessObservation { ProcessId = process.Id, Name = name, Path = path,
                        Publisher = publisher, StartedAt = process.StartTime.ToUniversalTime(), HasMainWindow = process.MainWindowHandle != IntPtr.Zero,
                        MainWindowTitle = process.MainWindowTitle, WorkingSetBytes = process.WorkingSet64 };
                    var protection = policy.Evaluate(observation, 0);
                    result.Add(new ProcessResourceInfo { ProcessId = process.Id, Name = name, Path = path, Publisher = publisher,
                        StartedAt = observation.StartedAt, MainWindowTitle = observation.MainWindowTitle, WorkingSetBytes = observation.WorkingSetBytes,
                        CanCloseGracefully = protection.CanCloseGracefully, CanForceClose = protection.CanForceClose,
                        IsCriticalSystemProcess = protection.IsCritical, Status = protection.Status });
                }
                catch { incomplete = true; }
            }
            return incomplete ? Missing<IReadOnlyList<ProcessResourceInfo>>("identité d'un processus inaccessible ; aucune fermeture proposée.")
                : DiagnosticValue<IReadOnlyList<ProcessResourceInfo>>.Available(result.AsReadOnly());
        }
        catch (OperationCanceledException) { throw; }
        catch { return Missing<IReadOnlyList<ProcessResourceInfo>>("inventaire des processus inaccessible."); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(IntPtr guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref DeviceInfo data, uint property, out uint type, byte[] buffer, uint size, out uint required);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
}
