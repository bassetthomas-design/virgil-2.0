using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Virgil.Domain;

namespace Virgil.Core.Cleanup;

public sealed class CleanupExecutionService : ICleanupExecutionService
{
    public static readonly TimeSpan PreviewValidity = TimeSpan.FromMinutes(10);

    private readonly Func<DateTimeOffset> _now;

    public CleanupExecutionService()
        : this(() => DateTimeOffset.Now)
    {
    }

    public CleanupExecutionService(Func<DateTimeOffset>? now)
    {
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public Task<CleanupStepResult> ExecuteZoneAsync(
        CleanupZonePreview preview,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => ExecuteZone(preview, progress, cancellationToken), CancellationToken.None);
    }

    public CleanupStepResult SkipZone(CleanupZonePreview preview)
    {
        return new CleanupStepResult(
            preview.Definition,
            CleanupStepStatus.Skipped,
            0,
            0,
            preview.EligibleFileCount,
            0,
            TimeSpan.Zero,
            Array.Empty<string>());
    }

    public CleanupStepResult CancelZone(CleanupZonePreview preview)
    {
        return new CleanupStepResult(
            preview.Definition,
            CleanupStepStatus.Cancelled,
            0,
            0,
            preview.EligibleFileCount,
            0,
            TimeSpan.Zero,
            Array.Empty<string>());
    }

    public CleanupSessionReport CreateReport(
        DateTimeOffset startedAt,
        IReadOnlyList<CleanupStepResult> results,
        IReadOnlyList<string> errors)
    {
        var finishedAt = _now();
        return new CleanupSessionReport(
            startedAt,
            finishedAt,
            finishedAt - startedAt,
            results,
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private CleanupStepResult ExecuteZone(
        CleanupZonePreview preview,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var deletedFiles = 0;
        var deletedBytes = 0L;
        var skippedFiles = 0;
        var errorFiles = 0;
        var status = CleanupStepStatus.Completed;

        if (!preview.Definition.IsExecutable ||
            preview.Definition.Classification is not (CleanupClassification.Cleanable or CleanupClassification.AdvancedCleanable))
        {
            stopwatch.Stop();
            return new CleanupStepResult(
                preview.Definition,
                CleanupStepStatus.Skipped,
                0,
                0,
                preview.EligibleFileCount,
                0,
                stopwatch.Elapsed,
                new[] { "Zone en information seulement : aucune suppression executee." });
        }

        if (_now() - preview.GeneratedAt > PreviewValidity)
        {
            return CreateExpiredResult(preview, stopwatch.Elapsed);
        }

        var eligible = preview.Candidates.Where(candidate => candidate.IsEligible).ToList();
        Report(progress, preview.Definition.Id, "Suppression", 0, eligible.Count, preview.Definition.DisplayName);

        for (var index = 0; index < eligible.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                status = CleanupStepStatus.Cancelled;
                skippedFiles += eligible.Count - index;
                break;
            }

            var candidate = eligible[index];
            var result = TryDeleteCandidate(preview.Definition, candidate);
            deletedFiles += result.Deleted ? 1 : 0;
            deletedBytes += result.Deleted ? result.Bytes : 0;
            skippedFiles += result.Skipped ? 1 : 0;
            errorFiles += result.Failed ? 1 : 0;

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                errors.Add(result.Error);
            }

            Report(
                progress,
                preview.Definition.Id,
                "Suppression",
                index + 1,
                eligible.Count,
                preview.Definition.DisplayName,
                deletedFiles,
                deletedBytes,
                skippedFiles,
                errorFiles);
        }

        if (status != CleanupStepStatus.Cancelled)
        {
            DeleteEmptySubdirectories(preview.Definition, errors);
        }
        stopwatch.Stop();

        if (status != CleanupStepStatus.Cancelled)
        {
            status = errorFiles > 0 ? CleanupStepStatus.PartialFailure : CleanupStepStatus.Completed;
        }

        Report(
            progress,
            preview.Definition.Id,
            "Termine",
            eligible.Count,
            eligible.Count,
            preview.Definition.DisplayName,
            deletedFiles,
            deletedBytes,
            skippedFiles,
            errorFiles);

        return new CleanupStepResult(
            preview.Definition,
            status,
            deletedFiles,
            deletedBytes,
            skippedFiles,
            errorFiles,
            stopwatch.Elapsed,
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private CandidateDeleteResult TryDeleteCandidate(
        CleanupZoneDefinition zone,
        CleanupCandidate candidate)
    {
        if (!CleanupPathGuard.TryValidateContainedFile(candidate.FullPath, zone.RootPath, out var fullPath, out var reason))
        {
            return CandidateDeleteResult.SkippedFile(reason);
        }

        if (!IsStillEligible(fullPath, zone, out var bytes, out reason))
        {
            return CandidateDeleteResult.SkippedFile(reason);
        }

        try
        {
            File.Delete(fullPath);
            return CandidateDeleteResult.DeletedFile(bytes);
        }
        catch
        {
            return CandidateDeleteResult.FailedFile($"Suppression impossible : {candidate.LogicalPath}.");
        }
    }

    private bool IsStillEligible(
        string fullPath,
        CleanupZoneDefinition zone,
        out long bytes,
        out string reason)
    {
        bytes = 0;
        reason = string.Empty;

        try
        {
            var fileInfo = new FileInfo(fullPath);
            var lastWrite = new DateTimeOffset(DateTime.SpecifyKind(fileInfo.LastWriteTimeUtc, DateTimeKind.Utc));

            if (_now() - lastWrite < zone.MinimumAge)
            {
                reason = "Fichier devenu recent.";
                return false;
            }

            bytes = Math.Max(0, fileInfo.Length);
            return true;
        }
        catch
        {
            reason = "Lecture du fichier impossible.";
            return false;
        }
    }

    private static void DeleteEmptySubdirectories(
        CleanupZoneDefinition zone,
        ICollection<string> errors)
    {
        foreach (var directoryPath in EnumerateDirectoriesDeepestFirst(zone.RootPath))
        {
            if (!CleanupPathGuard.IsStrictlyUnderRoot(directoryPath, zone.RootPath))
            {
                continue;
            }

            if (CleanupPathGuard.HasReparsePointAtPath(directoryPath))
            {
                continue;
            }

            TryDeleteEmptyDirectory(directoryPath, errors);
        }
    }

    private static IReadOnlyList<string> EnumerateDirectoriesDeepestFirst(string rootPath)
    {
        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var child in SafeGetDirectories(current))
            {
                if (!CleanupPathGuard.IsStrictlyUnderRoot(child, rootPath))
                {
                    continue;
                }

                if (CleanupPathGuard.HasReparsePointAtPath(child))
                {
                    continue;
                }

                directories.Add(child);
                pending.Push(child);
            }
        }

        return directories.OrderByDescending(path => path.Length).ToList();
    }

    private static IReadOnlyList<string> SafeGetDirectories(string directoryPath)
    {
        try
        {
            return Directory.GetDirectories(directoryPath);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void TryDeleteEmptyDirectory(string directoryPath, ICollection<string> errors)
    {
        try
        {
            if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                Directory.Delete(directoryPath, false);
            }
        }
        catch
        {
            errors.Add("Un sous-dossier vide n'a pas pu etre retire.");
        }
    }

    private static CleanupStepResult CreateExpiredResult(
        CleanupZonePreview preview,
        TimeSpan duration)
    {
        return new CleanupStepResult(
            preview.Definition,
            CleanupStepStatus.Expired,
            0,
            0,
            preview.EligibleFileCount,
            0,
            duration,
            new[] { "Previsualisation expiree. Relancer l'analyse." });
    }

    private static void Report(
        IProgress<CleanupProgress>? progress,
        CleanupZoneId zoneId,
        string step,
        int current,
        int total,
        string displayName,
        int deletedFiles = 0,
        long deletedBytes = 0,
        int skippedFiles = 0,
        int errorFiles = 0)
    {
        var percent = total == 0 ? 100 : (int)Math.Round(current * 100d / total);
        progress?.Report(new CleanupProgress(
            zoneId,
            step,
            percent,
            displayName,
            current,
            total,
            deletedFiles,
            deletedBytes,
            skippedFiles,
            errorFiles));
    }

    private sealed record CandidateDeleteResult(
        bool Deleted,
        bool Skipped,
        bool Failed,
        long Bytes,
        string? Error)
    {
        public static CandidateDeleteResult DeletedFile(long bytes)
        {
            return new CandidateDeleteResult(true, false, false, bytes, null);
        }

        public static CandidateDeleteResult SkippedFile(string reason)
        {
            return new CandidateDeleteResult(false, true, false, 0, null);
        }

        public static CandidateDeleteResult FailedFile(string error)
        {
            return new CandidateDeleteResult(false, false, true, 0, error);
        }
    }
}
