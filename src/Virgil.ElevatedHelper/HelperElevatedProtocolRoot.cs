using Virgil.Contracts.Elevated;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Virgil.ElevatedHelper;

[SupportedOSPlatform("windows")]
internal sealed class HelperElevatedProtocolRoot
{
    private static readonly SecurityIdentifier SystemSid =
        new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdministratorsSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private readonly ElevatedPathGuard _pathGuard;
    private readonly Func<SecurityIdentifier> _currentUserSidProvider;

    public HelperElevatedProtocolRoot(ElevatedPathGuard pathGuard)
        : this(pathGuard, CurrentUserSid)
    {
    }

    public HelperElevatedProtocolRoot(
        ElevatedPathGuard pathGuard,
        Func<SecurityIdentifier> currentUserSidProvider)
    {
        _pathGuard = pathGuard;
        _currentUserSidProvider = currentUserSidProvider;
    }

    public string ValidateExisting(string localAppData)
    {
        var normalizedLocalAppData = _pathGuard.NormalizeLocalAppData(localAppData);
        var rootDirectory = _pathGuard.ValidateRoot(
            normalizedLocalAppData,
            allowMissingSecureDirectories: false);

        ValidateAcl(Path.Combine(normalizedLocalAppData, ElevatedPathGuard.ApplicationDirectoryName));
        ValidateAcl(rootDirectory);
        return rootDirectory;
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
                throw new InvalidOperationException("ACL du dossier temporaire Virgil incomplète.");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or SystemException)
        {
            throw new InvalidOperationException("Permissions du dossier temporaire Virgil non garanties.", ex);
        }
    }

    private static SecurityIdentifier CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new InvalidOperationException("Identité Windows courante indisponible.");
    }
}
