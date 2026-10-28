namespace Virgil.Contracts.Diagnostic;

/// <summary>Une absence de mesure porte toujours sa raison, jamais une valeur de remplacement.</summary>
public sealed record DiagnosticValue<T>
{
    private readonly T? _value;
    private DiagnosticValue(bool available, T? value, string? reason) => (IsAvailable, _value, Reason) = (available, value, reason);
    public bool IsAvailable { get; }
    public bool IsReadFailure { get; private init; }
    public string? Reason { get; }
    public T Value => IsAvailable ? _value! : throw new InvalidOperationException(Reason);
    public static DiagnosticValue<T> Available(T value) => new(true, value, null);
    public static DiagnosticValue<T> Unavailable(string reason) => new(false, default,
        string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("Une raison est requise.", nameof(reason)) : reason) { IsReadFailure = true };
    public static DiagnosticValue<T> Unmeasured(string reason) => Unavailable(reason) with { IsReadFailure = false };
}

public sealed record CpuDiagnostic(DiagnosticValue<double> TotalPercent, IReadOnlyList<DiagnosticValue<double>> CorePercent,
    DiagnosticValue<double> CurrentMhz, DiagnosticValue<double> NominalMhz, DiagnosticValue<double> TemperatureCelsius);
public sealed record MemoryDiagnostic(DiagnosticValue<double> TotalBytes, DiagnosticValue<double> UsedBytes,
    DiagnosticValue<double> CachedBytes, DiagnosticValue<double> CompressedBytes, DiagnosticValue<double> AvailableBytes,
    DiagnosticValue<double> CommittedBytes);
public sealed record DiskDiagnostic(string Id, string Kind, DiagnosticValue<double> ReadBytesPerSecond,
    DiagnosticValue<double> WriteBytesPerSecond, DiagnosticValue<double> ResponseMilliseconds,
    DiagnosticValue<double> TotalBytes, DiagnosticValue<double> FreeBytes, DiagnosticValue<string> Health)
{
    public bool IsSystemVolume { get; init; }
}
public sealed record NetworkDiagnostic(string Id, string Type, DiagnosticValue<double> SendBytesPerSecond,
    DiagnosticValue<double> ReceiveBytesPerSecond, DiagnosticValue<double> LinkBitsPerSecond);
public sealed record GpuDiagnostic(string Id, DiagnosticValue<double> LoadPercent, DiagnosticValue<double> DedicatedUsedBytes,
    DiagnosticValue<double> DedicatedTotalBytes, DiagnosticValue<double> TemperatureCelsius);
public sealed record ProcessDiagnostic(string Name, int Pid, DiagnosticValue<double> CpuPercent,
    DiagnosticValue<double> PrivateBytes, DiagnosticValue<double> WorkingSetBytes, DiagnosticValue<double> DiskBytesPerSecond,
    DiagnosticValue<int> NetworkConnections, DiagnosticValue<string> Publisher, DiagnosticValue<string> Path, string Explanation);

public sealed record DiagnosticSnapshot(DateTimeOffset CapturedAtUtc, CpuDiagnostic Cpu, MemoryDiagnostic Memory,
    IReadOnlyList<DiskDiagnostic> Disks, IReadOnlyList<NetworkDiagnostic> Network, IReadOnlyList<GpuDiagnostic> Gpu,
    IReadOnlyList<ProcessDiagnostic> Processes);
