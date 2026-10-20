using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

public sealed class AdditionalCleanupScanner(IKnownFoldersProvider folders, IDownloadsSource downloads,
    ISoftwareFootprintSource footprints, LeftoverAnalyzer leftovers, PersonalFolderPolicy personal, TimeProvider clock)
{
    public static bool IsOldOrLarge(FootprintFile file, DateTimeOffset now) =>
        now - file.LastWriteUtc > TimeSpan.FromDays(90) || file.SizeBytes > 500L * 1024 * 1024;

    public async Task<IReadOnlyList<CleanupZonePreview>> ScanAsync(CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var result = new List<CleanupZonePreview>();
        foreach (var root in folders.GetFolders().Where(f => f.Kind == KnownFolder.Downloads))
        {
            foreach (var file in downloads.ReadFiles(root.Path, token).Where(f => IsOldOrLarge(f, now)))
            {
                token.ThrowIfCancellationRequested();
                var zone = new CleanupZoneDefinition(CleanupZoneId.OldDownloads, "Téléchargements anciens ou volumineux", file.Path,
                    root.Path, TimeSpan.Zero, CleanupRiskLevel.Medium, "Fichier de plus de 90 jours ou de plus de 500 Mo.",
                    "Mise en quarantaine après confirmation.", "Les autres dossiers ne sont pas parcourus pour les gros fichiers.", 450)
                { Classification = CleanupClassification.AdvancedCleanable, RequiresReinforcedConfirmation = true };
                result.Add(new(zone, now, 1, 1, file.SizeBytes, 0,
                    [new(CleanupZoneId.OldDownloads, file.Path, file.Path, file.SizeBytes, file.LastWriteUtc, true, null) { Group = CleanupGroup.AVerifier }], []));
            }
        }
        result.AddRange(leftovers.Analyze(await footprints.ReadAsync(token).ConfigureAwait(false), now));
        return result.Select(personal.Apply).ToArray();
    }
}
