using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class LotBureauWindowsTests
{
    [Fact]
    [Trait("Category", "Windows")]
    public async Task B3_Real_thumbnail_scan_and_cleanup_preserve_desktops_pins_taskband_and_icons()
    {
        var artifacts = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-bureau");
        Directory.CreateDirectory(artifacts);
        var zone = CleanupZoneCatalog.CreateDefault().Single(z => z.Id == CleanupZoneId.WindowsThumbnailCache);
        Directory.CreateDirectory(zone.RootPath);
        var suffix = Guid.NewGuid().ToString("N");
        var thumbnail = Path.Combine(zone.RootPath, $"thumbcache_lotbureau_{suffix}.db");
        var icon = Path.Combine(zone.RootPath, $"iconcache_lotbureau_{suffix}.db");
        var quarantineRoot = Path.Combine(Path.GetTempPath(), "virgil-lot-bureau-quarantine", suffix);
        foreach (var path in new[] { thumbnail, icon })
        {
            File.WriteAllText(path, "Fixture de preuve Windows du lot Bureau.");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-30));
        }
        try
        {
            var before = Snapshot(zone.RootPath);
            File.WriteAllLines(Path.Combine(artifacts, "avant.txt"), before);
            var preview = Assert.Single(await new CleanupPreviewService([zone]).PreviewAsync(null, CancellationToken.None));
            File.WriteAllText(Path.Combine(artifacts, "apercu.json"), JsonSerializer.Serialize(preview));
            var result = await new CleanupExecutionService(new CleanupDisk(), TestPlans.Authority,
                    quarantine: new QuarantineStore(quarantineRoot))
                .ExecuteZoneAsync(preview, TestPlans.Cleanup(preview), null, CancellationToken.None);
            var after = Snapshot(zone.RootPath);
            File.WriteAllLines(Path.Combine(artifacts, "apres.txt"), after);
            File.WriteAllText(Path.Combine(artifacts, "execution.json"), JsonSerializer.Serialize(result));
            File.WriteAllLines(Path.Combine(artifacts, "comparaison.txt"),
                before.Except(after).Select(line => "AVANT SEULEMENT: " + line)
                    .Concat(after.Except(before).Select(line => "APRÈS SEULEMENT: " + line)));
            Assert.Equal(before, after);
            Assert.Equal(CleanupStepStatus.Completed, result.Status);
            Assert.Equal(0, result.ErrorFiles);
            Assert.True(result.QuarantinedFiles >= 1);
            Assert.False(File.Exists(thumbnail));
            Assert.True(File.Exists(icon));
            var excluded = Assert.Single(preview.Candidates, c => c.FullPath == icon);
            Assert.False(excluded.IsEligible);
            Assert.Equal("Cache d'icônes Windows protégé : le supprimer modifie l'affichage du bureau et de la barre des tâches.", excluded.ExclusionReason);
        }
        finally
        {
            // Retirer uniquement les deux fichiers de fixture ; conserver la quarantaine réelle jusqu'à la fin du runner.
            File.Delete(thumbnail);
            File.Delete(icon);
        }
    }

    private static string[] Snapshot(string explorerRoot)
    {
        var lines = new List<string>();
        AddDirectory("Bureau", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddDirectory("Bureau public", Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        AddDirectory("Épinglages", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar"));
        lines.AddRange(Directory.EnumerateFiles(explorerRoot, "*.db")
            .Where(p => Path.GetFileName(p).StartsWith("iconcache", StringComparison.OrdinalIgnoreCase)).Select(p => FileLine("Icônes", explorerRoot, p)));
        using var taskband = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband", writable: false);
        if (taskband is null) lines.Add("Taskband: <absente>");
        else ReadKey(taskband, "Taskband");
        return lines.Order(StringComparer.Ordinal).ToArray();

        void AddDirectory(string name, string root)
        {
            lines.Add($"{name}: {root}: {(Directory.Exists(root) ? "présent" : "absent")}");
            if (!Directory.Exists(root)) return;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", options))
            {
                if (Directory.Exists(path)) lines.Add($"{name}: D: {Path.GetRelativePath(root, path)}");
                else lines.Add(FileLine(name, root, path));
            }
        }

        void ReadKey(RegistryKey key, string path)
        {
            lines.Add(path + ": <présente>");
            foreach (var name in key.GetValueNames())
            {
                var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                var encoded = value is byte[] bytes ? Convert.ToHexString(bytes) : JsonSerializer.Serialize(value);
                lines.Add($"{path}\\{name}: {key.GetValueKind(name)}: {encoded}");
            }
            foreach (var child in key.GetSubKeyNames())
            {
                using var subkey = key.OpenSubKey(child, writable: false);
                Assert.NotNull(subkey);
                ReadKey(subkey, path + "\\" + child);
            }
        }
    }

    private static string FileLine(string name, string root, string path)
    {
        var info = new FileInfo(path);
        using var input = File.OpenRead(path);
        return $"{name}: F: {Path.GetRelativePath(root, path)}: {info.Length}: {info.LastWriteTimeUtc:O}: {Convert.ToHexString(SHA256.HashData(input))}";
    }
}
