namespace Virgil.Core.Startup;

public sealed record StartupItem(string Name, string Source, string Path, string State,
    string Publisher, string Signature, string Impact, StartupApprovalTarget? ApprovalTarget = null);
public sealed record StartupInventory(IReadOnlyList<StartupItem> Items, IReadOnlyList<string> Warnings);
public interface IStartupInventorySource
{
    Task<StartupInventory> ReadAsync(CancellationToken cancellationToken);
}
