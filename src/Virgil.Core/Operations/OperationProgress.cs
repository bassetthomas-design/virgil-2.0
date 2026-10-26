namespace Virgil.Core.Operations;

/// <summary>Progression commune, sans dépendance à une interface graphique.</summary>
public sealed record OperationProgress(string Step, int Index = 0, int Count = 0, double? Percent = null, string? Message = null);

public interface IOperationProgress : IProgress<OperationProgress> { }

/// <summary>Regroupe les notifications intermédiaires à dix par seconde, sans perdre la dernière étape.
/// Le résultat final est toujours transmis immédiatement.</summary>
public sealed class ThrottledOperationProgress(IProgress<OperationProgress> target, TimeProvider? clock = null) : IOperationProgress, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _sync = new();
    private long? _lastTimestamp;
    private ITimer? _timer;
    private OperationProgress? _pending;
    private bool _disposed;

    public void Report(OperationProgress value)
    {
        lock (_sync)
        {
            if (_disposed) return;
            var now = _clock.GetTimestamp();
            var final = value.Percent >= 100 || value.Count > 0 && value.Index >= value.Count;
            if (!final && _lastTimestamp is { } last && _clock.GetElapsedTime(last, now) is var elapsed && elapsed < Interval)
            {
                _pending = value;
                _timer ??= _clock.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _timer.Change(Interval - elapsed, Timeout.InfiniteTimeSpan);
                return;
            }
            _pending = null;
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _lastTimestamp = now;
            target.Report(value);
        }
    }

    private void Flush()
    {
        lock (_sync)
        {
            if (_disposed || _pending is not { } value) return;
            var now = _clock.GetTimestamp();
            if (_lastTimestamp is { } last && _clock.GetElapsedTime(last, now) is var elapsed && elapsed < Interval)
            {
                _timer?.Change(Interval - elapsed, Timeout.InfiniteTimeSpan);
                return;
            }
            _pending = null;
            _lastTimestamp = now;
            target.Report(value);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _pending = null;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
