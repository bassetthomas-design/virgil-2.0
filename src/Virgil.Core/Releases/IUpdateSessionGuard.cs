namespace Virgil.Core.Releases;

public interface IUpdateSessionGuard : IDisposable
{
    bool TryCreate(Action<string> warning);
}
