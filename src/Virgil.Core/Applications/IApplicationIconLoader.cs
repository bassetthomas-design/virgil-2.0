namespace Virgil.Core.Applications;

public interface IApplicationIconLoader
{
    Task<byte[]?> LoadAsync(string? path, CancellationToken cancellationToken);
}
