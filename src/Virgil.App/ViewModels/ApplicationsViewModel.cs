using Virgil.Core.Actions;
using Virgil.Contracts.Actions;
using Virgil.App.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Virgil.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Virgil.Core.Applications;
using Virgil.Core.Reports;
using Virgil.Core.Processes;
using Virgil.Core.Scanning;
using Virgil.Contracts;
using Virgil.Contracts.Applications;

namespace Virgil.App.ViewModels;

public partial class ApplicationsViewModel : ObservableObject
{
    private readonly IConfirmationService _confirmationService;
    private readonly IApplicationInventoryService _inventoryService;
    private readonly ApplicationUninstallService _uninstallService;
    private readonly List<InstalledApplication> _applications = new();
    private readonly IReportHistoryService _reportHistoryService;
    private readonly IApplicationIconLoader? _iconLoader;
    private readonly HashSet<string> _selectedIds = new(StringComparer.Ordinal);
    private IReadOnlyList<ApplicationUninstallResult>? _batchResults;
    private TaskCompletionSource<bool>? _confirmationDecision;
    private bool _skipNext;
    private DateTimeOffset _operationStarted;
    private CancellationTokenSource? _operationCancellation;
    private ApplicationInventoryReport? _lastInventoryReport;
    private ApplicationUninstallResult? _lastUninstallResult;
    private InstalledApplication? _selectedApplication;
    private bool _operationInProgress;

    private readonly IProcessRunner _processRunner;
    private readonly IInstallationLocationService _locationService;

    public ApplicationsViewModel(IApplicationInventoryService inventoryService,
        ApplicationUninstallService uninstallService, IProcessRunner processRunner,
        IReportHistoryService reportHistoryService, IApplicationUninstallConfirmationDialog confirmationDialog,
        IConfirmationService confirmationService, IInstallationLocationService locationService, IApplicationIconLoader? iconLoader = null)
    {
        _iconLoader = iconLoader;
        _locationService = locationService ?? throw new ArgumentNullException(nameof(locationService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        Operation = new LongOperationViewModel(CancelActiveOperation);
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _uninstallService = uninstallService ?? throw new ArgumentNullException(nameof(uninstallService));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _reportHistoryService = reportHistoryService ?? throw new ArgumentNullException(nameof(reportHistoryService));
        ArgumentNullException.ThrowIfNull(confirmationDialog);
        Entries.Add(new ApplicationTextCardViewModel("Lancez un inventaire. Aucune action ne sera exécutée."));
    }

    public ObservableCollection<ApplicationItemViewModel> ProtectedEntries { get; } = new();
    [ObservableProperty] private string selectedSort = "Nom";
    partial void OnSelectedSortChanged(string value) => RenderApplications();
    [ObservableProperty] private bool isConfirmationOpen;
    [ObservableProperty] private string confirmationTitle = string.Empty;
    [ObservableProperty] private string confirmationBody = string.Empty;
    [ObservableProperty] private string selectionSummary = "Aucune application sélectionnée";
    [ObservableProperty] private string elapsedText = string.Empty;
    [RelayCommand] private void AcceptConfirmation() => _confirmationDecision?.TrySetResult(true);
    [RelayCommand] private void RefuseConfirmation() => _confirmationDecision?.TrySetResult(false);
    [RelayCommand] private void CancelOperation() { RefuseConfirmation(); CancelActiveOperation(); }
    [RelayCommand] private void SkipNext() { _skipNext = true; StatusText = "Le prochain élément sera passé."; }

    public ObservableCollection<object> Entries { get; } = new();
    public ObservableCollection<ApplicationTextCardViewModel> Remnants { get; } = new();
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string selectedFilter = "Applications";
    partial void OnSearchTextChanged(string value) => RenderApplications();
    partial void OnSelectedFilterChanged(string value) => RenderApplications();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isBusy;
    public bool IsIdle => !IsBusy;
    [ObservableProperty] private bool isReportButtonEnabled;
    [ObservableProperty] private bool isLaunchUninstallEnabled;
    [ObservableProperty] private bool isOpenApplicationLocationEnabled;
    [ObservableProperty] private bool isDetailsOpen;
    [ObservableProperty] private bool isReportOpen;
    [ObservableProperty] private double progress;
    [ObservableProperty] private string statusText = "Inventaire prêt. Sélectionnez les applications à désinstaller.";
    [ObservableProperty] private string inventorySummary = "NON ANALYSÉ";
    [ObservableProperty] private string riskSummary = "NON CLASSE";
    [ObservableProperty] private string detailsTitle = "DETAIL APPLICATION";
    [ObservableProperty] private string detailsBody = string.Empty;
    [ObservableProperty] private string detailsCommand = string.Empty;
    [ObservableProperty] private string detailsSafety = string.Empty;
    [ObservableProperty] private string launchUninstallLabel = "LANCER DÉSINSTALLATEUR";
    [ObservableProperty] private string openApplicationLocationLabel = "OUVRIR EMPLACEMENT";
    [ObservableProperty] private string reportSummary = string.Empty;
    [ObservableProperty] private string reportDetails = string.Empty;
    [ObservableProperty] private string reportErrors = string.Empty;

    public event Action<string>? VirgilMessageRequested;

    public event Action<VirgilCoreState, string>? VirgilStateRequested;

    public event EventHandler? ReturnHomeRequested;

    public event EventHandler? ReportPersisted;

    public LongOperationViewModel Operation { get; }
    public bool IsCancelling => Operation.IsCancelling;

    public void CancelActiveOperation()
    {
        Operation.AcknowledgeCancellation();
        OnPropertyChanged(nameof(IsCancelling));
        _confirmationDecision?.TrySetResult(false);
        _operationCancellation?.Cancel();
    }

    public bool TryCloseOverlay()
    {
        if (IsConfirmationOpen) { RefuseConfirmation(); return true; }
        if (IsReportOpen)
        {
            HideReport();
            return true;
        }

        if (IsDetailsOpen)
        {
            HideDetails();
            return true;
        }

        if (_operationInProgress)
        {
            CancelActiveOperation();
            VirgilMessageRequested?.Invoke("Annulation demandee.\nOpération applications en cours d'arret.");
            return true;
        }

        return false;
    }

    [RelayCommand]
    private void ViewReport() => ShowReport();
    [RelayCommand]
    private void ReturnHome() => ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand]
    private void CloseDetails() => HideDetails();
    [RelayCommand]
    private void CloseReport() => HideReport();
    [RelayCommand]
    private async Task LaunchUninstallAsync()
    {
        try
        {
            if (_selectedApplication is null)
            {
                ReportUninstallBlocked("Aucune application sélectionnée.\nOuvrez les détails d'une application avant de lancer une action.");
                return;
            }
            await LaunchUninstallAsync(_selectedApplication).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ReportUninstallException(exception);
        }
    }
    [RelayCommand]
    private async Task OpenApplicationLocationAsync()
    {
        if (_selectedApplication is not null)
            await OpenApplicationLocationOrSettingsAsync(_selectedApplication).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (!BeginOperation("Inventaire applications en cours."))
        {
            return;
        }

        try
        {
            RequestVirgilState(VirgilCoreState.Scanning, "APPLICATIONS");
            VirgilMessageRequested?.Invoke("Inventaire applications lancé.\nLecture seule.\nAucune désinstallation.");
            var session = _operationCancellation;
            var progress = new Progress<ApplicationInventoryProgress>(item =>
            {
                if (!ReferenceEquals(session, _operationCancellation)) return;
                Operation.Report(new Virgil.Core.Operations.OperationProgress(item.Step, Percent: item.Percent));
                if (Operation.IsCancelling) return;
                Progress = item.Percent ?? 0;
                StatusText = $"{item.Status} {item.Percent ?? 0} %";
            });

            var report = await _inventoryService
                .InventoryAsync(progress, _operationCancellation!.Token)
                .WaitAsync(_operationCancellation.Token).ConfigureAwait(true);

            _operationCancellation!.Token.ThrowIfCancellationRequested();
            _lastInventoryReport = report;
            _lastUninstallResult = null;
            _batchResults = null;
            _selectedIds.Clear();
            SelectionSummary = "Aucune application sélectionnée";
            _applications.Clear();
            _applications.AddRange(report.Applications);
            RenderInventory(report);
            RenderApplications();
            await PersistReportAsync(ApplicationReportMapper.FromInventory(report)).ConfigureAwait(true);
            IsReportButtonEnabled = true;
            StatusText = "Inventaire terminé.";
            RequestVirgilState(report.Errors.Count == 0 ? VirgilCoreState.Success : VirgilCoreState.Warning, "APPLICATIONS");
            VirgilMessageRequested?.Invoke(
                $"Inventaire terminé.\nApplications : {report.Applications.Count}.\nDésinstallables : {report.UninstallableCount}.");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Inventaire annulé.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Inventaire applications annulé.\nAucune action effectuée.");
        }
        catch
        {
            StatusText = "Inventaire applications indisponible.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Inventaire applications indisponible.\nAucune action effectuée.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task LaunchUninstallAsync(InstalledApplication application)
    {
        if (_operationInProgress || IsConfirmationOpen) return;
        var plan = _uninstallService.BuildPlan(application);
        if (!application.CanUninstall)
        {
            ReportUninstallBlocked($"Désinstallation bloquée.\n{plan.Validation.Reason}");
            return;
        }

        if (!plan.CanLaunch)
        {
            ReportUninstallBlocked($"Désinstallation bloquée.\n{plan.Validation.Reason}");
            return;
        }

        StatusText = "Confirmation utilisateur requise.";
        RequestVirgilState(VirgilCoreState.SensitiveAction, "VALIDATION");
        VirgilMessageRequested?.Invoke("Confirmation de désinstallation demandée.\nVérifiez l’aperçu dans la page Applications.");

        var confirmation = await RequestUninstallConfirmationAsync(application, plan).ConfigureAwait(true);
        var confirmationDecision = new ApplicationUninstallConfirmationPolicy().Validate(plan, confirmation);
        if (!confirmationDecision.CanProceed)
        {
            ReportUninstallCancelled($"Désinstallation annulée.\n{confirmationDecision.Reason}");
            return;
        }

        ConfirmedPlan confirmedPlan;
        try
        {
            confirmedPlan = _confirmationService.Issue(ApplicationActionPreviewFactory.Create(plan),
                new ConfirmationConsent(confirmation.ReinforcedConfirmed
                    ? ConfirmationLevel.Reinforced : ConfirmationLevel.Simple, null));
        }
        catch (ConfirmationRefusedException exception)
        {
            ReportUninstallCancelled($"Désinstallation annulée.\n{exception.Message}");
            return;
        }

        if (!BeginOperation("Désinstallateur officiel en cours de lancement."))
        {
            return;
        }
        Operation.Start(StatusText, immediately: true, nonInterruptible: true);

        try
        {
            RequestVirgilState(VirgilCoreState.SensitiveAction, "VALIDATION");
            var session = _operationCancellation;
            var progress = new Progress<ApplicationUninstallProgress>(item =>
            {
                if (!ReferenceEquals(session, _operationCancellation)) return;
                Operation.Report(new Virgil.Core.Operations.OperationProgress(item.Step, Percent: item.Percent));
                if (Operation.IsCancelling) return;
                Progress = item.Percent ?? 0;
                StatusText = item.Status;
            });

            var result = await _uninstallService
                .LaunchOfficialUninstallAsync(application, confirmedPlan, confirmation, progress, _operationCancellation!.Token)
                .ConfigureAwait(true);

            _lastUninstallResult = result;
            RenderUninstallResult(result);
            ShowDetails(application);
            await PersistReportAsync(ApplicationReportMapper.FromUninstall(result)).ConfigureAwait(true);
            IsReportButtonEnabled = true;
            RequestVirgilState(result.Errors.Count == 0 ? VirgilCoreState.Success : VirgilCoreState.Warning, "APPLICATIONS");
            VirgilMessageRequested?.Invoke(result.WasLaunched
                ? "Désinstalleur officiel lancé.\nLe statut final peut dépendre de l'assistant externe.\nRestes analysés en lecture seule."
                : $"Désinstalleur non lancé.\n{result.Result}\nRapport disponible.");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Désinstallation annulée.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Désinstallation annulée.\nAucun désinstallateur lancé.");
        }
        catch (Exception exception)
        {
            ReportUninstallException(exception);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task<ApplicationUninstallConfirmation> RequestUninstallConfirmationAsync(
        InstalledApplication application,
        ApplicationUninstallPlan plan)
    {
        var explicitMessage = string.Join("\n", new[]
        {
            "Confirmation explicite obligatoire.",
            string.Empty,
            $"Application : {application.DisplayName}",
            $"Editeur : {Empty(application.Publisher)}",
            $"Risque : {RiskLabel(application.RiskLevel)} - {application.RiskReason}",
            $"Methode : {UninstallMethodLabel(plan.Method)}",
            string.Empty,
            "Virgil va uniquement lancer le désinstallateur officiel valide.",
            "Aucun dossier d'application ne sera supprime directement.",
            "Aucune donnee personnelle ne sera supprimee automatiquement.",
            string.Empty,
            "Continuer ?"
        });

        var explicitResponse = await AskConfirmationAsync("Confirmation désinstallation", explicitMessage).ConfigureAwait(true);
        if (!explicitResponse)
        {
            return ApplicationUninstallConfirmation.None;
        }

        var reinforcedConfirmed = false;
        if (plan.RequiresReinforcedConfirmation)
        {
            var reinforcedMessage = string.Join("\n", new[]
            {
                "Confirmation renforcee requise.",
                string.Empty,
                $"Application : {application.DisplayName}",
                "Cette application est classee ATTENTION.",
                "Elle peut contenir des profils, projets, presets, bibliotheques ou sauvegardes.",
                string.Empty,
                "Virgil ne supprime rien lui-meme, mais l'assistant officiel peut modifier ou retirer l'application.",
                "Ne continuez que si vous avez sauvegarde ce qui doit l'être.",
                string.Empty,
                "Confirmer malgre ce risque ?"
            });

            reinforcedConfirmed = await AskConfirmationAsync("Confirmation renforcee applications", reinforcedMessage).ConfigureAwait(true);
        }

        return new ApplicationUninstallConfirmation
        {
            ExplicitlyConfirmed = true,
            ReinforcedConfirmed = reinforcedConfirmed,
            Source = "ApplicationsDetails"
        };
    }

    private async Task<bool> AskConfirmationAsync(string title, string body)
    {
        if (_confirmationDecision is not null) return false;
        var decision = new TaskCompletionSource<bool>();
        _confirmationDecision = decision;
        ConfirmationTitle = title;
        ConfirmationBody = body + "\nCertaines étapes ne peuvent pas être interrompues une fois lancées.";
        IsConfirmationOpen = true;
        try { return await decision.Task.ConfigureAwait(true); }
        finally { IsConfirmationOpen = false; _confirmationDecision = null; }
    }

    [RelayCommand]
    private async Task UninstallSelectionAsync()
    {
        if (_operationInProgress || IsConfirmationOpen) return;
        var selected = _applications.Where(app => _selectedIds.Contains(app.Id)).OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var plans = selected.Select(_uninstallService.BuildPlan).ToArray();
        if (plans.Length == 0 || plans.Any(plan => !plan.CanLaunch || !plan.Application.CanUninstall || IsProtectedComponent(plan.Application)))
        { ReportUninstallBlocked("Sélection vide, protégée ou commande officielle invalide."); return; }
        var preview = ApplicationBatchPreviewFactory.Create(plans);
        var reinforced = plans.Any(plan => plan.RequiresReinforcedConfirmation);
        if (!await AskConfirmationAsync(reinforced ? "Confirmation renforcée de la sélection" : "Désinstaller la sélection",
            string.Join("\n", preview.Effects) + "\nRisque maximal : " + preview.RiskLevel +
            "\nLes assistants officiels peuvent retirer des données locales. Sauvegardez vos profils et projets. Aucun reste ne sera supprimé par Virgil.").ConfigureAwait(true)) return;
        ConfirmedPlan plan;
        try { plan = _confirmationService.Issue(preview, new ConfirmationConsent(reinforced ? ConfirmationLevel.Reinforced : ConfirmationLevel.Simple, null)); }
        catch (ConfirmationRefusedException) { ReportUninstallCancelled("Confirmation de la sélection refusée."); return; }
        if (!BeginOperation("Désinstallation de la sélection en cours.")) return;
        Operation.Start(StatusText, immediately: true, nonInterruptible: true);
        try
        {
            _skipNext = false;
            var session = _operationCancellation;
            var progress = new Progress<ApplicationUninstallProgress>(item =>
            {
                if (!ReferenceEquals(session, _operationCancellation)) return;
                Operation.Report(new Virgil.Core.Operations.OperationProgress(item.Step, Percent: item.Percent));
                if (Operation.IsCancelling) return;
                Progress = item.Percent ?? 0;
                StatusText = item.Status;
                ElapsedText = "Temps écoulé : " + (DateTimeOffset.Now - _operationStarted).ToString(@"hh\:mm\:ss");
                VirgilMessageRequested?.Invoke(StatusText);
            });
            _batchResults = await _uninstallService.LaunchBatchAsync(selected, plan, new ApplicationUninstallConfirmation
            { ExplicitlyConfirmed = true, ReinforcedConfirmed = reinforced, Source = "ApplicationsBatchPreview" },
                progress, _operationCancellation!.Token, _ => { var skip = _skipNext; _skipNext = false; return skip; }).ConfigureAwait(true);
            foreach (var result in _batchResults) await PersistReportAsync(ApplicationReportMapper.FromUninstall(result));
            StatusText = "Parcours de désinstallation terminé. Rapport disponible.";
            VirgilMessageRequested?.Invoke(StatusText);
            ShowReport();
        }
        catch (OperationCanceledException) { StatusText = "Parcours annulé. L’assistant officiel peut rester ouvert."; VirgilMessageRequested?.Invoke(StatusText); }
        catch (Exception ex) { ReportUninstallException(ex); }
        finally { EndOperation(); }
    }

    private void ReportUninstallBlocked(string message)
    {
        StatusText = "Désinstallation bloquée.";
        RequestVirgilState(VirgilCoreState.Warning, "APPLICATIONS");
        VirgilMessageRequested?.Invoke(message);
    }

    private void ReportUninstallCancelled(string message)
    {
        StatusText = "Désinstallation annulée.";
        RequestVirgilState(VirgilCoreState.Idle, "REPOS");
        VirgilMessageRequested?.Invoke(message);
    }

    private void ReportUninstallException(Exception exception)
    {
        StatusText = "Désinstallateur officiel indisponible.";
        RequestVirgilState(VirgilCoreState.Error, "ERREUR");
        VirgilMessageRequested?.Invoke($"Désinstallateur officiel indisponible.\nErreur : {exception.Message}");
    }

    private void RenderInventory(ApplicationInventoryReport report)
    {
        InventorySummary = $"{report.Applications.Count} détectées, {report.UninstallableCount} désinstallables, {report.Errors.Count} avertissement(s).";
        RiskSummary = $"{report.SafeCount} simples, {report.CautionCount} attention, {report.ProtectedCount} protégées, {report.UnknownCount} inconnues.";
    }

    private void RenderApplications()
    {
        Entries.Clear();
        ProtectedEntries.Clear();
        foreach (var application in _applications.Where(IsProtectedComponent).OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            ProtectedEntries.Add(new ApplicationItemViewModel(application, ShowDetails, OpenApplicationLocationOrSettingsAsync, null, _iconLoader));
        var items = FilteredApplications().ToList();
        if (items.Count == 0)
        {
            Entries.Add(new ApplicationTextCardViewModel(_applications.Count == 0
                ? "Aucune application inventoriee."
                : "Aucune application ne correspond au filtre."));
            return;
        }
        foreach (var application in items)
            Entries.Add(new ApplicationItemViewModel(application, ShowDetails, OpenApplicationLocationOrSettingsAsync, (app, selected) =>
            {
                if (selected) _selectedIds.Add(app.Id); else _selectedIds.Remove(app.Id);
                SelectionSummary = $"{_selectedIds.Count} application(s) sélectionnée(s)";
            }, _iconLoader) { IsSelected = _selectedIds.Contains(application.Id) });
    }

    public IEnumerable<InstalledApplication> FilteredApplications()
    {
        var query = SearchText.Trim();
        var filter = SelectedFilter;
        var filtered = _applications.Where(application =>
        {
            var matchesQuery = string.IsNullOrWhiteSpace(query) ||
                application.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                application.Publisher.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                (application.WingetId?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false);
            if (!matchesQuery)
            {
                return false;
            }

            return filter switch
            {
                "Applications" => application.CanUninstall && !IsProtectedComponent(application),
                "Jeux" => application.IsGame && application.CanUninstall && !IsProtectedComponent(application),
                "Store" => application.Source == ApplicationInventorySource.Store && !IsProtectedComponent(application),
                "Système" => IsProtectedComponent(application),
                "DÉSINSTALLABLE" => application.CanUninstall,
                "ATTENTION" => application.RiskLevel == ApplicationRiskLevel.Caution,
                "PROTÉGÉ" => application.RiskLevel == ApplicationRiskLevel.Protected,
                "INCONNU" => application.RiskLevel == ApplicationRiskLevel.Unknown,
                _ => true
            };
        });
        return SelectedSort switch
        {
            "Taille" => filtered.OrderByDescending(app => app.EstimatedSizeBytes).ThenBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "Date d’installation" => filtered.OrderByDescending(app => app.InstallDate).ThenBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered.OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        };
    }

    private static bool IsProtectedComponent(InstalledApplication app) => app.IsSystemComponent || app.RiskLevel == ApplicationRiskLevel.Protected;

    private void ShowDetails(InstalledApplication application)
    {
        _selectedApplication = application;
        DetailsTitle = application.DisplayName;
        DetailsBody = string.Join("\n", new[]
        {
            $"Editeur : {Empty(application.Publisher)}",
            $"Version : {Empty(application.Version)}",
            $"Source : {string.Join(", ", application.Sources.Count == 0 ? [application.Source] : application.Sources)}",
            $"Architecture : {application.Architecture}",
            $"Emplacement : {Empty(application.InstallLocation)}",
            $"Risque : {RiskLabel(application.RiskLevel)}",
            $"Raison : {application.RiskReason}",
            $"Statut : {application.Status}"
        });
        DetailsCommand = application.UninstallKind switch
        {
            ApplicationUninstallKind.Winget => $"winget uninstall --id {application.WingetId} --exact",
            ApplicationUninstallKind.StoreSettings => "Paramètres Windows uniquement : identité du package non validée.",
            ApplicationUninstallKind.StorePackage => "Remove-AppxPackage : " + application.StorePackageFullName,
            _ => Empty(application.UninstallCommand ?? application.QuietUninstallCommand)
        };
        DetailsSafety = string.Join("\n", new[]
        {
            "Désinstallation individuelle uniquement depuis cet ecran de détails.",
            "Confirmation explicite obligatoire avant tout lancement.",
            "Désinstalleur officiel ou WinGet exact uniquement.",
            "Suppression par dossier interdite.",
            "Aucune suppression automatique de données personnelles.",
            "Pilotes, sécurité, runtimes et composants système bloqués.",
            application.RiskLevel == ApplicationRiskLevel.Caution
                ? "Attention : confirmation renforcee obligatoire, profils, projets, presets ou sauvegardes possibles."
                : "Validation stricte appliquee avant lancement."
        });
        LaunchUninstallLabel = !application.CanUninstall
            ? "DÉSINSTALLATION BLOQUÉE"
            : application.RiskLevel == ApplicationRiskLevel.Caution
                ? "LANCER AVEC VALIDATION RENFORCEE"
                : "LANCER DÉSINSTALLATEUR";
        IsLaunchUninstallEnabled = !_operationInProgress && application.CanUninstall && !IsProtectedComponent(application);
        IsOpenApplicationLocationEnabled = application.CanOpenLocation || application.UninstallKind == ApplicationUninstallKind.StoreSettings;
        OpenApplicationLocationLabel = application.UninstallKind == ApplicationUninstallKind.StoreSettings
            ? "PARAMÈTRES WINDOWS"
            : "OUVRIR EMPLACEMENT";
        RenderDetailsRemnants(application);
        IsDetailsOpen = true;
    }

    private void RenderDetailsRemnants(InstalledApplication application)
    {
        Remnants.Clear();
        var result = _lastUninstallResult;
        if (result?.Application.Id != application.Id)
        {
            Remnants.Add(new ApplicationTextCardViewModel("Disponibles après lancement du désinstallateur officiel. Analyse lecture seule uniquement."));
            return;
        }

        if (result.Remnants.Remnants.Count == 0)
        {
            Remnants.Add(new ApplicationTextCardViewModel("Aucun reste accessible détecté en lecture seule."));
            return;
        }

        foreach (var remnant in result.Remnants.Remnants.Take(12))
        {
            Remnants.Add(new ApplicationTextCardViewModel(
                $"{RemnantKindLabel(remnant.Kind)} - {remnant.Path}\n{remnant.Reason}\nAction automatique : aucune."));
        }
    }

    private void RenderUninstallResult(ApplicationUninstallResult result)
    {
        StatusText = result.Result;
        ReportSummary = $"{result.Application.DisplayName}\n{result.Result}";
        ReportDetails = string.Join("\n", new[]
        {
            $"Methode : {result.Method}",
            $"Lancé : {(result.WasLaunched ? "oui" : "non")}",
            $"Confirmation explicite : {(result.WasExplicitlyConfirmed ? "oui" : "non")}",
            $"Confirmation renforcee : {(result.WasReinforcedConfirmed ? "oui" : "non")}",
            $"Statut inconnu : {(result.StatusUnknown ? "oui" : "non")}",
            $"Code sortie : {(result.ExitCode.HasValue ? result.ExitCode.Value.ToString() : "N/A")}",
            $"Restes : {result.Remnants.Remnants.Count}",
            $"Techniques : {result.Remnants.TechnicalCount}",
            $"Personnels/protégés : {result.Remnants.UserDataCount + result.Remnants.ProtectedCount}",
            "Suppression automatique : aucune"
        });
        ReportErrors = result.Errors.Count == 0
            ? "Aucune"
            : string.Join("\n", result.Errors.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task OpenApplicationLocationOrSettingsAsync(InstalledApplication application)
    {
        if (application.UninstallKind == ApplicationUninstallKind.StoreSettings)
        {
            await OpenWindowsSettingsAsync().ConfigureAwait(true);
            return;
        }

        var path = application.InstallLocation;
        if (string.IsNullOrWhiteSpace(path))
        {
            VirgilMessageRequested?.Invoke("Emplacement indisponible.");
            return;
        }

        try
        {
            var result = await _locationService.OpenAsync(path, CancellationToken.None).ConfigureAwait(true);
            if (result == InstallationLocationOpenResult.Missing)
            {
                VirgilMessageRequested?.Invoke("Emplacement introuvable.");
                return;
            }

            VirgilMessageRequested?.Invoke(result == InstallationLocationOpenResult.Failed
                ? "Ouverture emplacement impossible."
                : "Emplacement ouvert.\nAucune suppression exécutée.");
        }
        catch
        {
            VirgilMessageRequested?.Invoke("Ouverture emplacement impossible.");
        }
    }

    private async Task OpenWindowsSettingsAsync()
    {
        try
        {
            await _processRunner.LaunchAsync(new ProcessLaunchRequest(
                "ms-settings:appsfeatures", Array.Empty<string>(), UseShellExecute: true), CancellationToken.None);
        }
        catch
        {
            // Non blocking. The report and inventory remain available.
        }
    }

    private void ShowReport()
    {
        if (_batchResults is not null)
        {
            ReportSummary = $"{_batchResults.Count} application(s) dans le rapport du lot.";
            ReportDetails = string.Join("\n\n", _batchResults.Select(result => result.Application.DisplayName + " : " + result.Result));
            ReportErrors = string.Join("\n", _batchResults.SelectMany(result => result.Errors));
        }
        else if (_lastUninstallResult is not null)
        {
            RenderUninstallResult(_lastUninstallResult);
        }
        else if (_lastInventoryReport is not null)
        {
            ReportSummary = $"{_lastInventoryReport.Applications.Count} application(s), {_lastInventoryReport.UninstallableCount} désinstallable(s).";
            ReportDetails = string.Join("\n", new[]
            {
                $"Protégées : {_lastInventoryReport.ProtectedCount}",
                $"Attention : {_lastInventoryReport.CautionCount}",
                $"Inconnues : {_lastInventoryReport.UnknownCount}",
                "Aucune action exécutée depuis l'inventaire.",
                "Aucune donnee personnelle supprimee automatiquement."
            });
            ReportErrors = _lastInventoryReport.Errors.Count == 0
                ? "Aucune"
                : string.Join("\n", _lastInventoryReport.Errors);
        }
        else
        {
            ReportSummary = "Aucun rapport applications disponible.";
            ReportDetails = "Lancez un inventaire applications.";
            ReportErrors = "Aucune";
        }

        IsReportOpen = true;
    }

    private void HideReport()
    {
        IsReportOpen = false;
    }

    private void HideDetails()
    {
        IsDetailsOpen = false;
        _selectedApplication = null;
    }

    private async Task PersistReportAsync(ReportEntry report)
    {
        var result = await _reportHistoryService.SaveAsync(report, CancellationToken.None).ConfigureAwait(true);
        if (!result.Success || !string.IsNullOrWhiteSpace(result.ReadableError))
        {
            VirgilMessageRequested?.Invoke(result.ReadableError ?? "Historique local indisponible. Rapport conservé en mémoire.");
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
            VirgilMessageRequested?.Invoke("Opération applications déjà en cours.");
            return false;
        }

        _operationInProgress = true;
        Operation.Start(status);
        _operationStarted = DateTimeOffset.Now;
        _operationCancellation = new CancellationTokenSource();

        StatusText = status;
        Progress = 0;
        SetInteractionEnabled(false);
        return true;
    }

    private async Task UpdateElapsedAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                ElapsedText = "Temps écoulé : " + (DateTimeOffset.Now - _operationStarted).ToString(@"hh\:mm\:ss");
                await Task.Delay(1000, token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void EndOperation()
    {
        Operation.Complete(StatusText);
        _operationInProgress = false;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        SetInteractionEnabled(true);
    }

    private void SetInteractionEnabled(bool enabled)
    {
        IsBusy = !enabled;
        IsReportButtonEnabled = enabled && (_lastInventoryReport is not null || _lastUninstallResult is not null);
        IsLaunchUninstallEnabled = enabled && _selectedApplication?.CanUninstall == true && !IsProtectedComponent(_selectedApplication);
        IsOpenApplicationLocationEnabled = enabled && (_selectedApplication?.CanOpenLocation == true ||
            _selectedApplication?.UninstallKind == ApplicationUninstallKind.StoreSettings);
    }

    private void RequestVirgilState(VirgilCoreState state, string label)
    {
        VirgilStateRequested?.Invoke(state, label);
    }

    internal static string RiskLabel(ApplicationRiskLevel risk)
    {
        return risk switch
        {
            ApplicationRiskLevel.SafeToUninstall => "désinstallable",
            ApplicationRiskLevel.Caution => "attention",
            ApplicationRiskLevel.Protected => "protégé",
            _ => "inconnu"
        };
    }

    private static string RemnantKindLabel(ApplicationRemnantKind kind)
    {
        return kind switch
        {
            ApplicationRemnantKind.TechnicalRemnant => "technique",
            ApplicationRemnantKind.UserData => "donnee personnelle",
            ApplicationRemnantKind.ProtectedRemnant => "protégé",
            _ => "inconnu"
        };
    }

    private static string UninstallMethodLabel(ApplicationUninstallKind kind)
    {
        return kind switch
        {
            ApplicationUninstallKind.Msi => "MSI officiel",
            ApplicationUninstallKind.RegistryUninstallString => "désinstalleur officiel",
            ApplicationUninstallKind.RegistryQuietUninstallString => "désinstalleur officiel silencieux",
            ApplicationUninstallKind.Winget => "WinGet exact",
            ApplicationUninstallKind.StoreSettings => "paramètres Windows",
            _ => "inconnue"
        };
    }

    internal static string Empty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "N/A" : value;
    }

    internal static string FormatBytes(long? bytes)
    {
        return bytes.HasValue ? ScanRules.FormatBytes(bytes.Value) : "N/A";
    }
}

