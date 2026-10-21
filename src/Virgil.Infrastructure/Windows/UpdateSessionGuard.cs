using System.Security;

namespace Virgil.Infrastructure.Windows;

/// <summary>Un handle par processus ; aucune acquisition exclusive entre les sessions.</summary>
public sealed class UpdateSessionGuard(Func<Mutex>? create = null) : Virgil.Core.Releases.IUpdateSessionGuard
{
    public const string MutexName = @"Global\Virgil.Update.v2";
    private Mutex? _mutex;
    public bool TryCreate(Action<string> warning)
    {
        if (_mutex is not null) return true;
        try { _mutex = create is null ? new Mutex(false, MutexName) : create(); return true; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException
            or WaitHandleCannotBeOpenedException or PlatformNotSupportedException)
        {
            warning("Mutex global de mise à jour indisponible ; fermez Virgil dans toutes les sessions avant une mise à jour.");
            return false;
        }
    }
    public void Dispose() { _mutex?.Dispose(); _mutex = null; }
}
