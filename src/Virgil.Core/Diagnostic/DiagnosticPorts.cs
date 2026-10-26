using Virgil.Contracts.Diagnostic;

namespace Virgil.Core.Diagnostic;

public enum DiagnosticCounterKind { Gauge, Rate, BusyTime, IdleTime, AverageMilliseconds }
/// <summary>Compteur cumulatif Windows ; TimeBase vaut secondes ou fréquence native selon le type.</summary>
public sealed record DiagnosticCounter(DiagnosticValue<double> Numerator, DiagnosticValue<double> Denominator,
    DiagnosticCounterKind Kind, double TimeBase = 1, string Identity = "");
public sealed record RawCpu(DiagnosticCounter Total, IReadOnlyList<DiagnosticCounter> Cores,
    DiagnosticValue<double> CurrentMhz, DiagnosticValue<double> NominalMhz, DiagnosticValue<double> Temperature);
public sealed record RawDisk(string Id, string Kind, DiagnosticCounter Read, DiagnosticCounter Write, DiagnosticCounter Response,
    DiagnosticValue<double> Total, DiagnosticValue<double> Free, DiagnosticValue<string> Health)
{
    public bool IsSystemVolume { get; init; }
}
public sealed record RawNetwork(string Id, string Type, DiagnosticCounter Send, DiagnosticCounter Receive, DiagnosticValue<double> Speed);
public sealed record RawGpu(string Id, IReadOnlyList<DiagnosticCounter> Engines, DiagnosticValue<double> Used,
    DiagnosticValue<double> Total, DiagnosticValue<double> Temperature);
public sealed record RawProcess(string Name, int Pid, string Identity, DiagnosticCounter Cpu,
    DiagnosticValue<double> Private, DiagnosticValue<double> WorkingSet, DiagnosticCounter Disk,
    DiagnosticValue<int> Connections, DiagnosticValue<string> Publisher, DiagnosticValue<string> Path);
public sealed record DiagnosticReading(RawCpu Cpu, MemoryDiagnostic Memory, IReadOnlyList<RawDisk> Disks,
    IReadOnlyList<RawNetwork> Network, IReadOnlyList<RawGpu> Gpu, IReadOnlyList<RawProcess> Processes);

/// <summary>Port d'observation groupé : aucune commande, écriture ou fermeture de processus.</summary>
public interface IDiagnosticSource
{
    Task<DiagnosticReading> ReadAsync(CancellationToken cancellationToken);
}
