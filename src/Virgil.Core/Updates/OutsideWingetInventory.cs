using Virgil.Contracts;

namespace Virgil.Core.Updates;

public sealed record OutsideWingetApplication(string Name, string Version, bool SystemComponent = false);
public sealed record OutsideWingetSnapshot(IReadOnlyList<OutsideWingetApplication> Installed,
    IReadOnlyList<string> ManagedNames, bool IsComplete = true);
public interface IOutsideWingetReader
{
    Task<OutsideWingetSnapshot> ReadAsync(CancellationToken token);
}
public interface IOutsideWingetBackend
{
    OutsideWingetInventory OutsideWinget { get; }
}
public sealed class OutsideWingetInventory(IOutsideWingetReader reader)
{
    public Task<IReadOnlyList<UpdateItem>> ScanAsync(CancellationToken token)
    {
        _ = reader;
        return Task.FromResult<IReadOnlyList<UpdateItem>>([]);
    }
}
