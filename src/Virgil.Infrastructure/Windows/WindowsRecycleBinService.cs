using System.Runtime.InteropServices;
using Virgil.Core.Cleanup;

namespace Virgil.Infrastructure.Windows;

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
            var after = Query();
            return RecycleBinEmptyPolicy.Interpret(before, result, after);
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
