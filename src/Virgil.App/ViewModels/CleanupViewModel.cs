using Virgil.Core.Actions;
using Virgil.Contracts.Actions;
using Virgil.App.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Virgil.Core.Cleanup;
using Virgil.Core.Reports;
using Virgil.Core.Processes;
using Virgil.Contracts;

namespace Virgil.App.ViewModels;

public partial class CleanupViewModel : ObservableObject
{
    private readonly CleanupMaintenanceModule _module;
    public ObservableCollection<CleanupGroupItem> Groups { get; } = new();
    [ObservableProperty] private string criticalConfirmationText = string.Empty;
    [ObservableProperty] private string expectedCriticalConfirmation = string.Empty;
    [ObservableProperty] private bool requiresCriticalConfirmation;

    private readonly QuarantineService? _quarantine;
    public ObservableCollection<QuarantineItemViewModel> QuarantineItems { get; } = new();
    [ObservableProperty] private bool hasExpiredQuarantine;
    [ObservableProperty] private string expiredQuarantineLabel = string.Empty;
    private readonly List<CleanupZonePreview> _lastPreview = new();
    private CleanupStorageAnalysis? _lastStorageAnalysis;
    private readonly IConfirmationService _confirmationService;
    private readonly IReportHistoryService _reportHistoryService;
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource<CleanupZoneDecision>? _activeDecision;
    private CleanupSessionReport? _lastReport;
    private bool _operationInProgress;

    private readonly IProcessRunner _processRunner;

    public CleanupViewModel(
        CleanupMaintenanceModule module,
        IProcessRunner processRunner,
        IReportHistoryService reportHistoryService,
        IConfirmationService confirmationService, QuarantineService? quarantine = null)
    {
        _confirmationService = confirmationService;
        _quarantine = quarantine;
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _reportHistoryService = reportHistoryService ?? throw new ArgumentNullException(nameof(reportHistoryService));
        Entries.Add(new CleanupTextCardItem("Lancez une analyse des zones pour afficher les fichiers temporaires eligibles. Aucune suppression ne demarre depuis le scan."));
    }

    public ObservableCollection<object> Entries { get; } = new();
    [ObservableProperty] private string statusText = "Aucune analyse nettoyage lancee.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isBusy;
    public bool IsIdle => !IsBusy;
    [ObservableProperty] private bool isAnalyzeEnabled = true;
    [ObservableProperty] private bool isLaunchCleanupEnabled;
    [ObservableProperty] private bool isAdvancedCleanupEnabled;
    [ObservableProperty] private bool isReviewManualEnabled;
    [ObservableProperty] private bool isReportButtonEnabled;
    [ObservableProperty] private bool isReturnHomeEnabled = true;
    [ObservableProperty] private bool isValidationOpen;
    [ObservableProperty] private string validationTitle = "VALIDATION REQUISE";
    [ObservableProperty] private string validationReason = string.Empty;
    [ObservableProperty] private string validationRoot = string.Empty;
    [ObservableProperty] private string validationMetrics = string.Empty;
    [ObservableProperty] private string validationEffect = string.Empty;
    [ObservableProperty] private string validationNotTouched = string.Empty;
    [ObservableProperty] private string validationWarning = string.Empty;
    [ObservableProperty] private string executeZoneLabel = "EXECUTER";
    [ObservableProperty] private bool isReportOpen;
    [ObservableProperty] private string reportText = string.Empty;
    [ObservableProperty] private VirgilCoreState miniCoreState = VirgilCoreState.Idle;
    [ObservableProperty] private VirgilCoreState validationCoreState = VirgilCoreState.Idle;
    [ObservableProperty] private VirgilCoreState reportCoreState = VirgilCoreState.Idle;

    public event Action<string>? VirgilMessageRequested;

    public event Action<VirgilCoreState, string>? VirgilStateRequested;

    public event EventHandler? ReturnHomeRequested;

    public event EventHandler? ReportPersisted;

    public void CancelActiveOperation()
    {
        _operationCancellation?.Cancel();
        CompleteDecision(CleanupZoneDecision.CancelAll);
    }

    public bool TryCloseOverlay()
    {
        if (IsReportOpen)
        {
            HideReport();
            return true;
        }

        if (IsValidationOpen)
        {
            CompleteDecision(CleanupZoneDecision.Skip);
            return true;
        }

        if (_operationInProgress)
        {
            CancelActiveOperation();
            VirgilMessageRequested?.Invoke("Annulation demandee.\nOperation en cours d'arret.");
            return true;
        }

        return false;
    }

    [RelayCommand]
    private void InitializeQuarantine() => RefreshQuarantine();

    [RelayCommand]
    private void RefreshQuarantine()
    {
        if (_quarantine is null) return;
        try
        {
            QuarantineItems.Clear();
            var now = _quarantine.UtcNow;
            foreach (var entry in _quarantine.List()) QuarantineItems.Add(new QuarantineItemViewModel(entry, now));
            var expired = QuarantineItems.Where(item => item.IsExpired).ToArray();
            HasExpiredQuarantine = expired.Length > 0;
            ExpiredQuarantineLabel = $"Effacer les éléments expirés ({expired.Length}, {FormatBytes(expired.Sum(item => item.Entry.SizeBytes))})";
        }
        catch { StatusText = "Lecture de la quarantaine impossible."; }
    }

    [RelayCommand]
    private Task RestoreQuarantineAsync() => RunQuarantineAsync(false);

    [RelayCommand]
    private Task PurgeQuarantineAsync() => RunQuarantineAsync(true);

    [RelayCommand]
    private Task PurgeExpiredQuarantineAsync() => RunQuarantineAsync(true, expiredOnly: true);

    private async Task RunQuarantineAsync(bool purge, bool expiredOnly = false)
    {
        if (_quarantine is null || !BeginOperation("Quarantaine")) return;
        try
        {
            var selection = QuarantineItems.Where(item => item.IsSelected).Select(item => item.Entry).ToArray();
            if (selection.Length == 0 && purge) selection = QuarantineItems.Select(item => item.Entry).ToArray();
            if (!expiredOnly && selection.Length == 0) { StatusText = "Sélectionnez les éléments à restaurer."; return; }
            var action = expiredOnly ? _quarantine.GetExpiredPurgeAction()
                : purge ? _quarantine.Purge(selection) : _quarantine.Restore(selection);
            if (action is null) { StatusText = "Aucun élément expiré à effacer."; return; }
            var preview = await action.PreviewAsync(_operationCancellation!.Token);
            ValidationTitle = expiredOnly ? "Effacer les éléments expirés" : preview.Title;
            ValidationReason = "Éléments sélectionnés dans la quarantaine";
            ValidationRoot = string.Join("\n", preview.Targets);
            ValidationMetrics = $"{preview.Parameters["Count"]} fichiers ({FormatBytes(long.Parse(preview.Parameters["Bytes"], System.Globalization.CultureInfo.InvariantCulture))})";
            ValidationEffect = string.Join("\n", preview.Effects);
            ValidationNotTouched = "Les éléments hors sélection restent inchangés. Aucun écrasement.";
            ValidationWarning = purge ? "Cette suppression est irréversible." : "Taille et SHA-256 vérifiés avant et après déplacement.";
            ExecuteZoneLabel = expiredOnly ? "EFFACER LES ÉLÉMENTS EXPIRÉS" : purge ? "VIDER LA QUARANTAINE MAINTENANT" : "RESTAURER";
            var decision = new TaskCompletionSource<CleanupZoneDecision>();
            _activeDecision = decision;
            IsValidationOpen = true;
            using var registration = _operationCancellation.Token.Register(() => CompleteDecision(CleanupZoneDecision.CancelAll));
            if (await decision.Task != CleanupZoneDecision.Execute) return;
            var plan = _confirmationService.Issue(preview, new ConfirmationConsent(ConfirmationLevel.Reinforced, null));
            var result = await action.ExecuteAsync(plan, _operationCancellation.Token);
            StatusText = result.WasRefused ? result.RefusalReason! : result.Succeeded ? "Quarantaine : opération terminée et vérifiée." : string.Join("\n", result.Errors);
        }
        catch (OperationCanceledException) { StatusText = "Opération annulée."; }
        catch { StatusText = "Opération de quarantaine impossible."; }
        finally { EndOperation(); RefreshQuarantine(); }
    }

    [RelayCommand]
    private Task LaunchCleanupSafeAsync() => LaunchSelectionAsync();

    [RelayCommand]
    private Task LaunchCleanupAdvancedAsync() => LaunchSelectionAsync();

    [RelayCommand]
    private void ReviewManual()
    {
        var count = _lastStorageAnalysis?.Items.Count ?? 0;
        VirgilMessageRequested?.Invoke(count == 0
            ? "Aucun element personnel volumineux detecte dans les emplacements analyses."
            : $"Elements volumineux detectes : {count}.\nIls semblent personnels ou ambigus. Virgil ne les supprimera pas automatiquement.");
    }

    [RelayCommand]
    private void ViewReport() => ShowReport();

    [RelayCommand]
    private void ReturnHome() => ReturnHomeRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ExecuteDecision() => CompleteDecision(CleanupZoneDecision.Execute);

    [RelayCommand]
    private void SkipDecision() => CompleteDecision(CleanupZoneDecision.Skip);

    [RelayCommand]
    private void CancelDecision() => CompleteDecision(CleanupZoneDecision.CancelAll);

    [RelayCommand]
    private void CloseReport() => HideReport();

    [RelayCommand]
    private async Task AnalyzeZonesAsync()
    {
        if (!BeginOperation("Analyse des zones autorisees."))
        {
            return;
        }

        try
        {
            _lastPreview.Clear();
            StatusText = "Analyse en cours.";
            RequestVirgilState(VirgilCoreState.Scanning, "NETTOYAGE");
            VirgilMessageRequested?.Invoke("Analyse des zones de nettoyage initialisee.");

            var findings = (CleanupFindings)await _module
                .ScanAsync(new Progress<CleanupProgress>(HandleProgress), _operationCancellation!.Token)
                .ConfigureAwait(true);
            _lastStorageAnalysis = findings.Analysis.Storage;
            _lastPreview.AddRange(findings.Analysis.Zones);
            Groups.Clear();
            foreach (var group in Enum.GetValues<CleanupGroup>())
                Groups.Add(new CleanupGroupItem(group, _lastPreview.Where(p => p.Definition.Group == group)));
            RenderPreviews();
            UpdateCommandState();
            StatusText = "Previsualisation disponible. Validation par zone requise.";
            RequestVirgilState(VirgilCoreState.Success, "PRET");
            AnnouncePreviewResult();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Analyse annulee.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Nettoyage interrompu.\nLes zones restantes n'ont pas ete modifiees.");
        }
        catch
        {
            StatusText = "Analyse nettoyage indisponible.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Analyse nettoyage indisponible.\nAucune action effectuee.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task LaunchSelectionAsync()
    {
        var selected = Groups.SelectMany(g => g.Rows).Where(r => r.CanSelect && r.IsSelected).Select(r => r.Preview).ToList();
        if (selected.Count == 0)
        {
            VirgilMessageRequested?.Invoke("Aucune zone eligible.\nRelancez une analyse.");
            return;
        }
        if (!BeginOperation("Nettoyage guide en attente de validation.")) return;
        var startedAt = DateTimeOffset.Now;
        var results = new List<CleanupStepResult>();
        var sessionErrors = new List<string>();
        try
        {
            var action = _module.Select(selected, new Progress<CleanupProgress>(HandleProgress));
            var preview = await action.PreviewAsync(_operationCancellation!.Token);
            ValidationTitle = preview.Title;
            ValidationReason = "Sélection complète — " + string.Join(", ", selected.Select(p => p.Definition.DisplayName).Distinct());
            ValidationRoot = string.Join("\n", preview.Targets);
            ValidationMetrics = $"Taille totale : {FormatBytes(selected.Sum(p => p.EligibleBytes))}\nNombre de fichiers : {preview.Parameters["Files"]}\nÉléments sensibles : {preview.Parameters["Sensitive"]}\n" +
                string.Join("\n", Groups.Select(g => g.Title + " : " + preview.Parameters[g.Group.ToString()]));
            ValidationEffect = string.Join("\n", preview.Effects);
            ValidationNotTouched = "Clés de registre en lecture seule. Fichiers hors sélection et racines conservés.";
            ValidationWarning = selected.Any(p => p.Definition.Id == CleanupZoneId.RecycleBin)
                ? "Corbeille : suppression irréversible via l'API Windows, confirmation renforcée obligatoire." : "Fichiers : quarantaine uniquement.";
            RequiresCriticalConfirmation = preview.RiskLevel == RiskLevel.Critical;
            CriticalConfirmationText = string.Empty;
            ExpectedCriticalConfirmation = RequiresCriticalConfirmation ? ConfirmationService.ExpectedCriticalText(preview) : string.Empty;
            if (RequiresCriticalConfirmation) ValidationWarning += "\nDossier personnel : confirmation Critical obligatoire. Recopiez le texte demandé.";
            ExecuteZoneLabel = "CONFIRMER LA SÉLECTION";
            ValidationCoreState = VirgilCoreState.SensitiveAction;
            RequestVirgilState(VirgilCoreState.SensitiveAction, "VALIDATION");
            var decision = new TaskCompletionSource<CleanupZoneDecision>();
            _activeDecision = decision;
            IsValidationOpen = true;
            VirgilMessageRequested?.Invoke("Validation requise. Sélection complète, aucun effet avant confirmation.");
            using var registration = _operationCancellation.Token.Register(() => CompleteDecision(CleanupZoneDecision.CancelAll));
            var choice = await decision.Task;
            if (choice == CleanupZoneDecision.Execute)
            {
                try
                {
                    var level = RiskConfirmationPolicy.RequiredLevel(preview.RiskLevel);
                    var plan = _confirmationService.Issue(preview, new ConfirmationConsent(level, RequiresCriticalConfirmation ? CriticalConfirmationText : null));
                    var outcome = (CleanupSelectionActionResult)await action.ExecuteAsync(plan, _operationCancellation.Token);
                    results.AddRange(outcome.Selection.Steps);
                    if (outcome.WasRefused) sessionErrors.Add(outcome.RefusalReason!);
                }
                catch (ConfirmationRefusedException) { choice = CleanupZoneDecision.CancelAll; }
            }
            if (choice != CleanupZoneDecision.Execute)
            {
                results.AddRange(selected.Select(p => choice == CleanupZoneDecision.Skip ? _module.SkipZone(p) : _module.CancelZone(p)));
                if (choice == CleanupZoneDecision.CancelAll) sessionErrors.Add("Session annulee par l'utilisateur.");
            }
        }
        catch (OperationCanceledException) { sessionErrors.Add("Session annulee."); }
        catch { sessionErrors.Add("Erreur pendant le nettoyage guide."); }
        finally
        {
            var storage = _lastStorageAnalysis;
            _lastReport = _module.CreateReport(startedAt, results, sessionErrors) with
            {
                EstimatedBytes = selected.Sum(preview => preview.EligibleBytes),
                ReviewItems = storage?.ReviewItemCount ?? 0,
                ReviewBytes = storage?.ReviewBytes ?? 0,
                ProtectedItems = storage?.ProtectedItemCount ?? 0,
                AdvancedRefused = results.Count(result => result.Zone.Classification == CleanupClassification.AdvancedCleanable && result.Status == CleanupStepStatus.Skipped),
                LockedFilesIgnored = results.Sum(result => result.ErrorFiles),
                ReparsePointsIgnored = _lastPreview.SelectMany(preview => preview.Errors).Count(error => error.Contains("reanalyse", StringComparison.OrdinalIgnoreCase)),
                RefusedPaths = _lastPreview.SelectMany(preview => preview.Errors).Where(error => error.Contains("refus", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
            await PersistReportAsync(ReportMapper.FromCleanup(_lastReport, _lastPreview.Count));
            IsReportButtonEnabled = true;
            RenderPreviews();
            EndOperation();
            CompleteSessionState(_lastReport);
            AnnounceSessionEnd(_lastReport);
            ShowReport();
        }
    }

    private async Task PersistReportAsync(ReportEntry report)
    {
        var result = await _reportHistoryService.SaveAsync(report, CancellationToken.None).ConfigureAwait(true);
        if (!result.Success || !string.IsNullOrWhiteSpace(result.ReadableError))
        {
            VirgilMessageRequested?.Invoke(result.ReadableError ?? "Historique local indisponible. Rapport conserve en memoire.");
        }

        if (result.Success)
        {
            ReportPersisted?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool BeginOperation(string status)
    {
        if (_operationInProgress)
        {
            VirgilMessageRequested?.Invoke("Operation deja en cours.");
            return false;
        }

        _operationInProgress = true;
        _operationCancellation = new CancellationTokenSource();
        StatusText = status;
        SetButtonsEnabled(false);
        IsBusy = true;
        return true;
    }

    private void EndOperation()
    {
        _operationInProgress = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        IsBusy = false;
        SetButtonsEnabled(true);
        UpdateCommandState();
    }

    private void SetButtonsEnabled(bool enabled)
    {
        IsAnalyzeEnabled = enabled;
        IsLaunchCleanupEnabled = enabled && HasSelection();
        IsAdvancedCleanupEnabled = enabled && HasSelection();
        IsReviewManualEnabled = enabled && (_lastStorageAnalysis?.Items.Count ?? 0) > 0;
        IsReportButtonEnabled = enabled && _lastReport is not null;
        IsReturnHomeEnabled = enabled;
    }

    private void UpdateCommandState()
    {
        IsLaunchCleanupEnabled = !_operationInProgress && HasSelection();
        IsAdvancedCleanupEnabled = !_operationInProgress && HasSelection();
        IsReviewManualEnabled = !_operationInProgress && (_lastStorageAnalysis?.Items.Count ?? 0) > 0;
        IsReportButtonEnabled = !_operationInProgress && _lastReport is not null;
    }

    private void HandleProgress(CleanupProgress progress)
    {
        if (progress.TotalFiles > 0)
        {
            StatusText = string.Join(" - ", new[]
            {
                $"{progress.Message} : {progress.ProcessedFiles}/{progress.TotalFiles} fichiers",
                $"{progress.DeletedFiles} mis en quarantaine",
                $"{FormatBytes(progress.DeletedBytes)} en quarantaine",
                $"{progress.SkippedFiles + progress.ErrorFiles} ignores"
            });
            return;
        }

        StatusText = progress.Percent.HasValue
            ? $"{progress.Step} - {progress.Percent.Value}%"
            : progress.Step;
    }

    private async Task<CleanupZoneDecision> AskZoneDecisionAsync(
        CleanupZonePreview preview,
        CancellationToken cancellationToken)
    {
        var decision = new TaskCompletionSource<CleanupZoneDecision>();
        _activeDecision = decision;
        ShowValidation(preview);

        using var registration = cancellationToken.Register(() => CompleteDecision(CleanupZoneDecision.CancelAll));
        return await decision.Task.ConfigureAwait(true);
    }

    private void ShowValidation(CleanupZonePreview preview)
    {
        ValidationTitle = preview.Definition.RequiresReinforcedConfirmation
            ? preview.Definition.Id == CleanupZoneId.RecycleBin
                ? "VIDER LA CORBEILLE - CONFIRMATION RENFORCEE"
                : "NETTOYAGE AVANCE - CONFIRMATION RENFORCEE"
            : "NETTOYAGE SUR - VALIDATION REQUISE";
        ValidationReason = $"Zone : {preview.Definition.DisplayName}\n{preview.Definition.Description}";
        ValidationRoot = LogicalRootLabel(preview.Definition.Id);
        ValidationMetrics = string.Join("\n", new[]
        {
            $"Taille : {FormatBytes(preview.EligibleBytes)}",
            $"Fichiers : {preview.EligibleFileCount}",
            $"Age minimal : {FormatAge(preview.Definition.MinimumAge)}",
            $"Risque : {RiskLabel(preview.Definition.RiskLevel)}"
        });
        ValidationEffect = preview.Definition.Id == CleanupZoneId.RecycleBin
            ? $"{preview.Definition.Effect}\nSuppression définitive après confirmation de cette zone uniquement."
            : string.Join("\n", CleanupActionPreviewFactory.Create(preview).Effects);
        ValidationNotTouched = preview.Definition.NotTouched;
        ValidationWarning = preview.Definition.Warning;
        ExecuteZoneLabel = preview.Definition.Id == CleanupZoneId.RecycleBin ? "VIDER LA CORBEILLE" : "NETTOYER";
        IsValidationOpen = true;
        RequestVirgilState(VirgilCoreState.SensitiveAction, "VALIDATION");
        ValidationCoreState = VirgilCoreState.SensitiveAction;
        VirgilMessageRequested?.Invoke($"Validation requise.\nZone : {preview.Definition.DisplayName.ToLowerInvariant()}.\nAucune action ne sera effectuee sans confirmation.");
    }

    private void CompleteDecision(CleanupZoneDecision decision)
    {
        if (_activeDecision is null)
        {
            return;
        }

        ValidationCoreState = VirgilCoreState.Idle;
        IsValidationOpen = false;
        var activeDecision = _activeDecision;
        _activeDecision = null;
        activeDecision.TrySetResult(decision);
    }

    private void RenderPreviews()
    {
        Entries.Clear();

        AddCategorySection("NETTOYAGE SUR", CleanupClassification.Cleanable);
        AddCategorySection("NETTOYAGE AVANCE", CleanupClassification.AdvancedCleanable);

        Entries.Add(new CleanupTextCardItem("NETTOYAGE TECHNIQUE BLOQUE\nLes acces refuses sont signales. Toute reparation exige un chemin technique allowliste, une confirmation critique et le helper eleve. Aucune commande libre takeown."));

        Entries.Add(new CleanupTextCardItem("A REVOIR MANUELLEMENT\nLes gros fichiers, dossiers, telechargements, archives, ISO, videos, photos, projets et sauvegardes restent en lecture seule. Aucun bouton supprimer."));
        if (_lastStorageAnalysis is not null)
        {
            foreach (var item in _lastStorageAnalysis.Items.Take(20))
            {
                Entries.Add(new CleanupReviewItemViewModel(item, _processRunner, message => VirgilMessageRequested?.Invoke(message)));
            }
        }

        Entries.Add(new CleanupTextCardItem("PROTEGE\nPhotos, videos, documents, projets, sauvegardes, jeux, applications, cloud, reseau, lecteurs externes et profils ambigus ne sont jamais nettoyes automatiquement."));

        if (Entries.Count == 0)
        {
            Entries.Add(new CleanupTextCardItem("Aucune zone analysee."));
        }
    }

    private void AddCategorySection(string title, CleanupClassification classification)
    {
        Entries.Add(new CleanupSectionTitleItem(title));
        foreach (var preview in _lastPreview
            .Where(preview => preview.Definition.Classification == classification)
            .OrderBy(preview => preview.Definition.DisplayOrder))
        {
            Entries.Add(new CleanupZoneItemViewModel(preview));
        }

        foreach (var preview in _lastPreview
            .Where(preview => preview.Definition.Classification == CleanupClassification.InformationOnly)
            .Where(preview => classification == CleanupClassification.AdvancedCleanable)
            .OrderBy(preview => preview.Definition.DisplayOrder))
        {
            Entries.Add(new CleanupZoneItemViewModel(preview));
        }
    }

    private void AnnouncePreviewResult()
    {
        var totalBytes = _lastPreview.Where(preview => preview.Definition.IsExecutable).Sum(preview => preview.EligibleBytes);
        var reviewCount = _lastStorageAnalysis?.Items.Count ?? 0;

        if (totalBytes == 0)
        {
            VirgilMessageRequested?.Invoke("Analyse terminee.\nAucun nettoyage necessaire.");
            return;
        }

        VirgilMessageRequested?.Invoke($"Previsualisation terminee.\n{_lastPreview.Count} zones analysees.\n{FormatBytes(totalBytes)} potentiellement recuperables.\n{reviewCount} elements personnels ou ambigus a revoir, aucune suppression proposee.");
    }

    private void AnnounceSessionEnd(CleanupSessionReport report)
    {
        if (IsCancellationReport(report))
        {
            VirgilMessageRequested?.Invoke("Nettoyage interrompu.\nLes zones restantes n'ont pas ete modifiees.");
            return;
        }

        if (HasBlockingFailure(report))
        {
            VirgilMessageRequested?.Invoke("Nettoyage interrompu.\nRapport partiel disponible.");
            return;
        }

        VirgilMessageRequested?.Invoke($"Nettoyage termine.\n{string.Join("\n", report.Results.Where(r => r.QuarantinedFiles > 0).Select(r => r.QuarantineSummary))}\nCorbeille : {FormatBytes(report.DeletedBytes)} libérés.\nRapport disponible.");
        RefreshQuarantine();
    }

    private void AnnounceZoneResult(CleanupStepResult result)
    {
        if (result.Status == CleanupStepStatus.Expired)
        {
            VirgilMessageRequested?.Invoke($"Zone expiree : {result.Zone.DisplayName}.\nRelancez l'analyse.");
            return;
        }

        VirgilMessageRequested?.Invoke(result.Zone.Id == CleanupZoneId.RecycleBin
            ? $"Corbeille : {FormatBytes(result.DeletedBytes)} libérés."
            : $"{result.QuarantineSummary}.\n{result.ErrorFiles} fichiers non traités.");
    }

    private void CompleteSessionState(CleanupSessionReport report)
    {
        StatusText = $"Rapport pret : {report.QuarantinedFiles} fichiers en quarantaine ({FormatBytes(report.QuarantinedBytes)}), corbeille : {FormatBytes(report.DeletedBytes)} libérés.";

        if (IsCancellationReport(report))
        {
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            return;
        }

        if (HasBlockingFailure(report))
        {
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            return;
        }

        if (report.Errors.Count > 0 || report.ErrorFiles > 0)
        {
            RequestVirgilState(VirgilCoreState.Warning, "ATTENTION");
            return;
        }

        RequestVirgilState(VirgilCoreState.Success, "TERMINE");
    }

    private void ShowReport()
    {
        if (_lastReport is null)
        {
            VirgilMessageRequested?.Invoke("Aucun rapport nettoyage disponible.");
            return;
        }

        ReportText = FormatReport(_lastReport, _lastPreview.Count);
        IsReportOpen = true;
        ReportCoreState = IsCancellationReport(_lastReport) || _lastReport.Errors.Count > 0 || _lastReport.ErrorFiles > 0
            ? VirgilCoreState.Warning
            : VirgilCoreState.Success;
    }

    private void HideReport()
    {
        ReportCoreState = VirgilCoreState.Idle;
        IsReportOpen = false;
    }

    private static string FormatReport(CleanupSessionReport report, int analyzedZones)
    {
        var ignoredFiles = report.Results.Sum(result => result.SkippedFiles);
        var partialErrors = report.Errors.Count + report.Results.Sum(result => result.Errors.Count);
        var builder = new StringBuilder();
        builder.AppendLine($"Date : {report.StartedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Duree : {report.Duration.TotalSeconds:0.0} s");
        builder.AppendLine($"Zones analysees : {analyzedZones}");
        builder.AppendLine($"Zones executees : {CountExecutedZones(report)}");
        builder.AppendLine($"Zones passees : {report.SkippedZones}");
        builder.AppendLine($"Annulation : {(IsCancellationReport(report) ? "oui" : "non")}");
        builder.AppendLine($"Fichiers mis en quarantaine : {report.QuarantinedFiles} ({FormatBytes(report.QuarantinedBytes)})");
        foreach (var step in report.Results.Where(r => r.QuarantinedFiles > 0)) builder.AppendLine(step.QuarantineSummary);
        builder.AppendLine($"Fichiers supprimés de la corbeille : {report.DeletedFiles}");
        builder.AppendLine($"Volume libere : {FormatBytes(report.DeletedBytes)}");
        builder.AppendLine($"Volume estime : {FormatBytes(report.EstimatedBytes)}");
        builder.AppendLine($"Elements a revoir : {report.ReviewItems} - {FormatBytes(report.ReviewBytes)}");
        builder.AppendLine($"Elements proteges : {report.ProtectedItems}");
        builder.AppendLine($"Zones avancees refusees : {report.AdvancedRefused}");
        builder.AppendLine($"Points de reanalyse ignores : {report.ReparsePointsIgnored}");
        builder.AppendLine($"Redemarrage conseille : {(report.RestartRecommended ? "oui" : "non")}");
        builder.AppendLine("Aucun fichier personnel supprime automatiquement.");
        builder.AppendLine($"Fichiers verrouilles : {report.ErrorFiles}");
        builder.AppendLine($"Fichiers ignores : {ignoredFiles}");
        builder.AppendLine($"Erreurs partielles : {partialErrors}");
        builder.AppendLine();

        foreach (var result in report.Results)
        {
            builder.AppendLine($"{result.Zone.DisplayName} : {StatusLabel(result.Status)}");
            builder.AppendLine(result.Zone.Id == CleanupZoneId.RecycleBin ? $"  Corbeille : {result.DeletedFiles} - {FormatBytes(result.DeletedBytes)}" : result.QuarantineSummary);
            builder.AppendLine($"  Ignores : {result.SkippedFiles} - Erreurs : {result.ErrorFiles}");

            foreach (var error in result.Errors.Take(3))
            {
                builder.AppendLine($"  Erreur : {error}");
            }
        }

        if (report.Errors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Erreurs session :");

            foreach (var error in report.Errors.Take(5))
            {
                builder.AppendLine($"- {error}");
            }
        }

        return builder.ToString();
    }

    private void RequestVirgilState(VirgilCoreState state, string label)
    {
        MiniCoreState = state;
        VirgilStateRequested?.Invoke(state, label);
    }

    private static int CountExecutedZones(CleanupSessionReport report)
    {
        return report.Results.Count(result => result.Status is
            CleanupStepStatus.Completed or
            CleanupStepStatus.PartialFailure or
            CleanupStepStatus.Expired or
            CleanupStepStatus.Failed);
    }

    private static bool IsCancellationReport(CleanupSessionReport report)
    {
        return report.CancelledZones > 0 ||
            report.Errors.Any(error => error.Contains("annulee", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasBlockingFailure(CleanupSessionReport report)
    {
        return report.Results.Count == 0 &&
            report.Errors.Any(error => error.Contains("Erreur", StringComparison.OrdinalIgnoreCase));
    }

    private static string StatusLabel(CleanupStepStatus status)
    {
        return status switch
        {
            CleanupStepStatus.Completed => "terminee",
            CleanupStepStatus.Skipped => "passee",
            CleanupStepStatus.Cancelled => "annulee",
            CleanupStepStatus.Expired => "preview expiree",
            CleanupStepStatus.PartialFailure => "partielle",
            _ => "echec"
        };
    }

    internal static string LogicalRootLabel(CleanupZoneId zoneId)
    {
        return zoneId switch
        {
            CleanupZoneId.UserTemporaryFiles => "%TEMP% utilisateur",
            CleanupZoneId.UserCrashDumps => "%LOCALAPPDATA%\\CrashDumps",
            CleanupZoneId.DirectXShaderCache => "%LOCALAPPDATA%\\D3DSCache",
            _ => "Zone autorisee"
        };
    }

    private bool HasSelection() => Groups.SelectMany(g => g.Rows).Any(r => r.CanSelect);

    private bool HasExecutable(CleanupClassification classification)
    {
        return _lastPreview.Any(preview => preview.Definition.Classification == classification && preview.Definition.IsExecutable && preview.HasEligibleCandidates);
    }

    internal static string ClassificationLabel(CleanupClassification classification)
    {
        return classification switch
        {
            CleanupClassification.Cleanable => "nettoyable sur",
            CleanupClassification.AdvancedCleanable => "nettoyable avance",
            CleanupClassification.ReviewOnly => "a revoir manuellement",
            CleanupClassification.Protected => "protege",
            _ => "information seulement"
        };
    }

    internal static string RiskLabel(CleanupRiskLevel risk)
    {
        return risk switch
        {
            CleanupRiskLevel.Low => "faible",
            CleanupRiskLevel.Medium => "moyen",
            _ => "eleve"
        };
    }

    internal static string FormatAge(TimeSpan age)
    {
        return age.TotalDays >= 1 ? $"{age.TotalDays:0} jours" : $"{age.TotalHours:0} heures";
    }

    internal static string FormatBytes(long bytes)
    {
        return ScanRules.FormatBytes(bytes);
    }

    private enum CleanupZoneDecision
    {
        Execute,
        Skip,
        CancelAll
    }
}
