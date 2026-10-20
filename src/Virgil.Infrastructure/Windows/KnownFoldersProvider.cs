using System.Runtime.InteropServices;
using Virgil.Core.Cleanup;

namespace Virgil.Infrastructure.Windows;

public sealed class KnownFoldersProvider : IKnownFoldersProvider
{
    private static readonly (KnownFolder Kind, Guid Id)[] Ids =
    [
        (KnownFolder.Pictures, new("33E28130-4E1E-4676-835A-98395C3BC3BB")),
        (KnownFolder.Videos, new("18989B1D-99B5-455B-841C-AB7C74E4DDFC")),
        (KnownFolder.Music, new("4BD8D571-6D19-48D3-BE97-422220080E43")),
        (KnownFolder.Documents, new("FDD39AD0-238F-46AF-ADB4-6C85480369C7")),
        (KnownFolder.Desktop, new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641")),
        (KnownFolder.Downloads, new("374DE290-123F-4565-9164-39C4925E467B"))
    ];

    public IReadOnlyList<KnownFolderPath> GetFolders()
    {
        var result = new List<KnownFolderPath>();
        foreach (var (kind, id) in Ids)
        {
            var folderId = id;
            var hr = SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out var pointer);
            try
            {
                // Un dossier personnel non résolu bloque le scan, plutôt que de perdre sa protection.
                Marshal.ThrowExceptionForHR(hr);
                var path = Marshal.PtrToStringUni(pointer);
                if (string.IsNullOrWhiteSpace(path)) throw new IOException("Dossier personnel introuvable.");
                result.Add(new(kind, path));
            }
            finally { if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer); }
        }
        foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } path)
                result.Add(new(KnownFolder.OneDrive, Path.GetFullPath(path)));
        return result.Distinct().ToArray();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint flags, IntPtr token, out IntPtr path);
}
