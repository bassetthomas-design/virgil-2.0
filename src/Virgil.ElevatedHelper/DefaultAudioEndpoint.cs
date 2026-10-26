using System.Runtime.InteropServices;

namespace Virgil.ElevatedHelper;

internal static class DefaultAudioEndpoint
{
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IntPtr endpoint);
    }

    internal static string Check()
    {
        object? instance = null;
        IntPtr endpoint = IntPtr.Zero;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;
            instance = Activator.CreateInstance(type);
            var result = ((IDeviceEnumerator)instance!).GetDefaultAudioEndpoint(0, 1, out endpoint);
            return result == 0 && endpoint != IntPtr.Zero ? "VIRGIL|DefaultAudioEndpoint|OK|Sortie audio par défaut disponible"
                : result == unchecked((int)0x80070490) ? "VIRGIL|DefaultAudioEndpoint|FINDING|Aucune sortie audio par défaut"
                : "VIRGIL|DefaultAudioEndpoint|UNKNOWN|Sortie audio par défaut indisponible";
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or TypeLoadException)
        { return "VIRGIL|DefaultAudioEndpoint|UNKNOWN|Sortie audio par défaut indisponible"; }
        finally
        {
            if (endpoint != IntPtr.Zero) Marshal.Release(endpoint);
            if (instance is not null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
        }
    }
}
