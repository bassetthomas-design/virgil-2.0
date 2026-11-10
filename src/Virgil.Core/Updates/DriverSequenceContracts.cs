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
    Task<IReadOnlyList<DriverPackageOffer>> ScanAsync(CancellationToken token);
    Task<UpdateSequenceStepResult> InstallAsync(UpdateSequenceStep step, CancellationToken token);
}

public interface IDriverSequenceBackend
{
    IDriverInventoryReader DriverInventory { get; }
    IBiosInformationReader Bios { get; }
    IDriverPackageSource DriverPackages { get; }
}
