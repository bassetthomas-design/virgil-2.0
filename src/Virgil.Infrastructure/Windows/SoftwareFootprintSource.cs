using Microsoft.Win32;
using System.Diagnostics;
using System.Security;
using System.Text.RegularExpressions;
using Virgil.Core.Applications;
using Virgil.Core.Cleanup;
using Virgil.Core.Processes;

namespace Virgil.Infrastructure.Windows;

/// <summary>Inventaire Windows en lecture seule. Une lecture incomplète interdit la sélection de groupe.</summary>
public sealed class SoftwareFootprintSource(IKnownFoldersProvider folders, IProcessRunner runner) : ISoftwareFootprintSource
{
    public async Task<SoftwareFootprintSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var installed = new List<InstalledSoftwareFootprint>();
        var active = new List<string>();
        var candidates = new List<SoftwareFootprintCandidate>();
        var complete = true;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false);
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var key = uninstall.OpenSubKey(name, false);
                    if (key?.GetValue("DisplayName") is not string display) continue;
                    var references = new List<string>();
                    foreach (var value in new[] { "InstallLocation", "DisplayIcon", "UninstallString", "QuietUninstallString" })
                        if (key.GetValue(value) is string text) references.AddRange(ExtractPaths(text));
                    installed.Add(new(display, references));
                    if (key.GetValue("Publisher") is string publisher && publisher.Length > 0)
                        installed.Add(new(publisher, references));
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
        }
        catch (Exception ex) when (IsReadError(ex)) { complete = false; }
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { if (process.MainModule?.FileName is { } path) active.Add(path); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { complete = false; }
            }
        }
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", false);
            if (services is null) complete = false;
            else foreach (var name in services.GetSubKeyNames())
            {
                using var service = services.OpenSubKey(name, false);
                if (service?.GetValue("ImagePath") is string path) active.AddRange(ExtractPaths(path));
                using var parameters = service?.OpenSubKey("Parameters", false);
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
                using var root = RegistryKey.OpenBaseKey(hive, view);
                var softwarePath = hive == RegistryHive.CurrentUser && view == RegistryView.Registry32
                    ? @"SOFTWARE\WOW6432Node" : "SOFTWARE";
                using var software = root.OpenSubKey(softwarePath, false);
                if (software is null) continue;
                foreach (var publisher in software.GetSubKeyNames().Where(n => !LeftoverAnalyzer.IsProtected(n) && n != "WOW6432Node"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var prefix = (hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM") + @"\SOFTWARE\" +
                        (view == RegistryView.Registry32 ? @"WOW6432Node\" : "") + publisher;
                    registry.Add((publisher, prefix));
                    using var key = software.OpenSubKey(publisher, false);
                    if (key is not null) foreach (var product in key.GetSubKeyNames().Where(n => !LeftoverAnalyzer.IsProtected(n)))
                        registry.Add((product, prefix + "\\" + product));
                }
            }
            catch (Exception ex) when (IsReadError(ex)) { complete = false; }
        }
        var roots = new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath).Concat(folders.GetFolders().Where(f => f.Kind == KnownFolder.Documents).Select(f => f.Path))
            .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var directory in Directory.EnumerateDirectories(root).Where(p => !LeftoverAnalyzer.IsProtected(Path.GetFileName(p))))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (HasReparseAncestor(directory) || !CleanupPathGuard.IsStrictlyUnderRoot(directory, root)) continue;
                    var name = Path.GetFileName(directory);
                    // Exclure avant de parcourir les fichiers d'un logiciel encore référencé.
                    if (installed.Any(i => LeftoverAnalyzer.MatchesInstalledName(name, i.Name)) ||
                        installed.SelectMany(i => i.ReferencedPaths).Concat(active).Any(p => Overlaps(directory, p))) continue;
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
        foreach (var key in registry.Where(k => !candidates.Any(c => c.RegistryKeys.Contains(k.Key))))
            candidates.Add(new(key.Name, "", "", [], [key.Key], null, true));
        return new(installed, active, candidates, complete);
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
        if (Directory.Exists(text)) { yield return Path.GetFullPath(text); yield break; }
        foreach (Match match in Regex.Matches(text, "[A-Za-z]:\\\\[^\\\"\\r\\n]*?\\.(?:exe|dll|sys|msi)", RegexOptions.IgnoreCase))
            yield return match.Value;
        if (Path.IsPathFullyQualified(text) && !text.Contains('"') && !text.Contains(" /", StringComparison.Ordinal))
            yield return text;
    }
    private static bool IsReadError(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException or System.ComponentModel.Win32Exception;
}
