using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Virgil.App.Controls;
using Virgil.Core.Cleanup;
using Virgil.Core.Monitoring;
using Virgil.Domain;

namespace Virgil.App;

public partial class MainWindow : Window
{
    private readonly IMonitoringService _monitoringService = new MonitoringService();
    private readonly ICleanupService _cleanupService = new CleanupPreviewService();

    private SystemHealthSnapshot? _lastSnapshot;
    private CleanupPreview? _lastCleanupPreview;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        SetCoreState(VirgilCoreState.Idle, "REPOS");
        AppendVirgilMessage("Système prêt.\nAucune analyse récente.");
    }

    public void AppendVirgilMessage(string message)
    {
        var bubble = new Border
        {
            Style = (Style)FindResource("VirgilChatBubble"),
            Child = new TextBlock
            {
                Text = "[VIRGIL]\n" + message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("App.TextPrimaryBrush"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                LineHeight = 18
            }
        };

        ChatMessages.Children.Add(bubble);
        ChatScrollViewer.ScrollToEnd();
    }

    private void ShowScanOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        ScanOverlay.Visibility = Visibility.Visible;
        OverlayCore.SetState(VirgilCoreState.Communicating);
        SetCoreState(VirgilCoreState.Communicating, "SÉLECTION");
        StatusText.Text = "Protocole en attente";
        StatusIndicator.Fill = FindBrush("App.AccentBrush");
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e)
    {
        ScanOverlay.Visibility = Visibility.Collapsed;
        SetCoreState(VirgilCoreState.Idle, "REPOS");
        StatusText.Text = "Système prêt";
        StatusIndicator.Fill = FindBrush("App.SuccessBrush");
    }

    private async void RunQuickScan_Click(object sender, RoutedEventArgs e)
    {
        await RunScanAsync(includeCleanupPreview: false);
    }

    private async void RunDeepScan_Click(object sender, RoutedEventArgs e)
    {
        await RunScanAsync(includeCleanupPreview: true);
    }

    private async Task RunScanAsync(bool includeCleanupPreview)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        SetScanControlsEnabled(false);
        ContextActionsPanel.Children.Clear();
        ScanOverlay.Visibility = Visibility.Collapsed;
        SetCoreState(VirgilCoreState.Scanning, "ANALYSE");
        OverlayCore.SetState(VirgilCoreState.Scanning);
        StatusText.Text = "Scan système en cours";
        StatusIndicator.Fill = FindBrush("App.AccentBrush");
        AppendVirgilMessage("Protocole d'analyse initialisé.\nScan système en cours.");

        try
        {
            var snapshot = await Task.Run(() => _monitoringService.CaptureSnapshot());
            _lastSnapshot = snapshot;
            RenderSnapshot(snapshot);

            if (includeCleanupPreview)
            {
                var cleanupPreview = await Task.Run(() => _cleanupService.PreviewTemporaryFiles());
                _lastCleanupPreview = cleanupPreview;
                RenderCleanupPreview(cleanupPreview);
                AppendVirgilMessage($"Analyse approfondie terminée.\nNettoyage potentiel : {FormatBytes(cleanupPreview.TotalBytes)}.\nActions ciblées disponibles.");
            }
            else
            {
                AppendVirgilMessage($"Scan rapide terminé.\nÉtat système : {snapshot.OverallStatus}.\nPriorités détectées : {snapshot.Recommendations.Count}.");
            }

            ShowContextActions();
            StatusText.Text = "Analyse terminée";
            StatusIndicator.Fill = snapshot.Recommendations.Count == 0
                ? FindBrush("App.SuccessBrush")
                : FindBrush("App.WarningBrush");
            SetCoreState(snapshot.Recommendations.Count == 0 ? VirgilCoreState.Success : VirgilCoreState.Warning, "SYNTHÈSE");

            await Task.Delay(850);

            if (!_isBusy)
            {
                return;
            }

            SetCoreState(snapshot.Recommendations.Count == 0 ? VirgilCoreState.Idle : VirgilCoreState.Warning,
                snapshot.Recommendations.Count == 0 ? "REPOS" : "ALERTE");
        }
        catch (Exception ex)
        {
            SetCoreState(VirgilCoreState.Error, "ERREUR");
            StatusText.Text = "Action interrompue";
            StatusIndicator.Fill = FindBrush("App.ErrorBrush");
            AppendVirgilMessage($"Action interrompue.\nCause probable : {GetFriendlyCause(ex)}.\nDétails techniques disponibles.");
        }
        finally
        {
            _isBusy = false;
            SetScanControlsEnabled(true);
        }
    }

    private void RenderSnapshot(SystemHealthSnapshot snapshot)
    {
        if (snapshot.Memory.TotalBytes == 0)
        {
            RamMetricText.Text = "N/A";
            RamDetailText.Text = "Lecture RAM inaccessible.";
            AppendVirgilMessage("Lecture RAM inaccessible.\nValeur affichée : N/A.");
        }
        else
        {
            RamMetricText.Text = $"{snapshot.Memory.UsedPercent:0.0} %";
            RamDetailText.Text = $"{FormatBytes(snapshot.Memory.UsedBytes)} utilisés / {FormatBytes(snapshot.Memory.TotalBytes)}";
        }

        var systemDrive = snapshot.Drives.FirstOrDefault(drive => string.Equals(drive.Name, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))
            ?? snapshot.Drives.FirstOrDefault();

        if (systemDrive is null)
        {
            DiskMetricText.Text = "N/A";
            DiskDetailText.Text = "Disque système inaccessible.";
            AppendVirgilMessage("Lecture disque inaccessible.\nValeur affichée : N/A.");
        }
        else
        {
            DiskMetricText.Text = $"{systemDrive.UsedPercent:0.0} %";
            DiskDetailText.Text = $"{systemDrive.Name} - {FormatBytes(systemDrive.UsedBytes)} utilisés / {FormatBytes(systemDrive.TotalBytes)}";
        }

        PriorityMetricText.Text = snapshot.Recommendations.Count.ToString();
        PriorityDetailText.Text = snapshot.Recommendations.Count == 0
            ? "Aucune priorité détectée"
            : snapshot.Recommendations[0];

        ScanHintText.Text = $"Dernière analyse : {snapshot.CapturedAt:HH:mm}";
    }

    private void RenderCleanupPreview(CleanupPreview preview)
    {
        CleanupMetricText.Text = FormatBytes(preview.TotalBytes);
        CleanupDetailText.Text = $"{preview.TotalFiles} fichiers détectés. Aucune suppression effectuée.";
    }

    private void ShowContextActions()
    {
        ContextActionsPanel.Children.Clear();
        ContextActionsPanel.Children.Add(CreateContextButton("NETTOYAGE", (_, _) => ShowCleanupAction()));
        ContextActionsPanel.Children.Add(CreateContextButton("RESSOURCES", (_, _) => ShowResourcesAction()));
        ContextActionsPanel.Children.Add(CreateContextButton("VOIR RAPPORT", (_, _) => ShowReportAction()));
        ContextActionsPanel.Children.Add(CreateContextButton("NOUVEAU SCAN", ShowScanOptions_Click));
    }

    private Button CreateContextButton(string label, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)FindResource("VirgilSecondaryButton"),
            Margin = new Thickness(0, 0, 8, 8),
            MinWidth = 112
        };

        button.Click += handler;
        return button;
    }

    private void ShowCleanupAction()
    {
        if (_lastCleanupPreview is null)
        {
            AppendVirgilMessage("Module préparé.\nImplémentation prévue dans une prochaine version.");
            SetCoreState(VirgilCoreState.Communicating, "MESSAGE");
            return;
        }

        AppendVirgilMessage($"Nettoyage potentiel : {FormatBytes(_lastCleanupPreview.TotalBytes)}.\nPrévisualisation uniquement. Aucune suppression effectuée.");
        SetCoreState(VirgilCoreState.SensitiveAction, "VERROU");
    }

    private void ShowResourcesAction()
    {
        if (_lastSnapshot is null)
        {
            AppendVirgilMessage("Module préparé.\nImplémentation prévue dans une prochaine version.");
            SetCoreState(VirgilCoreState.Communicating, "MESSAGE");
            return;
        }

        var memoryText = _lastSnapshot.Memory.TotalBytes == 0
            ? "RAM : N/A"
            : $"RAM : {_lastSnapshot.Memory.UsedPercent:0.0} %";
        var drive = _lastSnapshot.Drives.FirstOrDefault();
        var diskText = drive is null ? "Disque : N/A" : $"Disque : {drive.UsedPercent:0.0} %";

        AppendVirgilMessage($"{memoryText}\n{diskText}\nSurveillance active en preview.");
        SetCoreState(VirgilCoreState.Communicating, "MESSAGE");
    }

    private void ShowReportAction()
    {
        if (_lastSnapshot is null)
        {
            AppendVirgilMessage("Module préparé.\nImplémentation prévue dans une prochaine version.");
            SetCoreState(VirgilCoreState.Communicating, "MESSAGE");
            return;
        }

        var cleanupText = _lastCleanupPreview is null
            ? "Nettoyage potentiel : non évalué"
            : $"Nettoyage potentiel : {FormatBytes(_lastCleanupPreview.TotalBytes)}";

        AppendVirgilMessage($"Rapport synthétique.\nÉtat système : {_lastSnapshot.OverallStatus}.\nPriorités détectées : {_lastSnapshot.Recommendations.Count}.\n{cleanupText}.");
        SetCoreState(VirgilCoreState.Communicating, "MESSAGE");
    }

    private void SetCoreState(VirgilCoreState state, string stateText)
    {
        MainCore.SetState(state);
        VirgilStateText.Text = stateText;
    }

    private void SetScanControlsEnabled(bool isEnabled)
    {
        FullScanButton.IsEnabled = isEnabled;
        QuickScanButton.IsEnabled = isEnabled;
        DeepScanButton.IsEnabled = isEnabled;
    }

    private Brush FindBrush(string resourceKey)
    {
        return TryFindResource(resourceKey) as Brush ?? Brushes.Orange;
    }

    private static string GetFriendlyCause(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => "accès refusé à une donnée système",
            IOException => "lecture système momentanément indisponible",
            InvalidOperationException => "état système non disponible",
            _ => "lecture système interrompue"
        };
    }

    private static string FormatBytes(ulong bytes) => FormatBytes((double)bytes);

    private static string FormatBytes(long bytes) => FormatBytes((double)bytes);

    private static string FormatBytes(double bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        var value = Math.Max(0, bytes);
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
