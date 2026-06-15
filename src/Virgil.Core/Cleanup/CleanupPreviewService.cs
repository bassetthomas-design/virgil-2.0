using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Virgil.Domain;

namespace Virgil.Core.Cleanup;

public sealed class CleanupPreviewService : ICleanupService, ICleanupPreviewService
{
    private readonly IReadOnlyList<CleanupZoneDefinition> _zones;
    private readonly Func<DateTimeOffset> _now;

    public CleanupPreviewService()
        : this(CleanupZoneCatalog.CreateDefault(), () => DateTimeOffset.Now)
    {
    }

    public CleanupPreviewService(
        IReadOnlyList<CleanupZoneDefinition> zones,
        Func<DateTimeOffset>? now = null)
    {
        _zones = zones.OrderBy(zone => zone.DisplayOrder).ToList();
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public IReadOnlyList<CleanupZoneDefinition> GetZones()
    {
        return _zones;
    }

    public CleanupPreview PreviewTemporaryFiles()
    {
        var previews = PreviewAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        var targets = previews
            .Where(preview => preview.EligibleFileCount > 0 || preview.Errors.Count == 0)
            .Select(ToLegacyTarget)
            .ToList();

        return new CleanupPreview(DateTimeOffset.Now, targets);
    }

    public Task<IReadOnlyList<CleanupZonePreview>> PreviewAsync(
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => PreviewZones(progress, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<CleanupZonePreview> PreviewZones(
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var previews = new List<CleanupZonePreview>();
        var count = _zones.Count;

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var zone = _zones[index];
            Report(progress, zone.Id, "Analyse", index, count, $"Analyse de {zone.DisplayName}.");
            previews.Add(PreviewZone(zone, cancellationToken));
        }

        Report(progress, null, "Termine", count, count, "Previsualisation terminee.");
        return previews;
    }

    private CleanupZonePreview PreviewZone(
        CleanupZoneDefinition zone,
        CancellationToken cancellationToken)
    {
        var generatedAt = _now();
        var errors = new List<string>();
        var candidates = new List<CleanupCandidate>();

        if (string.IsNullOrWhiteSpace(zone.RootPath))
        {
            errors.Add("Zone indisponible.");
            return CreatePreview(zone, generatedAt, candidates, errors);
        }

        if (!DirectoryExists(zone.RootPath, errors))
        {
            return CreatePreview(zone, generatedAt, candidates, errors);
        }

        if (CleanupPathGuard.HasReparsePointAtPath(zone.RootPath))
        {
            errors.Add("Zone refusee : point de reanalyse detecte.");
            return CreatePreview(zone, generatedAt, candidates, errors);
        }

        foreach (var filePath in SafeEnumerateFiles(zone, errors, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidates.Add(ReadCandidate(zone, filePath, generatedAt));
        }

        return CreatePreview(zone, generatedAt, candidates, errors);
    }

    private CleanupCandidate ReadCandidate(
        CleanupZoneDefinition zone,
        string filePath,
        DateTimeOffset generatedAt)
    {
        if (!CleanupPathGuard.TryValidateContainedFile(filePath, zone.RootPath, out var fullPath, out var reason))
        {
            return CreateExcludedCandidate(zone, filePath, reason);
        }

        try
        {
            var fileInfo = new FileInfo(fullPath);
            var lastWrite = ToUtcOffset(fileInfo.LastWriteTimeUtc);
            var isOldEnough = generatedAt - lastWrite >= zone.MinimumAge;
            var exclusion = isOldEnough ? null : "Fichier recent.";

            return new CleanupCandidate(
                zone.Id,
                fullPath,
                MakeLogicalPath(fullPath, zone.RootPath),
                Math.Max(0, fileInfo.Length),
                lastWrite,
                isOldEnough,
                exclusion);
        }
        catch
        {
            return CreateExcludedCandidate(zone, filePath, "Lecture du fichier impossible.");
        }
    }

    private CleanupCandidate CreateExcludedCandidate(
        CleanupZoneDefinition zone,
        string filePath,
        string reason)
    {
        return new CleanupCandidate(
            zone.Id,
            filePath,
            MakeLogicalPath(filePath, zone.RootPath),
            0,
            DateTimeOffset.MinValue,
            false,
            reason);
    }

    private static CleanupZonePreview CreatePreview(
        CleanupZoneDefinition zone,
        DateTimeOffset generatedAt,
        IReadOnlyList<CleanupCandidate> candidates,
        IReadOnlyList<string> errors)
    {
        var eligible = candidates.Where(candidate => candidate.IsEligible).ToList();

        return new CleanupZonePreview(
            zone,
            generatedAt,
            candidates.Count,
            eligible.Count,
            eligible.Sum(candidate => candidate.SizeBytes),
            candidates.Count - eligible.Count,
            candidates,
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static IEnumerable<string> SafeEnumerateFiles(
        CleanupZoneDefinition zone,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(zone.RootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            foreach (var file in GetFiles(current, zone, errors))
            {
                yield return file;
            }

            foreach (var child in GetDirectories(current, zone, errors))
            {
                if (CleanupPathGuard.HasReparsePointAtPath(child))
                {
                    errors.Add("Sous-dossier refuse : point de reanalyse detecte.");
                    continue;
                }

                pending.Push(child);
            }
        }
    }

    private static IReadOnlyList<string> GetFiles(
        string directoryPath,
        CleanupZoneDefinition zone,
        ICollection<string> errors)
    {
        try
        {
            return Directory.GetFiles(directoryPath);
        }
        catch
        {
            errors.Add($"Lecture partielle de {zone.DisplayName}.");
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> GetDirectories(
        string directoryPath,
        CleanupZoneDefinition zone,
        ICollection<string> errors)
    {
        try
        {
            return Directory.GetDirectories(directoryPath);
        }
        catch
        {
            errors.Add($"Lecture partielle de {zone.DisplayName}.");
            return Array.Empty<string>();
        }
    }

    private static bool DirectoryExists(string directoryPath, ICollection<string> errors)
    {
        try
        {
            return Directory.Exists(directoryPath);
        }
        catch
        {
            errors.Add("Zone inaccessible.");
            return false;
        }
    }

    private static CleanupTarget ToLegacyTarget(CleanupZonePreview preview)
    {
        return new CleanupTarget(
            preview.Definition.DisplayName,
            preview.Definition.RootPath,
            preview.EligibleBytes,
            preview.EligibleFileCount,
            RiskLabel(preview.Definition.RiskLevel));
    }

    private static string MakeLogicalPath(string filePath, string rootPath)
    {
        try
        {
            var relative = Path.GetRelativePath(rootPath, filePath);
            return relative == "." ? "[racine]" : relative;
        }
        catch
        {
            return Path.GetFileName(filePath);
        }
    }

    private static DateTimeOffset ToUtcOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static string RiskLabel(CleanupRiskLevel level)
    {
        return level switch
        {
            CleanupRiskLevel.Low => "Faible",
            CleanupRiskLevel.Medium => "Moyen",
            _ => "Eleve"
        };
    }

    private static void Report(
        IProgress<CleanupProgress>? progress,
        CleanupZoneId? zoneId,
        string step,
        int current,
        int total,
        string message)
    {
        var percent = total == 0 ? 0 : (int)Math.Round(current * 100d / total);
        progress?.Report(new CleanupProgress(zoneId, step, percent, message));
    }
}
