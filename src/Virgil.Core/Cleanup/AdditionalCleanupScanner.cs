using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

public sealed class AdditionalCleanupScanner(IKnownFoldersProvider folders, IDownloadsSource downloads,
    ISoftwareFootprintSource footprints, LeftoverAnalyzer leftovers, PersonalFolderPolicy personal, TimeProvider clock, IWindowsOldSource? windowsOld = null)
{
    public static bool IsOldOrLarge(FootprintFile file, DateTimeOffset now) =>
        now - file.LastWriteUtc > TimeSpan.FromDays(90) || file.SizeBytes > 500L * 1024 * 1024;

    public Task<IReadOnlyList<CleanupZonePreview>> ScanAsync(CancellationToken token) =>
        Task.Run(() => ScanCoreAsync(token), token);

    private async Task<IReadOnlyList<CleanupZonePreview>> ScanCoreAsync(CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var result = new List<CleanupZonePreview>();
        if (windowsOld?.Read(token) is { } old) result.Add(old);
        foreach (var root in folders.GetFolders().Where(f => f.Kind == KnownFolder.Downloads))
        {
            var candidates = new List<CleanupCandidate>();
            foreach (var file in downloads.ReadFiles(root.Path, token).Where(f => IsOldOrLarge(f, now)))
            {
                token.ThrowIfCancellationRequested();
                var eligible = !CleanupSafetyClassifier.IsPersonalExtension(file.Path);
                candidates.Add(new(CleanupZoneId.OldDownloads, file.Path, file.Path, file.SizeBytes, file.LastWriteUtc,
                    eligible, eligible ? null : "Type personnel ou archive protégé par les gardes existants.")
                    { Group = CleanupGroup.AVerifier });
            }
            if (candidates.Count == 0) continue;
            var zone = new CleanupZoneDefinition(CleanupZoneId.OldDownloads, "Téléchargements anciens ou volumineux", root.Path,
                root.Path, TimeSpan.Zero, CleanupRiskLevel.Medium, "Fichiers de plus de 90 jours ou de plus de 500 Mo.",
                "Mise en quarantaine après confirmation.", "Les autres dossiers ne sont pas parcourus pour les gros fichiers.", 450)
            { Classification = CleanupClassification.AdvancedCleanable, RequiresReinforcedConfirmation = true };
            var eligibleCandidates = candidates.Where(c => c.IsEligible).ToArray();
            result.Add(new(zone, now, candidates.Count, eligibleCandidates.Length, eligibleCandidates.Sum(c => c.SizeBytes),
                candidates.Count - eligibleCandidates.Length, candidates, []));
        }
        result.AddRange(leftovers.Analyze(await footprints.ReadAsync(token).ConfigureAwait(false), now));
        return result.Select(personal.Apply).ToArray();
    }
}
