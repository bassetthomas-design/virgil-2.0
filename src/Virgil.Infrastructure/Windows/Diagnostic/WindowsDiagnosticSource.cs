using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Diagnostic;

namespace Virgil.Infrastructure.Windows.Diagnostic;

/// <summary>Lecture uniquement, aucun processus externe, aucun pilote tiers et aucun accès en écriture.</summary>
public sealed class WindowsDiagnosticSource : IDiagnosticSource
{
    private const string Idle = @"\Processor Information(_Total)\% Idle Time";
    private const string Current = @"\Processor Information(_Total)\Processor Frequency";
    private const string Cache = @"\Memory\Cache Bytes";
    private const string Compressed = @"\Memory\Compressed Page Size";
    private static DiagnosticValue<double> Number(double value) => DiagnosticValue<double>.Available(value);
    private static DiagnosticValue<T> Missing<T>(string reason = "capteur non exposé par Windows") => DiagnosticValue<T>.Unavailable("Indisponible : " + reason);
    private static DiagnosticCounter AbsentCounter(string reason) => new(Missing<double>(reason), Missing<double>(reason), DiagnosticCounterKind.Rate);
    public Task<DiagnosticReading> ReadAsync(CancellationToken cancellationToken) => Task.Run(() => Read(cancellationToken), cancellationToken);

    private static DiagnosticReading Read(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var pdh = new PdhReader();
        foreach (var path in new[] { Idle, Current, Cache, Compressed }) pdh.Add(path);
        var corePaths = pdh.Expand(@"\Processor Information(*)\% Idle Time");
        var cores = ProcessorInstanceOrdering.Sort(corePaths.Select(PdhReader.Instance));
        var diskRead = pdh.Expand(@"\PhysicalDisk(*)\Disk Read Bytes/sec");
        var diskWrite = pdh.Expand(@"\PhysicalDisk(*)\Disk Write Bytes/sec");
        var diskResponse = pdh.Expand(@"\PhysicalDisk(*)\Avg. Disk sec/Transfer");
        var volumeRead = pdh.Expand(@"\LogicalDisk(*)\Disk Read Bytes/sec");
        var volumeWrite = pdh.Expand(@"\LogicalDisk(*)\Disk Write Bytes/sec");
        var volumeResponse = pdh.Expand(@"\LogicalDisk(*)\Avg. Disk sec/Transfer");
        var gpuEngines = pdh.Expand(@"\GPU Engine(*)\Utilization Percentage");
        var gpuUsed = pdh.Expand(@"\GPU Adapter Memory(*)\Dedicated Usage");
        var gpuTotal = pdh.Expand(@"\GPU Adapter Memory(*)\Dedicated Limit");
        pdh.Collect();
        token.ThrowIfCancellationRequested();
        var memory = ReadMemory(pdh);
        var idle = pdh.Counter(Idle, DiagnosticCounterKind.IdleTime);
        if (!idle.Numerator.IsAvailable) idle = SystemIdle();
        var coreCounters = cores.IsAvailable
            ? cores.Value.Select(instance => pdh.Counter(corePaths.First(p => PdhReader.Instance(p) == instance),
                DiagnosticCounterKind.IdleTime) with { Identity = "ProcessorInformation:" + instance }).ToArray()
            : Array.Empty<DiagnosticCounter>();
        if (!cores.IsAvailable || coreCounters.Length != Environment.ProcessorCount)
        {
            var reason = cores.IsAvailable ? "Indisponible : charge par cœur non exposée par Windows" : cores.Reason!;
            coreCounters = Enumerable.Range(0, Environment.ProcessorCount)
                .Select(_ => new DiagnosticCounter(DiagnosticValue<double>.Unavailable(reason),
                    DiagnosticValue<double>.Unavailable(reason), DiagnosticCounterKind.IdleTime)).ToArray();
        }
        var cpu = new RawCpu(idle, Array.AsReadOnly(coreCounters),
            pdh.Gauge(Current), Missing<double>("fréquence nominale non exposée par les compteurs Windows"), Missing<double>());
        var disks = new List<RawDisk>();
        DiagnosticCounter Match(IReadOnlyList<string> paths, string id, DiagnosticCounterKind kind) =>
            pdh.Counter(paths.FirstOrDefault(p => PdhReader.Instance(p) == id) ?? "", kind);
        foreach (var path in diskRead.Where(p => PdhReader.Instance(p) != "_Total"))
        {
            token.ThrowIfCancellationRequested();
            var id = PdhReader.Instance(path);
            disks.Add(new(id, "Disque physique", pdh.Counter(path, DiagnosticCounterKind.Rate),
                Match(diskWrite, id, DiagnosticCounterKind.Rate), Match(diskResponse, id, DiagnosticCounterKind.AverageMilliseconds),
                ReadPhysicalCapacity(id), Missing<double>("espace libre disponible par volume"), ReadHealth(id)));
        }
        foreach (var drive in DriveInfo.GetDrives())
        {
            token.ThrowIfCancellationRequested();
            var id = drive.Name.TrimEnd('\\');
            disks.Add(new(id, "Volume", Match(volumeRead, id, DiagnosticCounterKind.Rate), Match(volumeWrite, id, DiagnosticCounterKind.Rate),
                Match(volumeResponse, id, DiagnosticCounterKind.AverageMilliseconds),
                ReadValue(() => (double)drive.TotalSize), ReadValue(() => (double)drive.TotalFreeSpace),
                Missing<string>("santé disponible sur le disque physique uniquement")));
        }
        var network = new List<RawNetwork>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var stats = nic.GetIPStatistics();
                network.Add(new(nic.Name, NetworkType(nic.NetworkInterfaceType),
                    new(Number(stats.BytesSent), Number(0), DiagnosticCounterKind.Rate),
                    new(Number(stats.BytesReceived), Number(0), DiagnosticCounterKind.Rate),
                    nic.Speed > 0 ? Number(nic.Speed) : Missing<double>("vitesse de lien non exposée")));
            }
            catch (NetworkInformationException)
            {
                network.Add(new(nic.Name, NetworkType(nic.NetworkInterfaceType), AbsentCounter("compteurs réseau inaccessibles"),
                    AbsentCounter("compteurs réseau inaccessibles"), Missing<double>("vitesse de lien inaccessible")));
            }
        }
        static string Adapter(string instance)
        {
            var start = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            var end = instance.IndexOf("_eng_", StringComparison.OrdinalIgnoreCase);
            return start >= 0 ? instance[start..(end > start ? end : instance.Length)] : instance;
        }
        var gpu = new List<RawGpu>();
        var adapterIds = gpuUsed.Concat(gpuEngines).Select(p => Adapter(PdhReader.Instance(p))).Distinct().Order().ToArray();
        foreach (var id in adapterIds)
        {
            token.ThrowIfCancellationRequested();
            var engines = gpuEngines.Where(p => Adapter(PdhReader.Instance(p)) == id).Order(StringComparer.Ordinal).ToArray();
            // Les instances GPU Engine sont par PID. Les identités sont conservées dans l'ordre stable ;
            // une modification de cet ensemble invalide la charge pour cet intervalle.
            var engineCounters = engines.GroupBy(p => PdhReader.Instance(p)[PdhReader.Instance(p).IndexOf("_eng_", StringComparison.Ordinal)..])
                .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => SumEngine(g, pdh)).ToArray();
            DiagnosticValue<double> AdapterGauge(IReadOnlyList<string> paths) => pdh.Gauge(paths.FirstOrDefault(p => Adapter(PdhReader.Instance(p)) == id) ?? "");
            gpu.Add(new(id, Array.AsReadOnly(engineCounters), AdapterGauge(gpuUsed), AdapterGauge(gpuTotal), Missing<double>()));
        }
        var connections = ConnectionReader.Read(token);
        var processes = new List<RawProcess>();
        var all = Process.GetProcesses();
        try
        {
            foreach (var process in all)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var name = process.ProcessName;
                    var pid = process.Id;
                    var path = ReadText(() => process.MainModule?.FileName);
                    var publisher = path.IsAvailable ? ReadText(() => FileVersionInfo.GetVersionInfo(path.Value).CompanyName) : Missing<string>("éditeur inaccessible");
                    var identity = ReadText(() => process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var cpuTime = identity.IsAvailable ? ReadValue(() => process.TotalProcessorTime.TotalSeconds) : Missing<double>("identité du processus inaccessible");
                    // Diviser par le nombre logique de processeurs : 100 % représente toute la machine.
                    var processCpu = new DiagnosticCounter(cpuTime.IsAvailable ? Number(cpuTime.Value / Environment.ProcessorCount) : cpuTime,
                        Number(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency), DiagnosticCounterKind.BusyTime);
                    processes.Add(new(name, pid, identity.IsAvailable ? identity.Value : "inaccessible", processCpu,
                        ReadValue(() => (double)process.PrivateMemorySize64), ReadValue(() => (double)process.WorkingSet64),
                        AbsentCounter("E/S disque par processus non isolables des autres E/S par ces compteurs"),
                        ConnectionReader.ForPid(pid, connections), publisher, path));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                { /* Processus terminé pendant l'inventaire : pas de ligne ni d'identité inventée. */ }
            }
        }
        finally { foreach (var process in all) process.Dispose(); }
        token.ThrowIfCancellationRequested();
        return new(cpu, memory, disks.AsReadOnly(), network.AsReadOnly(), gpu.AsReadOnly(), processes.AsReadOnly());
    }
    private static DiagnosticCounter SumEngine(IEnumerable<string> paths, PdhReader pdh)
    {
        var values = paths.Select(p => pdh.Counter(p, DiagnosticCounterKind.BusyTime)).ToArray();
        if (values.Any(v => !v.Numerator.IsAvailable || !v.Denominator.IsAvailable))
            return new(Missing<double>("compteur moteur GPU inaccessible"), Missing<double>(), DiagnosticCounterKind.BusyTime);
        return new(Number(values.Sum(v => v.Numerator.Value)), values[0].Denominator, DiagnosticCounterKind.BusyTime,
            Identity: string.Join("|", paths));
    }
    private static string NetworkType(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi", NetworkInterfaceType.Ethernet => "Ethernet",
        NetworkInterfaceType.Loopback => "Boucle locale", NetworkInterfaceType.Tunnel => "Tunnel", _ => "Autre carte réseau"
    };
    private static DiagnosticValue<double> ReadValue(Func<double> read)
    {
        try { return Number(read()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or NotSupportedException)
        { return Missing<double>("lecture refusée ou périphérique indisponible"); }
    }
    private static DiagnosticValue<string> ReadText(Func<string?> read)
    {
        try { var value = read(); return string.IsNullOrWhiteSpace(value) ? Missing<string>("information non exposée") : DiagnosticValue<string>.Available(value); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or NotSupportedException)
        { return Missing<string>("lecture refusée ou processus terminé"); }
    }
    private static MemoryDiagnostic ReadMemory(PdhReader pdh)
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var ok = GlobalMemoryStatusEx(ref status);
        var info = new PerformanceInfo { Size = (uint)Marshal.SizeOf<PerformanceInfo>() };
        var perf = GetPerformanceInfo(ref info, info.Size);
        return new(ok ? Number(status.TotalPhysical) : Missing<double>(),
            ok ? Number(status.TotalPhysical - status.AvailablePhysical) : Missing<double>(),
            perf ? Number(info.SystemCache.ToUInt64() * (double)info.PageSize.ToUInt64()) : pdh.Gauge(Cache), pdh.Gauge(Compressed),
            ok ? Number(status.AvailablePhysical) : Missing<double>(),
            perf ? Number(info.CommitTotal.ToUInt64() * (double)info.PageSize.ToUInt64()) : Missing<double>());
    }
    private static DiagnosticCounter SystemIdle()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return AbsentCounter("temps processeur Windows inaccessible");
        static double T(System.Runtime.InteropServices.ComTypes.FILETIME t) => ((ulong)(uint)t.dwHighDateTime << 32) | (uint)t.dwLowDateTime;
        return new(Number(T(idle)), Number(T(kernel) + T(user)), DiagnosticCounterKind.IdleTime, Identity: "GetSystemTimes");
    }
    private static DiagnosticValue<double> ReadPhysicalCapacity(string id)
    {
        var number = id.Split(' ')[0];
        if (!int.TryParse(number, out _)) return Missing<double>();
        using var handle = CreateFile(@"\\.\PhysicalDrive" + number, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) return Missing<double>("capacité physique inaccessible");
        var buffer = new byte[256];
        // IOCTL_DISK_GET_DRIVE_GEOMETRY_EX : FILE_ANY_ACCESS, lecture seule, DiskSize à l'offset 24.
        if (!DeviceIoControl(handle, 0x000700A0, IntPtr.Zero, 0, buffer, (uint)buffer.Length, out var returned, IntPtr.Zero) || returned < 32)
            return Missing<double>("géométrie du disque non exposée par Windows");
        return Number(BitConverter.ToInt64(buffer, 24));
    }
    private static DiagnosticValue<string> ReadHealth(string id)
    {
        var number = id.Split(' ')[0];
        if (!int.TryParse(number, out _)) return Missing<string>();
        using var handle = CreateFile(@"\\.\PhysicalDrive" + number, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) return Missing<string>("santé du disque inaccessible sans droits supplémentaires");
        var buffer = new byte[516];
        if (!DeviceIoControl(handle, 0x002D1100, IntPtr.Zero, 0, buffer, (uint)buffer.Length, out var returned, IntPtr.Zero) || returned < 4)
            return Missing<string>("SMART non exposé par Windows sans pilote tiers");
        return DiagnosticValue<string>.Available(BitConverter.ToUInt32(buffer, 0) != 0
            ? "Défaillance prédite par Windows" : "Aucune défaillance prédite par Windows (contrôle limité)");
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PerformanceInfo
    {
        public uint Size;
        public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("psapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPerformanceInfo(ref PerformanceInfo info, uint size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME idle, out System.Runtime.InteropServices.ComTypes.FILETIME kernel, out System.Runtime.InteropServices.ComTypes.FILETIME user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle handle, uint control, IntPtr input, uint inputSize, [Out] byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
}
