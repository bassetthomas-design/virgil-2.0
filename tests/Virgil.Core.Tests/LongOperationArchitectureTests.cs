using System.Text.RegularExpressions;
using Virgil.Core.Operations;
using Xunit;

namespace Virgil.Tests;

public sealed class LongOperationArchitectureTests
{
    [Fact]
    public void NoUserCancellationTokenNone()
    {
        // Exact file + complete source line + count: no wildcard or file-wide exemption.
        var allowed = new Dictionary<(string File, string Line), int>
        {
            // Lecture/persistance de l’historique ; jamais le jeton du scan.
            { ("src/Virgil.App/MainWindow.xaml.cs", "var persistent = await _reportHistoryService.GetLatestAsync(CancellationToken.None);"), 1 },
            // Lecture/persistance de l’historique ; jamais le jeton du scan.
            { ("src/Virgil.App/MainWindow.xaml.cs", "var result = await _reportHistoryService.SaveAsync(report, CancellationToken.None);"), 1 },
            // Lecture/persistance de l’historique ; jamais le jeton du scan.
            { ("src/Virgil.App/MainWindow.xaml.cs", "var latest = await _reportHistoryService.GetLatestAsync(CancellationToken.None);"), 1 },
            // Chargement cosmétique indépendant des opérations utilisateur.
            { ("src/Virgil.App/ViewModels/ApplicationItemViewModel.cs", "try { IconBytes = await loader.LoadAsync(path, CancellationToken.None).ConfigureAwait(true); }"), 1 },
            // Rapport final ou ouverture ponctuelle d’un emplacement/des paramètres.
            { ("src/Virgil.App/ViewModels/ApplicationsViewModel.cs", "var result = await _locationService.OpenAsync(path, CancellationToken.None).ConfigureAwait(true);"), 1 },
            // Rapport final ou ouverture ponctuelle d’un emplacement/des paramètres.
            { ("src/Virgil.App/ViewModels/ApplicationsViewModel.cs", "\"ms-settings:appsfeatures\", Array.Empty<string>(), UseShellExecute: true), CancellationToken.None);"), 1 },
            // Rapport final ou ouverture ponctuelle d’un emplacement/des paramètres.
            { ("src/Virgil.App/ViewModels/ApplicationsViewModel.cs", "var result = await _reportHistoryService.SaveAsync(report, CancellationToken.None).ConfigureAwait(true);"), 1 },
            // Ouverture ponctuelle de l’explorateur, sans séquence destructive.
            { ("src/Virgil.App/ViewModels/CleanupEntryItems.cs", "RawArguments: $\"/select,\\\"{_item.FullPath}\\\"\"), CancellationToken.None);"), 1 },
            // Persistance du rapport ou relecture de quarantaine après opération ; doit aboutir après annulation.
            { ("src/Virgil.App/ViewModels/CleanupViewModel.cs", "try { await LoadQuarantineAsync(CancellationToken.None); }"), 2 },
            // Persistance du rapport ou relecture de quarantaine après opération ; doit aboutir après annulation.
            { ("src/Virgil.App/ViewModels/CleanupViewModel.cs", "var result = await _reportHistoryService.SaveAsync(report, CancellationToken.None).ConfigureAwait(true);"), 1 },
            // Persistance finale du rapport après opération, même annulée.
            { ("src/Virgil.App/ViewModels/InterventionsViewModel.cs", "var result = await _reportHistoryService.SaveAsync(report, CancellationToken.None).ConfigureAwait(true);"), 1 },
            // Historique et export legacy hors L5 ; opérations explicitement recensées.
            { ("src/Virgil.App/ViewModels/ReportsHistoryViewModel.cs", "var result = await _historyService.LoadAsync(CancellationToken.None).ConfigureAwait(true);"), 1 },
            // Historique et export legacy hors L5 ; opérations explicitement recensées.
            { ("src/Virgil.App/ViewModels/ReportsHistoryViewModel.cs", "var result = await _historyService.SaveAsync(_selectedReport, CancellationToken.None).ConfigureAwait(true);"), 1 },
            // Historique et export legacy hors L5 ; opérations explicitement recensées.
            { ("src/Virgil.App/ViewModels/ReportsHistoryViewModel.cs", "CancellationToken.None).ConfigureAwait(true);"), 1 },
            // Persistance finale du rapport, indépendante de l’annulation.
            { ("src/Virgil.App/ViewModels/ResourcesViewModel.cs", ".SaveAsync(ReportMapper.FromResources(BuildSessionReport()), CancellationToken.None)"), 1 },
            // Inventaire legacy du démarrage, hors des cinq écrans de L5.
            { ("src/Virgil.App/ViewModels/StartupViewModel.cs", "var inventory = await _source.ReadAsync(CancellationToken.None);"), 1 },
            // Persistance finale du rapport ou ouverture ponctuelle des paramètres Windows.
            { ("src/Virgil.App/ViewModels/UpdatesViewModel.cs", "var result = await _reportHistoryService.SaveAsync(report, CancellationToken.None).ConfigureAwait(true);"), 1 },
            // Persistance finale du rapport ou ouverture ponctuelle des paramètres Windows.
            { ("src/Virgil.App/ViewModels/UpdatesViewModel.cs", "info.SettingsUri, Array.Empty<string>(), UseShellExecute: true), CancellationToken.None);"), 1 },
            // Étape non interruptible : assistant externe ; arrêt entre les éléments.
            { ("src/Virgil.Core/Applications/ApplicationUninstallService.cs", ".LaunchAsync(executable, arguments, useShellExecute: true, CancellationToken.None)"), 1 },
            // Étape non interruptible : assistant externe ; arrêt entre les éléments.
            { ("src/Virgil.Core/Applications/ApplicationUninstallService.cs", "useShellExecute: true, CancellationToken.None).ConfigureAwait(false);"), 1 },
            // Démarrer le délégué même si annulé pour produire le résultat et préserver la quarantaine ; le corps reçoit le jeton utilisateur.
            { ("src/Virgil.Core/Cleanup/CleanupExecutionService.cs", "return Task.Run(() => ExecuteZone(preview, plan, progress, cancellationToken), CancellationToken.None);"), 1 },
            // Démarrer le délégué même si annulé pour produire le résultat et préserver la quarantaine ; le corps reçoit le jeton utilisateur.
            { ("src/Virgil.Core/Cleanup/CleanupExecutionService.cs", "}, CancellationToken.None);"), 1 },
            // Aperçu synchrone legacy sans opération utilisateur asynchrone.
            { ("src/Virgil.Core/Cleanup/CleanupPreviewService.cs", "var previews = PreviewZones(null, CancellationToken.None);"), 1 },
            // Étape non interruptible : lire le résultat final après .cancel avant de nettoyer le protocole.
            { ("src/Virgil.Core/Interventions/ElevatedHelperClient.cs", "var result = await _requestStore.ReadResultAsync(requestFile, CancellationToken.None).ConfigureAwait(false);"), 1 },
            // Étape non interruptible : ne jamais tuer une installation en cours.
            { ("src/Virgil.Core/Updates/WingetUpdateExecutionService.cs", "CancellationToken.None)"), 1 },
        };
        var root = TestRepositoryRoot.FindRepositoryRoot();
        var found = new Dictionary<(string File, string Line), int>();
        foreach (var project in new[] { "Virgil.App", "Virgil.Core" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", project), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin")) continue;
            foreach (var line in File.ReadLines(file).Where(line => line.Contains("CancellationToken.None", StringComparison.Ordinal)))
            {
                var key = (Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'), line.Trim());
                Assert.True(allowed.ContainsKey(key), $"Unexpected CancellationToken.None: {key}");
                found[key] = found.GetValueOrDefault(key) + 1;
            }
        }
        Assert.All(found, pair => Assert.Equal(allowed[pair.Key], pair.Value));
        Assert.Equal(allowed.Count, found.Count);
    }

    [Fact]
    public void AppAndCoreNeverSynchronouslyWaitForTasks()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        foreach (var project in new[] { "Virgil.Core", "Virgil.App", "Virgil.Infrastructure", "Virgil.ElevatedHelper" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", project), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            var code = File.ReadAllText(file);
            // Hosting lifecycle bridge is the sole synchronous startup/shutdown exception.
            if (Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/') == "src/Virgil.App/AppBootstrap.cs")
                code = Regex.Replace(code, @"_host\??\.(?:StartAsync|StopAsync)\(\)\.GetAwaiter\(\)\.GetResult\(\)", "HostLifecycle");
            Assert.DoesNotMatch(@"\.\s*GetAwaiter\s*\(\s*\)\s*\.\s*GetResult\s*\(", code);
            Assert.DoesNotMatch(@"\.\s*Wait\s*\(", code);
            Assert.DoesNotMatch(@"\bThread\s*\.\s*Sleep\s*\(", code);
            Assert.DoesNotMatch(@"\.\s*WaitForExit\s*\(\s*\)", code);
            // Result is also a string field on our report DTOs. Only those precise, existing
            // receivers are exempt; a new task.Result remains a violation.
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var dtoReceiver = relative switch
            {
                "src/Virgil.App/ViewModels/ReportsHistoryViewModel.cs" or
                "src/Virgil.Core/Reports/ReportSanitizer.cs" or
                "src/Virgil.Core/Reports/ReportExportService.cs" => "action",
                "src/Virgil.App/ViewModels/ApplicationsViewModel.cs" or
                "src/Virgil.Core/Applications/ApplicationReportMapper.cs" => "result",
                _ => null
            };
            if (dtoReceiver is not null)
                code = Regex.Replace(code, @"\b" + dtoReceiver + @"\.Result\b", "ReportResultText");
            Assert.DoesNotMatch(@"\.\s*Result\b", code);
        }
    }

    [Fact]
    public void ProgressIsThrottledAndCompletionIsNeverLost()
    {
        var clock = new Clock();
        var received = new Sink();
        using var progress = new ThrottledOperationProgress(received, clock);
        progress.Report(new("Première étape"));
        for (var i = 0; i < 10000; i++) progress.Report(new("Lecture", i, 10001));
        Assert.Single(received.Values);
        clock.Ticks += TimeSpan.FromMilliseconds(100).Ticks;
        progress.Report(new("Étape suivante", 1, 2, 50));
        progress.Report(new("Terminé", 2, 2, 100));
        Assert.Equal(3, received.Values.Count);
        Assert.Equal("Terminé", received.Values[^1].Step);
    }

    [Fact]
    public void LastIntermediateStageIsDeliveredWithoutAnotherReport()
    {
        var clock = new Clock();
        var received = new Sink();
        using var progress = new ThrottledOperationProgress(received, clock);
        progress.Report(new("Aperçu terminé", Percent: 100));
        clock.Advance(TimeSpan.FromMilliseconds(10));
        progress.Report(new("Ancienne étape"));
        clock.Advance(TimeSpan.FromMilliseconds(10));
        progress.Report(new("Inventaire des logiciels"));
        clock.Advance(TimeSpan.FromMilliseconds(79));
        Assert.Single(received.Values);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, received.Values.Count);
        Assert.Equal("Inventaire des logiciels", received.Values[^1].Step);
        Assert.Null(received.Values[^1].Percent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletionOrDisposalSuppressesStalePendingStage(bool dispose)
    {
        var clock = new Clock();
        var received = new Sink();
        using var progress = new ThrottledOperationProgress(received, clock);
        progress.Report(new("Lecture"));
        progress.Report(new("Étape en attente"));
        if (dispose) progress.Dispose();
        else progress.Report(new("Terminé", Percent: 100));
        var count = received.Values.Count;
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(count, received.Values.Count);
        Assert.DoesNotContain(received.Values, value => value.Step == "Étape en attente");
    }

    private sealed class Clock : TimeProvider
    {
        private readonly List<Timer> _timers = [];
        public long Ticks { get; set; }
        public override long GetTimestamp() => Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
        public void Advance(TimeSpan duration)
        {
            Ticks += duration.Ticks;
            foreach (var timer in _timers.ToArray()) timer.FireIfDue();
        }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            private long? _due;
            private bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed) return false;
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.Ticks + dueTime.Ticks;
                return true;
            }
            public void FireIfDue()
            {
                if (_disposed || _due is not { } due || clock.Ticks < due) return;
                _due = null;
                callback(state);
            }
            public void Dispose() { _disposed = true; _due = null; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class Sink : IProgress<OperationProgress>
    {
        public List<OperationProgress> Values { get; } = [];
        public void Report(OperationProgress value) => Values.Add(value);
    }
}
