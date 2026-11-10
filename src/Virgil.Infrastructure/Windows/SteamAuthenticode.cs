using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Virgil.Infrastructure.Windows;

// WinVerifyTrust est l’API Windows officielle de validation Authenticode.
internal static class SteamAuthenticode
{
    internal static bool IsValveSigned(string path)
    {
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        try
        {
            Marshal.StructureToPtr(file, pointer, false);
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1, File = pointer,
                StateAction = 1, ProviderFlags = 0x1000 | 0x80 };
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            try
            {
                if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return false;
                using var certificate = X509Certificate.CreateFromSignedFile(path);
                using var signer = new X509Certificate2(certificate);
                var name = signer.GetNameInfo(X509NameType.SimpleName, false);
                return name is "Valve Corp." or "Valve Corporation";
            }
            catch (CryptographicException) { return false; }
            finally { data.StateAction = 2; _ = WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
        }
        finally { Marshal.DestroyStructure<TrustFile>(pointer); Marshal.FreeHGlobal(pointer); }
    }
    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr PolicyCallback;
        public IntPtr SipClient;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr Url;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
