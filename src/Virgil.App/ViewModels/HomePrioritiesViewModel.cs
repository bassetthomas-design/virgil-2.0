using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Virgil.Core.Processes;
using Virgil.Core.Status;

namespace Virgil.App.ViewModels;

public sealed partial class HomePrioritiesViewModel : ObservableObject, IDisposable
{
    private readonly ISystemStatusStore _store;
    private readonly IProcessRunner _runner;
    private readonly TimeProvider _clock;
    private readonly SynchronizationContext? _context;
    private bool _disposed;
    public HomePrioritiesViewModel(ISystemStatusStore store, IProcessRunner runner, TimeProvider clock)
    {
        _store = store; _runner = runner; _clock = clock; _context = SynchronizationContext.Current;
        _store.Changed += StoreChanged; Refresh();
    }
    public ObservableCollection<HomePriorityItem> Items { get; } = new();
    public ObservableCollection<string> Resolved { get; } = new();
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string measuredAt = string.Empty;
    [ObservableProperty] private string tileValue = string.Empty;
    [ObservableProperty] private string tileDetail = string.Empty;
    public event Action<string>? NavigationRequested;
    private void StoreChanged(object? sender, EventArgs args)
    {
        if (_context is null || SynchronizationContext.Current == _context) Refresh();
        else _context.Post(_ => { if (!_disposed) Refresh(); }, null);
    }
    private void Refresh()
    {
        var measure = _store.LastPriorityPoints;
        Items.Clear();
        foreach (var point in measure?.Value ?? []) Items.Add(HomePriorityCatalog.For(point, _clock.LocalTimeZone));
        Resolved.Clear();
        foreach (var point in _store.LastResolvedPoints) Resolved.Add(HomePriorityCatalog.ResolvedText(point, _clock.LocalTimeZone));
        StatusText = measure is null ? "Aucune analyse récente : lancez un scan pour voir les priorités."
            : Items.Count == 0 ? "Aucun point prioritaire au dernier contrôle." : string.Empty;
        MeasuredAt = measure is null ? string.Empty : "Mesuré à " + TimeZoneInfo.ConvertTime(measure.CapturedAt, _clock.LocalTimeZone)
            .ToString("HH:mm", CultureInfo.GetCultureInfo("fr-FR"));
        TileValue = measure is null ? "--" : Items.Count.ToString(CultureInfo.InvariantCulture);
        TileDetail = measure is null ? "ANALYSE À LANCER" : (Items.Count switch
        {
            0 => "AUCUNE PRIORITÉ DÉTECTÉE",
            1 => "1 POINT À VÉRIFIER",
            _ => $"{TileValue} POINTS À VÉRIFIER"
        }) + " · " + MeasuredAt;
    }
    [RelayCommand]
    private void ShowAll() => NavigationRequested?.Invoke("Priorités");
    [RelayCommand]
    private async Task OpenAsync(HomePriorityItem? item)
    {
        if (item is null || !Items.Contains(item)) return;
        if (item.Destination == "ms-settings:backup")
        {
            try
            {
                var result = await _runner.LaunchAsync(new ProcessLaunchRequest("ms-settings:backup", [], UseShellExecute: true), default);
                StatusText = !result.Started || result.LaunchError is not null
                    ? "Le réglage Sauvegarde Windows n'a pas pu être ouvert." : string.Empty;
            }
            catch (Exception) { StatusText = "Le réglage Sauvegarde Windows n'a pas pu être ouvert."; }
        }
        else NavigationRequested?.Invoke(item.Destination);
    }
    public void Dispose()
    {
        _disposed = true; _store.Changed -= StoreChanged;
    }
}
