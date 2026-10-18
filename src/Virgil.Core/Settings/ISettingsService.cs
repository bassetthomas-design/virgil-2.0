using Virgil.Contracts;

namespace Virgil.Core.Settings;

public interface ISettingsService
{
    VirgilSettings Current { get; }
    Task SaveAsync(VirgilSettings settings, CancellationToken ct);
}
