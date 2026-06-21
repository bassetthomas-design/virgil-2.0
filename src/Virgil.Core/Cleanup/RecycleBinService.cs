using System.Runtime.InteropServices;

namespace Virgil.Core.Cleanup;

public sealed record RecycleBinState(bool EstimateAvailable, long ItemCount, long SizeBytes, string? ReadableError = null);

public sealed record RecycleBinActionResult(bool Success, long ItemCount, long FreedBytes, string? ReadableError = null);

public interface IRecycleBinService
{
    RecycleBinState Query();
    RecycleBinActionResult Empty();
}

public sealed class WindowsRecycleBinService : IRecycleBinService
{
    private const uint NoConfirmation = 0x00000001;
    private const uint NoProgressUi = 0x00000002;
    private const uint NoSound = 0x00000004;

    public RecycleBinState Query()
    {
        try
        {
            var info = new ShQueryRbInfo { Size = Marshal.SizeOf<ShQueryRbInfo>() };
            var result = SHQueryRecycleBin(null, ref info);
            return result == 0
                ? new RecycleBinState(true, Math.Max(0, info.ItemCount), Math.Max(0, info.TotalSize))
                : new RecycleBinState(false, 0, 0, "Estimation de la corbeille indisponible.");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            return new RecycleBinState(false, 0, 0, "Estimation de la corbeille indisponible.");
        }
    }

    public RecycleBinActionResult Empty()
    {
        var before = Query();
        try
        {
            var result = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
            return result == 0
                ? new RecycleBinActionResult(true, before.ItemCount, before.SizeBytes)
                : new RecycleBinActionResult(false, 0, 0, "Windows n'a pas pu vider la corbeille.");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            return new RecycleBinActionResult(false, 0, 0, "Service corbeille indisponible.");
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct ShQueryRbInfo
    {
        public int Size;
        public long TotalSize;
        public long ItemCount;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr windowHandle, string? rootPath, uint flags);
}
