using Virgil.Core.Actions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

public sealed class CleanupExecutionService : ICleanupExecutionService
{
    public static readonly TimeSpan PreviewValidity = TimeSpan.FromMinutes(10);

    private readonly Func<DateTimeOffset> _now;
    private readonly IConfirmedPlanGate _gate;
    private readonly IRecycleBinService _recycleBinService;
    private readonly CleanupSafetyClassifier _safetyClassifier;

    private readonly PersonalFolderPolicy? _personal;
    private readonly ICleanupDisk _disk;
    private readonly IQuarantineStore? _quarantine;

    public CleanupExecutionService(
        ICleanupDisk disk,
        IConfirmedPlanGate gate,
        Func<DateTimeOffset>? now = null,
        IRecycleBinService? recycleBinService = null,
        CleanupSafetyClassifier? safetyClassifier = null,
        IQuarantineStore? quarantine = null, PersonalFolderPolicy? personal = null)
    {
        _disk = disk;
        _personal = personal;
        _quarantine = quarantine;
        _gate = gate;
        _now = now ?? (() => DateTimeOffset.Now);
        _recycleBinService = recycleBinService ?? disk.RecycleBin;
        _safetyClassifier = safetyClassifier ?? new CleanupSafetyClassifier();
    }

    public Task<CleanupStepResult> ExecuteZoneAsync(
        CleanupZonePreview preview,
        ConfirmedPlan? plan,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => ExecuteZone(preview, plan, progress, cancellationToken), CancellationToken.None);
    }

    public Task<CleanupSelectionResult> ExecuteSelectionAsync(IReadOnlyList<CleanupZonePreview> selection, ConfirmedPlan? plan,
        IProgress<CleanupProgress>? progress, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var snapshot = selection.Select(CleanupMaintenanceSnapshot.Copy).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.Length == 0 || snapshot.Any(p => p.Definition.Id == CleanupZoneId.WindowsOld || !p.Definition.IsExecutable ||
            p.Definition.Classification is not (CleanupClassification.Cleanable or CleanupClassification.AdvancedCleanable) ||
            _now() - p.GeneratedAt > PreviewValidity || p.GeneratedAt > _now().AddMinutes(1)))
            return CleanupSelectionResult.Refused("Sélection absente, non exécutable ou expirée.");
        // Recalculer le caractère personnel avant la porte : un dossier redirigé change le hash et impose un nouveau scan.
        if (_personal is not null && snapshot.Any(p =>
            _personal.Apply(p).Definition.IsPersonalFolder != p.Definition.IsPersonalFolder))
            return CleanupSelectionResult.Refused("Dossier personnel modifié : relancez l'analyse.");
        var validation = _gate.TryConsume(plan, CleanupSelectionAction.CreatePreview(snapshot));
        if (!validation.IsAccepted) return CleanupSelectionResult.Refused(validation.Message);
        var results = new List<CleanupStepResult>();
        foreach (var zone in snapshot)
        {
            results.Add(cancellationToken.IsCancellationRequested ? CancelZone(zone)
                : ExecuteConfirmedZone(zone, progress, cancellationToken));
        }
        return new CleanupSelectionResult(results, null);
    }, CancellationToken.None);

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
        ConfirmedPlan? plan,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        if (cancellationToken.IsCancellationRequested)
            return CancelZone(preview);

        if (preview.Definition.Id == CleanupZoneId.WindowsOld || !preview.Definition.IsExecutable ||
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
                new[] { "Zone en information seulement : aucune suppression exécutée." });
        }

        if (_now() - preview.GeneratedAt > PreviewValidity)
        {
            return CreateExpiredResult(preview, stopwatch.Elapsed);
        }

        if (_personal is not null && _personal.Apply(preview).Definition.IsPersonalFolder != preview.Definition.IsPersonalFolder)
            return CancelZone(preview) with { RefusalReason = "Dossier personnel modifié : relancez l'analyse." };
        var validation = _gate.TryConsume(plan, CleanupActionPreviewFactory.Create(preview));
        if (!validation.IsAccepted)
            return CancelZone(preview) with
            {
                Errors = new[] { "Nettoyage annulé.\n" + validation.Message },
                RefusalReason = validation.Message
            };

        return ExecuteConfirmedZone(preview, progress, cancellationToken);
    }

    // Accessible uniquement après consommation de la porte, par zone ou par sélection complète.
    private CleanupStepResult ExecuteConfirmedZone(CleanupZonePreview preview, IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var deletedFiles = 0;
        var deletedBytes = 0L;
        var skippedFiles = 0;
        var errorFiles = 0;
        var status = CleanupStepStatus.Completed;
        if (cancellationToken.IsCancellationRequested) return CancelZone(preview);
        if (_now() - preview.GeneratedAt > PreviewValidity) return CreateExpiredResult(preview, stopwatch.Elapsed);

        if (preview.Definition.Id == CleanupZoneId.RecycleBin)
        {
            var recycleResult = _recycleBinService.Empty();
            stopwatch.Stop();
            return new CleanupStepResult(
                preview.Definition,
                recycleResult.Success ? CleanupStepStatus.Completed : CleanupStepStatus.Failed,
                (int)Math.Clamp(recycleResult.ItemCount, 0, int.MaxValue),
                recycleResult.FreedBytes,
                0,
                recycleResult.Success ? 0 : 1,
                stopwatch.Elapsed,
                string.IsNullOrWhiteSpace(recycleResult.ReadableError) ? Array.Empty<string>() : new[] { recycleResult.ReadableError });
        }

        var eligible = preview.Candidates.Where(candidate => candidate.IsEligible).ToList();
        Report(progress, preview.Definition.Id, "Mise en quarantaine", 0, eligible.Count, preview.Definition.DisplayName);

        for (var index = 0; index < eligible.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                status = CleanupStepStatus.Cancelled;
                skippedFiles += eligible.Count - index;
                break;
            }

            var candidate = eligible[index];
            var result = TryDeleteCandidate(preview.Definition, candidate, preview.QuarantineRetentionDays);
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
                "Mise en quarantaine",
                index + 1,
                eligible.Count,
                preview.Definition.DisplayName,
                deletedFiles,
                deletedBytes,
                skippedFiles,
                errorFiles);
        }

        // Conserver les dossiers pour permettre une restauration à leur emplacement exact.
        stopwatch.Stop();

        if (status != CleanupStepStatus.Cancelled)
        {
            status = errorFiles > 0 ? CleanupStepStatus.PartialFailure : CleanupStepStatus.Completed;
        }

        Report(
            progress,
            preview.Definition.Id,
            "Terminé",
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
            0,
            0,
            skippedFiles,
            errorFiles,
            stopwatch.Elapsed,
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            QuarantinedFiles = deletedFiles, QuarantinedBytes = deletedBytes,
            QuarantineRetentionDays = preview.QuarantineRetentionDays
        };
    }

    private CandidateDeleteResult TryDeleteCandidate(
        CleanupZoneDefinition zone,
        CleanupCandidate candidate, int retentionDays)
    {
        if (zone.RemnantZones.Count > 0)
        {
            var resolved = CleanupRemnantResolver.Resolve(zone, candidate.FullPath);
            if (resolved is null) return CandidateDeleteResult.SkippedFile("Racine du reste absente ou ambiguë.");
            zone = resolved;
        }
        if (_personal is not null && !_personal.CanQuarantine(zone, candidate.FullPath))
            return CandidateDeleteResult.SkippedFile("Dossier personnel : fichier hors d'un reste identifié.");
        if (zone.Id == CleanupZoneId.SoftwareLeftovers &&
            (zone.ValidatedRemnantRoot is null || zone.RemnantAllowedRoot is null ||
             !string.Equals(zone.RootPath, zone.ValidatedRemnantRoot, StringComparison.OrdinalIgnoreCase) ||
             !CleanupPathGuard.IsStrictlyUnderRoot(zone.RootPath, zone.RemnantAllowedRoot) ||
             CleanupPathGuard.ContainsReparsePoint(zone.RootPath, zone.RemnantAllowedRoot)))
            return CandidateDeleteResult.SkippedFile("Racine du reste non validée.");
        if (!_safetyClassifier.CanDeleteCandidate(zone, candidate.FullPath, out var safetyReason))
        {
            return CandidateDeleteResult.SkippedFile(safetyReason);
        }

        if (!CleanupPathGuard.TryValidateContainedFile(candidate.FullPath, zone.RootPath, out var fullPath, out var reason))
        {
            return CandidateDeleteResult.SkippedFile(reason);
        }

        if (!IsStillEligible(fullPath, zone, out _, out reason))
        {
            return CandidateDeleteResult.SkippedFile(reason);
        }

        try
        {
            if (_quarantine is null) throw new InvalidOperationException("Stockage de quarantaine indisponible.");
            var entry = _quarantine.MoveToQuarantine(fullPath, zone, _now(), retentionDays);
            return CandidateDeleteResult.DeletedFile(entry.SizeBytes);
        }
        catch
        {
            return CandidateDeleteResult.FailedFile($"Mise en quarantaine impossible (volume, verrou ou droits) : {candidate.LogicalPath}.");
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
            var fileInfo = _disk.ReadFile(fullPath);
            var lastWrite = fileInfo.LastWriteTimeUtc;

            if (zone.Id == CleanupZoneId.OldDownloads &&
                !AdditionalCleanupScanner.IsOldOrLarge(new(fullPath, fileInfo.Length, lastWrite), _now()))
            {
                reason = "Téléchargement devenu inéligible.";
                return false;
            }
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
            new[] { "Prévisualisation expiree. Relancer l'analyse." })
        {
            RefusalReason = "Prévisualisation expiree. Relancer l'analyse."
        };
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
