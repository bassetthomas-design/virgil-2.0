using Virgil.Contracts;
using Virgil.Core.Cleanup;

namespace Virgil.Infrastructure.Windows;

public sealed class WindowsOldSource : IWindowsOldSource
{
    public CleanupZonePreview? Read(CancellationToken cancellationToken)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var root = Path.Combine(Path.GetPathRoot(windows) ?? throw new IOException("Volume Windows indisponible."), "Windows.old");
        if (!Directory.Exists(root)) return null;
        var files = SoftwareFootprintSource.ReadFiles(root, cancellationToken);
        var zone = new CleanupZoneDefinition(CleanupZoneId.WindowsOld, "Windows.old",
            $"Ancienne installation Windows détectée : {files.Sum(f => f.SizeBytes)} octets, {files.Count} fichiers. Suppression déléguée aux outils Windows.",
            root, TimeSpan.Zero, CleanupRiskLevel.Medium, "Détection et taille uniquement.",
            "Aucune suppression par Virgil.", "Windows.old reste intact.", 300)
        { Classification = CleanupClassification.AdvancedCleanable, IsExecutable = false, RequiresElevation = true };
        return new(zone, DateTimeOffset.UtcNow, files.Count, 0, 0, files.Count, [], []);
    }
}
