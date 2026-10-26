using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using Virgil.Core.Operations;

namespace Virgil.App.ViewModels;

/// <summary>Reusable presentation state; cancellation remains owned by the caller.</summary>
public sealed partial class LongOperationViewModel : ObservableObject
{
    private readonly Stopwatch _elapsed = new();
    private readonly Action _cancel;
    private Timer? _timer;
    private SynchronizationContext? _context;
    public LongOperationViewModel(Action cancel) => _cancel = cancel;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private string step = string.Empty;
    [ObservableProperty] private double percent;
    [ObservableProperty] private bool isIndeterminate = true;
    [ObservableProperty] private string timing = string.Empty;
    [RelayCommand] private void Cancel() { Step = "Annulation en cours…"; _cancel(); }
    public void Start(string label)
    {
        _context = SynchronizationContext.Current;
        _elapsed.Restart(); Step = label; Percent = 0; IsIndeterminate = true; IsRunning = true;
        UpdateTiming();
        _timer?.Dispose();
        _timer = new Timer(_ =>
        {
            if (_context is not null) _context.Post(_ => { if (IsRunning) UpdateTiming(); }, null);
        }, null, 1000, 1000);
    }
    public void Report(OperationProgress progress)
    {
        if (!IsRunning) return;
        Step = string.IsNullOrWhiteSpace(progress.Message) ? progress.Step : progress.Message;
        var value = progress.Percent ?? (progress.Count > 0 ? 100d * progress.Index / progress.Count : (double?)null);
        IsIndeterminate = !value.HasValue;
        Percent = Math.Clamp(value ?? 0, 0, 100);
        UpdateTiming();
    }
    public void Complete(string result)
    {
        _timer?.Dispose(); _timer = null; _elapsed.Stop(); IsRunning = false; Step = result; UpdateTiming();
    }
    private void UpdateTiming()
    {
        Timing = $"Écoulé : {_elapsed.Elapsed:mm\\:ss}";
        if (IsRunning && !IsIndeterminate && Percent > 0 && Percent < 100)
        {
            var remaining = TimeSpan.FromSeconds(Math.Min(86400, _elapsed.Elapsed.TotalSeconds * (100 - Percent) / Percent));
            Timing += $" · Temps restant estimé : {remaining:mm\\:ss}";
        }
    }
}
