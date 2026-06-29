using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Virgil.App.Controls;
using Virgil.Core.Applications;
using Virgil.Core.Reports;
using Virgil.Core.Scanning;
using Virgil.Domain;
using Virgil.Domain.Applications;

namespace Virgil.App.Views;

public partial class ApplicationsView : UserControl
{
    private readonly IApplicationInventoryService _inventoryService;
    private readonly ApplicationUninstallService _uninstallService;
    private readonly List<InstalledApplication> _applications = new();
    private IReportHistoryService? _reportHistoryService;
    private CancellationTokenSource? _operationCancellation;
    private ApplicationInventoryReport? _lastInventoryReport;
    private ApplicationUninstallResult? _lastUninstallResult;
    private InstalledApplication? _selectedApplication;
    private bool _operationInProgress;

    public ApplicationsView()
        : this(new ApplicationInventoryService(), new ApplicationUninstallService())
    {
    }

    public ApplicationsView(
        IApplicationInventoryService inventoryService,
        ApplicationUninstallService uninstallService)
    {
        InitializeComponent();
        _inventoryService = inventoryService;
        _uninstallService = uninstallService;
    }

    public event Action<string>? VirgilMessageRequested;

    public event Action<VirgilCoreState, string>? VirgilStateRequested;

    public event EventHandler? ReturnHomeRequested;

    public event EventHandler? ReportPersisted;

    public void ConfigureReportHistory(IReportHistoryService reportHistoryService)
    {
        _reportHistoryService = reportHistoryService;
    }

    public void FocusAnalyzeButton()
    {
        AnalyzeApplicationsButton.Focus();
    }

    public void CancelActiveOperation()
    {
        _operationCancellation?.Cancel();
    }

    public bool TryCloseOverlay()
    {
        if (ApplicationsReportOverlay.Visibility == Visibility.Visible)
        {
            HideReport();
            return true;
        }

        if (ApplicationDetailsOverlay.Visibility == Visibility.Visible)
        {
            HideDetails();
            return true;
        }

        if (_operationInProgress)
        {
            CancelActiveOperation();
            VirgilMessageRequested?.Invoke("Annulation demandee.\nOperation applications en cours d'arret.");
            return true;
        }

        return false;
    }

    private async void AnalyzeApplications_Click(object sender, RoutedEventArgs e)
    {
        await AnalyzeAsync().ConfigureAwait(true);
    }

    private void ViewApplicationsReport_Click(object sender, RoutedEventArgs e)
    {
        ShowReport();
    }

    private void ReturnHome_Click(object sender, RoutedEventArgs e)
    {
        ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SearchApplications_Changed(object sender, TextChangedEventArgs e)
    {
        RenderApplications();
    }

    private void FilterApplications_Changed(object sender, SelectionChangedEventArgs e)
    {
        RenderApplications();
    }

    private async void LaunchUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedApplication is not null)
        {
            await LaunchUninstallAsync(_selectedApplication).ConfigureAwait(true);
        }
    }

    private async void OpenApplicationLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedApplication is not null)
        {
            await OpenApplicationLocationOrSettingsAsync(_selectedApplication).ConfigureAwait(true);
        }
    }

    private void CloseDetails_Click(object sender, RoutedEventArgs e)
    {
        HideDetails();
    }

    private void CloseApplicationsReport_Click(object sender, RoutedEventArgs e)
    {
        HideReport();
    }

    private async Task AnalyzeAsync()
    {
        if (!BeginOperation("Inventaire applications en cours."))
        {
            return;
        }

        try
        {
            VirgilStateRequested?.Invoke(VirgilCoreState.Scanning, "APPLICATIONS");
            VirgilMessageRequested?.Invoke("Inventaire applications lance.\nLecture seule.\nAucune desinstallation.");
            var progress = new Progress<ApplicationInventoryProgress>(item =>
            {
                ApplicationsProgressBar.Value = item.Percent ?? 0;
                ApplicationsStatusText.Text = $"{item.Status} {item.Percent ?? 0} %";
            });

            var report = await _inventoryService
                .InventoryAsync(progress, _operationCancellation!.Token)
                .ConfigureAwait(true);

            _lastInventoryReport = report;
            _lastUninstallResult = null;
            _applications.Clear();
            _applications.AddRange(report.Applications);
            RenderInventory(report);
            RenderApplications();
            await PersistReportAsync(ApplicationReportMapper.FromInventory(report)).ConfigureAwait(true);
            ViewApplicationsReportButton.IsEnabled = true;
            ApplicationsStatusText.Text = "Inventaire termine.";
            VirgilStateRequested?.Invoke(report.Errors.Count == 0 ? VirgilCoreState.Success : VirgilCoreState.Warning, "APPLICATIONS");
            VirgilMessageRequested?.Invoke(
                $"Inventaire termine.\nApplications : {report.Applications.Count}.\nDesinstallables : {report.UninstallableCount}.");
        }
        catch (OperationCanceledException)
        {
            ApplicationsStatusText.Text = "Inventaire annule.";
            VirgilStateRequested?.Invoke(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Inventaire applications annule.\nAucune action effectuee.");
        }
        catch
        {
            ApplicationsStatusText.Text = "Inventaire applications indisponible.";
            VirgilStateRequested?.Invoke(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Inventaire applications indisponible.\nAucune action effectuee.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task LaunchUninstallAsync(InstalledApplication application)
    {
        if (!application.CanUninstall)
        {
            VirgilMessageRequested?.Invoke("Desinstallation bloquee.\nApplication protegee, inconnue ou Store.");
            return;
        }

        if (!BeginOperation("Desinstallateur officiel en cours de lancement."))
        {
            return;
        }

        try
        {
            VirgilStateRequested?.Invoke(VirgilCoreState.SensitiveAction, "VALIDATION");
            var progress = new Progress<ApplicationUninstallProgress>(item =>
            {
                ApplicationsProgressBar.Value = item.Percent ?? 0;
                ApplicationsStatusText.Text = item.Status;
            });

            var result = await _uninstallService
                .LaunchOfficialUninstallAsync(application, userConfirmed: true, progress, _operationCancellation!.Token)
                .ConfigureAwait(true);

            _lastUninstallResult = result;
            RenderUninstallResult(result);
            ShowDetails(application);
            await PersistReportAsync(ApplicationReportMapper.FromUninstall(result)).ConfigureAwait(true);
            ViewApplicationsReportButton.IsEnabled = true;
            VirgilStateRequested?.Invoke(result.Errors.Count == 0 ? VirgilCoreState.Success : VirgilCoreState.Warning, "APPLICATIONS");
            VirgilMessageRequested?.Invoke(result.WasLaunched
                ? "Desinstalleur officiel lance.\nLe statut final peut dependre de l'assistant externe.\nRestes analyses en lecture seule."
                : "Desinstalleur non lance.\nRapport disponible.");
        }
        catch (OperationCanceledException)
        {
            ApplicationsStatusText.Text = "Desinstallation annulee.";
            VirgilStateRequested?.Invoke(VirgilCoreState.Idle, "REPOS");
        }
        catch
        {
            ApplicationsStatusText.Text = "Desinstallateur officiel indisponible.";
            VirgilStateRequested?.Invoke(VirgilCoreState.Error, "ERREUR");
        }
        finally
        {
            EndOperation();
        }
    }

    private void RenderInventory(ApplicationInventoryReport report)
    {
        InventorySummaryText.Text = $"{report.Applications.Count} detectees, {report.UninstallableCount} desinstallables, {report.Errors.Count} avertissement(s).";
        RiskSummaryText.Text = $"{report.SafeCount} simples, {report.CautionCount} attention, {report.ProtectedCount} protegees, {report.UnknownCount} inconnues.";
    }

    private void RenderApplications()
    {
        if (ApplicationsListPanel is null)
        {
            return;
        }

        ApplicationsListPanel.Children.Clear();
        var items = FilteredApplications().ToList();
        if (items.Count == 0)
        {
            ApplicationsListPanel.Children.Add(CreateTextCard(_applications.Count == 0
                ? "Aucune application inventoriee."
                : "Aucune application ne correspond au filtre."));
            return;
        }

        foreach (var application in items)
        {
            ApplicationsListPanel.Children.Add(CreateApplicationCard(application));
        }
    }

    private IEnumerable<InstalledApplication> FilteredApplications()
    {
        var query = ApplicationSearchBox?.Text?.Trim() ?? string.Empty;
        var filter = ((ApplicationFilterBox?.SelectedItem as ComboBoxItem)?.Content as string) ?? "TOUT";
        return _applications.Where(application =>
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
                "DESINSTALLABLE" => application.CanUninstall,
                "ATTENTION" => application.RiskLevel == ApplicationRiskLevel.Caution,
                "PROTEGE" => application.RiskLevel == ApplicationRiskLevel.Protected,
                "INCONNU" => application.RiskLevel == ApplicationRiskLevel.Unknown,
                _ => true
            };
        });
    }

    private UIElement CreateApplicationCard(InstalledApplication application)
    {
        var card = new Border
        {
            Style = TryFindResource("VirgilHudCard") as Style,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var stack = new StackPanel();
        stack.Children.Add(CreateText(application.DisplayName, primary: true));
        stack.Children.Add(CreateText(string.Join("\n", new[]
        {
            $"Editeur : {Empty(application.Publisher)}",
            $"Version : {Empty(application.Version)}",
            $"Source : {string.Join(", ", application.Sources.Count == 0 ? [application.Source] : application.Sources)}",
            $"Risque : {RiskLabel(application.RiskLevel)} - {application.RiskReason}",
            $"Taille estimee : {FormatBytes(application.EstimatedSizeBytes)}"
        })));

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(CreateButton("DETAILS", () =>
        {
            ShowDetails(application);
            return Task.CompletedTask;
        }, primary: true));

        if (application.CanUninstall)
        {
            actions.Children.Add(CreateButton("DESINSTALLER", () => LaunchUninstallAsync(application)));
        }

        if (application.CanOpenLocation || application.UninstallKind == ApplicationUninstallKind.StoreSettings)
        {
            actions.Children.Add(CreateButton(
                application.UninstallKind == ApplicationUninstallKind.StoreSettings ? "PARAMETRES" : "OUVRIR EMPLACEMENT",
                () => OpenApplicationLocationOrSettingsAsync(application)));
        }

        stack.Children.Add(actions);
        card.Child = stack;
        return card;
    }

    private void ShowDetails(InstalledApplication application)
    {
        _selectedApplication = application;
        DetailsTitleText.Text = application.DisplayName;
        DetailsBodyText.Text = string.Join("\n", new[]
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
        DetailsCommandText.Text = application.UninstallKind switch
        {
            ApplicationUninstallKind.Winget => $"winget uninstall --id {application.WingetId} --exact",
            ApplicationUninstallKind.StoreSettings => "Parametres Windows. Aucun retrait Store execute par Virgil V1.",
            _ => Empty(application.UninstallCommand ?? application.QuietUninstallCommand)
        };
        DetailsSafetyText.Text = string.Join("\n", new[]
        {
            "Desinstallation individuelle uniquement.",
            "Desinstalleur officiel ou WinGet exact uniquement.",
            "Suppression par dossier interdite.",
            "Aucune suppression automatique de donnees personnelles.",
            "Pilotes, securite, runtimes et composants systeme bloques.",
            application.RiskLevel == ApplicationRiskLevel.Caution
                ? "Attention : cette application peut contenir profils, projets, presets ou sauvegardes."
                : "Validation stricte appliquee avant lancement."
        });
        LaunchUninstallButton.IsEnabled = application.CanUninstall && !_operationInProgress;
        OpenApplicationLocationButton.IsEnabled = application.CanOpenLocation || application.UninstallKind == ApplicationUninstallKind.StoreSettings;
        OpenApplicationLocationButton.Content = application.UninstallKind == ApplicationUninstallKind.StoreSettings
            ? "PARAMETRES WINDOWS"
            : "OUVRIR EMPLACEMENT";
        RenderDetailsRemnants(application);
        ApplicationDetailsOverlay.Visibility = Visibility.Visible;
        CloseDetailsButton.Focus();
    }

    private void RenderDetailsRemnants(InstalledApplication application)
    {
        DetailsRemnantsPanel.Children.Clear();
        var result = _lastUninstallResult;
        if (result?.Application.Id != application.Id)
        {
            DetailsRemnantsPanel.Children.Add(CreateText("Disponibles apres lancement du desinstallateur officiel. Analyse lecture seule uniquement."));
            return;
        }

        if (result.Remnants.Remnants.Count == 0)
        {
            DetailsRemnantsPanel.Children.Add(CreateText("Aucun reste accessible detecte en lecture seule."));
            return;
        }

        foreach (var remnant in result.Remnants.Remnants.Take(12))
        {
            DetailsRemnantsPanel.Children.Add(CreateText(
                $"{RemnantKindLabel(remnant.Kind)} - {remnant.Path}\n{remnant.Reason}\nAction automatique : aucune."));
        }
    }

    private void RenderUninstallResult(ApplicationUninstallResult result)
    {
        ApplicationsStatusText.Text = result.Result;
        ApplicationsReportSummaryText.Text = $"{result.Application.DisplayName}\n{result.Result}";
        ApplicationsReportDetailsText.Text = string.Join("\n", new[]
        {
            $"Methode : {result.Method}",
            $"Lance : {(result.WasLaunched ? "oui" : "non")}",
            $"Statut inconnu : {(result.StatusUnknown ? "oui" : "non")}",
            $"Code sortie : {(result.ExitCode.HasValue ? result.ExitCode.Value.ToString() : "N/A")}",
            $"Restes : {result.Remnants.Remnants.Count}",
            $"Techniques : {result.Remnants.TechnicalCount}",
            $"Personnels/proteges : {result.Remnants.UserDataCount + result.Remnants.ProtectedCount}",
            "Suppression automatique : aucune"
        });
        ApplicationsReportErrorsText.Text = result.Errors.Count == 0
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
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                VirgilMessageRequested?.Invoke("Emplacement introuvable.");
                return;
            }

            VirgilMessageRequested?.Invoke("Emplacement ouvert.\nAucune suppression executee.");
        }
        catch
        {
            VirgilMessageRequested?.Invoke("Ouverture emplacement impossible.");
        }
    }

    private static Task OpenWindowsSettingsAsync()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:appsfeatures",
                UseShellExecute = true
            });
        }
        catch
        {
            // Non blocking. The report and inventory remain available.
        }

        return Task.CompletedTask;
    }

    private void ShowReport()
    {
        if (_lastUninstallResult is not null)
        {
            RenderUninstallResult(_lastUninstallResult);
        }
        else if (_lastInventoryReport is not null)
        {
            ApplicationsReportSummaryText.Text = $"{_lastInventoryReport.Applications.Count} application(s), {_lastInventoryReport.UninstallableCount} desinstallable(s).";
            ApplicationsReportDetailsText.Text = string.Join("\n", new[]
            {
                $"Protegees : {_lastInventoryReport.ProtectedCount}",
                $"Attention : {_lastInventoryReport.CautionCount}",
                $"Inconnues : {_lastInventoryReport.UnknownCount}",
                "Aucune action executee depuis l'inventaire.",
                "Aucune donnee personnelle supprimee automatiquement."
            });
            ApplicationsReportErrorsText.Text = _lastInventoryReport.Errors.Count == 0
                ? "Aucune"
                : string.Join("\n", _lastInventoryReport.Errors);
        }
        else
        {
            ApplicationsReportSummaryText.Text = "Aucun rapport applications disponible.";
            ApplicationsReportDetailsText.Text = "Lancez un inventaire applications.";
            ApplicationsReportErrorsText.Text = "Aucune";
        }

        ApplicationsReportOverlay.Visibility = Visibility.Visible;
    }

    private void HideReport()
    {
        ApplicationsReportOverlay.Visibility = Visibility.Collapsed;
    }

    private void HideDetails()
    {
        ApplicationDetailsOverlay.Visibility = Visibility.Collapsed;
        _selectedApplication = null;
    }

    private async Task PersistReportAsync(ReportEntry report)
    {
        if (_reportHistoryService is null)
        {
            return;
        }

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
            VirgilMessageRequested?.Invoke("Operation applications deja en cours.");
            return false;
        }

        _operationInProgress = true;
        _operationCancellation = new CancellationTokenSource();
        ApplicationsStatusText.Text = status;
        ApplicationsProgressBar.Value = 0;
        SetInteractionEnabled(false);
        Cursor = Cursors.Wait;
        return true;
    }

    private void EndOperation()
    {
        _operationInProgress = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        SetInteractionEnabled(true);
        Cursor = Cursors.Arrow;
    }

    private void SetInteractionEnabled(bool enabled)
    {
        AnalyzeApplicationsButton.IsEnabled = enabled;
        ViewApplicationsReportButton.IsEnabled = enabled && (_lastInventoryReport is not null || _lastUninstallResult is not null);
        ReturnHomeButton.IsEnabled = enabled;
        ApplicationSearchBox.IsEnabled = enabled;
        ApplicationFilterBox.IsEnabled = enabled;
        LaunchUninstallButton.IsEnabled = enabled && _selectedApplication?.CanUninstall == true;
        OpenApplicationLocationButton.IsEnabled = enabled && (_selectedApplication?.CanOpenLocation == true ||
            _selectedApplication?.UninstallKind == ApplicationUninstallKind.StoreSettings);
    }

    private Border CreateTextCard(string text)
    {
        return new Border
        {
            Style = TryFindResource("VirgilHudCard") as Style,
            Margin = new Thickness(0, 0, 0, 8),
            Child = CreateText(text)
        };
    }

    private TextBlock CreateText(string text, bool primary = false)
    {
        return new TextBlock
        {
            Text = text,
            Style = TryFindResource(primary ? "VirgilHudSectionTitle" : "VirgilHudSecondaryText") as Style,
            TextWrapping = TextWrapping.Wrap,
            Margin = primary ? new Thickness(0, 0, 0, 6) : new Thickness(0, 0, 0, 0)
        };
    }

    private Button CreateButton(string content, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Content = content,
            Style = TryFindResource(primary ? "VirgilPrimaryButton" : "VirgilSecondaryButton") as Style,
            Margin = new Thickness(0, 0, 8, 8),
            MinWidth = 110
        };
        button.Click += async (_, _) => await action().ConfigureAwait(true);
        return button;
    }

    private static string RiskLabel(ApplicationRiskLevel risk)
    {
        return risk switch
        {
            ApplicationRiskLevel.SafeToUninstall => "desinstallable",
            ApplicationRiskLevel.Caution => "attention",
            ApplicationRiskLevel.Protected => "protege",
            _ => "inconnu"
        };
    }

    private static string RemnantKindLabel(ApplicationRemnantKind kind)
    {
        return kind switch
        {
            ApplicationRemnantKind.TechnicalRemnant => "technique",
            ApplicationRemnantKind.UserData => "donnee personnelle",
            ApplicationRemnantKind.ProtectedRemnant => "protege",
            _ => "inconnu"
        };
    }

    private static string Empty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "N/A" : value;
    }

    private static string FormatBytes(long? bytes)
    {
        return bytes.HasValue ? ScanRules.FormatBytes(bytes.Value) : "N/A";
    }
}
