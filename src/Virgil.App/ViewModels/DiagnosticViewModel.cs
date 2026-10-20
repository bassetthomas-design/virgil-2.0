using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Diagnostic;

namespace Virgil.App.ViewModels;

public sealed class DiagnosticViewModel : ObservableObject
{
    private readonly DiagnosticModule _module;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CancellationTokenSource? _presentation;
    private bool _visible, _minimized;
    private DiagnosticSnapshot? _snapshot;
    private string _status = "Prêt à observer.";
    private DiagnosticProcessSort _sort = DiagnosticProcessSort.Cpu;
    private bool _descending = true;
    private IReadOnlyList<DiagnosticProcessRow> _processes = Array.Empty<DiagnosticProcessRow>();
    public DiagnosticViewModel(DiagnosticModule module, TimeProvider clock)
    {
        _module = module;
        _clock = clock;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }
    public IAsyncRelayCommand RefreshCommand { get; }
    public Task PresentationTask { get; private set; } = Task.CompletedTask;
    public bool IsRefreshing { get; private set; }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public DiagnosticSnapshot? Snapshot => _snapshot;
    public IReadOnlyList<DiagnosticProcessRow> Processes { get => _processes; private set => SetProperty(ref _processes, value); }
    public IReadOnlyList<string> CpuDetails => _snapshot is null ? Array.Empty<string>() : new[]
    {
        "Charge totale : " + DiagnosticDisplay.Percent(_snapshot.Cpu.TotalPercent),
        "Fréquence actuelle : " + DiagnosticDisplay.Number(_snapshot.Cpu.CurrentMhz, "MHz"),
        "Fréquence nominale : " + DiagnosticDisplay.Number(_snapshot.Cpu.NominalMhz, "MHz"),
        "Température : " + DiagnosticDisplay.Number(_snapshot.Cpu.TemperatureCelsius, "°C")
    }.Concat(_snapshot.Cpu.CorePercent.Select((v, i) => $"Cœur logique {i + 1} : {DiagnosticDisplay.Percent(v)}")).ToArray();
    public IReadOnlyList<string> MemoryDetails => _snapshot is null ? Array.Empty<string>() : new[]
    {
        "Totale : " + DiagnosticDisplay.Bytes(_snapshot.Memory.TotalBytes),
        "Utilisée : " + DiagnosticDisplay.Bytes(_snapshot.Memory.UsedBytes),
        "En cache : " + DiagnosticDisplay.Bytes(_snapshot.Memory.CachedBytes),
        "Compressée : " + DiagnosticDisplay.Bytes(_snapshot.Memory.CompressedBytes),
        "Disponible : " + DiagnosticDisplay.Bytes(_snapshot.Memory.AvailableBytes),
        "Engagée : " + DiagnosticDisplay.Bytes(_snapshot.Memory.CommittedBytes)
    };
    public IReadOnlyList<string> DiskDetails => _snapshot?.Disks.Select(d => $"{d.Kind} {d.Id} — " +
        $"Lecture : {DiagnosticDisplay.Rate(d.ReadBytesPerSecond)} ; écriture : {DiagnosticDisplay.Rate(d.WriteBytesPerSecond)} ; " +
        $"réponse : {DiagnosticDisplay.Number(d.ResponseMilliseconds, "ms")} ; total : {DiagnosticDisplay.Bytes(d.TotalBytes)} ; " +
        $"libre : {DiagnosticDisplay.Bytes(d.FreeBytes)} ; santé : {DiagnosticDisplay.Text(d.Health)}").ToArray() ?? Array.Empty<string>();
    public IReadOnlyList<string> NetworkDetails => _snapshot?.Network.Select(n => $"{n.Id} ({n.Type}) — " +
        $"Envoi : {DiagnosticDisplay.Rate(n.SendBytesPerSecond)} ; réception : {DiagnosticDisplay.Rate(n.ReceiveBytesPerSecond)} ; " +
        $"lien : {DiagnosticDisplay.Number(n.LinkBitsPerSecond, "Mbit/s", 1_000_000)}").ToArray() ?? Array.Empty<string>();
    public IReadOnlyList<string> GpuDetails => _snapshot is null ? Array.Empty<string>() : _snapshot.Gpu.Count == 0
        ? new[] { "Indisponible : cartes graphiques et compteurs GPU non exposés par Windows" }
        : _snapshot.Gpu.Select(g => $"{g.Id} — Charge : {DiagnosticDisplay.Percent(g.LoadPercent)} ; " +
            $"VRAM dédiée utilisée : {DiagnosticDisplay.Bytes(g.DedicatedUsedBytes)} ; totale : {DiagnosticDisplay.Bytes(g.DedicatedTotalBytes)} ; " +
            $"température : {DiagnosticDisplay.Number(g.TemperatureCelsius, "°C")}").ToArray();

    public void SetPresentation(bool visible, bool minimized)
    {
        _visible = visible;
        _minimized = minimized;
        if (!_visible || _minimized)
        {
            _presentation?.Cancel();
            _presentation = null;
            return;
        }
        if (_presentation is not null) return;
        var session = new CancellationTokenSource();
        _presentation = session;
        PresentationTask = RunPresentationAsync(session, PresentationTask);
    }
    private async Task RunPresentationAsync(CancellationTokenSource session, Task previous)
    {
        try
        {
            // Reprise : attendre la fin effective de la lecture annulée avant de relire.
            await previous;
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), _clock);
            await RefreshAsync();
            while (await timer.WaitForNextTickAsync(session.Token)) await RefreshAsync();
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        finally { session.Dispose(); }
    }
    public async Task RefreshAsync()
    {
        var session = _presentation;
        if (session is null || session.IsCancellationRequested || !_visible || _minimized) return;
        var token = session.Token;
        if (!await _refreshGate.WaitAsync(0)) return;
        try
        {
            IsRefreshing = true;
            OnPropertyChanged(nameof(IsRefreshing));
            Status = "Mesure en cours…";
            var findings = (DiagnosticFindings)await _module.ScanAsync(token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(session, _presentation)) return;
            _snapshot = findings.Snapshot;
            Status = $"Mesuré à {_snapshot.CapturedAtUtc.ToLocalTime():HH:mm:ss} — lecture seule";
            foreach (var name in new[] { nameof(Snapshot), nameof(CpuDetails), nameof(MemoryDetails), nameof(DiskDetails), nameof(NetworkDetails), nameof(GpuDetails) })
                OnPropertyChanged(name);
            ApplySort();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Status = "Observation arrêtée."; }
        catch (Exception) { Status = "Indisponible : lecture Windows impossible. Utilisez Actualiser pour réessayer."; }
        finally
        {
            IsRefreshing = false;
            OnPropertyChanged(nameof(IsRefreshing));
            _refreshGate.Release();
        }
    }
    public void Sort(DiagnosticProcessSort sort)
    {
        _descending = _sort == sort ? !_descending : sort != DiagnosticProcessSort.Name;
        _sort = sort;
        ApplySort();
    }
    private void ApplySort() => Processes = _snapshot is null ? Array.Empty<DiagnosticProcessRow>()
        : DiagnosticProcessOrdering.Sort(_snapshot.Processes, _sort, _descending).Select(p => new DiagnosticProcessRow(p)).ToArray();
}

public sealed class DiagnosticProcessRow(ProcessDiagnostic process)
{
    public string Name => process.Name;
    public int Pid => process.Pid;
    public string Cpu => DiagnosticDisplay.Percent(process.CpuPercent);
    public string Memory => DiagnosticDisplay.Bytes(process.PrivateBytes);
    public string WorkingSet => DiagnosticDisplay.Bytes(process.WorkingSetBytes);
    public string Disk => DiagnosticDisplay.Rate(process.DiskBytesPerSecond);
    public string Network => process.NetworkConnections.IsAvailable ? process.NetworkConnections.Value.ToString(CultureInfo.GetCultureInfo("fr-FR")) : process.NetworkConnections.Reason!;
    public string Publisher => DiagnosticDisplay.Text(process.Publisher);
    public string Path => DiagnosticDisplay.Text(process.Path);
    public string Explanation => process.Explanation;
}
public static class DiagnosticDisplay
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    public static string Number(DiagnosticValue<double> value, string unit, double divisor = 1) => value.IsAvailable
        ? (value.Value / divisor).ToString("0.##", French) + " " + unit : value.Reason!;
    public static string Percent(DiagnosticValue<double> value) => Number(value, "%");
    public static string Rate(DiagnosticValue<double> value) => Number(value, "Mo/s", 1024 * 1024);
    public static string Bytes(DiagnosticValue<double> value) => !value.IsAvailable ? value.Reason!
        : value.Value >= 1024 * 1024 * 1024 ? Number(value, "Go", 1024 * 1024 * 1024)
        : Number(value, "Mo", 1024 * 1024);
    public static string Text(DiagnosticValue<string> value) => value.IsAvailable ? value.Value : value.Reason!;
}
