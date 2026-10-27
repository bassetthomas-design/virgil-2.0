using Microsoft.Win32;
using System.Security;
using System.Text.RegularExpressions;
using Virgil.Core.Applications;
using Virgil.Core.Cleanup;
using Virgil.Core.Processes;

namespace Virgil.Infrastructure.Windows;

/// <summary>Inventaire Windows en lecture seule. Une lecture incomplète interdit la sélection de groupe.</summary>
public sealed class SoftwareFootprintSource : ISoftwareFootprintSource
{
    private readonly IKnownFoldersProvider folders;
    private readonly IProcessRunner runner;
    private readonly IFootprintRegistry registryReader;
    private readonly FootprintFileRoots fileRoots;
    private readonly IFootprintProcessPaths processes;

    public SoftwareFootprintSource(IKnownFoldersProvider folders, IProcessRunner runner)
        : this(folders, runner, new WindowsFootprintRegistry(), FootprintFileRoots.Windows(), new WindowsFootprintProcessPaths()) { }

    public SoftwareFootprintSource(IKnownFoldersProvider folders, IProcessRunner runner,
        IFootprintRegistry registry, FootprintFileRoots roots, IFootprintProcessPaths processes)
    {
        this.folders = folders;
        this.runner = runner;
        registryReader = registry;
        fileRoots = roots;
        this.processes = processes;
    }

    public Task<SoftwareFootprintSnapshot> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() => ReadInventoryAsync(cancellationToken), cancellationToken);

    private async Task<SoftwareFootprintSnapshot> ReadInventoryAsync(CancellationToken cancellationToken)
    {
        var installed = new List<InstalledSoftwareFootprint>();
        var active = new List<string>();
        var evidence = new List<InstalledEvidence>();
        var candidates = new List<SoftwareFootprintCandidate>();
        var complete = true;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                if (hive == RegistryHive.CurrentUser && view == RegistryView.Registry32) continue;
                using var root = registryReader.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var key = uninstall.OpenSubKey(name);
                    if (key?.GetValue("DisplayName") is not string display) continue;
                    var references = new List<string>();
                    foreach (var value in new[] { "InstallLocation", "DisplayIcon", "UninstallString", "QuietUninstallString" })
                        if (key.GetValue(value) is string text) references.AddRange(ExtractPaths(text));
                    installed.Add(new(display, references));
                    // Le nom de la sous-clé identifie aussi les profils dont le nom diffère du DisplayName.
                    // Une identité MSI GUID ne fournit aucun nom de dossier exploitable.
                    if (!Guid.TryParse(name, out _))
                        evidence.Add(new(name, references, "Identité de désinstallation Windows"));
                    evidence.Add(new(display, references, "Désinstallation Windows"));
                    if (key.GetValue("Publisher") is string publisher && publisher.Length > 0)
                    {
                        installed.Add(new(publisher, references));
                        evidence.Add(new(publisher, references, "Éditeur Windows"));
                    }
                }
            }
            catch (Exception ex) when (IsReadError(ex)) { complete = false; }
        }
        try
        {
            var store = await new ApplicationStoreReader(runner).ReadAsync(cancellationToken).ConfigureAwait(false);
            if (store.Errors.Count > 0) complete = false;
            installed.AddRange(store.Applications.Select(a => new InstalledSoftwareFootprint(a.DisplayName,
                string.IsNullOrWhiteSpace(a.InstallLocation) ? [] : new[] { a.InstallLocation! })));
            evidence.AddRange(store.Applications.Select(a => new InstalledEvidence(a.DisplayName,
                string.IsNullOrWhiteSpace(a.InstallLocation) ? [] : new[] { a.InstallLocation! }, "Paquet Microsoft Store")));
        }
        catch (Exception ex) when (IsReadError(ex)) { complete = false; }
        var processPaths = processes.Read(cancellationToken);
        active.AddRange(processPaths.Paths);
        if (!processPaths.IsComplete) complete = false;
        try
        {
            using var serviceRoot = registryReader.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var services = serviceRoot.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is null) complete = false;
            else foreach (var name in services.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var service = services.OpenSubKey(name);
                if (name.Equals("Steam Client Service", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("SteamService", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(service?.GetValue("DisplayName") as string, "Steam Client Service", StringComparison.OrdinalIgnoreCase))
                    evidence.Add(new("Steam", [], "Service Windows"));
                if (service?.GetValue("ImagePath") is string path) active.AddRange(ExtractPaths(path));
                using var parameters = service?.OpenSubKey("Parameters");
                if (parameters?.GetValue("ServiceDll") is string dll) active.AddRange(ExtractPaths(dll));
            }
        }
        catch (Exception ex) when (IsReadError(ex)) { complete = false; }

        var registry = new List<(string Name, string Key)>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = registryReader.OpenBaseKey(hive, view);
                if (hive == RegistryHive.CurrentUser && view == RegistryView.Registry32) continue;
                const string softwarePath = "SOFTWARE";
                using var software = root.OpenSubKey(softwarePath);
                if (software is null) continue;
                foreach (var publisher in software.GetSubKeyNames().Where(n => !LeftoverAnalyzer.IsProtected(n) && n != "WOW6432Node"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var prefix = (hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM") + @"\SOFTWARE\" +
                        (hive == RegistryHive.LocalMachine && view == RegistryView.Registry32 ? @"WOW6432Node\" : "") + publisher;
                    registry.Add((publisher, prefix));
                    using var key = software.OpenSubKey(publisher);
                    if (key is not null)
                    {
                        ReadInstallValues(key, publisher, prefix, evidence);
                        foreach (var product in key.GetSubKeyNames().Where(n => !LeftoverAnalyzer.IsProtected(n)))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            registry.Add((product, prefix + "\\" + product));
                            using var productKey = key.OpenSubKey(product);
                            if (productKey is not null) ReadInstallValues(productKey, product, prefix + "\\" + product, evidence);
                        }
                    }
                }
            }
            catch (Exception ex) when (IsReadError(ex)) { complete = false; }
        }
        ReadLaunchers(evidence, cancellationToken, ref complete);
        var knownFolders = folders.GetFolders();
        var roots = fileRoots.All.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var directory in Directory.EnumerateDirectories(root).Where(p => !LeftoverAnalyzer.IsProtected(Path.GetFileName(p))))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (knownFolders.Any(f => f.Kind != KnownFolder.Downloads && Overlaps(directory, f.Path)) || HasReparseAncestor(directory) || !CleanupPathGuard.IsStrictlyUnderRoot(directory, root)) continue;
                    var name = Path.GetFileName(directory);
                    // Exclure avant de parcourir les fichiers d'un logiciel encore référencé.
                    if (installed.Any(i => LeftoverAnalyzer.MatchesInstalledName(name, i.Name)) ||
                        evidence.Any(e => LeftoverAnalyzer.MatchesInstalledName(name, e.Name)) ||
                        installed.SelectMany(i => i.ReferencedPaths).Concat(active).Concat(evidence.SelectMany(e => e.Paths)).Any(p => Overlaps(directory, p))) continue;
                    var keys = registry.Where(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase)).Select(k => k.Key).Distinct().ToArray();
                    IReadOnlyList<FootprintFile> files;
                    try { files = ReadFiles(directory, cancellationToken); }
                    catch (Exception ex) when (IsReadError(ex)) { complete = false; continue; }
                    var last = files.Count > 0 ? files.Max(f => f.LastWriteUtc) : new DateTimeOffset(Directory.GetLastWriteTimeUtc(directory), TimeSpan.Zero);
                    candidates.Add(new(name, directory, root, files, keys, last, IsUncertain: keys.Length == 0));
                }
            }
            catch (Exception ex) when (IsReadError(ex)) { complete = false; }
        }
        // Les clés seules ne sont pas des restes nettoyables dans cette alpha.
        return new(installed, active, candidates, complete) { Evidence = evidence };
    }

    private static readonly string[] InstallValueNames =
        ["InstallPath", "InstallDir", "InstallLocation", "Path", "ExePath", "Install_Dir", "SteamPath"];

    private static void ReadInstallValues(IFootprintRegistryKey key, string name, string keyPath, List<InstalledEvidence> evidence)
    {
        var paths = key.GetValueNames().Where(n => InstallValueNames.Contains(n, StringComparer.OrdinalIgnoreCase))
            .Select(n => key.GetValue(n)).OfType<string>().SelectMany(ExtractPaths)
            .Where(p => Directory.Exists(p) || File.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length > 0) evidence.Add(new(name, paths, "Chemin du registre", [keyPath]));
    }

    private void ReadLaunchers(List<InstalledEvidence> evidence, CancellationToken token, ref bool complete)
    {
        var steamPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            if (hive == RegistryHive.CurrentUser && view == RegistryView.Registry32) continue;
            try
            {
                using var root = registryReader.OpenBaseKey(hive, view);
                foreach (var vendor in new[] { @"Valve\Steam", @"Ubisoft\Launcher", @"Tencent\WeGame", @"Blizzard Entertainment", @"Battle.net", @"Electronic Arts", @"EA Desktop", @"GOG.com\Games" })
                {
                    token.ThrowIfCancellationRequested();
                    using var key = root.OpenSubKey(@"SOFTWARE\" + vendor);
                    if (key is null) continue;
                    var prefix = (hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU") + @"\SOFTWARE\" +
                        (hive == RegistryHive.LocalMachine && view == RegistryView.Registry32 ? @"WOW6432Node\" : "") + vendor;
                    ReadLauncherKey(key, vendor.Split('\\').Last(), prefix, evidence, token, 0);
                    if (vendor == @"Valve\Steam")
                        foreach (var value in new[] { "InstallPath", "SteamPath" })
                            if (key.GetValue(value) is string path && Directory.Exists(path)) steamPaths.Add(path);
                }
            }
            catch (Exception ex) when (IsReadError(ex)) { complete = false; }
        }
        foreach (var steam in steamPaths)
        {
            evidence.Add(new("Steam", [steam], "Steam"));
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steam };
            try
            {
                var file = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(file)) libraries.UnionWith(LauncherManifestParser.SteamLibraries(File.ReadAllText(file)));
                else complete = false;
            }
            catch (Exception ex) when (IsReadError(ex) || ex is FormatException) { complete = false; }
            foreach (var library in libraries)
            {
                token.ThrowIfCancellationRequested();
                var apps = Path.Combine(library, "steamapps");
                // La bibliothèque entière est une preuve : elle peut contenir une installation dont le manifeste est illisible.
                if (Directory.Exists(apps)) evidence.Add(new("Steam", [apps], "Bibliothèque Steam"));
                try
                {
                    if (!Directory.Exists(apps)) { complete = false; continue; }
                    foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var application = LauncherManifestParser.SteamApplication(File.ReadAllText(manifest));
                            var path = Path.GetFullPath(Path.Combine(apps, "common", application.InstallDirectory));
                            if (!CleanupPathGuard.IsStrictlyUnderRoot(path, Path.Combine(apps, "common"))) { complete = false; continue; }
                            if (Directory.Exists(path)) evidence.Add(new(application.Name, [path], "Manifeste Steam"));
                        }
                        catch (Exception ex) when (IsReadError(ex) || ex is FormatException or ArgumentException) { complete = false; }
                    }
                }
                catch (Exception ex) when (IsReadError(ex)) { complete = false; }
            }
        }
        try
        {
            var manifests = Path.Combine(fileRoots.ProgramData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (Directory.Exists(manifests)) foreach (var manifest in Directory.EnumerateFiles(manifests, "*.item"))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var application = LauncherManifestParser.EpicApplication(File.ReadAllText(manifest));
                    if (Directory.Exists(application.InstallDirectory)) evidence.Add(new(application.Name, [application.InstallDirectory], "Manifeste Epic"));
                }
                catch (Exception ex) when (IsReadError(ex) || ex is System.Text.Json.JsonException or FormatException or KeyNotFoundException or InvalidOperationException) { complete = false; }
            }
        }
        catch (Exception ex) when (IsReadError(ex)) { complete = false; }
    }

    private static void ReadLauncherKey(IFootprintRegistryKey key, string name, string prefix, List<InstalledEvidence> evidence, CancellationToken token, int depth)
    {
        token.ThrowIfCancellationRequested();
        ReadInstallValues(key, key.GetValue("DisplayName") as string ?? key.GetValue("GameName") as string ?? name, prefix, evidence);
        if (depth >= 4) return;
        foreach (var child in key.GetSubKeyNames())
        {
            using var subkey = key.OpenSubKey(child);
            if (subkey is not null) ReadLauncherKey(subkey, child, prefix + "\\" + child, evidence, token, depth + 1);
        }
    }

    internal static IReadOnlyList<FootprintFile> ReadFiles(string root, CancellationToken token)
    {
        var files = new List<FootprintFile>();
        if (!Directory.Exists(root) || HasReparseAncestor(root)) return files;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                token.ThrowIfCancellationRequested();
                if (!CleanupPathGuard.TryValidateContainedFile(file, root, out var path, out _)) continue;
                var info = new FileInfo(path);
                files.Add(new(path, info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
            }
            foreach (var child in Directory.EnumerateDirectories(directory))
                if (!CleanupPathGuard.HasReparsePointAtPath(child)) pending.Push(child);
        }
        return files;
    }

    private static bool HasReparseAncestor(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
    private static bool Overlaps(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
        CleanupPathGuard.IsStrictlyUnderRoot(a, b) || CleanupPathGuard.IsStrictlyUnderRoot(b, a);
    private static IEnumerable<string> ExtractPaths(string text)
    {
        text = Environment.ExpandEnvironmentVariables(text.Trim());
        // Les chemins d'installation sont parfois entourés de guillemets même sans arguments.
        var exactPath = text.Trim('"');
        if (Directory.Exists(exactPath) || File.Exists(exactPath))
        { yield return Path.GetFullPath(exactPath); yield break; }
        if (Directory.Exists(text)) { yield return Path.GetFullPath(text); yield break; }
        foreach (Match match in Regex.Matches(text, "[A-Za-z]:\\\\[^\\\"\\r\\n]*?\\.(?:exe|dll|sys|msi)", RegexOptions.IgnoreCase))
            yield return match.Value;
        if (Path.IsPathFullyQualified(text) && !text.Contains('"') && !text.Contains(" /", StringComparison.Ordinal))
            yield return text;
    }
    private static bool IsReadError(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException or System.ComponentModel.Win32Exception;
}
