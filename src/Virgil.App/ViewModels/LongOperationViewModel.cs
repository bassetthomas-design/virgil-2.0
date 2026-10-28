using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Virgil.Core.Operations;

namespace Virgil.App.ViewModels;

/// <summary>État commun ; le propriétaire conserve le jeton et attend la fin des étapes sûres.</summary>
public sealed partial class LongOperationViewModel : ObservableObject
{
    private readonly Action _cancel;
    private readonly TimeProvider _clock;
    private ITimer? _timer;
    private ThrottledOperationProgress? _progress;
    private SynchronizationContext? _context;
    private long _started;
    private long _generation;
    private bool _immediate;
    public LongOperationViewModel(Action cancel, TimeProvider? clock = null)
    { _cancel = cancel; _clock = clock ?? TimeProvider.System; }
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool isVisible;
    [ObservableProperty] private bool isCancelling;
    [ObservableProperty] private bool isNonInterruptible;
    [ObservableProperty] private string step = string.Empty;
    [ObservableProperty] private double percent;
    [ObservableProperty] private bool isIndeterminate = true;
    [ObservableProperty] private string timing = string.Empty;
    public bool CanCancel => IsRunning && !IsCancelling;
    partial void OnIsRunningChanged(bool value) { OnPropertyChanged(nameof(CanCancel)); CancelCommand.NotifyCanExecuteChanged(); }
    partial void OnIsCancellingChanged(bool value) { OnPropertyChanged(nameof(CanCancel)); CancelCommand.NotifyCanExecuteChanged(); }
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        AcknowledgeCancellation();
        _cancel();
    }
    public void AcknowledgeCancellation()
    {
        if (!IsRunning || IsCancelling) return;
        IsCancelling = true;
        IsVisible = true;
        Step = IsNonInterruptible
            ? "Annulation demandée — l’étape en cours se terminé, rien d’autre ne sera lancé"
            : "Annulation en cours…";
    }
    public void Start(string label, bool immediately = false, bool nonInterruptible = false)
    {
        _timer?.Dispose(); _progress?.Dispose();
        var generation = ++_generation;
        _context = SynchronizationContext.Current;
        _started = _clock.GetTimestamp(); _immediate = immediately;
        IsCancelling = false; IsNonInterruptible = nonInterruptible;
        Step = label; Percent = 0; IsIndeterminate = true; IsRunning = true; IsVisible = immediately;
        _progress = new ThrottledOperationProgress(new CallbackProgress(value => Dispatch(() =>
        {
            if (generation != _generation || !IsRunning || IsCancelling) return;
            Step = string.IsNullOrWhiteSpace(value.Message) ? value.Step : value.Message;
            var percent = value.Percent ?? (value.Count > 0 ? 100d * value.Index / value.Count : (double?)null);
            IsIndeterminate = !percent.HasValue; Percent = Math.Clamp(percent ?? 0, 0, 100);
            UpdateTiming();
        })), _clock);
        UpdateTiming();
        _timer = _clock.CreateTimer(_ => Dispatch(() =>
        { if (generation == _generation && IsRunning) UpdateTiming(); }), null,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
    }
    public void Report(OperationProgress progress) => _progress?.Report(progress);
    public void Complete(string result)
    {
        _timer?.Dispose(); _timer = null; _progress?.Dispose(); _progress = null;
        IsRunning = false; IsVisible = false; Step = result; UpdateTiming();
    }
    private void Dispatch(Action action)
    { if (_context is null || SynchronizationContext.Current == _context) action(); else _context.Post(_ => action(), null); }
    private void UpdateTiming()
    {
        var elapsed = _clock.GetElapsedTime(_started);
        if (IsRunning) IsVisible = _immediate || IsCancelling || elapsed >= TimeSpan.FromSeconds(2);
        Timing = $"Écoulé : {elapsed:mm\\:ss}";
        if (IsRunning && !IsIndeterminate && Percent > 0 && Percent < 100)
        {
            var remaining = TimeSpan.FromSeconds(Math.Min(86400, elapsed.TotalSeconds * (100 - Percent) / Percent));
            Timing += $" · Temps restant estimé : {remaining:mm\\:ss}";
        }
    }
    private sealed class CallbackProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    { public void Report(OperationProgress value) => report(value); }
}
