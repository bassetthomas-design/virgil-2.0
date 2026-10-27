using System.Collections.Concurrent;
using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.UIA3;
using Xunit;

namespace Virgil.UiTests;

public sealed class UiSession : IAsyncLifetime, IDisposable
{
    private static readonly (string Screen, string Navigation, string View)[] Screens =
    [
        ("accueil", "HomeNavButton", "MainScanButton"),
        ("scan", "ScanNavButton", "DeepScanButton"),
        ("nettoyage", "CleanupNavButton", "CleanupView"),
        ("diagnostic", "DiagnosticNavButton", "DiagnosticView"),
        ("ressources", "ResourcesNavButton", "ResourcesView"),
        ("applications", "ApplicationsNavButton", "ApplicationsView"),
        ("mises-a-jour", "UpdatesNavButton", "UpdatesView"),
        ("demarrage", "StartupNavButton", "StartupView"),
        ("reseau", "NetworkNavButton", "NetworkView"),
        ("reparation", "InterventionsNavButton", "InterventionsView"),
        ("rapports", "ReportsNavButton", "ReportsHistoryView")
    ];

    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentQueue<string> violations = new();
    private readonly ConcurrentQueue<double> latencies = new();
    private readonly Dictionary<string, Exception?> screenResults = new(StringComparer.Ordinal);
    private readonly SessionFiles files = new();
    private readonly Lazy<Task> journey;
    private Process? process;
    private UIA3Automation? automation;
    private Window? window;
    private SafeUi? ui;
    private Task? observer;
    private Exception? scanError;
    private bool disposed;
    private volatile bool measureLatency;

    public UiSession() => journey = new Lazy<Task>(RunJourneyAsync);

    public double MaximumLatency => latencies.DefaultIfEmpty().Max();
    public double MedianLatency
    {
        get
        {
            var sorted = latencies.Order().ToArray();
            return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
        }
    }

    public async Task InitializeAsync()
    {
        Assert.True(OperatingSystem.IsWindows(), "La collection UI exige une session Windows interactive.");
        try
        {
            Directory.CreateDirectory(SessionFiles.ArtifactDirectory);
            using (var desktop = Capture.Screen())
            {
                desktop.ToFile(Path.Combine(SessionFiles.ArtifactDirectory, "session-desktop.png"));
                Assert.True(HasPixelVariance(desktop.Bitmap),
                    "Session Windows non interactive (capture noire) : les tests UI ne peuvent pas tourner sur ce runner");
            }

            files.Begin();
            // Start observing before launch, including startup elevation and error dialogs.
            observer = Task.Run(ObserveAsync);
            var executable = Environment.GetEnvironmentVariable("VIRGIL_APP_EXE");
            if (string.IsNullOrWhiteSpace(executable))
                executable = Path.Combine(SessionFiles.RepositoryRoot, "artifacts", "Virgil2", "Virgil.App.exe");
            executable = Path.GetFullPath(executable);
            Assert.True(File.Exists(executable), $"Exécutable publié introuvable : {executable}");
            process = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            }) ?? throw new InvalidOperationException("Impossible de lancer Virgil.");
            automation = new UIA3Automation();
            await UntilAsync(() =>
            {
                Assert.False(process.HasExited, "Virgil a quitté avant l'ouverture de la fenêtre principale.");
                process.Refresh();
                return process.MainWindowHandle != IntPtr.Zero;
            }, TimeSpan.FromSeconds(60), "Fenêtre principale absente après 60 s.");
            window = automation.FromHandle(process.MainWindowHandle).AsWindow();
            // Also used when Set-DisplayResolution is unavailable on the hosted runner.
            window.Patterns.Transform.Pattern.Move(0, 0);
            window.Patterns.Transform.Pattern.Resize(1600, 900);
            ui = new SafeUi(InvokeAsync);
            await WaitVisibleAsync("HomeNavButton");
            AssertHealthy();
        }
        catch
        {
            // xUnit may not call Dispose after a failed fixture initialization.
            await DisposeAsync();
            throw;
        }
    }

    public Task RunAsync() => journey.Value;

    private async Task RunJourneyAsync()
    {
        measureLatency = true;
        // A single ordered journey, shared results, no dependency on xUnit's test order.
        for (var i = 0; i < Screens.Length; i++)
        {
            var screen = Screens[i];
            try
            {
                await ui!.Click(screen.Navigation);
                // Injected *ModuleView fields have no x:Name. WPF's
                // UserControlAutomationPeer exposes their concrete class names.
                await UntilAsync(() => screen.View.EndsWith("View", StringComparison.Ordinal)
                    ? window!.FindAllDescendants(cf => cf.ByClassName(screen.View)).Any(IsVisible)
                    : Visible(screen.View), TimeSpan.FromSeconds(60), $"Vue invisible : {screen.View}");
                AssertHealthy();
                SaveWindow($"{i + 1:00}-{screen.Screen}.png");
                screenResults[screen.Screen] = null;
            }
            catch (Exception exception)
            {
                screenResults[screen.Screen] = exception;
            }
            finally
            {
                if (screen.Screen == "scan" && Visible("DeepScanButton"))
                    await ui!.Click("CancelProtocolButton");
            }
        }

        try
        {
            await ui!.Click("ScanNavButton");
            await WaitVisibleAsync("DeepScanButton");
            await ui.Click("DeepScanButton");
            // Invoke dispatches the async handler. Navigation is disabled synchronously
            // before its first await, so this proves the scan actually started.
            await UntilAsync(() => !Find("HomeNavButton")!.IsEnabled,
                TimeSpan.FromSeconds(2), "Le scan approfondi n'a pas démarré.");
            await UntilAsync(() => Find("HomeNavButton")!.IsEnabled,
                TimeSpan.FromSeconds(120), "Le scan approfondi dépasse 120 s.");
            Assert.True(window!.FindAllDescendants().Any(element =>
                element.Name.StartsWith("Analyse approfondie terminee.", StringComparison.Ordinal)),
                "Le scan approfondi n'a pas annoncé sa fin réussie.");
            AssertHealthy();
        }
        catch (Exception exception)
        {
            scanError = exception;
        }
        measureLatency = false;
        files.CollectLogs();
    }

    public void AssertScreen(string screen)
    {
        Assert.True(screenResults.TryGetValue(screen, out var error), $"Écran non parcouru : {screen}");
        Assert.True(error is null, $"Écran {screen} : {error}");
    }

    public void AssertResponsive()
    {
        foreach (var screen in Screens) AssertScreen(screen.Screen);
        Assert.True(scanError is null, $"Scan approfondi : {scanError}");
        Assert.NotEmpty(latencies);
        Assert.True(MaximumLatency <= 500, $"Latence UI max : {MaximumLatency:F0} ms (limite 500 ms)");
    }

    public void AssertHealthy()
    {
        Assert.NotNull(process);
        Assert.False(process.HasExited, "Le processus Virgil a quitté pendant les tests.");
        files.CollectLogs();
        Assert.True(files.ErrorLines.Count == 0, string.Join(Environment.NewLine, files.ErrorLines));
        Assert.True(violations.IsEmpty, string.Join(Environment.NewLine, violations));
    }

    public void AssertNoSideEffects()
    {
        AssertHealthy();
        files.AssertQuarantineUnchanged();
    }

    private AutomationElement? Find(string id) => window!.FindFirstDescendant(cf => cf.ByAutomationId(id));
    private static bool IsVisible(AutomationElement element) => !element.IsOffscreen &&
        element.BoundingRectangle.Width > 0 && element.BoundingRectangle.Height > 0;
    private bool Visible(string id) => Find(id) is { } element && IsVisible(element);

    private Task WaitVisibleAsync(string id) => UntilAsync(() => Visible(id),
        TimeSpan.FromSeconds(60), $"Vue/contrôle UIA invisible : {id}");

    private async Task InvokeAsync(string id)
    {
        await UntilAsync(() => Visible(id) && Find(id)!.IsEnabled,
            TimeSpan.FromSeconds(60), $"Bouton UIA indisponible : {id}");
        // InvokePattern uses the AutomationId; there is no coordinate or label fallback.
        Find(id)!.Patterns.Invoke.Pattern.Invoke();
    }

    private static async Task UntilAsync(Func<bool> condition, TimeSpan timeout, string message)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            if (condition()) return;
            await Task.Delay(100);
        } while (timer.Elapsed < timeout);
        Assert.Fail(message);
    }

    private void SaveWindow(string filename)
    {
        using var capture = Capture.Element(window!);
        capture.ToFile(Path.Combine(SessionFiles.ArtifactDirectory, filename));
        Assert.True(HasPixelVariance(capture.Bitmap), $"Capture uniforme/noire : {filename}");
    }

    private static bool HasPixelVariance(System.Drawing.Bitmap bitmap)
    {
        // Sample all areas, including the interior. Uniform captures have zero variance.
        double sum = 0, squares = 0;
        var count = 0;
        for (var y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 100))
        for (var x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 100))
        {
            var pixel = bitmap.GetPixel(x, y);
            var value = (pixel.R + pixel.G + pixel.B) / 3d;
            sum += value;
            squares += value * value;
            count++;
        }
        return count > 0 && squares / count - Math.Pow(sum / count, 2) > 1;
    }

    private async Task ObserveAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                NativeUi.ObserveForbiddenProcesses(violations);
                if (process is { HasExited: false })
                {
                    NativeUi.ObserveErrorDialogs(process.Id, violations);
                    process.Refresh();
                    if (measureLatency && process.MainWindowHandle != IntPtr.Zero)
                    {
                        try { latencies.Enqueue(NativeUi.MeasureLatency(process.MainWindowHandle)); }
                        catch (Exception exception) { violations.Enqueue(exception.Message); }
                    }
                }
                await Task.Delay(100, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception exception) { violations.Enqueue($"Sonde de session : {exception}"); }
    }

    public async Task DisposeAsync()
    {
        try
        {
            Dispose();
            if (observer is not null) await observer;
        }
        finally
        {
            files.CollectLogs();
            if (files.Started)
            {
                files.AssertQuarantineUnchanged();
                Assert.True(violations.IsEmpty, string.Join(Environment.NewLine, violations));
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stop.Cancel();
        try
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
        }
        finally
        {
            automation?.Dispose();
            // The observer still owns its Process reference until DisposeAsync awaits it.
        }
    }
}
