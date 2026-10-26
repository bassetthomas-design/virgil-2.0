using Microsoft.Win32;
using Virgil.Core.Startup;
using System.Xml.Linq;

namespace Virgil.Infrastructure.Windows.Startup;

/// <summary>Inventaire en lecture seule. Les signatures inconnues ne sont jamais présentées comme valides.</summary>
public sealed class WindowsStartupInventorySource : IStartupInventorySource
{
    public Task<StartupInventory> ReadAsync(CancellationToken cancellationToken) => Task.Run(() => Read(cancellationToken), cancellationToken);
    private static StartupInventory Read(CancellationToken token)
    {
        var items = new List<StartupItem>();
        var warnings = new List<string>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var run = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                using var approvalRoot = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var approved = approvalRoot.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + (view == RegistryView.Registry32 ? "Run32" : "Run"), false);
                if (run is null) continue;
                foreach (var name in run.GetValueNames())
                    items.Add(Item(name, $"{(hive == RegistryHive.CurrentUser ? "Compte actuel" : "Tous les comptes")} · Registre Run ({(view == RegistryView.Registry32 ? "32" : "64")} bits)", run.GetValue(name) as string ?? "Chemin indisponible", State(approved?.GetValue(name) as byte[])) with { ApprovalTarget = new(hive == RegistryHive.CurrentUser ? StartupApprovalHive.CurrentUser : StartupApprovalHive.LocalMachine, view == RegistryView.Registry32 ? StartupApprovalKind.Run32 : StartupApprovalKind.Run, name) });
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            { warnings.Add($"Clé Run inaccessible : {(hive == RegistryHive.CurrentUser ? "compte actuel" : "tous les comptes")} ({(view == RegistryView.Registry32 ? "32" : "64")} bits)."); }
        }
        foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var path = Environment.GetFolderPath(folder);
                if (!Directory.Exists(path)) continue;
                var hive = folder == Environment.SpecialFolder.Startup ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var approved = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", false);
                foreach (var file in Directory.EnumerateFiles(path).Where(f => !string.Equals(System.IO.Path.GetFileName(f), "desktop.ini", StringComparison.OrdinalIgnoreCase)))
                    items.Add(Item(System.IO.Path.GetFileName(file), "Dossier Démarrage · " + (folder == Environment.SpecialFolder.Startup ? "Compte actuel" : "Tous les comptes"), file, State(approved?.GetValue(System.IO.Path.GetFileName(file)) as byte[]), directFile: true) with { ApprovalTarget = new(hive == RegistryHive.CurrentUser ? StartupApprovalHive.CurrentUser : StartupApprovalHive.LocalMachine, StartupApprovalKind.StartupFolder, System.IO.Path.GetFileName(file)) });
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            { warnings.Add("Dossier Démarrage inaccessible : " + (folder == Environment.SpecialFolder.Startup ? "compte actuel." : "tous les comptes.")); }
        }
        var tasks = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
        ReadTasks(tasks, items, warnings, token);
        return new(items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), warnings);
    }
    private static StartupItem Item(string name, string source, string path, string state, bool directFile = false)
    {
        var metadata = StartupFileMetadata.Read(path, directFile);
        return new(name, source, metadata.Path, state, metadata.Publisher, metadata.Signature, "Impact au démarrage non mesuré");
    }
    public static string State(byte[]? value) => value is { Length: >= 12 } ? value[0] switch
    { 2 or 6 => "Activé", 3 or 7 => "Désactivé", _ => "État inconnu" } : "État inconnu";
    private static void ReadTasks(string folder, List<StartupItem> items, List<string> warnings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var document = XDocument.Load(path);
                    var ns = document.Root?.Name.Namespace ?? XNamespace.None;
                    if (!document.Descendants(ns + "LogonTrigger").Any()) continue;
                    var command = document.Descendants(ns + "Exec").Elements(ns + "Command").Select(e => e.Value).FirstOrDefault() ?? "Commande indisponible";
                    var disabled = document.Descendants(ns + "Settings").Elements(ns + "Enabled").Any(e => e.Value == "false");
                    items.Add(Item(System.IO.Path.GetFileName(path), "Tâche à l’ouverture de session", command, disabled ? "Désactivé" : "Activé", directFile: true));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
                { warnings.Add("Une tâche planifiée est illisible."); }
            }
            foreach (var child in Directory.EnumerateDirectories(folder)) ReadTasks(child, items, warnings, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { warnings.Add("Inventaire des tâches planifiées incomplet : accès limité."); }
    }
}
