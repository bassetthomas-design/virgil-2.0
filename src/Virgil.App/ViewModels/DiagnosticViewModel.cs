using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Virgil.Contracts.Diagnostic;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Diagnostic;

namespace Virgil.App.ViewModels;

public sealed partial class DiagnosticViewModel : ObservableObject
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
    public DiagnosticViewModel(DiagnosticModule module, TimeProvider clock, IConfirmationService? confirmation = null)
    {
        _module = module;
        _clock = clock;
        _confirmation = confirmation;
        RefreshCommand = new AsyncRelayCommand(() => { _module.InvalidateSignals(); return RefreshAsync(); });
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
    }.Concat(_snapshot.Cpu.CorePercent.Select((v, i) => v.IsAvailable
        ? $"Cœur logique {i + 1} : {DiagnosticDisplay.Percent(v)}"
        : "Charge par cœur : " + v.Reason).Distinct()).ToArray();
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
            CancelConfirmation();
            _presentation?.Cancel();
            _presentation = null;
            return;
        }
        if (_presentation is not null) return;
        var session = new CancellationTokenSource();
        _module.InvalidateSignals();
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
            ApplyFindings(findings);
            Status = $"Mesuré à {_snapshot.CapturedAtUtc.ToLocalTime():HH:mm:ss} — aucune action sans votre validation";
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
    private readonly IConfirmationService? _confirmation;
    private IReadOnlyList<DiagnosticFindingRow> _findings = Array.Empty<DiagnosticFindingRow>();
    private IReadOnlyList<string> _top3 = Array.Empty<string>();
    private IReadOnlyList<string> _unavailable = Array.Empty<string>();
    private string _health = "Note non calculée";
    private IMaintenanceAction? _pending;
    private ActionPreview? _preview;
    private CancellationTokenSource? _actionCancellation;
    [ObservableProperty] private bool isValidationOpen;
    [ObservableProperty] private string validationTitle = string.Empty;
    [ObservableProperty] private string validationDetails = string.Empty;
    [ObservableProperty] private string validationSafety = string.Empty;
    public string Health => _health;
    public IReadOnlyList<string> Top3 => _top3;
    public string Top3Caption => _top3.Count == 0 ? "Aucun problème détecté dans les signaux disponibles." : "Top 3 des problèmes";
    public IReadOnlyList<string> Unavailable => _unavailable;
    public IReadOnlyList<DiagnosticFindingRow> Findings => _findings;
    public event Action<DiagnosticSuggestion>? NavigationRequested;
    public void ApplyFindings(DiagnosticFindings findings)
    {
        _health = $"Note de santé : {findings.Score.Value}/100" + (findings.Score.IsPartial ? " — partielle" : "");
        _top3 = findings.Top3.Select(i => i.Title + " : " + i.Description).ToArray();
        _unavailable = findings.Unavailable;
        var actions = _module.GetActions(findings);
        _findings = findings.Issues.Select(issue => new DiagnosticFindingRow(issue, actions.Where(a =>
            a is DiagnosticCloseAction close && issue.Process?.ProcessId == close.ProcessId ||
            a is DiagnosticNavigationAction nav && issue.Suggestion == nav.Destination).ToArray(), PrepareActionAsync)).ToArray();
        foreach (var name in new[] { nameof(Health), nameof(Top3), nameof(Top3Caption), nameof(Unavailable), nameof(Findings) }) OnPropertyChanged(name);
    }
    public async Task PrepareActionAsync(IMaintenanceAction action)
    {
        if (IsValidationOpen || _actionCancellation is not null) return;
        try
        {
            _preview = await action.PreviewAsync(CancellationToken.None);
            _pending = action;
            ValidationTitle = _preview.Title;
            ValidationDetails = string.Join("\n", _preview.Targets.Concat(_preview.Parameters.Select(p => p.Key + " : " + p.Value)).Concat(_preview.Effects));
            ValidationSafety = action.RiskLevel == RiskLevel.Sensitive
                ? "Confirmation renforcée : fermeture forcée, perte possible de données non sauvegardées. Aucun autre processus ne sera fermé."
                : "Validation simple. Aucune fermeture forcée automatique. Aucun point de restauration requis pour un processus.";
            IsValidationOpen = true;
        }
        catch { Status = "Impossible de préparer cette action. Aucune action effectuée."; }
    }
    [RelayCommand]
    private async Task ExecuteDecisionAsync()
    {
        var action = _pending;
        var preview = _preview;
        if (!IsValidationOpen || action is null || preview is null || _confirmation is null) return;
        IsValidationOpen = false;
        _pending = null;
        using var cancellation = new CancellationTokenSource();
        _actionCancellation = cancellation;
        try
        {
            var plan = _confirmation.Issue(preview, new ConfirmationConsent(RiskConfirmationPolicy.RequiredLevel(preview.RiskLevel), null));
            var result = await action.ExecuteAsync(plan, cancellation.Token);
            Status = result.WasRefused ? "Action refusée : " + result.RefusalReason
                : !result.Succeeded ? "Action incomplète : " + string.Join(" ; ", result.Errors.Concat(result.Effects))
                : string.Join(" ; ", result.Effects);
            if (result.Succeeded && action is DiagnosticNavigationAction navigation) NavigationRequested?.Invoke(navigation.Destination);
        }
        catch (OperationCanceledException) { Status = "Action annulée."; }
        catch (ConfirmationRefusedException) { Status = "Validation insuffisante. Aucune action effectuée."; }
        catch { Status = "Action impossible. Vérifiez l'état du programme puis actualisez."; }
        finally { _actionCancellation = null; _preview = null; }
    }
    [RelayCommand]
    public void CancelConfirmation()
    {
        IsValidationOpen = false;
        _pending = null;
        _preview = null;
        _actionCancellation?.Cancel();
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

public sealed class DiagnosticFindingRow
{
    public DiagnosticFindingRow(DiagnosticIssue issue, IReadOnlyList<IMaintenanceAction> actions, Func<IMaintenanceAction, Task> prepare)
    {
        Title = issue.Title;
        Description = issue.Description;
        Recommendation = issue.Suggestion switch
        {
            DiagnosticSuggestion.None when issue.Category == "disk" => "Action proposée : sauvegardez vos fichiers et faites contrôler le disque.",
            DiagnosticSuggestion.None => "Action proposée : vérifiez la ventilation et réduisez la charge.",
            DiagnosticSuggestion.StartupLater => "Modification du démarrage disponible plus tard (sauvegarde et point de restauration requis)",
            _ => "Choisissez une proposition ci-dessous, puis validez son aperçu."
        };
        Actions = actions.Select(a => new DiagnosticActionRow(a, prepare)).ToArray();
    }
    public string Title { get; }
    public string Description { get; }
    public string Recommendation { get; }
    public IReadOnlyList<DiagnosticActionRow> Actions { get; }
}
public sealed class DiagnosticActionRow(IMaintenanceAction action, Func<IMaintenanceAction, Task> prepare)
{
    public string Title => action is DiagnosticCloseAction close ? close.IsForced ? "Fermer de force…" : "Fermer proprement…"
        : action is DiagnosticNavigationAction nav && nav.Destination == DiagnosticSuggestion.Cleanup ? "Nettoyer…" : "Mettre à jour…";
    public IAsyncRelayCommand PrepareCommand { get; } = new AsyncRelayCommand(() => prepare(action));
}
