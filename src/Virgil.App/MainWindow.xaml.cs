using System.Windows;
using Virgil.Core.Cleanup;
using Virgil.Core.Monitoring;

namespace Virgil.App;

public partial class MainWindow : Window
{
    private readonly IMonitoringService _monitoringService = new MonitoringService();
    private readonly ICleanupService _cleanupService = new CleanupPreviewService();

    public MainWindow()
    {
        InitializeComponent();
        AppendLog("Interface chargée. Diagnostic express disponible.");
    }

    private void RunDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = _monitoringService.CaptureSnapshot();
        var mainDrive = snapshot.Drives.FirstOrDefault();

        RamMetricText.Text = $"{snapshot.Memory.UsedPercent:0.0} %";
        RamDetailText.Text = FormatBytes(snapshot.Memory.UsedBytes) + " utilisés / " + FormatBytes(snapshot.Memory.TotalBytes);

        if (mainDrive is not null)
        {
            DiskMetricText.Text = $"{mainDrive.UsedPercent:0.0} %";
            DiskDetailText.Text = $"{mainDrive.Name} - {FormatBytes(mainDrive.UsedBytes)} utilisés / {FormatBytes(mainDrive.TotalBytes)}";
        }
        else
        {
            DiskMetricText.Text = "N/A";
            DiskDetailText.Text = "Aucun disque fixe détecté.";
        }

        OverallText.Text = snapshot.OverallStatus;
        StatusText.Text = "Diagnostic terminé";

        AppendLog("Diagnostic express terminé.");
        AppendLog($"RAM : {snapshot.Memory.UsedPercent:0.0} %");

        foreach (var recommendation in snapshot.Recommendations)
        {
            AppendLog("Recommandation : " + recommendation);
        }

        if (snapshot.Recommendations.Count == 0)
        {
            AppendLog("Aucune recommandation critique.");
        }
    }

    private void PreviewCleanup_Click(object sender, RoutedEventArgs e)
    {
        var preview = _cleanupService.PreviewTemporaryFiles();

        CleanupMetricText.Text = FormatBytes(preview.TotalBytes);
        CleanupDetailText.Text = $"{preview.TotalFiles} fichiers détectés. Aucune suppression effectuée.";
        StatusText.Text = "Prévisualisation terminée";

        AppendLog("Prévisualisation nettoyage terminée.");
        AppendLog($"Espace potentiel : {FormatBytes(preview.TotalBytes)}");
        AppendLog($"Fichiers détectés : {preview.TotalFiles}");
    }

    private void AppendLog(string message)
    {
        LogText.Text += $"[VIRGIL] {DateTime.Now:HH:mm:ss} - {message}\n";
    }

    private static string FormatBytes(ulong bytes) => FormatBytes((double)bytes);

    private static string FormatBytes(long bytes) => FormatBytes((double)bytes);

    private static string FormatBytes(double bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        var value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
