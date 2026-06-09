using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Virgil.App.Controls;
using Virgil.Core.Cleanup;
using Virgil.Core.Monitoring;
using Virgil.Domain;

namespace Virgil.App;

public partial class MainWindow : Window
{
    private readonly IMonitoringService _monitoringService = new MonitoringService();
    private readonly ICleanupService _cleanupService = new CleanupPreviewService();
    private bool _scanInProgress;
    private SystemHealthSnapshot? _lastSnapshot;
    private CleanupPreview? _lastCleanupPreview;
    private bool _lastScanIncludedCleanup;

    public MainWindow()
    {
        InitializeComponent();
        SessionTimeText.Text = $"SESSION {DateTime.Now:HH:mm}";
        SetVirgilState(VirgilCoreState.Idle, "REPOS");
        AppendVirgilMessage("Systeme pret.\nAucune analyse recente.\nEn attente d'instruction.");
    }

    private void OpenScanProtocol_Click(object sender, RoutedEventArgs e) => OpenScanProtocol();

    private void CloseScanProtocol_Click(object sender, RoutedEventArgs e) => CloseScanProtocol();

    private async void RunQuickScan_Click(object sender, RoutedEventArgs e) => await RunQuickScanAsync();

    private async void RunDeepScan_Click(object sender, RoutedEventArgs e) => await RunDeepScanAsync();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ScanProtocolOverlay.Visibility == Visibility.Visible && !_scanInProgress)
        {
            CloseScanProtocol();
            e.Handled = true;
        }
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "ACCUEIL";
        AppendVirgilMessage("Accueil actif.");
    }

    private void ModulePlaceholder_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "MODULE PRET";
        AppendVirgilMessage("Module en preparation.");
    }

    private void LastReport_Click(object sender, RoutedEventArgs e)
    {
        if (_lastSnapshot is null && _lastCleanupPreview is null)
        {
            AppendVirgilMessage("Aucun rapport disponible.");
            return;
        }

        var status = _lastSnapshot?.OverallStatus ?? "partiel";
        var cleanup = _lastScanIncludedCleanup && _lastCleanupPreview is not null
            ? FormatBytes(_lastCleanupPreview.TotalBytes)
            : "non analyse";
        AppendVirgilMessage($"Dernier rapport : {status}. Nettoyage potentiel : {cleanup}.");
    }

    private void OpenScanProtocol()
    {
        if (_scanInProgress)
        {
            AppendVirgilMessage("Analyse deja en cours.");
            return;
        }

        ScanProtocolOverlay.Visibility = Visibility.Visible;
        QuickScanButton.Focus();
    }

    private void CloseScanProtocol()
    {
        ScanProtocolOverlay.Visibility = Visibility.Collapsed;
        MainScanButton.Focus();
    }

    private async Task RunQuickScanAsync()
    {
        if (!BeginScan("Scan rapide lance."))
        {
            return;
        }

        var errors = new List<string>();
        SystemHealthSnapshot? snapshot = null;

        try
        {
            snapshot = await CaptureSnapshotAsync(errors);
            _lastSnapshot = snapshot;
            _lastCleanupPreview = null;
            _lastScanIncludedCleanup = false;

            ApplySnapshot(snapshot, errors);
            ApplyCleanup(null, attempted: false, errors);
            CompleteScan(snapshot is not null, errors, "Diagnostic termine.");
        }
        catch
        {
            AddError(errors, "Analyse interrompue.");
            ApplySnapshot(snapshot, errors);
            ApplyCleanup(null, attempted: false, errors);
            CompleteScan(hasPrimaryData: false, errors, "Diagnostic indisponible.");
        }
        finally
        {
            FinishScan();
        }
    }

    private async Task RunDeepScanAsync()
    {
        if (!BeginScan("Analyse approfondie lancee."))
        {
            return;
        }

        var errors = new List<string>();
        SystemHealthSnapshot? snapshot = null;
        CleanupPreview? preview = null;

        try
        {
            snapshot = await CaptureSnapshotAsync(errors);
            preview = await PreviewCleanupAsync(errors);
            _lastSnapshot = snapshot;
            _lastCleanupPreview = preview;
            _lastScanIncludedCleanup = true;

            ApplySnapshot(snapshot, errors);
            ApplyCleanup(preview, attempted: true, errors);
            CompleteScan(snapshot is not null || preview is not null, errors, "Analyse approfondie terminee.");
        }
        catch
        {
            AddError(errors, "Analyse interrompue.");
            ApplySnapshot(snapshot, errors);
            ApplyCleanup(preview, attempted: true, errors);
            CompleteScan(hasPrimaryData: false, errors, "Analyse indisponible.");
        }
        finally
        {
            FinishScan();
        }
    }

    private bool BeginScan(string message)
    {
        if (_scanInProgress)
        {
            AppendVirgilMessage("Analyse deja en cours.");
            return false;
        }

        _scanInProgress = true;
        CloseScanProtocol();
        SetInterfaceBusy(true);
        SetVirgilState(VirgilCoreState.Scanning, "SCAN EN COURS");
        StatusText.Text = "ANALYSE";
        GlobalStatusText.Text = "ANALYSE EN COURS";
        ReportErrorsText.Text = "AUCUNE";
        AppendVirgilMessage(message);
        return true;
    }

    private void FinishScan()
    {
        SetInterfaceBusy(false);
        _scanInProgress = false;
        SessionTimeText.Text = $"SESSION {DateTime.Now:HH:mm}";
    }

    private async Task<SystemHealthSnapshot?> CaptureSnapshotAsync(ICollection<string> errors)
    {
        try
        {
            return await Task.Run(() => _monitoringService.CaptureSnapshot());
        }
        catch
        {
            AddError(errors, "Diagnostic systeme indisponible.");
            return null;
        }
    }

    private async Task<CleanupPreview?> PreviewCleanupAsync(ICollection<string> errors)
    {
        try
        {
            return await Task.Run(() => _cleanupService.PreviewTemporaryFiles());
        }
        catch
        {
            AddError(errors, "Dossier TEMP inaccessible.");
            return null;
        }
    }

    private void ApplySnapshot(SystemHealthSnapshot? snapshot, ICollection<string> errors)
    {
        if (snapshot is null)
        {
            MemoryValueText.Text = "N/A";
            MemoryDetailText.Text = "LECTURE INDISPONIBLE";
            DiskValueText.Text = "N/A";
            DiskDetailText.Text = "LECTURE INDISPONIBLE";
            GlobalStatusText.Text = "INDISPONIBLE";
            ReportRamText.Text = "N/A - lecture indisponible";
            ReportDiskText.Text = "N/A - lecture indisponible";
            ReportRecommendationsText.Text = "Diagnostic non disponible.";
            return;
        }

        ApplyMemory(snapshot.Memory, errors);
        ApplyDisk(snapshot.Drives.FirstOrDefault(), errors);
        GlobalStatusText.Text = snapshot.OverallStatus.ToUpperInvariant();
        ReportRecommendationsText.Text = snapshot.Recommendations.Count == 0
            ? "Aucune recommandation critique."
            : string.Join("\n", snapshot.Recommendations.Take(3));
    }

    private void ApplyMemory(MemoryStatus memory, ICollection<string> errors)
    {
        if (memory.TotalBytes == 0)
        {
            MemoryValueText.Text = "N/A";
            MemoryDetailText.Text = "LECTURE MEMOIRE ECHOUEE";
            ReportRamText.Text = "N/A - lecture memoire indisponible";
            AddError(errors, "Lecture memoire indisponible.");
            return;
        }

        MemoryValueText.Text = $"{memory.UsedPercent:0.0} %";
        MemoryDetailText.Text = $"{FormatBytes(memory.UsedBytes)} / {FormatBytes(memory.TotalBytes)}";
        ReportRamText.Text = $"{memory.UsedPercent:0.0} % utilises";
    }

    private void ApplyDisk(DriveStatus? drive, ICollection<string> errors)
    {
        if (drive is null || drive.TotalBytes <= 0)
        {
            DiskValueText.Text = "N/A";
            DiskDetailText.Text = "AUCUN DISQUE ACCESSIBLE";
            ReportDiskText.Text = "N/A - aucun disque fixe accessible";
            AddError(errors, "Aucun disque fixe accessible.");
            return;
        }

        DiskValueText.Text = $"{drive.UsedPercent:0.0} %";
        DiskDetailText.Text = $"{drive.Name} - {FormatBytes(drive.UsedBytes)} / {FormatBytes(drive.TotalBytes)}";
        ReportDiskText.Text = $"{drive.Name} : {drive.UsedPercent:0.0} % utilises";
    }

    private void ApplyCleanup(CleanupPreview? preview, bool attempted, ICollection<string> errors)
    {
        if (!attempted)
        {
            CleanupValueText.Text = "--";
            CleanupDetailText.Text = "NON ANALYSE";
            ReportCleanupText.Text = "Non analyse pour scan rapide.";
            return;
        }

        if (preview is null || preview.Targets.Count == 0)
        {
            CleanupValueText.Text = "N/A";
            CleanupDetailText.Text = "TEMP INACCESSIBLE";
            ReportCleanupText.Text = "N/A - dossier TEMP inaccessible";
            AddError(errors, "Dossier TEMP inaccessible ou vide.");
            return;
        }

        CleanupValueText.Text = FormatBytes(preview.TotalBytes);
        CleanupDetailText.Text = $"{preview.TotalFiles} fichiers. Aucune suppression.";
        ReportCleanupText.Text = $"{FormatBytes(preview.TotalBytes)} potentiels. Aucune suppression.";
    }

    private void CompleteScan(bool hasPrimaryData, ICollection<string> errors, string message)
    {
        var visibleErrors = errors.Distinct().ToList();
        ReportErrorsText.Text = visibleErrors.Count == 0 ? "AUCUNE" : string.Join("\n", visibleErrors);
        UpdatePriorities(visibleErrors.Count);

        if (!hasPrimaryData)
        {
            StatusText.Text = "ERREUR";
            SetVirgilState(VirgilCoreState.Error, "ERREUR");
            AppendVirgilMessage(message);
            return;
        }

        if (visibleErrors.Count > 0)
        {
            StatusText.Text = "ATTENTION";
            SetVirgilState(VirgilCoreState.Warning, "ATTENTION");
        }
        else
        {
            StatusText.Text = "SCAN TERMINE";
            SetVirgilState(VirgilCoreState.Success, "SUCCES");
        }

        AppendVirgilMessage(message);
    }

    private void UpdatePriorities(int errorCount)
    {
        var recommendationCount = _lastSnapshot?.Recommendations.Count ?? 0;
        var total = recommendationCount + errorCount;
        PriorityValueText.Text = total.ToString();
        PriorityDetailText.Text = total == 0
            ? "AUCUNE PRIORITE"
            : total == 1 ? "1 POINT A VERIFIER" : $"{total} POINTS A VERIFIER";
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
            Style = TryFindResource("VirgilChatMessageText") as Style
        };

        ChatMessagesPanel.Children.Add(entry);
        ChatScrollViewer.Dispatcher.InvokeAsync(() => ChatScrollViewer.ScrollToEnd());
    }

    private void SetVirgilState(VirgilCoreState state, string label)
    {
        VirgilCore.SetState(state);
        VirgilStateText.Text = $"ETAT VIRGIL : {label}";
    }

    private void SetInterfaceBusy(bool busy)
    {
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
        MainScanButton.IsEnabled = !busy;
        ChatScanButton.IsEnabled = !busy;
        LastReportButton.IsEnabled = !busy;
        NavigationPanel.IsEnabled = !busy;
        QuickScanButton.IsEnabled = !busy;
        DeepScanButton.IsEnabled = !busy;
        CancelProtocolButton.IsEnabled = !busy;
    }

    private static void AddError(ICollection<string> errors, string message)
    {
        if (!errors.Contains(message))
        {
            errors.Add(message);
        }
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

    private static string FormatBytes(ulong bytes) => FormatBytes((double)bytes);

    private static string FormatBytes(long bytes) => FormatBytes((double)Math.Max(0, bytes));

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
