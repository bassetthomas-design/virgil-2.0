using Virgil.Contracts;
using Virgil.Core.Cleanup;

namespace Virgil.Infrastructure.Windows;

public sealed class WindowsOldSource(string? detectionRoot = null) : IWindowsOldSource
{
    public CleanupZonePreview? Read(CancellationToken cancellationToken)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var root = detectionRoot ?? Path.Combine(Path.GetPathRoot(windows) ?? throw new IOException("Volume Windows indisponible."), "Windows.old");
        if (!Directory.Exists(root)) return null;
        IReadOnlyList<FootprintFile> files;
        var errors = new List<string>();
        try { files = SoftwareFootprintSource.ReadFiles(root, cancellationToken); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            files = [];
            errors.Add("Windows.old détecté, taille indisponible : lecture refusée ou incomplète. Utilisez les outils Windows.");
        }
        var zone = new CleanupZoneDefinition(CleanupZoneId.WindowsOld, "Windows.old",
            $"Ancienne installation Windows détectée : {files.Sum(f => f.SizeBytes)} octets, {files.Count} fichiers{(errors.Count > 0 ? " (taille indisponible)" : "")}. Suppression déléguée aux outils Windows.",
            root, TimeSpan.Zero, CleanupRiskLevel.Medium, "Détection et taille uniquement.",
            "Aucune suppression par Virgil.", "Windows.old reste intact.", 300)
        { Classification = CleanupClassification.AdvancedCleanable, IsExecutable = false, RequiresElevation = true };
        return new(zone, DateTimeOffset.UtcNow, files.Count, 0, 0, files.Count, [], errors);
    }
}
