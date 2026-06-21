using Virgil.Domain;

namespace Virgil.Core.Cleanup;

public sealed class CleanupStorageAnalyzer
{
    private static readonly string[] CloudFragments = { "onedrive", "icloud", "dropbox", "google drive" };
    private readonly IReadOnlyList<string> _roots;
    private readonly CleanupSafetyClassifier _classifier;
    private readonly long _largeFileThreshold;
    private readonly int _maximumItems;
    private readonly Func<DateTimeOffset> _now;

    public CleanupStorageAnalyzer(
        IReadOnlyList<string>? roots = null,
        CleanupSafetyClassifier? classifier = null,
        long largeFileThreshold = 500L * 1024 * 1024,
        int maximumItems = 100,
        Func<DateTimeOffset>? now = null)
    {
        _roots = roots ?? DefaultRoots();
        _classifier = classifier ?? new CleanupSafetyClassifier();
        _largeFileThreshold = Math.Max(1, largeFileThreshold);
        _maximumItems = Math.Clamp(maximumItems, 1, 500);
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public Task<CleanupStorageAnalysis> AnalyzeAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => Analyze(cancellationToken), cancellationToken);
    }

    private CleanupStorageAnalysis Analyze(CancellationToken cancellationToken)
    {
        var items = new List<CleanupStorageReviewItem>();
        var skipped = new List<string>();
        var errors = new List<string>();

        foreach (var root in _roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanScanRoot(root, out var reason))
            {
                skipped.Add($"{root} : {reason}");
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddFile(file, items);
                    if (items.Count >= _maximumItems) break;
                }

                if (items.Count >= _maximumItems) break;

                foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddDirectory(directory, items);
                    if (items.Count >= _maximumItems) break;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                errors.Add($"Lecture partielle de {root}.");
            }

            if (items.Count >= _maximumItems) break;
        }

        return new CleanupStorageAnalysis(
            _now(),
            items.OrderByDescending(item => item.SizeBytes).ToList(),
            skipped,
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private void AddFile(string path, ICollection<CleanupStorageReviewItem> items)
    {
        try
        {
            var file = new FileInfo(path);
            var classification = _classifier.ClassifyPath(path);
            if (file.Length < _largeFileThreshold && !CleanupSafetyClassifier.IsPersonalExtension(path)) return;

            items.Add(new CleanupStorageReviewItem(
                file.FullName,
                file.Name,
                file.Extension.TrimStart('.').ToUpperInvariant() is { Length: > 0 } type ? type : "Fichier",
                Math.Max(0, file.Length),
                new DateTimeOffset(DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc)),
                classification,
                classification == CleanupClassification.Protected
                    ? "Donnee personnelle protegee. Aucune suppression proposee."
                    : "Element volumineux ou ambigu a revoir manuellement."));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
    }

    private void AddDirectory(string path, ICollection<CleanupStorageReviewItem> items)
    {
        if (CleanupPathGuard.HasReparsePointAtPath(path)) return;

        var classification = _classifier.ClassifyPath(path, isDirectory: true);
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).Take(500))
            {
                try { size += Math.Max(0, new FileInfo(file).Length); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
            }

            if (size < _largeFileThreshold && classification != CleanupClassification.Protected) return;
            var info = new DirectoryInfo(path);
            items.Add(new CleanupStorageReviewItem(
                info.FullName,
                info.Name,
                "Dossier",
                size,
                new DateTimeOffset(DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc)),
                classification,
                classification == CleanupClassification.Protected
                    ? "Dossier personnel, projet, sauvegarde ou jeu protege."
                    : "Taille partielle du dossier; analyse en lecture seule."));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
    }

    private static bool CanScanRoot(string root, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            reason = "indisponible";
            return false;
        }

        if (!Path.IsPathFullyQualified(root) || CleanupPathGuard.HasReparsePointAtPath(root))
        {
            reason = "chemin non local ou point de reanalyse";
            return false;
        }

        var normalized = root.Replace('/', '\\');
        if (CloudFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            reason = "cloud non analyse par defaut";
            return false;
        }

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(root)!);
            if (drive.DriveType is DriveType.Network or DriveType.Removable)
            {
                reason = "lecteur reseau ou externe non analyse par defaut";
                return false;
            }
        }
        catch
        {
            reason = "lecteur non verifiable";
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> DefaultRoots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[] { Path.Combine(profile, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
    }
}
