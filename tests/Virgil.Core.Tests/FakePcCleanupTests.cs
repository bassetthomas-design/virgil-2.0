using Microsoft.Win32;
using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Virgil.Core.Processes;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class FakePcCleanupTests
{
    [Fact]
    public async Task Installed_games_and_launchers_are_never_proposed()
    {
        using var pc = new FakePc();
        var previews = await pc.Scan();
        Assert.NotEmpty(previews);
        foreach (var name in new[] { "Steam", "SteamService", "WeGame", "The Division 2", "Ubisoft Game Launcher" })
            Assert.DoesNotContain(previews, p => p.Definition.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Genuine_leftover_of_uninstalled_app_is_proposed()
    {
        using var pc = new FakePc();
        var preview = Assert.Single(await pc.Scan(), p => p.Definition.SoftwareName == "OldTool");
        Assert.Equal(CleanupZoneId.SoftwareLeftovers, preview.Definition.Id);
        Assert.False(preview.Definition.NeedsReview);
        Assert.True(preview.Definition.IsExecutable);
        Assert.Equal("Mise en quarantaine des fichiers uniquement.", preview.Definition.Effect);
        Assert.Contains(@"HKCU\SOFTWARE\OldTool", preview.Definition.RegistryKeys);
        Assert.All(preview.Candidates, c => Assert.True(c.IsEligible));
    }

    [Fact]
    public async Task Fake_pc_scan_writes_nothing()
    {
        using var pc = new FakePc();
        var before = pc.Fingerprint();
        var registryBefore = pc.Registry.Fingerprint();
        await pc.Scan();
        Assert.Equal(before, pc.Fingerprint());
        Assert.Equal(registryBefore, pc.Registry.Fingerprint());
        Assert.Equal(0, pc.Registry.WriteCalls);
    }

    [Fact]
    public void Footprint_registry_port_exposes_no_write_api()
    {
        foreach (var type in new[] { typeof(IFootprintRegistry), typeof(IFootprintRegistryKey) })
        {
            Assert.DoesNotContain(type.GetMethods(), m => new[] { "Set", "Delete", "Create" }.Any(prefix => m.Name.StartsWith(prefix, StringComparison.Ordinal)));
            Assert.DoesNotContain(type.GetMethods(), m => m.ReturnType == typeof(RegistryKey));
        }
    }

    [Theory]
    [InlineData("npm-cache")]
    [InlineData("NuGet")]
    [InlineData("pnpm-cache")]
    [InlineData("pnpm")]
    public async Task Dev_caches_are_not_uninstalled_leftovers(string name)
    {
        using var pc = new FakePc();
        pc.Candidate(name);
        if (name == "NuGet")
        {
            pc.File(Path.Combine(pc.Roots.LocalAppData, name, "v3-cache", "cache.tmp"), "cache");
            pc.File(Path.Combine(pc.Roots.LocalAppData, name, "plugins-cache", "cache.tmp"), "cache");
        }
        var previews = await pc.Scan();
        Assert.Contains(previews, p => p.Definition.SoftwareName == "OldTool");
        Assert.DoesNotContain(previews, p => p.Definition.SoftwareName == name);
    }

    [Fact]
    public async Task Installed_Deluge_with_different_display_name_and_HKCU_uninstall_is_never_proposed()
    {
        using var pc = new FakePc();
        var snapshot = await pc.Source.ReadAsync(CancellationToken.None);
        Assert.Contains(snapshot.Installed, i => i.Name == "Client BitTorrent (par utilisateur)");
        Assert.Contains(snapshot.Evidence, e => e.Name == "Deluge");
        Assert.DoesNotContain(new LeftoverAnalyzer().Analyze(snapshot, DateTimeOffset.UtcNow), p => p.Definition.SoftwareName == "deluge");
    }

    private sealed class FakePc : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("VirgilFakePc-").FullName;
        public MemoryRegistry Registry { get; } = new();
        public FootprintFileRoots Roots { get; }
        public SoftwareFootprintSource Source { get; }

        public FakePc()
        {
            Roots = new(Path.Combine(root, "LocalAppData"), Path.Combine(root, "AppData"), Path.Combine(root, "ProgramData"),
                Path.Combine(root, "ProgramFiles"), Path.Combine(root, "ProgramFilesX86"));
            foreach (var path in Roots.All) Directory.CreateDirectory(path);
            Registry.Seed(RegistryHive.LocalMachine, RegistryView.Default, @"SYSTEM\CurrentControlSet\Services\SteamService",
                "DisplayName", "Steam Client Service");
            var steam = Path.Combine(root, "Steam");
            var library = Path.Combine(root, "SteamLibrary");
            File(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
                "\"libraryfolders\" { \"0\" { \"path\" \"" + library.Replace("\\", "\\\\", StringComparison.Ordinal) + "\" } }");
            var fixture = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "tests", "Virgil.Core.Tests", "Fixtures", "Steam", "appmanifest_2221490.acf");
            var manifest = System.IO.File.ReadAllText(fixture);
            File(Path.Combine(library, "steamapps", "appmanifest_2221490.acf"), manifest);
            Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", LauncherManifestParser.SteamApplication(manifest).InstallDirectory));
            Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", "The Division 2"));
            Registry.Seed(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath", steam);
            Registry.Seed(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\SteamService", "Version", "1");
            var weGame = Directory.CreateDirectory(Path.Combine(root, "WeGame")).FullName;
            Registry.Seed(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Tencent\WeGame", "InstallPath", weGame);
            Registry.Seed(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Ubisoft\The Division 2 - Steam", "Version", "1");
            var ubisoft = Directory.CreateDirectory(Path.Combine(root, "UbisoftLauncher")).FullName;
            Registry.Seed(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Ubisoft\Launcher", "InstallPath", ubisoft);
            foreach (var name in new[] { "Steam", "SteamService", "WeGame", "The Division 2", "Ubisoft Game Launcher", "OldTool" }) Candidate(name);
            File(Path.Combine(Roots.AppData, "deluge", "profile.tmp"), "profil");
            Registry.Seed(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\deluge", "Version", "2");
            Registry.Seed(RegistryHive.CurrentUser, RegistryView.Registry64,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Deluge", "DisplayName", "Client BitTorrent (par utilisateur)");
            Registry.SealForScan();
            Source = new(new EmptyFolders(), new EmptyStore(), Registry, Roots, new EmptyProcesses());
        }

        public void Candidate(string name)
        {
            File(Path.Combine(Roots.LocalAppData, name, "cache.tmp"), "cache non vide");
            Registry.Seed(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\" + name, "Version", "1");
        }
        public void File(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content);
        }
        public async Task<IReadOnlyList<CleanupZonePreview>> Scan()
        {
            Registry.SealForScan();
            var snapshot = await Source.ReadAsync(CancellationToken.None);
            Assert.True(snapshot.IsComplete);
            return new LeftoverAnalyzer().Analyze(snapshot, DateTimeOffset.UtcNow);
        }
        public string[] Fingerprint() => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(p => new FileInfo(p)).Select(f =>
                $"{f.FullName}|{(Directory.Exists(f.FullName) ? 0 : f.Length)}|{f.CreationTimeUtc.Ticks}|{f.LastWriteTimeUtc.Ticks}").ToArray();
        public void Dispose() => Directory.Delete(root, recursive: true);
    }

    // Vue 32 bits : SOFTWARE\Valve représente physiquement SOFTWARE\WOW6432Node\Valve.
    private sealed class MemoryRegistry : IFootprintRegistry
    {
        private readonly Dictionary<(RegistryHive, RegistryView), Node> roots = new();
        public int WriteCalls { get; private set; }
        public MemoryRegistry()
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Default, RegistryView.Registry64, RegistryView.Registry32 })
                roots[(hive, view)] = new();
        }
        public void SealForScan() => WriteCalls = 0;
        public string[] Fingerprint() => roots.OrderBy(r => r.Key.Item1).ThenBy(r => r.Key.Item2)
            .SelectMany(r => r.Value.Fingerprint($"{r.Key.Item1}/{r.Key.Item2}")).ToArray();
        public IFootprintRegistryKey OpenBaseKey(RegistryHive hive, RegistryView view)
        {
            return roots[(hive, view)];
        }
        public void Seed(RegistryHive hive, RegistryView view, string path, string name, object value)
        {
            WriteCalls++;
            // Les ajouts explicites de données de fixture sont autorisés entre les scans.
            // Le compteur est remis à zéro avant chaque scan, jamais par le port de lecture.
            var node = (Node)OpenBaseKey(hive, view);
            foreach (var part in path.Split('\\'))
            {
                if (!node.Children.TryGetValue(part, out var child)) node.Children[part] = child = new();
                node = child;
            }
            node.Values[name] = value;
        }
        private sealed class Node : IFootprintRegistryKey
        {
            public Dictionary<string, Node> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
            public IFootprintRegistryKey? OpenSubKey(string name)
            {
                Node? node = this;
                foreach (var part in name.Split('\\'))
                {
                    if (node is null || !node.Children.TryGetValue(part, out var child)) return null;
                    node = child;
                }
                return node;
            }
            public IEnumerable<string> Fingerprint(string prefix)
            {
                yield return prefix;
                foreach (var value in Values.OrderBy(v => v.Key, StringComparer.Ordinal)) yield return $"{prefix}/{value.Key}={value.Value}";
                foreach (var child in Children.OrderBy(c => c.Key, StringComparer.Ordinal))
                    foreach (var entry in child.Value.Fingerprint(prefix + "/" + child.Key)) yield return entry;
            }
            public string[] GetSubKeyNames() => Children.Keys.ToArray();
            public string[] GetValueNames() => Values.Keys.ToArray();
            public object? GetValue(string name) => Values.GetValueOrDefault(name);
            public void Dispose() { }
        }
    }
    private sealed class EmptyFolders : IKnownFoldersProvider
    { public IReadOnlyList<KnownFolderPath> GetFolders() => []; }
    private sealed class EmptyProcesses : IFootprintProcessPaths
    { public FootprintProcessPaths Read(CancellationToken cancellationToken) => new([], true); }
    private sealed class EmptyStore : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken) => Task.FromResult(new ProcessRunResult(0, "[]", ""));
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("Aucun lancement autorisé.");
    }
}
