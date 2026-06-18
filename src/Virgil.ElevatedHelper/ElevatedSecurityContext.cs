using System.Security.Principal;

namespace Virgil.ElevatedHelper;

public interface IElevatedSecurityContext
{
    bool IsAdministrator { get; }
}

public sealed class ElevatedSecurityContext : IElevatedSecurityContext
{
    public bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
