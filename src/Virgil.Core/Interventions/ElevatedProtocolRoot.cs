using System.Security.AccessControl;
using System.Security.Principal;

namespace Virgil.Core.Interventions;

public sealed class ElevatedProtocolRoot
{
    private static readonly SecurityIdentifier SystemSid =
        new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdministratorsSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private readonly ElevatedPathGuard _pathGuard;
    private readonly Func<SecurityIdentifier> _currentUserSidProvider;

    public ElevatedProtocolRoot()
        : this(new ElevatedPathGuard(), CurrentUserSid)
    {
    }

    public ElevatedProtocolRoot(ElevatedPathGuard pathGuard)
        : this(pathGuard, CurrentUserSid)
    {
    }

    public ElevatedProtocolRoot(
        ElevatedPathGuard pathGuard,
        Func<SecurityIdentifier> currentUserSidProvider)
    {
        _pathGuard = pathGuard;
        _currentUserSidProvider = currentUserSidProvider;
    }

    public string EnsureCreated(string localAppData)
    {
        var normalizedLocalAppData = _pathGuard.NormalizeLocalAppData(localAppData);
        var virgilDirectory = Path.Combine(normalizedLocalAppData, "Virgil");
        var rootDirectory = Path.Combine(virgilDirectory, "Temp");

        _pathGuard.ValidateRoot(normalizedLocalAppData, allowMissingSecureDirectories: true);
        EnsureDirectory(virgilDirectory);
        _pathGuard.ValidateRoot(normalizedLocalAppData, allowMissingSecureDirectories: true);
        ApplyAndValidateAcl(virgilDirectory);
        _pathGuard.ValidateRoot(normalizedLocalAppData, allowMissingSecureDirectories: true);
        EnsureDirectory(rootDirectory);
        _pathGuard.ValidateRoot(normalizedLocalAppData, allowMissingSecureDirectories: false);
        ApplyAndValidateAcl(rootDirectory);
        return _pathGuard.ValidateRoot(normalizedLocalAppData, allowMissingSecureDirectories: false);
    }

    public string ValidateExisting(string localAppData)
    {
        var normalizedLocalAppData = _pathGuard.NormalizeLocalAppData(localAppData);
        var rootDirectory = _pathGuard.ValidateRoot(
            normalizedLocalAppData,
            allowMissingSecureDirectories: false);

        ValidateAcl(Path.Combine(normalizedLocalAppData, "Virgil"));
        ValidateAcl(rootDirectory);
        return rootDirectory;
    }

    private void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    private void ApplyAndValidateAcl(string path)
    {
        try
        {
            var userSid = _currentUserSidProvider();
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetOwner(userSid);
            AddFullControl(security, userSid);
            AddFullControl(security, SystemSid);
            AddFullControl(security, AdministratorsSid);
            new DirectoryInfo(path).SetAccessControl(security);
            ValidateAcl(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or SystemException)
        {
            throw new InvalidOperationException("Permissions du dossier temporaire Virgil non garanties.", ex);
        }
    }

    private void ValidateAcl(string path)
    {
        try
        {
            var userSid = _currentUserSidProvider();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                userSid.Value,
                SystemSid.Value,
                AdministratorsSid.Value
            };
            var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access);
            var rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier));
            var fullControlSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (FileSystemAccessRule rule in rules)
            {
                var sid = ((SecurityIdentifier)rule.IdentityReference).Value;
                if (rule.IsInherited || !allowed.Contains(sid))
                {
                    throw new InvalidOperationException("ACL du dossier temporaire Virgil trop large.");
                }

                if (rule.AccessControlType == AccessControlType.Allow &&
                    (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
                {
                    fullControlSids.Add(sid);
                }
            }

            if (!allowed.SetEquals(fullControlSids))
            {
                throw new InvalidOperationException("ACL du dossier temporaire Virgil incomplete.");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or SystemException)
        {
            throw new InvalidOperationException("Permissions du dossier temporaire Virgil non garanties.", ex);
        }
    }

    private static void AddFullControl(DirectorySecurity security, SecurityIdentifier sid)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            sid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }

    private static SecurityIdentifier CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new InvalidOperationException("Identite Windows courante indisponible.");
    }
}
