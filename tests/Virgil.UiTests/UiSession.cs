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
    private readonly ConcurrentQueue<(string Screen, double Milliseconds)> latencies = new();
    private readonly Dictionary<string, Exception?> screenResults = new(StringComparer.Ordinal);
    private readonly SessionFiles files = new();
    private readonly string fixtureTemp = Path.Combine(Path.GetTempPath(), "Virgil-ui-smoke-" + Guid.NewGuid().ToString("N"), "temp-lecture-seule");
    private readonly Lazy<Task> journey;
    private Process? process;
    private UIA3Automation? automation;
    private Window? window;
    private SafeUi? ui;
    private Task? observer;
    private Exception? scanError;
    private bool disposed;
    private volatile bool measureLatency;
    private volatile string currentScreen = "accueil";
    private Exception? recapError;

    public UiSession() => journey = new Lazy<Task>(RunJourneyAsync);

    public double MaximumLatency => latencies.Select(s => s.Milliseconds).DefaultIfEmpty().Max();
    public double MedianLatency
    {
        get
        {
            var sorted = latencies.Select(s => s.Milliseconds).Order().ToArray();
            return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
        }
    }

    public IEnumerable<string> LatencyReport() => latencies.GroupBy(s => s.Screen)
        .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var samples = g.Select(s => s.Milliseconds).Order().ToArray();
            var p95 = samples[(int)Math.Ceiling(samples.Length * 0.95) - 1];
            return $"Latence {g.Key} : max {samples[^1]:F2} ms ; p95 {p95:F2} ms ; n={samples.Length}";
        });

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
            // Un fichier ancien déterministe rend le récapitulatif accessible sans effet OS.
            Directory.CreateDirectory(fixtureTemp);
            var fixtureFile = Path.Combine(fixtureTemp, "temoin-smoke.tmp");
            await File.WriteAllTextAsync(fixtureFile, "Témoin de lecture seule : ne jamais nettoyer.");
            File.SetLastWriteTimeUtc(fixtureFile, DateTime.UtcNow.AddDays(-7));
            process = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                Environment = { ["TEMP"] = fixtureTemp, ["TMP"] = fixtureTemp }
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
            await UntilAsync(() => window.Properties.ItemStatus.ValueOrDefault == "Prêt",
                TimeSpan.FromSeconds(60), "Fenêtre non prête après le premier rendu et Idle.");
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
            currentScreen = screen.Screen;
            try
            {
                SampleLatency();
                await ui!.Click(screen.Navigation);
                // Injected *ModuleView fields have no x:Name. WPF's
                // UserControlAutomationPeer exposes their concrete class names.
                await UntilAsync(() => screen.View.EndsWith("View", StringComparison.Ordinal)
                    ? window!.FindAllDescendants(cf => cf.ByClassName(screen.View)).Any(IsVisible)
                    : Visible(screen.View), TimeSpan.FromSeconds(60), $"Vue invisible : {screen.View}");
                SampleLatency();
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

        currentScreen = "recap-nettoyage";
        try
        {
            await ui!.Click("CleanupNavButton");
            await ui.Click("AnalyzeZonesButton");
            await ui.Click("LaunchCleanupButton");
            await WaitVisibleAsync("CleanupConfirmationSummaryText");
            var summary = Find("CleanupConfirmationSummaryText")!;
            Assert.False(string.IsNullOrWhiteSpace(summary.Name), "Phrase de confirmation vide.");
            var bounds = summary.BoundingRectangle;
            Assert.True(bounds.Left >= window!.BoundingRectangle.Left && bounds.Top >= window.BoundingRectangle.Top &&
                bounds.Right <= window.BoundingRectangle.Right && bounds.Bottom <= window.BoundingRectangle.Bottom, "Phrase de confirmation hors capture.");
            SaveWindow("12-recap-nettoyage.png");
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
            AssertHealthy();
        }
        catch (Exception exception) { recapError = exception; }

        currentScreen = "scan-approfondi";
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
            // Chat entries prepend "[VIRGIL]\n" to the completion message.
            await UntilAsync(() => window!.FindAllDescendants().Any(element =>
                element.Name?.Contains("Analyse approfondie terminée.", StringComparison.Ordinal) == true),
                TimeSpan.FromSeconds(5), "Le scan approfondi n'a pas annoncé sa fin réussie.");
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
        Assert.True(recapError is null, $"Récapitulatif : {recapError}");
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
        Assert.Equal("Témoin de lecture seule : ne jamais nettoyer.",
            File.ReadAllText(Path.Combine(fixtureTemp, "temoin-smoke.tmp")));
        Assert.False(Directory.Exists(Path.Combine(SessionFiles.ArtifactDirectory, "temp-lecture-seule")));
        var expected = Screens.Select((screen, i) => $"{i + 1:00}-{screen.Screen}.png")
            .Append("12-recap-nettoyage.png").Append("session-desktop.png").Order(StringComparer.Ordinal).ToArray();
        var actual = Directory.EnumerateFiles(SessionFiles.ArtifactDirectory, "*.png")
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
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
        for (var y = bitmap.Height / 10; y < bitmap.Height * 9 / 10; y += Math.Max(1, bitmap.Height / 100))
        for (var x = bitmap.Width / 10; x < bitmap.Width * 9 / 10; x += Math.Max(1, bitmap.Width / 100))
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
                        SampleLatency();
                    }
                }
                await Task.Delay(100, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception exception) { violations.Enqueue($"Sonde de session : {exception}"); }
    }

    private void SampleLatency()
    {
        try
        {
            var screen = currentScreen;
            latencies.Enqueue((screen, NativeUi.MeasureLatency(process!.MainWindowHandle)));
        }
        catch (Exception exception) { violations.Enqueue(exception.Message); }
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

