using Virgil.Core.Status;
using System.Windows.Automation;
using Virgil.App.Views;
using Virgil.App.Animation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Virgil.App.Controls;
using Virgil.Core.Reports;
using Virgil.Core.Scanning;
using Virgil.Contracts;
using Virgil.Contracts.Applications;

namespace Virgil.App;

public partial class MainWindow : Window
{
    private readonly ISystemStatusStore _statusStore;
    private readonly Virgil.App.ViewModels.VirgilReleaseViewModel _virgilRelease;
    private readonly ISystemScanService _systemScanService;
    private readonly IReportHistoryService _reportHistoryService;
    private readonly IReportExportService _reportExportService;
    private readonly HashSet<string> _reportedProgressSteps = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _activeScanCancellation;
    private Storyboard? _startupStoryboard;
    private bool _scanInProgress;
    private bool _startupAnimationPlayed;
    private bool _hasPersistentReport;
    private SystemScanReport? _lastReport;

    private readonly StartupView StartupModuleView;
    private readonly NetworkView NetworkModuleView;
    private readonly DiagnosticView DiagnosticModuleView;
    private readonly CleanupView CleanupModuleView;
    private readonly UpdatesView UpdatesModuleView;
    private readonly InterventionsView InterventionsModuleView;
    private readonly ResourcesView ResourcesModuleView;
    private readonly ApplicationsView ApplicationsModuleView;
    private readonly ReportsHistoryView ReportsModuleView;

    public MainWindow(
        ISystemScanService systemScanService,
        IReportHistoryService reportHistoryService,
        IReportExportService reportExportService,
        CleanupView cleanupView, UpdatesView updatesView, InterventionsView interventionsView,
        ResourcesView resourcesView, ApplicationsView applicationsView, ReportsHistoryView reportsHistoryView, DiagnosticView diagnosticView,
        VirgilReleaseView releaseView, Virgil.App.ViewModels.VirgilReleaseViewModel virgilRelease,
        StartupView startupView, NetworkView networkView, ISystemStatusStore statusStore)
    {
        _statusStore = statusStore;
        _virgilRelease = virgilRelease;
        _systemScanService = systemScanService;
        _reportHistoryService = reportHistoryService;
        _reportExportService = reportExportService;
        CleanupModuleView = cleanupView;
        UpdatesModuleView = updatesView;
        InterventionsModuleView = interventionsView;
        ResourcesModuleView = resourcesView;
        ApplicationsModuleView = applicationsView;
        ReportsModuleView = reportsHistoryView;
        DiagnosticModuleView = diagnosticView;
        DiagnosticModuleView.NavigationRequested += destination =>
        {
            if (destination == Virgil.Core.Diagnostic.DiagnosticSuggestion.Cleanup) ShowCleanup();
            else if (destination == Virgil.Core.Diagnostic.DiagnosticSuggestion.Updates) ShowUpdates();
        };
        StartupModuleView = startupView;
        NetworkModuleView = networkView;
        ((Virgil.App.ViewModels.NetworkViewModel)networkView.DataContext).RepairRequested += (_, _) => ShowInterventions();
        InitializeComponent();
        _scanOperation = new Virgil.App.ViewModels.LongOperationViewModel(() => _activeScanCancellation?.Cancel());
        ScanProgressPanel.DataContext = _scanOperation;
        StartupModuleView.Visibility = Visibility.Collapsed;
        NetworkModuleView.Visibility = Visibility.Collapsed;
        ModuleHostGrid.Children.Add(StartupModuleView);
        ModuleHostGrid.Children.Add(NetworkModuleView);
        _statusStore.Changed += SystemStatusChanged;
        ApplySystemStatus();
        ReleasePanelHost.Children.Add(releaseView);
        DiagnosticModuleView.Visibility = Visibility.Collapsed;
        ModuleHostGrid.Children.Add(DiagnosticModuleView);
        CleanupModuleView.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(CleanupModuleView, "Module nettoyage sécurisé");
        ModuleHostGrid.Children.Add(CleanupModuleView);
        UpdatesModuleView.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(UpdatesModuleView, "Module mises à jour sécurisé");
        ModuleHostGrid.Children.Add(UpdatesModuleView);
        InterventionsModuleView.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(InterventionsModuleView, "Module interventions ciblées");
        ModuleHostGrid.Children.Add(InterventionsModuleView);
        ResourcesModuleView.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(ResourcesModuleView, "Module ressources système");
        ModuleHostGrid.Children.Add(ResourcesModuleView);
        ApplicationsModuleView.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(ApplicationsModuleView, "Module applications et désinstallateur");
        ModuleHostGrid.Children.Add(ApplicationsModuleView);
        ReportsModuleView.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(ReportsModuleView, "Module rapports et historique local");
        ModuleHostGrid.Children.Add(ReportsModuleView);

        SessionTimeText.Text = $"SESSION {DateTime.Now:HH:mm}";
        LastReportButton.IsEnabled = false;
        WireCleanupModule();
        WireUpdatesModule();
        WireInterventionsModule();
        WireResourcesModule();
        WireApplicationsModule();
        WireReportsModule();
        PrepareStartupVisuals();
        SetVirgilState(VirgilCoreState.Idle, "REPOS");
        AppendVirgilMessage("Système prêt.\nAucune analyse récente.\nEn attente d'instruction.");
    }

    private void OpenScanProtocol_Click(object sender, RoutedEventArgs e) => OpenScanProtocol();

    private void CloseScanProtocol_Click(object sender, RoutedEventArgs e) => CloseScanProtocol();

    private async void RunQuickScan_Click(object sender, RoutedEventArgs e) => await RunScanAsync(ScanMode.Quick);

    private async void RunDeepScan_Click(object sender, RoutedEventArgs e) => await RunScanAsync(ScanMode.Deep);

    private void CloseReport_Click(object sender, RoutedEventArgs e) => CloseReport();

    private async void Window_ContentRendered(object? sender, EventArgs e)
    {
        PlayStartupAnimation();
        // Signal UIA après le premier rendu et le drainage du dispatcher WPF.
        await Dispatcher.InvokeAsync(() => AutomationProperties.SetItemStatus(this, "Prêt"),
            DispatcherPriority.ApplicationIdle);
        await StartupInitialization.StartAsync(RefreshPersistentReportStateAsync, _virgilRelease.StartAsync);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (ReportOverlay.Visibility == Visibility.Visible)
        {
            CloseReport();
            e.Handled = true;
            return;
        }

        if (DiagnosticModuleView.Visibility == Visibility.Visible && DiagnosticModuleView.TryCloseOverlay())
        {
            e.Handled = true;
            return;
        }
        if (CleanupModuleView.Visibility == Visibility.Visible && CleanupModuleView.TryCloseOverlay())
        {
            e.Handled = true;
            return;
        }

        if (UpdatesModuleView.Visibility == Visibility.Visible && UpdatesModuleView.TryCloseOverlay())
        {
            e.Handled = true;
            return;
        }

        if (InterventionsModuleView.Visibility == Visibility.Visible && InterventionsModuleView.TryCloseOverlay())
        {
            e.Handled = true;
            return;
        }

        if (ResourcesModuleView.Visibility == Visibility.Visible && ResourcesModuleView.TryCloseOverlay())
        {
            e.Handled = true;
            return;
        }

        if (ApplicationsModuleView.Visibility == Visibility.Visible && ApplicationsModuleView.TryCloseOverlay())
        {
            e.Handled = true;
            return;
        }

        if (ReportsModuleView.Visibility == Visibility.Visible && ReportsModuleView.TryCloseOverlay())
        {
            e.Handled = true;
            return;
        }

        if (ScanProtocolOverlay.Visibility == Visibility.Visible && !_scanInProgress)
        {
            CloseScanProtocol();
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        ((Virgil.App.ViewModels.NetworkViewModel)NetworkModuleView.DataContext).CancelRefresh();
        _statusStore.Changed -= SystemStatusChanged;
        _virgilRelease.Dispose();
        _activeScanCancellation?.Cancel();
        CleanupModuleView.CancelActiveOperation();
        UpdatesModuleView.CancelActiveOperation();
        InterventionsModuleView.CancelActiveOperation();
        ResourcesModuleView.CancelActiveOperation();
        ApplicationsModuleView.CancelActiveOperation();
        StopStartupStoryboard();
        VirgilCore.SetState(VirgilCoreState.Idle);
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        ShowHome();
    }

    private void Cleanup_Click(object sender, RoutedEventArgs e)
    {
        if (_scanInProgress)
        {
            AppendVirgilMessage("Analyse en cours.\nNettoyage indisponible.");
            return;
        }

        ShowCleanup();
    }

    private void Updates_Click(object sender, RoutedEventArgs e)
    {
        if (_scanInProgress)
        {
            AppendVirgilMessage("Analyse en cours.\nMises à jour indisponibles.");
            return;
        }

        ShowUpdates();
    }

    private void Interventions_Click(object sender, RoutedEventArgs e)
    {
        if (_scanInProgress)
        {
            AppendVirgilMessage("Analyse en cours.\nInterventions indisponibles.");
            return;
        }

        ShowInterventions();
    }

    private void Resources_Click(object sender, RoutedEventArgs e)
    {
        if (_scanInProgress)
        {
            AppendVirgilMessage("Analyse en cours.\nRessources indisponibles.");
            return;
        }

        ShowResources();
    }

    private void Applications_Click(object sender, RoutedEventArgs e)
    {
        if (_scanInProgress)
        {
            AppendVirgilMessage("Analyse en cours.\nApplications indisponibles.");
            return;
        }

        ShowApplications();
    }

    private void Reports_Click(object sender, RoutedEventArgs e)
    {
        if (_scanInProgress)
        {
            AppendVirgilMessage("Analyse en cours.\nRapports indisponibles.");
            return;
        }

        ShowReports();
    }

    private void Diagnostic_Click(object sender, RoutedEventArgs e)
    {
        if (_scanInProgress) return;
        ShowHome();
        HomeContentGrid.Visibility = Visibility.Collapsed;
        DiagnosticModuleView.Visibility = Visibility.Visible;
        HomeNavButton.Tag = null;
        DiagnosticNavButton.Tag = "Active";
        StatusText.Text = "DIAGNOSTIC — AUCUNE ACTION SANS VOTRE VALIDATION";
    }

    private void ShowHome()
    {
        HideAdditionalModules();
        DiagnosticModuleView.Visibility = Visibility.Collapsed;
        DiagnosticNavButton.Tag = null;
        HomeContentGrid.Visibility = Visibility.Visible;
        CleanupModuleView.Visibility = Visibility.Collapsed;
        UpdatesModuleView.Visibility = Visibility.Collapsed;
        InterventionsModuleView.Visibility = Visibility.Collapsed;
        ResourcesModuleView.Visibility = Visibility.Collapsed;
        ApplicationsModuleView.Visibility = Visibility.Collapsed;
        ReportsModuleView.Visibility = Visibility.Collapsed;
        HomeNavButton.Tag = "Active";
        CleanupNavButton.Tag = null;
        UpdatesNavButton.Tag = null;
        InterventionsNavButton.Tag = null;
        ResourcesNavButton.Tag = null;
        ApplicationsNavButton.Tag = null;
        ReportsNavButton.Tag = null;
        StatusText.Text = "ACCUEIL";
        SetVirgilState(VirgilCoreState.Idle, "REPOS");
        AppendVirgilMessage("Accueil actif.");
    }

    private void ShowCleanup()
    {
        HideAdditionalModules();
        DiagnosticModuleView.Visibility = Visibility.Collapsed;
        DiagnosticNavButton.Tag = null;
        HomeContentGrid.Visibility = Visibility.Collapsed;
        CleanupModuleView.Visibility = Visibility.Visible;
        UpdatesModuleView.Visibility = Visibility.Collapsed;
        InterventionsModuleView.Visibility = Visibility.Collapsed;
        ResourcesModuleView.Visibility = Visibility.Collapsed;
        ApplicationsModuleView.Visibility = Visibility.Collapsed;
        ReportsModuleView.Visibility = Visibility.Collapsed;
        HomeNavButton.Tag = null;
        CleanupNavButton.Tag = "Active";
        UpdatesNavButton.Tag = null;
        InterventionsNavButton.Tag = null;
        ResourcesNavButton.Tag = null;
        ApplicationsNavButton.Tag = null;
        ReportsNavButton.Tag = null;
        StatusText.Text = "NETTOYAGE";
        SetVirgilState(VirgilCoreState.Idle, "NETTOYAGE");
        AppendVirgilMessage("Nettoyage sécurisé prêt.\nAucune action automatique.");
        CleanupModuleView.FocusAnalyzeButton();
    }

    private void ShowUpdates()
    {
        HideAdditionalModules();
        DiagnosticModuleView.Visibility = Visibility.Collapsed;
        DiagnosticNavButton.Tag = null;
        HomeContentGrid.Visibility = Visibility.Collapsed;
        CleanupModuleView.Visibility = Visibility.Collapsed;
        UpdatesModuleView.Visibility = Visibility.Visible;
        InterventionsModuleView.Visibility = Visibility.Collapsed;
        ResourcesModuleView.Visibility = Visibility.Collapsed;
        ApplicationsModuleView.Visibility = Visibility.Collapsed;
        ReportsModuleView.Visibility = Visibility.Collapsed;
        HomeNavButton.Tag = null;
        CleanupNavButton.Tag = null;
        UpdatesNavButton.Tag = "Active";
        InterventionsNavButton.Tag = null;
        ResourcesNavButton.Tag = null;
        ApplicationsNavButton.Tag = null;
        ReportsNavButton.Tag = null;
        StatusText.Text = "MISES À JOUR";
        SetVirgilState(VirgilCoreState.Idle, "MISES À JOUR");
        AppendVirgilMessage("Module mises à jour prêt.\nValidation individuelle requise.");
        UpdatesModuleView.FocusScanButton();
    }

    private void ShowInterventions()
    {
        HideAdditionalModules();
        DiagnosticModuleView.Visibility = Visibility.Collapsed;
        DiagnosticNavButton.Tag = null;
        HomeContentGrid.Visibility = Visibility.Collapsed;
        CleanupModuleView.Visibility = Visibility.Collapsed;
        UpdatesModuleView.Visibility = Visibility.Collapsed;
        InterventionsModuleView.Visibility = Visibility.Visible;
        ResourcesModuleView.Visibility = Visibility.Collapsed;
        ApplicationsModuleView.Visibility = Visibility.Collapsed;
        ReportsModuleView.Visibility = Visibility.Collapsed;
        HomeNavButton.Tag = null;
        CleanupNavButton.Tag = null;
        UpdatesNavButton.Tag = null;
        InterventionsNavButton.Tag = "Active";
        ResourcesNavButton.Tag = null;
        ApplicationsNavButton.Tag = null;
        ReportsNavButton.Tag = null;
        StatusText.Text = "INTERVENTIONS";
        SetVirgilState(VirgilCoreState.Idle, "INTERVENTIONS");
        AppendVirgilMessage("Interventions ciblées prêtes.\nDiagnostic requis avant action.");
        InterventionsModuleView.FocusAnalyzeButton();
    }

    private void ShowResources()
    {
        HideAdditionalModules();
        DiagnosticModuleView.Visibility = Visibility.Collapsed;
        DiagnosticNavButton.Tag = null;
        HomeContentGrid.Visibility = Visibility.Collapsed;
        CleanupModuleView.Visibility = Visibility.Collapsed;
        UpdatesModuleView.Visibility = Visibility.Collapsed;
        InterventionsModuleView.Visibility = Visibility.Collapsed;
        ResourcesModuleView.Visibility = Visibility.Visible;
        ApplicationsModuleView.Visibility = Visibility.Collapsed;
        ReportsModuleView.Visibility = Visibility.Collapsed;
        HomeNavButton.Tag = null;
        CleanupNavButton.Tag = null;
        UpdatesNavButton.Tag = null;
        InterventionsNavButton.Tag = null;
        ResourcesNavButton.Tag = "Active";
        ApplicationsNavButton.Tag = null;
        ReportsNavButton.Tag = null;
        StatusText.Text = "RESSOURCES";
        SetVirgilState(VirgilCoreState.Idle, "RESSOURCES");
        AppendVirgilMessage("Module ressources prêt.\nAnalyse CPU/RAM en lecture seule.\nAucune fermeture automatique.");
        ResourcesModuleView.FocusAnalyzeButton();
    }

    private void ShowApplications()
    {
        HideAdditionalModules();
        DiagnosticModuleView.Visibility = Visibility.Collapsed;
        DiagnosticNavButton.Tag = null;
        HomeContentGrid.Visibility = Visibility.Collapsed;
        CleanupModuleView.Visibility = Visibility.Collapsed;
        UpdatesModuleView.Visibility = Visibility.Collapsed;
        InterventionsModuleView.Visibility = Visibility.Collapsed;
        ResourcesModuleView.Visibility = Visibility.Collapsed;
        ApplicationsModuleView.Visibility = Visibility.Visible;
        ReportsModuleView.Visibility = Visibility.Collapsed;
        HomeNavButton.Tag = null;
        CleanupNavButton.Tag = null;
        UpdatesNavButton.Tag = null;
        InterventionsNavButton.Tag = null;
        ResourcesNavButton.Tag = null;
        ApplicationsNavButton.Tag = "Active";
        ReportsNavButton.Tag = null;
        StatusText.Text = "APPLICATIONS";
        SetVirgilState(VirgilCoreState.Idle, "APPLICATIONS");
        AppendVirgilMessage("Module applications prêt.\nDésinstallation individuelle uniquement.\nAucune suppression de données personnelles.");
        ApplicationsModuleView.FocusAnalyzeButton();
    }

    private void ShowReports(bool refresh = true)
    {
        HideAdditionalModules();
        DiagnosticModuleView.Visibility = Visibility.Collapsed;
        DiagnosticNavButton.Tag = null;
        HomeContentGrid.Visibility = Visibility.Collapsed;
        CleanupModuleView.Visibility = Visibility.Collapsed;
        UpdatesModuleView.Visibility = Visibility.Collapsed;
        InterventionsModuleView.Visibility = Visibility.Collapsed;
        ResourcesModuleView.Visibility = Visibility.Collapsed;
        ApplicationsModuleView.Visibility = Visibility.Collapsed;
        ReportsModuleView.Visibility = Visibility.Visible;
        HomeNavButton.Tag = null;
        CleanupNavButton.Tag = null;
        UpdatesNavButton.Tag = null;
        InterventionsNavButton.Tag = null;
        ResourcesNavButton.Tag = null;
        ApplicationsNavButton.Tag = null;
        ReportsNavButton.Tag = "Active";
        StatusText.Text = "RAPPORTS";
        SetVirgilState(VirgilCoreState.Idle, "RAPPORTS");
        AppendVirgilMessage("Historique local prêt.\nAucun envoi en ligne.\nExport manuel uniquement.");
        ReportsModuleView.FocusPrimaryButton();
        if (refresh)
        {
            _ = ReportsModuleView.RefreshAsync();
        }
    }

    private void HideAdditionalModules()
    {
        StartupModuleView.Visibility = Visibility.Collapsed;
        NetworkModuleView.Visibility = Visibility.Collapsed;
        ScanNavButton.Tag = null;
        StartupNavButton.Tag = null;
        NetworkNavButton.Tag = null;
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        if (_scanInProgress) return;
        ShowHome();
        HomeContentGrid.Visibility = Visibility.Collapsed;
        StartupModuleView.Visibility = Visibility.Visible;
        HomeNavButton.Tag = null;
        StartupNavButton.Tag = "Active";
        StatusText.Text = "DÉMARRAGE";
    }

    private async void Network_Click(object sender, RoutedEventArgs e)
    {
        if (_scanInProgress) return;
        ShowHome();
        HomeContentGrid.Visibility = Visibility.Collapsed;
        NetworkModuleView.Visibility = Visibility.Visible;
        HomeNavButton.Tag = null;
        NetworkNavButton.Tag = "Active";
        StatusText.Text = "RÉSEAU — LECTURE SEULE";
        await ((Virgil.App.ViewModels.NetworkViewModel)NetworkModuleView.DataContext).RefreshCommand.ExecuteAsync(null);
    }

    private void WireCleanupModule()
    {
        CleanupModuleView.VirgilMessageRequested += AppendVirgilMessage;
        CleanupModuleView.VirgilStateRequested += SetVirgilState;
        CleanupModuleView.ReturnHomeRequested += (_, _) => ShowHome();
        CleanupModuleView.ReportPersisted += HandleReportPersisted;
    }

    private void WireUpdatesModule()
    {
        UpdatesModuleView.VirgilMessageRequested += AppendVirgilMessage;
        UpdatesModuleView.VirgilStateRequested += SetVirgilState;
        UpdatesModuleView.ReturnHomeRequested += (_, _) => ShowHome();
        UpdatesModuleView.ReportPersisted += HandleReportPersisted;
    }

    private void WireInterventionsModule()
    {
        InterventionsModuleView.VirgilMessageRequested += AppendVirgilMessage;
        InterventionsModuleView.VirgilStateRequested += SetVirgilState;
        InterventionsModuleView.ReturnHomeRequested += (_, _) => ShowHome();
        InterventionsModuleView.ReportPersisted += HandleReportPersisted;
    }

    private void WireResourcesModule()
    {
        ResourcesModuleView.VirgilMessageRequested += AppendVirgilMessage;
        ResourcesModuleView.VirgilStateRequested += SetVirgilState;
        ResourcesModuleView.ReturnHomeRequested += (_, _) => ShowHome();
        ResourcesModuleView.ReportPersisted += HandleReportPersisted;
    }

    private void WireApplicationsModule()
    {
        ApplicationsModuleView.VirgilMessageRequested += AppendVirgilMessage;
        ApplicationsModuleView.VirgilStateRequested += SetVirgilState;
        ApplicationsModuleView.ReturnHomeRequested += (_, _) => ShowHome();
        ApplicationsModuleView.ReportPersisted += HandleReportPersisted;
    }

    private void WireReportsModule()
    {
        ReportsModuleView.VirgilMessageRequested += AppendVirgilMessage;
        ReportsModuleView.VirgilStateRequested += SetVirgilState;
        ReportsModuleView.ReturnHomeRequested += (_, _) => ShowHome();
        ReportsModuleView.ReportPersisted += HandleReportPersisted;
    }

    private void HandleReportPersisted(object? sender, EventArgs e)
    {
        _hasPersistentReport = true;
        LastReportButton.IsEnabled = !_scanInProgress;
    }

    private async void LastReport_Click(object sender, RoutedEventArgs e)
    {
        var persistent = await _reportHistoryService.GetLatestAsync(CancellationToken.None);
        if (persistent is not null)
        {
            ShowReports(refresh: false);
            await ReportsModuleView.OpenLatestAsync();
            return;
        }

        if (_lastReport is null)
        {
            AppendVirgilMessage("Aucun rapport disponible.\nLancez une analyse système.");
            return;
        }

        ShowReports(refresh: false);
        ReportsModuleView.ShowTransientReport(ReportMapper.FromSystemScan(_lastReport));
    }

    private void OpenScanProtocol()
    {
        if (_scanInProgress)
        {
            AppendVirgilMessage("Analyse déjà en cours.");
            return;
        }

        if (StartupModuleView.Visibility == Visibility.Visible ||
            NetworkModuleView.Visibility == Visibility.Visible ||
            CleanupModuleView.Visibility == Visibility.Visible ||
            UpdatesModuleView.Visibility == Visibility.Visible ||
            InterventionsModuleView.Visibility == Visibility.Visible ||
            DiagnosticModuleView.Visibility == Visibility.Visible ||
            ResourcesModuleView.Visibility == Visibility.Visible ||
            ApplicationsModuleView.Visibility == Visibility.Visible ||
            ReportsModuleView.Visibility == Visibility.Visible)
        {
            ShowHome();
        }

        HomeNavButton.Tag = null;
        ScanNavButton.Tag = "Active";
        ScanProtocolOverlay.Visibility = Visibility.Visible;
        QuickScanButton.Focus();
    }

    private void CloseScanProtocol()
    {
        ScanProtocolOverlay.Visibility = Visibility.Collapsed;
        ScanNavButton.Tag = null;
        HomeNavButton.Tag = "Active";
        MainScanButton.Focus();
    }

    private readonly Virgil.App.ViewModels.LongOperationViewModel _scanOperation;

    private async Task RunScanAsync(ScanMode mode)
    {
        if (!BeginScan(mode))
        {
            return;
        }

        try
        {
            var token = _activeScanCancellation!.Token;
            var progress = new Progress<ScanProgress>(value => { if (!token.IsCancellationRequested) HandleProgress(value); });
            var report = await _systemScanService.RunAsync(mode, progress, token).WaitAsync(token);
            _activeScanCancellation!.Token.ThrowIfCancellationRequested();
            _lastReport = report;
            await PersistReportSafelyAsync(ReportMapper.FromSystemScan(report));

            ApplyReport(report);
            CompleteScan(report);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Analyse annulée. Aucune modification effectuée.";
            GlobalStatusText.Text = "Analyse annulée. Aucune modification effectuée.";
            SetVirgilState(VirgilCoreState.Idle, "REPOS");
            AppendVirgilMessage("Analyse annulée. Aucune modification effectuée.");
        }
        catch
        {
            StatusText.Text = "ERREUR";
            GlobalStatusText.Text = "ÉCHEC DU SCAN";
            SetVirgilState(VirgilCoreState.Error, "ERREUR");
            AppendVirgilMessage("Analyse indisponible.\nAucune modification effectuée.");
        }
        finally
        {
            FinishScan();
        }
    }

    private bool BeginScan(ScanMode mode)
    {
        if (_scanInProgress)
        {
            AppendVirgilMessage("Analyse déjà en cours.");
            return false;
        }

        _scanInProgress = true;
        _reportedProgressSteps.Clear();
        _activeScanCancellation = new CancellationTokenSource();
        _scanOperation.Start("Analyse en cours…");
        CloseReport();
        CloseScanProtocol();
        SetInterfaceBusy(true);
        SetVirgilState(VirgilCoreState.Scanning, "SCAN EN COURS");
        StatusText.Text = "ANALYSE";
        GlobalStatusText.Text = "ANALYSE EN COURS";
        ReportCpuText.Text = "EN COURS";
        ReportRamText.Text = "EN COURS";
        ReportDiskText.Text = "EN COURS";
        ReportCleanupText.Text = mode == ScanMode.Deep ? "EN COURS" : "NON ANALYSÉ";
        ReportRecommendationsText.Text = "ANALYSE EN COURS";
        ReportErrorsText.Text = "AUCUNE";

        AppendVirgilMessage($"Protocole d'analyse initialisé.\nMode : {ModeLabel(mode).ToLowerInvariant()}.");
        AppendVirgilMessage("Analyse système en cours.");
        return true;
    }

    private void FinishScan()
    {
        _scanOperation.Complete(StatusText.Text);
        SetInterfaceBusy(false);
        _scanInProgress = false;
        _activeScanCancellation?.Dispose();
        _activeScanCancellation = null;
        SessionTimeText.Text = $"SESSION {DateTime.Now:HH:mm}";
    }

    private void HandleProgress(ScanProgress progress)
    {
        if (!_scanInProgress || _scanOperation.IsCancelling) return;
        _scanOperation.Report(new Virgil.Core.Operations.OperationProgress(progress.Step, Percent: progress.Percent, Message: progress.Message));
        StatusText.Text = progress.Step.ToUpperInvariant();

        if (!_reportedProgressSteps.Add(progress.Step))
        {
            return;
        }

        if (progress.Step is "Informations Windows" or "Stockage" or "Nettoyage potentiel")
        {
            AppendVirgilMessage(progress.Message);
        }
    }

    private void ApplyReport(SystemScanReport report)
    {
        var systemDisk = ScanRules.SelectSystemDisk(report.Disks, Environment.SystemDirectory);
        GlobalStatusText.Text = report.OverallStatus.ToUpperInvariant();
        ReportCpuText.Text = report.Resources.WasAnalyzed
            ? $"{report.Resources.AverageCpuPercent:0.0} % moyenne courte"
            : $"{report.Processor.UsagePercent:0.0} % - {report.Processor.Status}";
        ReportRamText.Text = report.Memory.TotalPhysicalBytes == 0
            ? "N/A - lecture mémoire indisponible"
            : $"{report.Memory.UsedPercent:0.0} % utilisés";
        ReportDiskText.Text = systemDisk is null
            ? "N/A - aucun disque fixe accessible"
            : $"{systemDisk.Name} : {systemDisk.UsedPercent:0.0} % utilisés";
        ReportCleanupText.Text = report.Cleanup.WasAnalyzed
            ? $"Sans risque : {FormatBytes(report.Cleanup.SafePotentialBytes)} · À vérifier : {FormatBytes(report.Cleanup.AdvancedPotentialBytes)} · {report.Cleanup.ReviewItemCount} éléments à revoir."
            : "Non analysé pour scan rapide.";
        ReportRecommendationsText.Text = report.Recommendations.Count == 0
            ? "Aucune recommandation prioritaire."
            : string.Join("\n", report.Recommendations.Take(3));
        ReportErrorsText.Text = report.Errors.Count == 0
            ? "AUCUNE"
            : string.Join("\n", report.Errors.Distinct().Take(4));

        ApplySystemStatus();
    }

    private void SystemStatusChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) ApplySystemStatus();
        else _ = Dispatcher.InvokeAsync(ApplySystemStatus);
    }

    private void ApplySystemStatus()
    {
        if (_statusStore.LastMemory is { } memory)
        {
            MemoryValueText.Text = $"{memory.Value.UsedPercent:0.0} %";
            MemoryDetailText.Text = $"{SystemStatusText.Bytes((long)memory.Value.UsedPhysicalBytes)} / {SystemStatusText.Bytes((long)memory.Value.TotalPhysicalBytes)} · {SystemStatusText.MeasuredAt(memory.CapturedAt)}";
        }
        if (_statusStore.LastSystemDisk is { } disk)
        {
            DiskValueText.Text = $"{disk.Value.UsedPercent:0.0} %";
            DiskDetailText.Text = $"{disk.Value.Name} · {SystemStatusText.Bytes(disk.Value.UsedBytes)} / {SystemStatusText.Bytes(disk.Value.TotalBytes)} · {SystemStatusText.MeasuredAt(disk.CapturedAt)}";
        }
        if (_statusStore.LastCleanupEstimate is { } cleanup)
        {
            CleanupValueText.Text = SystemStatusText.CleanupValue(cleanup.Value);
            CleanupDetailText.Text = SystemStatusText.CleanupDetail(cleanup.Value) + "\n" + SystemStatusText.MeasuredAt(cleanup.CapturedAt);
        }
        if (_statusStore.LastPriorities is { } priorities)
        {
            UpdatePriorities(priorities.Value);
            PriorityDetailText.Text += " · " + SystemStatusText.MeasuredAt(priorities.CapturedAt);
        }
    }

    private void CompleteScan(SystemScanReport report)
    {
        var priorityCount = CountPriorities(report);

        if (IsStable(report))
        {
            StatusText.Text = "SCAN TERMINÉ";
            SetVirgilState(VirgilCoreState.Success, "SUCCES");
        }
        else
        {
            StatusText.Text = "ATTENTION";
            SetVirgilState(VirgilCoreState.Warning, "ATTENTION");
        }

        if (report.Mode == ScanMode.Deep)
        {
            AppendVirgilMessage(
                $"Analyse approfondie terminée.\nCPU moyen court : {report.Resources.AverageCpuPercent:0.0} %.\n" +
                $"Processus lourds : {report.Resources.HeavyProcessCount}.\nAucune action exécutée.");
            return;
        }

        AppendVirgilMessage($"Scan rapide terminé.\nÉtat système : {report.OverallStatus.ToLowerInvariant()}.\nPriorités détectées : {priorityCount}.");
    }

    private void ShowLastReport(SystemScanReport report)
    {
        var systemDisk = ScanRules.SelectSystemDisk(report.Disks, Environment.SystemDirectory);

        ReportPopupSummaryText.Text = $"{ModeLabel(report.Mode)} - {report.OverallStatus} - {report.CapturedAt:yyyy-MM-dd HH:mm:ss}";
        ReportPopupDetailsText.Text = string.Join("\n", new[]
        {
            $"Mode : {ModeLabel(report.Mode)}",
            $"Date : {report.CapturedAt:yyyy-MM-dd HH:mm:ss}",
            $"Durée : {report.Duration.TotalSeconds:0.0} s",
            $"État général : {report.OverallStatus}",
            $"Windows : {report.Windows.Edition} - {report.Windows.Version} - build {report.Windows.Build}",
            $"Architecture : système {report.Windows.SystemArchitecture}, processus {report.Windows.ProcessArchitecture}",
            $"Machine : {report.Windows.MachineName}",
            $"Uptime : {FormatDuration(report.Windows.Uptime)}",
            $"CPU : {report.Processor.Name} - {report.Processor.LogicalProcessorCount} logiques - {report.Processor.UsagePercent:0.0} % ({report.Processor.Status})",
            $"Mémoire : {FormatBytes(report.Memory.UsedPhysicalBytes)} / {FormatBytes(report.Memory.TotalPhysicalBytes)} ({report.Memory.UsedPercent:0.0} %)",
            $"Disque système : {FormatDisk(systemDisk)}",
            $"Réseau : {FormatNetwork(report.Network)}",
            $"Nettoyage potentiel : {FormatCleanup(report.Cleanup)}",
            $"Ressources système : {FormatResources(report.Resources)}",
            $"Mises à jour : {FormatUpdates(report.Updates)}",
            $"Interventions ciblées : {FormatInterventions(report.Interventions)}",
            $"Applications : {FormatApplications(report.Applications)}"
        });
        ReportPopupRecommendationsText.Text = report.Recommendations.Count == 0
            ? "Aucune recommandation prioritaire."
            : string.Join("\n", report.Recommendations);
        ReportPopupErrorsText.Text = report.Errors.Count == 0
            ? "Aucune"
            : string.Join("\n", report.Errors);

        ReportOverlay.Visibility = Visibility.Visible;
        CloseReportButton.Focus();
    }

    private void CloseReport()
    {
        ReportOverlay.Visibility = Visibility.Collapsed;
    }

    private void PlayStartupAnimation()
    {
        if (_startupAnimationPlayed)
        {
            return;
        }

        _startupAnimationPlayed = true;

        if (!SystemParameters.ClientAreaAnimation)
        {
            FinishStartupVisuals();
            return;
        }

        try
        {
            PrepareStartupVisuals();

            var storyboard = new Storyboard
            {
                FillBehavior = FillBehavior.Stop
            };

            AddOpacityAnimation(storyboard, HeaderPanel, 0, 1, 0.00, 0.32);
            AddOpacityAnimation(storyboard, NavigationContainer, 0, 1, 0.12, 0.42);
            AddTranslateAnimation(storyboard, NavigationStartupTransform, TranslateTransform.XProperty, -10, 0, 0.12, 0.42);
            AddOpacityAnimation(storyboard, VirgilMainContainer, 0, 1, 0.24, 0.46);
            AddTranslateAnimation(storyboard, VirgilMainStartupTransform, TranslateTransform.YProperty, 10, 0, 0.24, 0.46);
            AddOpacityAnimation(storyboard, CommunicationContainer, 0, 1, 0.48, 0.38);
            AddTranslateAnimation(storyboard, CommunicationStartupTransform, TranslateTransform.XProperty, 10, 0, 0.48, 0.38);
            AddMetricAnimation(storyboard, MemoryMetricCard, MemoryMetricTransform, 0.62);
            AddMetricAnimation(storyboard, DiskMetricCard, DiskMetricTransform, 0.70);
            AddMetricAnimation(storyboard, CleanupMetricCard, CleanupMetricTransform, 0.78);
            AddMetricAnimation(storyboard, PriorityMetricCard, PriorityMetricTransform, 0.86);

            storyboard.Completed += StartupAnimation_Completed;
            _startupStoryboard = storyboard;
            storyboard.Begin(this, true);
        }
        catch
        {
            StopStartupStoryboard();
            FinishStartupVisuals();
        }
    }

    private void StartupAnimation_Completed(object? sender, EventArgs e)
    {
        if (_startupStoryboard is not null)
        {
            _startupStoryboard.Completed -= StartupAnimation_Completed;
            _startupStoryboard.Remove(this);
            _startupStoryboard = null;
        }

        FinishStartupVisuals();
    }

    private void StopStartupStoryboard()
    {
        if (_startupStoryboard is null)
        {
            return;
        }

        _startupStoryboard.Completed -= StartupAnimation_Completed;
        _startupStoryboard.Stop(this);
        _startupStoryboard.Remove(this);
        _startupStoryboard = null;
    }

    private void PrepareStartupVisuals()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            FinishStartupVisuals();
            return;
        }

        HeaderPanel.Opacity = 0;
        NavigationContainer.Opacity = 0;
        VirgilMainContainer.Opacity = 0;
        CommunicationContainer.Opacity = 0;
        NavigationStartupTransform.X = -10;
        VirgilMainStartupTransform.Y = 10;
        CommunicationStartupTransform.X = 10;
        SetMetricCardState(0, 8);
    }

    private void FinishStartupVisuals()
    {
        HeaderPanel.Opacity = 1;
        NavigationContainer.Opacity = 1;
        VirgilMainContainer.Opacity = 1;
        CommunicationContainer.Opacity = 1;
        NavigationStartupTransform.X = 0;
        VirgilMainStartupTransform.Y = 0;
        CommunicationStartupTransform.X = 0;
        MetricsStartupTransform.Y = 0;
        SetMetricCardState(1, 0);
        ScanProtocolOverlay.Visibility = Visibility.Collapsed;
        ReportOverlay.Visibility = Visibility.Collapsed;
        SetVirgilState(VirgilCoreState.Idle, "REPOS");
    }

    private void SetMetricCardState(double opacity, double offsetY)
    {
        MemoryMetricCard.Opacity = opacity;
        DiskMetricCard.Opacity = opacity;
        CleanupMetricCard.Opacity = opacity;
        PriorityMetricCard.Opacity = opacity;
        MemoryMetricTransform.Y = offsetY;
        DiskMetricTransform.Y = offsetY;
        CleanupMetricTransform.Y = offsetY;
        PriorityMetricTransform.Y = offsetY;
    }

    private static void AddMetricAnimation(
        Storyboard storyboard,
        UIElement card,
        TranslateTransform transform,
        double beginSeconds)
    {
        AddOpacityAnimation(storyboard, card, 0, 1, beginSeconds, 0.28);
        AddTranslateAnimation(storyboard, transform, TranslateTransform.YProperty, 8, 0, beginSeconds, 0.28);
    }

    private static void AddOpacityAnimation(
        Storyboard storyboard,
        DependencyObject target,
        double from,
        double to,
        double beginSeconds,
        double durationSeconds)
    {
        AddDoubleAnimation(storyboard, target, UIElement.OpacityProperty, from, to, beginSeconds, durationSeconds);
    }

    private static void AddTranslateAnimation(
        Storyboard storyboard,
        DependencyObject target,
        DependencyProperty property,
        double from,
        double to,
        double beginSeconds,
        double durationSeconds)
    {
        AddDoubleAnimation(storyboard, target, property, from, to, beginSeconds, durationSeconds);
    }

    private static void AddDoubleAnimation(
        Storyboard storyboard,
        DependencyObject target,
        DependencyProperty property,
        double from,
        double to,
        double beginSeconds,
        double durationSeconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(durationSeconds))
        {
            BeginTime = TimeSpan.FromSeconds(beginSeconds),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private void UpdatePriorities(int priorityCount)
    {
        PriorityValueText.Text = priorityCount.ToString();
        PriorityDetailText.Text = priorityCount == 0
            ? "AUCUNE PRIORITÉ"
            : priorityCount == 1 ? "1 POINT À VÉRIFIER" : $"{priorityCount} POINTS À VÉRIFIER";
    }

    private void AppendVirgilMessage(string message)
    {
        var cleanMessage = NormalizeVirgilMessage(message);
        if (string.IsNullOrWhiteSpace(cleanMessage))
        {
            return;
        }

        var entry = new TextBlock
        {
            Text = "[VIRGIL]\n" + cleanMessage,
            Style = TryFindResource("VirgilChatMessageText") as Style,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        ChatMessagesPanel.Children.Add(entry);
        VirgilCore.NotifyCommunication();
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => ChatScrollViewer.ScrollToEnd()));
    }

    private void SetVirgilState(VirgilCoreState state, string label)
    {
        VirgilCore.SetState(state);
        VirgilStateText.Text = $"ÉTAT VIRGIL : {label}";
    }

    private async Task PersistReportSafelyAsync(ReportEntry report)
    {
        var result = await _reportHistoryService.SaveAsync(report, CancellationToken.None);
        if (result.Success)
        {
            _hasPersistentReport = true;
            LastReportButton.IsEnabled = !_scanInProgress;
            AppendVirgilMessage("Rapport généré et conservé localement.\nAucun envoi en ligne.");
            if (!string.IsNullOrWhiteSpace(result.ReadableError))
            {
                AppendVirgilMessage(result.ReadableError);
            }

            return;
        }

        AppendVirgilMessage(result.ReadableError ?? "Historique local indisponible.\nRapport conservé en mémoire.");
    }

    private async Task RefreshPersistentReportStateAsync()
    {
        var latest = await _reportHistoryService.GetLatestAsync(CancellationToken.None);
        _hasPersistentReport = latest is not null;
        LastReportButton.IsEnabled = _hasPersistentReport || _lastReport is not null;
    }

    private void SetInterfaceBusy(bool busy)
    {
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
        MainScanButton.IsEnabled = !busy;
        LastReportButton.IsEnabled = !busy && (_hasPersistentReport || _lastReport is not null);
        NavigationPanel.IsEnabled = !busy;
        QuickScanButton.IsEnabled = !busy;
        DeepScanButton.IsEnabled = !busy;
        CancelProtocolButton.IsEnabled = !busy;
    }

    private static int CountPriorities(SystemScanReport report)
    {
        return report.Findings.Count(finding => finding.Severity >= ScanSeverity.Attention);
    }

    private static bool IsStable(SystemScanReport report)
    {
        return string.Equals(report.OverallStatus, "Stable", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVirgilMessage(string message)
    {
        var cleanMessage = message.Trim();
        const string prefix = "[VIRGIL]";

        while (cleanMessage.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            cleanMessage = cleanMessage[prefix.Length..].TrimStart(' ', '-', ':', '\r', '\n');
        }

        return cleanMessage;
    }

    private static string ModeLabel(ScanMode mode)
    {
        return mode == ScanMode.Deep ? "Approfondi" : "Rapide";
    }

    private static string FormatDisk(DiskScanInfo? disk)
    {
        return disk is null
            ? "N/A"
            : $"{disk.Name} {FormatBytes(disk.UsedBytes)} / {FormatBytes(disk.TotalBytes)} ({disk.UsedPercent:0.0} %, {disk.Status})";
    }

    private static string FormatNetwork(NetworkScanInfo network)
    {
        var speed = network.SpeedBitsPerSecond > 0
            ? $"{network.SpeedBitsPerSecond / 1_000_000d:0.#} Mb/s"
            : "N/A";
        var dns = network.DnsServers.Count == 0 ? "N/A" : string.Join(", ", network.DnsServers);

        return $"{network.Name} - {network.Type} - {network.Status} - {speed} - IPv4 {network.IPv4Address} - passerelle {network.Gateway} - DNS {dns}";
    }

    private static string FormatCleanup(CleanupScanInfo cleanup)
    {
        return cleanup.WasAnalyzed
            ? $"sur {FormatBytes(cleanup.SafePotentialBytes)}, avancé {FormatBytes(cleanup.AdvancedPotentialBytes)}, {cleanup.ReviewItemCount} a revoir ({FormatBytes(cleanup.ReviewItemBytes)}), protection personnelle active, aucune action, zones : {FormatZones(cleanup.Zones)}"
            : "Non analysé";
    }

    private static string FormatUpdates(UpdateScanSummary updates)
    {
        if (!updates.WasAnalyzed)
        {
            return "Non analysé";
        }

        return string.Join(", ", new[]
        {
            updates.Status,
            updates.WingetAvailable ? "WinGet disponible" : "WinGet non détecté",
            $"{updates.ApplicationUpdates} applicatives",
            $"{updates.SensitiveUpdates} sensibles",
            $"{updates.DriverCount} pilotes inventoriés"
        });
    }

    private static string FormatResources(ResourceScanSummary resources)
    {
        if (!resources.WasAnalyzed)
        {
            return "Non analysé";
        }

        var topMemory = resources.TopMemoryProcesses.Count == 0
            ? "aucun processus accessible"
            : string.Join(", ", resources.TopMemoryProcesses);
        return $"CPU moyen {resources.AverageCpuPercent:0.0} %, RAM {resources.MemoryPercent:0.0} %, " +
            $"{resources.HeavyProcessCount} lourds, session {FormatDuration(resources.Uptime)}, " +
            $"principaux RAM : {topMemory}, aucune action exécutée";
    }

    private static string FormatInterventions(InterventionScanSummary interventions)
    {
        if (!interventions.WasAnalyzed)
        {
            return "Non analysé";
        }

        return string.Join(", ", new[]
        {
            $"{interventions.AvailableActions} disponibles",
            $"{interventions.RecommendedActions} recommandées",
            interventions.RebootPotentiallyRequired
                ? "redémarrage manuel possible"
                : "pas de redémarrage attendu",
            "aucune exécution depuis le scan"
        });
    }

    private static string FormatApplications(ApplicationScanSummary applications)
    {
        if (!applications.WasAnalyzed)
        {
            return "Non analysé";
        }

        return string.Join(", ", new[]
        {
            $"{applications.DetectedCount} détectées",
            $"{applications.UninstallableCount} désinstallables",
            $"{applications.ProtectedCount} protégées",
            $"{applications.UnknownCount} inconnues",
            $"{applications.LargeApplicationCount} volumineuses"
        });
    }

    private static string FormatZones(IReadOnlyList<string> zones)
    {
        return zones.Count == 0 ? "N/A" : string.Join(", ", zones);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalDays >= 1
            ? $"{(int)duration.TotalDays} j {duration.Hours} h"
            : $"{duration.Hours} h {duration.Minutes} min";
    }

    private static string FormatBytes(ulong bytes) => ScanRules.FormatBytes(bytes);

    private static string FormatBytes(long bytes) => ScanRules.FormatBytes(bytes);
}

