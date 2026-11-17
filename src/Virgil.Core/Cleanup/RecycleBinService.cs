using System.Globalization;

namespace Virgil.Core.Cleanup;

public sealed record RecycleBinState(bool EstimateAvailable, long ItemCount, long SizeBytes, string? ReadableError = null);

public sealed record RecycleBinActionResult(bool Success, long ItemCount, long FreedBytes, string? ReadableError = null);

public interface IRecycleBinService
{
    RecycleBinState Query();
    RecycleBinActionResult Empty();
}

public sealed class UnavailableRecycleBinService : IRecycleBinService
{
    public RecycleBinState Query() => new(false, 0, 0, "Estimation de la corbeille indisponible.");
    public RecycleBinActionResult Empty() => new(false, 0, 0, "Service corbeille indisponible.");
}

public static class RecycleBinEmptyPolicy
{
    public static RecycleBinActionResult Interpret(RecycleBinState before, int hresult, RecycleBinState after)
    {
        var code = hresult.ToString("X8", CultureInfo.InvariantCulture);
        if (!after.EstimateAvailable)
            return new(false, 0, 0, $"Windows ne permet pas de confirmer le vidage de la corbeille (code 0x{code}). Ouvrez la Corbeille pour vérifier.");
        if (after.ItemCount > 0)
        {
            var removed = before.EstimateAvailable ? Math.Max(0, before.ItemCount - after.ItemCount) : 0;
            var freed = before.EstimateAvailable ? Math.Max(0, before.SizeBytes - after.SizeBytes) : 0;
            return new(false, removed, freed, $"Vidage incomplet de la corbeille : {after.ItemCount} élément(s) restant(s) (code 0x{code}).");
        }
        return new(true, before.EstimateAvailable ? before.ItemCount : 0, before.EstimateAvailable ? before.SizeBytes : 0, null);
    }
}
