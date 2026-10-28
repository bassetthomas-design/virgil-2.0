namespace Virgil.Core.Startup;

public static class StartupInventoryMerger
{
    public static IReadOnlyList<StartupItem> Merge(IEnumerable<StartupItem> items) => items.ToArray();
}
