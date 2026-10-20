using Virgil.Contracts.Actions;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Actions;

namespace Virgil.Core.Diagnostic;

public sealed class DiagnosticModule(DiagnosticSampler sampler) : IMaintenanceModule
{
    public string Id => "diagnostic";
    public string Title => "Diagnostic";
    public string Category => "Observation";
    public async Task<Findings> ScanAsync(CancellationToken cancellationToken)
    {
        var snapshot = await sampler.SampleAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new DiagnosticFindings(snapshot);
    }
    public IReadOnlyList<IMaintenanceAction> GetActions(Findings findings) => Array.Empty<IMaintenanceAction>();
}

public sealed class DiagnosticFindings(DiagnosticSnapshot snapshot) : Findings(
    new[] { new MaintenanceFinding("diagnostic.read-only", "Diagnostic en lecture seule",
        "Observation uniquement ; aucune action ni écriture. Les mesures indisponibles indiquent leur raison.") },
    Array.Empty<ProposedMaintenanceAction>())
{
    public DiagnosticSnapshot Snapshot { get; } = snapshot;
}

public enum DiagnosticProcessSort { Cpu, Memory, Disk, Network, Name }
public static class DiagnosticProcessOrdering
{
    public static IReadOnlyList<ProcessDiagnostic> Sort(IEnumerable<ProcessDiagnostic> processes, DiagnosticProcessSort sort, bool descending = true)
    {
        double? Metric(ProcessDiagnostic p) => sort switch
        {
            DiagnosticProcessSort.Cpu => p.CpuPercent.IsAvailable ? p.CpuPercent.Value : null,
            DiagnosticProcessSort.Memory => p.PrivateBytes.IsAvailable ? p.PrivateBytes.Value : null,
            DiagnosticProcessSort.Disk => p.DiskBytesPerSecond.IsAvailable ? p.DiskBytesPerSecond.Value : null,
            DiagnosticProcessSort.Network => p.NetworkConnections.IsAvailable ? p.NetworkConnections.Value : null,
            _ => null
        };
        if (sort == DiagnosticProcessSort.Name) return (descending
            ? processes.OrderByDescending(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            : processes.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)).ThenBy(p => p.Pid).ToArray();
        var availableFirst = processes.OrderBy(p => Metric(p) is null);
        return (descending ? availableFirst.ThenByDescending(Metric) : availableFirst.ThenBy(Metric))
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Pid).ToArray();
    }
}
