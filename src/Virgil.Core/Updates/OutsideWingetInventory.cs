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
    public async Task<IReadOnlyList<UpdateItem>> ScanAsync(CancellationToken token)
    {
        var snapshot = await reader.ReadAsync(token).ConfigureAwait(false);
        if (!snapshot.IsComplete)
            return [new UpdateItem { Id = "outside-inventory", Name = "Inventaire des autres logiciels", Status = UpdateItemStatus.InformationOnly,
                Message = "Inventaire incomplet : les sources de mise à jour n'ont pas pu être comparées." }];
        var managed = snapshot.ManagedNames.Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return snapshot.Installed.Where(a => !a.SystemComponent && !string.IsNullOrWhiteSpace(a.Name) && !managed.Contains(a.Name.Trim()) && LauncherCatalog.Detect([a.Name]).Count == 0)
            .DistinctBy(a => a.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select((a, index) => new UpdateItem { Id = "outside-" + index, Name = a.Name.Trim(), InstalledVersion = a.Version,
                Status = UpdateItemStatus.InformationOnly, Message = "pas de source de mise à jour connue" }).ToArray();
    }
}
