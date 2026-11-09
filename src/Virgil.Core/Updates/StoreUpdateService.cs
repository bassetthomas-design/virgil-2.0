using Virgil.Contracts;

namespace Virgil.Core.Updates;

public sealed record StorePackage(string ProductId, string FamilyName, string Name, string Version);
public sealed record StoreSearch(bool Supported, string Reason, IReadOnlyList<StorePackage> Packages);
public sealed record StoreStart(bool Accepted, string Reason);
public interface IStoreUpdateClient
{
    Task<StoreSearch> SearchAsync(CancellationToken token);
    Task<IReadOnlyDictionary<string, string>> ReadVersionsAsync(CancellationToken token);
    Task<StoreStart> StartAsync(StorePackage package, CancellationToken token);
}
public interface IStoreUpdateBackend
{
    StoreUpdateService StoreUpdates { get; }
}

// Port de test : le comportement actuel reste inchangé jusqu'au correctif séparé.
public sealed class StoreUpdateService(IStoreUpdateClient client)
{
    public Task<IReadOnlyList<UpdateSequenceStep>> ScanAsync(IReadOnlyList<UpdateItem> applications, CancellationToken token)
    {
        _ = client;
        return Task.FromResult<IReadOnlyList<UpdateSequenceStep>>([]);
    }
    public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) =>
        Task.FromResult(new UpdateSequenceStepResult(step, false, false, "à terminer dans le Microsoft Store") { WasUnavailable = true });
    public Task<UpdateSourceVerification> VerifyAsync(CancellationToken token) =>
        Task.FromResult(new UpdateSourceVerification(UpdateComponent.Store, "à vérifier", "à terminer dans le Microsoft Store"));
}
