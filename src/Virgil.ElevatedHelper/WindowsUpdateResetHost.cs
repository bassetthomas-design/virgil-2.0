using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Virgil.ElevatedHelper;

internal sealed class WindowsUpdateResetHost : IWindowsUpdateResetHost
{
    private const uint Connect = 0x0001;
    private const uint QueryConfig = 0x0001;
    private const uint QueryStatus = 0x0004;
    private const uint Start = 0x0010;
    private const uint Stop = 0x0020;
    private const uint StopControl = 1;
    private const int ServiceDisabled = 4;
    private const int InsufficientBuffer = 122;
    private const int AlreadyRunning = 1056;
    private const int NotActive = 1062;

    public WindowsUpdateServiceInfo InspectService(string name)
    {
        using var service = Open(name, QueryConfig | QueryStatus);
        if (QueryServiceConfigW(service, IntPtr.Zero, 0, out var size))
            throw new InvalidOperationException($"Taille de configuration inattendue pour {name}.");
        var error = Marshal.GetLastWin32Error();
        if (error != InsufficientBuffer)
            throw ServiceError("Lecture de la configuration", name, error);
        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            if (!QueryServiceConfigW(service, buffer, size, out _))
                throw ServiceError("Lecture de la configuration", name, Marshal.GetLastWin32Error());
            // Le type de démarrage est le deuxième DWORD de QUERY_SERVICE_CONFIGW.
            return new(ReadStatus(service, name), Marshal.ReadInt32(buffer, sizeof(uint)) == ServiceDisabled);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public WindowsUpdateServiceState ReadServiceState(string name)
    {
        using var service = Open(name, QueryStatus);
        return ReadStatus(service, name);
    }

    public void RequestStop(string name)
    {
        using var service = Open(name, Stop);
        if (!ControlService(service, StopControl, out _))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NotActive)
                throw ServiceError("Arrêt", name, error);
        }
    }

    public void RequestStart(string name)
    {
        using var service = Open(name, Start);
        if (!StartServiceW(service, 0, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != AlreadyRunning)
                throw ServiceError("Démarrage", name, error);
        }
    }

    public bool IsRebootPending()
    {
        using var cbs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
        using var update = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        return cbs is not null || update is not null;
    }

    public FileAttributes? ReadAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public void MoveDirectory(string source, string destination)
    {
        if (ReadAttributes(source) is not { } attributes ||
            (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            throw new IOException($"Un dossier ordinaire est attendu à {source}.");
        Directory.Move(source, destination);
    }

    private static WindowsUpdateServiceState ReadStatus(ServiceHandle service, string name)
    {
        if (!QueryServiceStatusEx(service, 0, out var status, (uint)Marshal.SizeOf<ServiceStatusEx>(), out _))
            throw ServiceError("Lecture de l’état", name, Marshal.GetLastWin32Error());
        return status.Status.CurrentState;
    }

    private static ServiceHandle Open(string name, uint access)
    {
        using var manager = OpenSCManagerW(null, null, Connect);
        if (manager.IsInvalid)
            throw ServiceError("Ouverture du gestionnaire de services", name, Marshal.GetLastWin32Error());
        var service = OpenServiceW(manager, name, access);
        if (!service.IsInvalid)
            return service;
        var error = Marshal.GetLastWin32Error();
        service.Dispose();
        throw ServiceError("Ouverture du service", name, error);
    }

    private static Win32Exception ServiceError(string operation, string name, int error) =>
        new(error, $"{operation} ({name}) : {new Win32Exception(error).Message}");

    private sealed class ServiceHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public WindowsUpdateServiceState CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusEx
    {
        public ServiceStatus Status;
        public uint ServiceId;
        public uint ServiceFlags;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern ServiceHandle OpenSCManagerW(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern ServiceHandle OpenServiceW(ServiceHandle manager, string name, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(ServiceHandle service, int infoLevel,
        out ServiceStatusEx status, uint bufferSize, out uint bytesNeeded);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfigW(ServiceHandle service, IntPtr buffer, uint bufferSize, out uint bytesNeeded);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(ServiceHandle service, uint control, out ServiceStatus status);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceW(ServiceHandle service, uint argumentCount, IntPtr arguments);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr service);
}
