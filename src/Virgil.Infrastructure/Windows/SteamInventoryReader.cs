using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

// Port de lecture pour les preuves ; comportement actuel avant le correctif.
public sealed class SteamInventoryReader(ISteamReadAccess access)
{
    public IReadOnlyList<SteamGameState> Read()
    {
        _ = access;
        return [];
    }
}
