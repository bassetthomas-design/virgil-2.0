using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Virgil.UiTests;

internal sealed class SessionFiles
{
    private readonly string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Virgil");
    private readonly Dictionary<string, long> offsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> sessionLogs = new(StringComparer.OrdinalIgnoreCase);
    private string[] quarantineBefore = [];

    public static string RepositoryRoot { get; } = FindRepositoryRoot();
    public static string ArtifactDirectory => Path.Combine(RepositoryRoot, "artifacts", "ui-smoke");
    public bool Started { get; private set; }
    public IReadOnlyList<string> ErrorLines => sessionLogs.Values.SelectMany(text => text.Split('\n'))
        .Where(line => line.Contains("[ERR]", StringComparison.Ordinal) || line.Contains("[FTL]", StringComparison.Ordinal)).ToArray();

    public void Begin()
    {
        quarantineBefore = SnapshotQuarantine();
        foreach (var path in LogPaths()) offsets[path] = new FileInfo(path).Length;
        Started = true;
    }

    public void CollectLogs()
    {
        if (!Started) return;
        Directory.CreateDirectory(Path.Combine(ArtifactDirectory, "logs"));
        foreach (var path in LogPaths())
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var offset = offsets.GetValueOrDefault(path);
            input.Position = input.Length < offset ? 0 : offset;
            using var reader = new StreamReader(input, Encoding.UTF8);
            var appended = reader.ReadToEnd();
            offsets[path] = input.Position;
            if (appended.Length == 0) continue;
            File.AppendAllText(Path.Combine(ArtifactDirectory, "logs", Path.GetFileName(path)), appended);
            // Keep partial writes too: an [ERR] token can straddle two reads.
            sessionLogs[path] = sessionLogs.GetValueOrDefault(path, "") + appended;
        }
    }

    public static string[] SnapshotReports()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virgil", "reports");
        return !Directory.Exists(root) ? [] : Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
            .Order(StringComparer.Ordinal).ToArray();
    }

    public void AssertQuarantineUnchanged() => Assert.Equal(quarantineBefore, SnapshotQuarantine());

    private string[] SnapshotQuarantine()
    {
        var root = Path.Combine(data, "Quarantine");
        if (!Directory.Exists(root)) return ["<absente>"];
        return new[] { "<présente>" }
            .Concat(Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .Select(path => $"D:{Path.GetRelativePath(root, path)}"))
            .Concat(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(path =>
            {
                using var stream = File.OpenRead(path);
                return $"F:{Path.GetRelativePath(root, path)}:{stream.Length}:{Convert.ToHexString(SHA256.HashData(stream))}";
            }))
            .Order(StringComparer.Ordinal).ToArray();
    }

    private IEnumerable<string> LogPaths()
    {
        var directory = Path.Combine(data, "logs");
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "virgil-*.log") : [];
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Virgil.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Virgil.sln introuvable depuis le dossier des tests.");
    }
}
