namespace Virgil.Core.Operations;

/// <summary>Progression commune, sans dépendance à une interface graphique.</summary>
public sealed record OperationProgress(string Step, int Index = 0, int Count = 0, double? Percent = null, string? Message = null);

public interface IOperationProgress : IProgress<OperationProgress> { }

/// <summary>Limite les notifications intermédiaires à dix par seconde. Le résultat final est toujours transmis.</summary>
public sealed class ThrottledOperationProgress(IProgress<OperationProgress> target, TimeProvider? clock = null) : IOperationProgress
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _sync = new();
    private long? _lastTimestamp;

    public void Report(OperationProgress value)
    {
        lock (_sync)
        {
            var now = _clock.GetTimestamp();
            var final = value.Percent >= 100 || value.Count > 0 && value.Index >= value.Count;
            if (!final && _lastTimestamp is { } last && _clock.GetElapsedTime(last, now) < TimeSpan.FromMilliseconds(100)) return;
            _lastTimestamp = now;
            target.Report(value);
        }
    }
}
