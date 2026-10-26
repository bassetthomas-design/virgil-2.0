using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Virgil.Infrastructure.Windows.Startup;

/// <summary>Lecture seule des métadonnées. Aucun lancement et aucune résolution réseau de signature.</summary>
public sealed record StartupFileMetadata(string Path, string Publisher, string Signature)
{
    public static string? ParseCommandPath(string command, bool directFile = false)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var text = command.Trim();
        string path;
        if (directFile) path = text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;
        else if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            if (end < 0 || end + 1 < text.Length && !char.IsWhiteSpace(text[end + 1])) return null;
            path = text[1..end];
        }
        else
        {
            var end = text.IndexOfAny([' ', '\t', '\r', '\n']);
            path = end < 0 ? text : text[..end];
            // Un chemin non cité contenant des espaces est ambigu pour CreateProcess.
            if (!new[] { ".exe", ".com", ".cmd", ".bat", ".lnk" }.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))) return null;
        }
        return path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/') &&
            path.IndexOfAny(['"', '\r', '\n', '\0']) < 0 ? path : null;
    }

    public static StartupFileMetadata Read(string command, bool directFile = false)
    {
        const string unknownPublisher = "Éditeur indisponible";
        const string unknownSignature = "Signature non vérifiée";
        if (!OperatingSystem.IsWindows()) return new(command, unknownPublisher, unknownSignature + " : Windows requis");
        var path = ParseCommandPath(Environment.ExpandEnvironmentVariables(command), directFile);
        if (path is null) return new(command, unknownPublisher, unknownSignature + " : chemin ambigu ou non local");
        try
        {
            if (!IsLocalFile(path)) return new(path, unknownPublisher, unknownSignature + " : fichier local inaccessible");
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                path = ReadShortcutTarget(path);
                if (path is null || !IsLocalFile(path)) return new(command, unknownPublisher, unknownSignature + " : cible du raccourci inaccessible");
            }
            var publisher = FileVersionInfo.GetVersionInfo(path).CompanyName;
            return new(path, string.IsNullOrWhiteSpace(publisher) ? unknownPublisher : publisher + " (déclaré par le fichier)", VerifySignature(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or COMException or ArgumentException or NotSupportedException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or System.ComponentModel.Win32Exception)
        {
            return new(path ?? command, unknownPublisher, unknownSignature + " : lecture impossible");
        }
    }

    private static bool IsLocalFile(string path) =>
        (new DriveInfo(System.IO.Path.GetPathRoot(path)!).DriveType is DriveType.Fixed or DriveType.Removable) && File.Exists(path);

    private static string? ReadShortcutTarget(string path)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            shell = Activator.CreateInstance(type);
            if (shell is null) return null;
            // CreateShortcut charge le fichier existant en mémoire ; Save n'est jamais appelé.
            shortcut = ((dynamic)shell).CreateShortcut(path);
            return ParseCommandPath(Environment.ExpandEnvironmentVariables((string)((dynamic)shortcut).TargetPath), directFile: true);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string VerifySignature(string path)
    {
        var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = Marshal.StringToCoTaskMemUni(path), FileHandle = IntPtr.Zero, KnownSubject = IntPtr.Zero };
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        var data = new TrustData
        {
            Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1,
            PolicyCallback = IntPtr.Zero, SipClient = IntPtr.Zero, RevocationChecks = 0,
            StateData = IntPtr.Zero, UrlReference = IntPtr.Zero, UiContext = 0,
            File = filePointer, StateAction = 1,
            // WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE : jamais d'appel réseau.
            ProviderFlags = 0x1000 | 0x10
        };
        try
        {
            Marshal.StructureToPtr(file, filePointer, false);
            return SignatureStatus(WinVerifyTrust(new IntPtr(-1), ref action, ref data));
        }
        finally
        {
            data.StateAction = 2;
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.FreeHGlobal(filePointer);
            Marshal.FreeCoTaskMem(file.Path);
        }
    }

    public static string SignatureStatus(uint result) => result switch
    {
        0 => "Signature valide (révocation non vérifiée)",
        0x800B0100 => "Fichier non signé",
        _ => $"Signature non validée (0x{result:X8})"
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustFile
    {
        public uint Size;
        public IntPtr Path;
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
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
    }
    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern uint WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
}
