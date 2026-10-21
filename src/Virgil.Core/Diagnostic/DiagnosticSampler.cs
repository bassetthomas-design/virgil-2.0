using Virgil.Contracts.Diagnostic;

namespace Virgil.Core.Diagnostic;

/// <summary>Logique pure et horloge injectée ; sérialisation de toutes les demandes, y compris manuelles.</summary>
public sealed class DiagnosticSampler(IDiagnosticSource source, TimeProvider clock)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<DiagnosticSnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var first = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
            var started = clock.GetTimestamp();
            await Task.Delay(TimeSpan.FromSeconds(1), clock, cancellationToken).ConfigureAwait(false);
            var second = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
            var elapsed = clock.GetElapsedTime(started).TotalSeconds;
            cancellationToken.ThrowIfCancellationRequested();
            return Calculate(first, second, elapsed, clock.GetUtcNow());
        }
        finally { _gate.Release(); }
    }

    public static DiagnosticValue<double> CalculateCounter(DiagnosticCounter? before, DiagnosticCounter after, double seconds)
    {
        if (!after.Numerator.IsAvailable) return DiagnosticValue<double>.Unavailable(after.Numerator.Reason!);
        if (after.Kind == DiagnosticCounterKind.Gauge) return after.Numerator;
        if (before is null) return Missing("nouveau compteur, seconde mesure nécessaire");
        if (!before.Numerator.IsAvailable) return DiagnosticValue<double>.Unavailable(before.Numerator.Reason!);
        if (before.Identity != after.Identity) return Missing("identité du compteur modifiée");
        if (before.Kind != after.Kind || seconds <= 0) return Missing("intervalle de mesure invalide");
        var delta = after.Numerator.Value - before.Numerator.Value;
        if (delta < 0) return Missing("compteur réinitialisé");
        double value;
        if (after.Kind == DiagnosticCounterKind.Rate) value = delta / seconds;
        else
        {
            if (!before.Denominator.IsAvailable) return DiagnosticValue<double>.Unavailable(before.Denominator.Reason!);
            if (!after.Denominator.IsAvailable) return DiagnosticValue<double>.Unavailable(after.Denominator.Reason!);
            var denominator = after.Denominator.Value - before.Denominator.Value;
            if (denominator <= 0) return Missing("compteur sans intervalle exploitable");
            value = after.Kind == DiagnosticCounterKind.AverageMilliseconds
                ? delta / denominator / after.TimeBase * 1000
                : 100 * (after.Kind == DiagnosticCounterKind.IdleTime ? 1 - delta / denominator : delta / denominator);
        }
        if (!double.IsFinite(value) || value < 0 ||
            (after.Kind is DiagnosticCounterKind.BusyTime or DiagnosticCounterKind.IdleTime && value > 100.01))
            return Missing("mesure hors limites");
        return DiagnosticValue<double>.Available(value);
    }

    public static DiagnosticSnapshot Calculate(DiagnosticReading before, DiagnosticReading after, double seconds, DateTimeOffset utc)
    {
        DiagnosticValue<double> Measure(DiagnosticCounter? a, DiagnosticCounter b) => CalculateCounter(a, b, seconds);
        var disks = after.Disks.Select(d =>
        {
            var old = before.Disks.FirstOrDefault(x => x.Id == d.Id && x.Kind == d.Kind);
            return new DiskDiagnostic(d.Id, d.Kind, Measure(old?.Read, d.Read), Measure(old?.Write, d.Write),
                Measure(old?.Response, d.Response), d.Total, d.Free, d.Health);
        }).ToArray();
        var network = after.Network.Select(n =>
        {
            var old = before.Network.FirstOrDefault(x => x.Id == n.Id);
            return new NetworkDiagnostic(n.Id, n.Type, Measure(old?.Send, n.Send), Measure(old?.Receive, n.Receive), n.Speed);
        }).ToArray();
        var gpu = after.Gpu.Select(g =>
        {
            var old = before.Gpu.FirstOrDefault(x => x.Id == g.Id);
            var loads = g.Engines.Select((e, i) => Measure(old is not null && i < old.Engines.Count ? old.Engines[i] : null, e)).ToArray();
            // Le moteur le plus occupé détermine l'activité, sans additionner les moteurs parallèles.
            var load = loads.Length == 0 ? Missing("compteurs GPU non exposés par Windows")
                : loads.Any(x => !x.IsAvailable) ? loads.First(x => !x.IsAvailable)
                : DiagnosticValue<double>.Available(loads.Max(x => x.Value));
            return new GpuDiagnostic(g.Id, load, g.Used, g.Total, g.Temperature);
        }).ToArray();
        var processes = after.Processes.Select(p =>
        {
            var old = before.Processes.FirstOrDefault(x => x.Pid == p.Pid && x.Identity == p.Identity);
            return new ProcessDiagnostic(p.Name, p.Pid, Measure(old?.Cpu, p.Cpu), p.Private, p.WorkingSet,
                Measure(old?.Disk, p.Disk), p.Connections, p.Publisher, p.Path,
                ProcessExplanationCatalog.Explain(p.Name, p.Publisher, p.Path));
        }).ToArray();
        var cpu = new CpuDiagnostic(Measure(before.Cpu.Total, after.Cpu.Total),
            Array.AsReadOnly(after.Cpu.Cores.Select((c, i) => Measure(i < before.Cpu.Cores.Count ? before.Cpu.Cores[i] : null, c)).ToArray()),
            after.Cpu.CurrentMhz, after.Cpu.NominalMhz, after.Cpu.Temperature);
        return new DiagnosticSnapshot(utc.ToUniversalTime(), cpu, after.Memory, Array.AsReadOnly(disks),
            Array.AsReadOnly(network), Array.AsReadOnly(gpu), Array.AsReadOnly(processes));
    }

    internal static DiagnosticValue<double> Missing(string reason) => DiagnosticValue<double>.Unavailable("Indisponible : " + reason);
}
