using Virgil.Contracts;

namespace Virgil.Core.Updates;

// Ports de preuve D1-bis-c : aucun accès système ni installation ici.
public interface IDriverInventoryReader
{
    Task<DriverInventoryReport> ReadAsync(CancellationToken token);
}

public sealed record DriverPackageOffer(UpdateItem Item, bool InstallsDriversSilently, string Reason);

public interface IDriverPackageSource
{
    void SetInventory(IReadOnlyList<UpdateItem> items) { }
    Task<IReadOnlyList<DriverPackageOffer>> ScanAsync(CancellationToken token);
    Task<UpdateSequenceStepResult> InstallAsync(UpdateSequenceStep step, CancellationToken token);
}

public interface IDriverSequenceBackend
{
    IDriverInventoryReader DriverInventory { get; }
    IBiosInformationReader Bios { get; }
    IDriverPackageSource DriverPackages { get; }
}

public sealed record DriverFinalPassInventory(bool IsComplete, IReadOnlyList<UpdateItem> Items, string Detail);
public interface IDriverFinalPassBackend
{
    Task<DriverFinalPassInventory> ReadFinalApplicationsAsync(CancellationToken token);
}
