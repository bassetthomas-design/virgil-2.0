using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Virgil.App.Controls;
using Virgil.Core.Scanning;
using Virgil.Domain;

namespace Virgil.App;

public partial class MainWindow : Window
{
    private readonly ISystemScanService _systemScanService = new SystemScanService();
    private readonly HashSet<string> _reportedProgressSteps = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _activeScanCancellation;
    private bool _scanInProgress;
    private SystemScanReport? _lastReport;

    public MainWindow()
    {
        InitializeComponent();
        SessionTimeText.Text = $"SESSION {DateTime.Now:HH:mm}";
        SetVirgilState(VirgilCoreState.Idle, "REPOS");
        AppendVirgilMessage("Systeme pret.\nAucune analyse recente.\nEn attente d'instruction.");
    }

    private void OpenScanProtocol_Click(object sender, RoutedEventArgs e) => OpenScanProtocol();

    private void CloseScanProtocol_Click(object sender, RoutedEventArgs e) => CloseScanProtocol();

    private async void RunQuickScan_Click(object sender, RoutedEventArgs e) => await RunScanAsync(ScanMode.Quick);

    private async void RunDeepScan_Click(object sender, RoutedEventArgs e) => await RunScanAsync(ScanMode.Deep);

    private void CloseReport_Click(object sender, RoutedEventArgs e) => CloseReport();

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

        if (ScanProtocolOverlay.Visibility == Visibility.Visible && !_scanInProgress)
        {
            CloseScanProtocol();
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _activeScanCancellation?.Cancel();
        VirgilCore.SetState(VirgilCoreState.Idle);
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
        if (_lastReport is null)
        {
            AppendVirgilMessage("Aucun rapport disponible.\nLancez une analyse systeme.");
            return;
        }

        ShowLastReport(_lastReport);
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

    private async Task RunScanAsync(ScanMode mode)
    {
        if (!BeginScan(mode))
        {
            return;
        }

        try
        {
            var progress = new Progress<ScanProgress>(HandleProgress);
            var report = await _systemScanService.RunAsync(mode, progress, _activeScanCancellation!.Token);
            _lastReport = report;

            ApplyReport(report);
            CompleteScan(report);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "ANNULE";
            GlobalStatusText.Text = "ANNULE";
            SetVirgilState(VirgilCoreState.Idle, "REPOS");
            AppendVirgilMessage("Analyse annulee.\nAucune modification effectuee.");
        }
        catch
        {
            StatusText.Text = "ERREUR";
            GlobalStatusText.Text = "ECHEC DU SCAN";
            SetVirgilState(VirgilCoreState.Error, "ERREUR");
            AppendVirgilMessage("Analyse indisponible.\nAucune modification effectuee.");
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
            AppendVirgilMessage("Analyse deja en cours.");
            return false;
        }

        _scanInProgress = true;
        _reportedProgressSteps.Clear();
        _activeScanCancellation = new CancellationTokenSource();
        CloseReport();
        CloseScanProtocol();
        SetInterfaceBusy(true);
        SetVirgilState(VirgilCoreState.Scanning, "SCAN EN COURS");
        StatusText.Text = "ANALYSE";
        GlobalStatusText.Text = "ANALYSE EN COURS";
        ReportCpuText.Text = "EN COURS";
        ReportRamText.Text = "EN COURS";
        ReportDiskText.Text = "EN COURS";
        ReportCleanupText.Text = mode == ScanMode.Deep ? "EN COURS" : "NON ANALYSE";
        ReportRecommendationsText.Text = "ANALYSE EN COURS";
        ReportErrorsText.Text = "AUCUNE";

        AppendVirgilMessage($"Protocole d'analyse initialise.\nMode : {ModeLabel(mode).ToLowerInvariant()}.");
        AppendVirgilMessage("Analyse systeme en cours.");
        return true;
    }

    private void FinishScan()
    {
        SetInterfaceBusy(false);
        _scanInProgress = false;
        _activeScanCancellation?.Dispose();
        _activeScanCancellation = null;
        SessionTimeText.Text = $"SESSION {DateTime.Now:HH:mm}";
    }

    private void HandleProgress(ScanProgress progress)
    {
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
        var priorityCount = CountPriorities(report);

        GlobalStatusText.Text = report.OverallStatus.ToUpperInvariant();
        ReportCpuText.Text = $"{report.Processor.UsagePercent:0.0} % - {report.Processor.Status}";
        ReportRamText.Text = report.Memory.TotalPhysicalBytes == 0
            ? "N/A - lecture memoire indisponible"
            : $"{report.Memory.UsedPercent:0.0} % utilises";
        ReportDiskText.Text = systemDisk is null
            ? "N/A - aucun disque fixe accessible"
            : $"{systemDisk.Name} : {systemDisk.UsedPercent:0.0} % utilises";
        ReportCleanupText.Text = report.Cleanup.WasAnalyzed
            ? $"{FormatBytes(report.Cleanup.PotentialBytes)} potentiels. Aucune suppression."
            : "Non analyse pour scan rapide.";
        ReportRecommendationsText.Text = report.Recommendations.Count == 0
            ? "Aucune recommandation prioritaire."
            : string.Join("\n", report.Recommendations.Take(3));
        ReportErrorsText.Text = report.Errors.Count == 0
            ? "AUCUNE"
            : string.Join("\n", report.Errors.Distinct().Take(4));

        ApplyMemoryCard(report.Memory);
        ApplyDiskCard(systemDisk);
        ApplyCleanupCard(report.Cleanup);
        UpdatePriorities(priorityCount);
    }

    private void ApplyMemoryCard(MemoryScanInfo memory)
    {
        if (memory.TotalPhysicalBytes == 0)
        {
            MemoryValueText.Text = "N/A";
            MemoryDetailText.Text = "LECTURE MEMOIRE ECHOUEE";
            return;
        }

        MemoryValueText.Text = $"{memory.UsedPercent:0.0} %";
        MemoryDetailText.Text = $"{FormatBytes(memory.UsedPhysicalBytes)} / {FormatBytes(memory.TotalPhysicalBytes)}";
    }

    private void ApplyDiskCard(DiskScanInfo? disk)
    {
        if (disk is null || disk.TotalBytes <= 0)
        {
            DiskValueText.Text = "N/A";
            DiskDetailText.Text = "AUCUN DISQUE ACCESSIBLE";
            return;
        }

        DiskValueText.Text = $"{disk.UsedPercent:0.0} %";
        DiskDetailText.Text = $"{disk.Name} - {FormatBytes(disk.UsedBytes)} / {FormatBytes(disk.TotalBytes)}";
    }

    private void ApplyCleanupCard(CleanupScanInfo cleanup)
    {
        if (!cleanup.WasAnalyzed)
        {
            CleanupValueText.Text = "--";
            CleanupDetailText.Text = "NON ANALYSE";
            return;
        }

        CleanupValueText.Text = FormatBytes(cleanup.PotentialBytes);
        CleanupDetailText.Text = $"{cleanup.FileCount} fichiers. Aucune suppression.";
    }

    private void CompleteScan(SystemScanReport report)
    {
        var priorityCount = CountPriorities(report);

        if (IsStable(report))
        {
            StatusText.Text = "SCAN TERMINE";
            SetVirgilState(VirgilCoreState.Success, "SUCCES");
        }
        else
        {
            StatusText.Text = "ATTENTION";
            SetVirgilState(VirgilCoreState.Warning, "ATTENTION");
        }

        if (report.Mode == ScanMode.Deep)
        {
            AppendVirgilMessage($"Analyse approfondie terminee.\nNettoyage potentiel : {FormatBytes(report.Cleanup.PotentialBytes)}.\nRapport disponible.");
            return;
        }

        AppendVirgilMessage($"Scan rapide termine.\nEtat systeme : {report.OverallStatus.ToLowerInvariant()}.\nPriorites detectees : {priorityCount}.");
    }

    private void ShowLastReport(SystemScanReport report)
    {
        var systemDisk = ScanRules.SelectSystemDisk(report.Disks, Environment.SystemDirectory);

        ReportPopupSummaryText.Text = $"{ModeLabel(report.Mode)} - {report.OverallStatus} - {report.CapturedAt:yyyy-MM-dd HH:mm:ss}";
        ReportPopupDetailsText.Text = string.Join("\n", new[]
        {
            $"Mode : {ModeLabel(report.Mode)}",
            $"Date : {report.CapturedAt:yyyy-MM-dd HH:mm:ss}",
            $"Duree : {report.Duration.TotalSeconds:0.0} s",
            $"Etat general : {report.OverallStatus}",
            $"Windows : {report.Windows.Edition} - {report.Windows.Version} - build {report.Windows.Build}",
            $"Architecture : systeme {report.Windows.SystemArchitecture}, processus {report.Windows.ProcessArchitecture}",
            $"Machine : {report.Windows.MachineName}",
            $"Uptime : {FormatDuration(report.Windows.Uptime)}",
            $"CPU : {report.Processor.Name} - {report.Processor.LogicalProcessorCount} logiques - {report.Processor.UsagePercent:0.0} % ({report.Processor.Status})",
            $"Memoire : {FormatBytes(report.Memory.UsedPhysicalBytes)} / {FormatBytes(report.Memory.TotalPhysicalBytes)} ({report.Memory.UsedPercent:0.0} %)",
            $"Disque systeme : {FormatDisk(systemDisk)}",
            $"Reseau : {FormatNetwork(report.Network)}",
            $"Nettoyage potentiel : {FormatCleanup(report.Cleanup)}"
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

    private void UpdatePriorities(int priorityCount)
    {
        PriorityValueText.Text = priorityCount.ToString();
        PriorityDetailText.Text = priorityCount == 0
            ? "AUCUNE PRIORITE"
            : priorityCount == 1 ? "1 POINT A VERIFIER" : $"{priorityCount} POINTS A VERIFIER";
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
            ? $"{FormatBytes(cleanup.PotentialBytes)}, {cleanup.FileCount} fichiers, zones : {FormatZones(cleanup.Zones)}"
            : "Non analyse";
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
