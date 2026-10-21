using Virgil.Contracts.Actions;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Actions;

namespace Virgil.Core.Diagnostic;

public sealed class DiagnosticModule(DiagnosticSampler sampler, IDiagnosticSignalSource? signals = null,
    Virgil.Core.Resources.IProcessActionService? processes = null, IConfirmedPlanGate? gate = null) : IMaintenanceModule
{
    private DiagnosticSignals? _cached;
    public string Id => "diagnostic";
    public string Title => "Diagnostic";
    public string Category => "Observation";
    public void InvalidateSignals() => _cached = null;
    public async Task<Findings> ScanAsync(CancellationToken cancellationToken)
    {
        var snapshot = await sampler.SampleAsync(cancellationToken).ConfigureAwait(false);
        var observed = _cached ?? (signals is null ? DiagnosticSignals.Unavailable : await signals.ReadAsync(cancellationToken).ConfigureAwait(false));
        cancellationToken.ThrowIfCancellationRequested();
        _cached = observed;
        return DiagnosticAnalyzer.Analyze(snapshot, observed);
    }
    public IReadOnlyList<IMaintenanceAction> GetActions(Findings findings)
    {
        if (findings is not DiagnosticFindings diagnostic) return Array.Empty<IMaintenanceAction>();
        var actions = new List<IMaintenanceAction>();
        if (processes is not null) actions.AddRange(diagnostic.Issues.Where(i => i.Suggestion == DiagnosticSuggestion.Close && i.Process is not null)
            .SelectMany(i => new IMaintenanceAction[] { new DiagnosticCloseAction(processes, i.Process!, false), new DiagnosticCloseAction(processes, i.Process!, true) }));
        if (gate is not null) actions.AddRange(diagnostic.Issues.Select(i => i.Suggestion).Where(s => s is DiagnosticSuggestion.Cleanup or DiagnosticSuggestion.Updates)
            .Distinct().Select(s => new DiagnosticNavigationAction(gate, s)));
        return actions;
    }
}

public sealed class DiagnosticFindings : Findings
{
    public DiagnosticFindings(DiagnosticSnapshot snapshot, DiagnosticSignals signals, IReadOnlyList<DiagnosticIssue> issues,
        HealthScore score, IReadOnlyList<string> unavailable) : base(issues.Select(i => new MaintenanceFinding(i.Id, i.Title, i.Description)),
        issues.Where(i => i.Suggestion is DiagnosticSuggestion.Cleanup or DiagnosticSuggestion.Updates or DiagnosticSuggestion.Close)
            .Select(i => new ProposedMaintenanceAction(i.Id, i.Suggestion == DiagnosticSuggestion.Cleanup ? "Nettoyer" : i.Suggestion == DiagnosticSuggestion.Updates ? "Mettre à jour" : "Fermer", RiskLevel.Low, false, false, Reversibility.None)))
        => (Snapshot, Signals, Issues, Score, Unavailable) = (snapshot, signals, issues, score, unavailable);
    public DiagnosticSnapshot Snapshot { get; }
    public DiagnosticSignals Signals { get; }
    public IReadOnlyList<DiagnosticIssue> Issues { get; }
    public HealthScore Score { get; }
    public IReadOnlyList<DiagnosticIssue> Top3 => Issues.Where(i => i.Severity > 0).Take(3).ToArray();
    public IReadOnlyList<string> Unavailable { get; }
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
