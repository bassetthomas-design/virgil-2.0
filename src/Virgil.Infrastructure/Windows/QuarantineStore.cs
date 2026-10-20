using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Virgil.Contracts;
using Virgil.Core.Cleanup;

namespace Virgil.Infrastructure.Windows;

/// <summary>Un manifeste atomique par élément, nommé par son identifiant ; aucun chemin stocké vers le contenu.</summary>
public sealed class QuarantineStore : IQuarantineStore
{
    private readonly string _root;
    private readonly Action<string> _log;
    private readonly Func<string, string> _volumeIdentity;
    private readonly object _sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public QuarantineStore(string? root = null, Action<string>? log = null, Func<string, string>? volumeIdentity = null)
    {
        _root = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Virgil", "Quarantine"));
        _log = log ?? (_ => { });
        _volumeIdentity = volumeIdentity ?? Volume;
    }
    public IReadOnlyList<QuarantineEntry> List()
    {
        lock (_sync)
        {
            if (!Directory.Exists(_root)) return [];
            EnsureSafeRoot();
            return Directory.GetFiles(_root, "*.json").OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => Read(p)).ToArray();
        }
    }
    private QuarantineEntry Read(string path)
    {
        if (CleanupPathGuard.HasReparsePointAtPath(path)) throw new IOException("Manifeste non sûr.");
        var e = JsonSerializer.Deserialize<QuarantineEntry>(File.ReadAllText(path)) ?? throw new IOException("Manifeste invalide.");
        if (Path.GetFileNameWithoutExtension(path) != e.Id.ToString("N") || e.SizeBytes < 0 || e.Sha256.Length != 64 ||
            e.QuarantinedAtUtc.Offset != TimeSpan.Zero || e.ExpiresAtUtc.Offset != TimeSpan.Zero || e.ExpiresAtUtc <= e.QuarantinedAtUtc)
            throw new IOException("Manifeste invalide.");
        return e;
    }
    public QuarantineEntry MoveToQuarantine(string path, CleanupZoneDefinition zone, DateTimeOffset now, int retentionDays)
    {
        lock (_sync)
        {
            if (retentionDays is < 1 or > 30) throw new IOException("Durée de quarantaine invalide.");
            if (!CleanupPathGuard.TryValidateContainedFile(path, zone.RootPath, out path, out _) ||
                !new CleanupSafetyClassifier().CanDeleteCandidate(zone, path, out _))
                throw new IOException("Chemin refusé pour la quarantaine.");
            Directory.CreateDirectory(_root);
            EnsureSafeRoot();
            RequireSameVolume(path, _root);
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var entry = new QuarantineEntry(Guid.NewGuid(), path, zone, source.Length,
                Convert.ToHexString(SHA256.HashData(source)), now.ToUniversalTime(), now.ToUniversalTime().AddDays(retentionDays));
            WriteManifest(entry); // La traçabilité précède le déplacement, y compris en cas d'arrêt brutal.
            try { File.Move(path, Content(entry), false); }
            catch
            {
                File.Delete(Manifest(entry));
                _log("Mise en quarantaine impossible ; aucun repli en suppression.");
                throw;
            }
            _log("Fichier mis en quarantaine ; échéance enregistrée en UTC.");
            return entry;
        }
    }
    public string? ValidateRestore(QuarantineEntry entry)
    {
        lock (_sync)
        {
            EnsureCurrent(entry);
            if (File.Exists(entry.OriginalPath) || Directory.Exists(entry.OriginalPath)) return "L'emplacement d'origine existe déjà : aucun écrasement autorisé.";
            if (!CleanupPathGuard.TryNormalizePath(entry.OriginalPath, entry.Zone.RootPath, out _, out _, out _) ||
                CleanupPathGuard.ContainsReparsePoint(entry.OriginalPath, entry.Zone.RootPath))
                return "Le chemin d'origine ne passe plus les gardes de sécurité.";
            if (!Matches(Content(entry), entry)) return "La taille ou le SHA-256 diffère du manifeste : restauration refusée.";
            return null;
        }
    }
    public void Restore(QuarantineEntry entry)
    {
        lock (_sync)
        {
            if (ValidateRestore(entry) is { } reason) throw new IOException(reason);
            var parent = Path.GetDirectoryName(entry.OriginalPath)!;
            // Le nettoyage conserve les dossiers ; ne pas recréer une arborescence disparue sans validation.
            if (!Directory.Exists(parent)) throw new IOException("Dossier d'origine absent.");
            RequireSameVolume(Content(entry), parent);
            using var source = new FileStream(Content(entry), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (source.Length != entry.SizeBytes || Convert.ToHexString(SHA256.HashData(source)) != entry.Sha256)
                throw new IOException("Vérification avant restauration impossible.");
            File.Move(Content(entry), entry.OriginalPath, false);
            if (!Matches(entry.OriginalPath, entry))
            {
                _log("Échec de vérification après restauration ; manifeste conservé.");
                throw new IOException("Échec de vérification après restauration.");
            }
            File.Delete(Manifest(entry));
            _log("Fichier restauré ; taille et SHA-256 vérifiés après déplacement.");
        }
    }
    public void Purge(QuarantineEntry entry)
    {
        lock (_sync)
        {
            EnsureCurrent(entry);
            if (CleanupPathGuard.HasReparsePointAtPath(Content(entry))) throw new IOException("Contenu non sûr.");
            // Ne pas retirer une entrée qui serait restée après une vérification de restauration échouée.
            if (!File.Exists(Content(entry))) throw new IOException("Contenu absent ; manifeste conservé.");
            File.Delete(Content(entry));
            File.Delete(Manifest(entry));
            _log("Élément de quarantaine effacé définitivement.");
        }
    }
    private void EnsureCurrent(QuarantineEntry entry)
    {
        EnsureSafeRoot();
        var current = Read(Manifest(entry));
        if (JsonSerializer.Serialize(current) != JsonSerializer.Serialize(entry)) throw new IOException("Le manifeste a changé.");
    }
    private void EnsureSafeRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(_root);
        while (current is not null)
        {
            if (current.Exists && CleanupPathGuard.HasReparsePoint(current.Attributes)) throw new IOException("Racine de quarantaine non sûre.");
            current = current.Parent;
        }
    }
    private string Content(QuarantineEntry e) => Path.Combine(_root, e.Id.ToString("N") + ".data");
    private string Manifest(QuarantineEntry e) => Path.Combine(_root, e.Id.ToString("N") + ".json");
    private static bool Matches(string path, QuarantineEntry entry)
    {
        if (CleanupPathGuard.HasReparsePointAtPath(path)) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return stream.Length == entry.SizeBytes && Convert.ToHexString(SHA256.HashData(stream)) == entry.Sha256;
    }
    private void WriteManifest(QuarantineEntry entry)
    {
        var target = Manifest(entry);
        var temporary = target + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, entry, JsonOptions);
                stream.Flush(true);
            }
            File.Move(temporary, target, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Volume(string path)
    {
            var mount = new StringBuilder(1024);
            var name = new StringBuilder(1024);
            if (!GetVolumePathName(path, mount, mount.Capacity) || !GetVolumeNameForVolumeMountPoint(mount.ToString(), name, name.Capacity))
                throw new IOException("Volume impossible à vérifier ; fichier non traité.");
        return name.ToString();
    }
    private void RequireSameVolume(string source, string destination)
    {
        if (!string.Equals(_volumeIdentity(source), _volumeIdentity(destination), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Autre volume : déplacement en quarantaine impossible ; fichier non traité.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(string fileName, StringBuilder volumePathName, int bufferLength);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string volumeMountPoint, StringBuilder volumeName, int bufferLength);
}
