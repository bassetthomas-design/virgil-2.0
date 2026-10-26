using Microsoft.Win32;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Virgil.Contracts;

namespace Virgil.ElevatedHelper;

internal static class StartupApprovalHandler
{
    internal static void Execute(StartupApprovalWrite payload)
    {
        if (!payload.IsValid()) throw new ArgumentException("Demande de démarrage invalide.");
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        foreach (var part in new[] { "Virgil.StartupBackups" })
        {
            directory = Path.Combine(directory, part);
            var info = new DirectoryInfo(directory);
            if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Dossier de sauvegarde refusé.");
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            if (!info.Exists) info.Create(security);
            else
            {
                var existing = info.GetAccessControl();
                var owner = existing.GetOwner(typeof(SecurityIdentifier));
                if (owner is not SecurityIdentifier ownerSid ||
                    (!ownerSid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) && !ownerSid.IsWellKnown(WellKnownSidType.LocalSystemSid)))
                    throw new IOException("Propriétaire du dossier de sauvegarde refusé.");
                if (!existing.AreAccessRulesProtected) throw new IOException("Dossier de sauvegarde non protégé.");
                foreach (FileSystemAccessRule rule in existing.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                    if (rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference is SecurityIdentifier sid &&
                        !sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) && !sid.IsWellKnown(WellKnownSidType.LocalSystemSid))
                        throw new IOException("Autorisations du dossier de sauvegarde refusées.");
            }
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Dossier de sauvegarde refusé.");
        }
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + payload.Kind, true)
            ?? throw new IOException("État de démarrage introuvable.");
        var expected = Convert.FromHexString(payload.ExpectedHex);
        if (key.GetValueKind(payload.Name) != RegistryValueKind.Binary || key.GetValue(payload.Name) is not byte[] current || !current.SequenceEqual(expected))
            throw new IOException("État de démarrage modifié depuis l’aperçu.");
        var backup = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        var text = JsonSerializer.Serialize(payload);
        using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream)) { writer.Write(text); writer.Flush(); stream.Flush(true); }
        if (File.ReadAllText(backup) != text) throw new IOException("Sauvegarde non vérifiée.");
        var replacement = Convert.FromHexString(payload.NewHex);
        if (key.GetValueKind(payload.Name) != RegistryValueKind.Binary || key.GetValue(payload.Name) is not byte[] rechecked || !rechecked.SequenceEqual(expected))
            throw new IOException("État de démarrage modifié pendant la sauvegarde.");
        key.SetValue(payload.Name, replacement, RegistryValueKind.Binary);
        key.Flush();
        if (key.GetValue(payload.Name) is not byte[] actual || !actual.SequenceEqual(replacement))
            throw new IOException("Écriture de démarrage non vérifiée.");
    }
}
