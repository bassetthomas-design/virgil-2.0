using System.Reflection;
using System.Text.Json;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Processes;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class LotMisesAJourSteamCoreTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact, Trait("Category", "Windows")]
    public void Second_disk_library_and_last_updated_metadata_are_read()
    {
        var games = new SteamInventoryReader(new ReadAccess()).Read();
        Assert.Equal(new[] { "10", "20" }, games.Select(game => game.Id).OrderBy(id => id));
        Assert.Null(Assert.Single(games, game => game.Id == "20").Error);
        var property = typeof(SteamGameState).GetProperty("LastUpdatedUtc");
        Assert.NotNull(property);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1763890200),
            Assert.IsType<DateTimeOffset>(property.GetValue(Assert.Single(games, game => game.Id == "10"))));
    }

    [Fact]
    public async Task Steam_scan_distinguishes_statuses_freshness_and_local_metadata_limits_without_jargon()
    {
        var scan = ScanMethod();
        var backend = new SteamBackend
        {
            Games = [new("10", "A", 4, "1"), new("20", "B", 2, ""), new("30", "C", 1048576, ""),
                new("40", "D", 512, ""), new("50", "E", 128, "1")]
        };
        var property = typeof(SteamGameState).GetProperty("LastUpdatedUtc");
        Assert.NotNull(property);
        property.SetValue(backend.Games[0], new DateTimeOffset(2026, 11, 23, 9, 30, 0, TimeSpan.Zero));
        var cards = await Scan(scan, backend.CreateService());
        var games = cards.Where(card => card.Step.InformationOnly).ToArray();
        Assert.Equal(new[] { "à jour localement", "en file d’attente", "en téléchargement", "téléchargement arrêté", "en erreur" },
            games.Select(game => game.VerifiedStatus));
        Assert.Equal("Dernière mise à jour locale : 23/11/2026 10:30. Donnée locale Steam uniquement (peut retarder si Steam est fermé ou hors ligne).",
            games[0].Message);
        Assert.All(games, game =>
        {
            Assert.EndsWith("Donnée locale Steam uniquement (peut retarder si Steam est fermé ou hors ligne).", game.Message);
            foreach (var term in new[] { "appmanifest", "StateFlags", "buildid", "LastUpdated", "vdf", "acf" })
                Assert.DoesNotContain(term, game.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Steam_scan_distinguishes_closed_offline_connected_and_empty_libraries()
    {
        var running = typeof(SteamSession).GetProperty("IsProcessRunning");
        Assert.NotNull(running);
        var scan = ScanMethod();
        var backend = new SteamBackend { Games = [new("10", "A", 2, "")], Session = new(false, "C:/Steam/steam.exe") };
        var service = backend.CreateService();
        var closed = Launcher(await Scan(scan, service));
        Assert.Equal("non pris en charge automatiquement", closed.VerifiedStatus);
        Assert.Equal("Steam est fermé : 1 jeu en file d’attente. Ouvrez Steam pour vérifier « Téléchargements ».", closed.Message);

        running.SetValue(backend.Session, true);
        var offline = Launcher(await Scan(scan, service));
        Assert.Equal("non pris en charge automatiquement", offline.VerifiedStatus);
        Assert.Equal("Steam est hors ligne ou sans session active : 1 jeu en file d’attente. Connectez Steam puis vérifiez « Téléchargements ».", offline.Message);

        backend.Session = backend.Session with { IsOpen = true };
        Assert.Equal("reste à installer", Launcher(await Scan(scan, service)).VerifiedStatus);
        backend.Games = [];
        var empty = Assert.Single(await Scan(scan, service));
        Assert.Equal("à vérifier", empty.VerifiedStatus);
        Assert.Equal("Aucun jeu détecté dans les bibliothèques locales Steam ; vérifiez « Téléchargements » dans Steam.", empty.Message);
        backend.Detected = false;
        Assert.Empty(await Scan(scan, service));
    }

    [Fact]
    public async Task Steam_update_service_does_not_conclude_current_on_first_15s_poll_when_all_games_start_installed()
    {
        var backend = new SteamBackend
        {
            Games = [new("10", "A", 4, "1"), new("20", "B", 4, "1")],
            ChangedAfterSecondPoll = true
        };
        var service = backend.CreateService();
        var steps = (await service.ScanAsync([], default).WaitAsync(Deadline))
            .Where(step => step.Component is UpdateComponent.Launcher or UpdateComponent.WingetFinal).ToArray();
        var result = await Execute(service, backend.Authority, steps);
        Assert.Equal("reste à installer", Assert.Single(result.Steps, result => result.Step.Id == "SteamGame:10").VerifiedStatus);
        Assert.Equal(3, backend.GameReads);
    }

    [Fact]
    public async Task Typed_safety_blocked_field_overrides_message_heuristics_and_beats_unavailable()
    {
        var safety = typeof(UpdateSequenceStepResult).GetProperty("SafetyBlocked");
        Assert.NotNull(safety);
        var step = new UpdateSequenceStep("failed", "Windows", UpdateComponent.Windows);
        var blocked = new UpdateSequenceStepResult(step, false, false, "Accès refusé par stratégie.");
        var failed = blocked with { Message = "Installation bloquée." };
        safety.SetValue(blocked, true);
        safety.SetValue(failed, false);
        Assert.True(UpdateSequenceService.IsSafetyBlocked(blocked));
        Assert.False(UpdateSequenceService.IsSafetyBlocked(failed));

        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(new OutcomeBackend(), authority);
        var result = await Execute(service, authority, [step with { Id = "unavailable" }, step]);
        Assert.Equal("en erreur", Assert.Single(result.Verifications, check => check.Component == UpdateComponent.Windows).Status);
    }

    [Fact]
    public async Task Outside_winget_registry_access_reads_without_writing_and_rejects_writable_access()
    {
        var assembly = typeof(OutsideWingetWindowsReader).Assembly;
        var port = assembly.GetType("Virgil.Infrastructure.Windows.IOutsideWingetRegistryAccess");
        Assert.NotNull(port);
        Assert.All(port.GetMethods(), method =>
            Assert.DoesNotContain(new[] { "Write", "Set", "Delete", "Create" }, verb => method.Name.StartsWith(verb, StringComparison.Ordinal)));
        var proxy = DispatchProxy.Create(port, typeof(RegistryProxy));
        var reader = Assert.IsType<OutsideWingetWindowsReader>(Activator.CreateInstance(typeof(OutsideWingetWindowsReader),
            new ManagedInventoryRunner(), proxy));
        var snapshot = await reader.ReadAsync(default).WaitAsync(Deadline);
        Assert.Equal(new OutsideWingetApplication("Application locale", "1.0"), Assert.Single(snapshot.Installed));
        Assert.Equal(new[] { "Paquet géré" }, snapshot.ManagedNames);
        Assert.True(snapshot.IsComplete);
    }

    [Fact]
    public void Final_winget_pass_preserves_force_and_include_unknown()
    {
        var args = WindowsUpdateSequenceBackend.BuildFinalArguments(WingetAvailabilityService.GetCapabilities("1.8.1911"));
        Assert.Contains("--force", args);
        Assert.Contains("--include-unknown", args);
        Assert.DoesNotContain("--ignore-security-hash", args);
    }

    [Fact]
    public async Task Store_sequence_with_unsupported_and_failed_packages_remains_failed()
    {
        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(new StoreBackend(), authority);
        var steps = (await service.ScanAsync([], default).WaitAsync(Deadline)).Where(step => step.Component == UpdateComponent.Store).ToArray();
        Assert.Equal(new[] { "unsupported", "failed" }, steps.Select(step => step.Id));
        var result = await Execute(service, authority, steps);
        Assert.True(result.HasFailures);
        Assert.True(Assert.Single(result.Steps, outcome => outcome.Step.Id == "unsupported").WasUnavailable);
        Assert.Equal("en erreur", Assert.Single(result.Steps, outcome => outcome.Step.Id == "failed").VerifiedStatus);
        Assert.Equal("reste à installer ; 1 échec ; non pris en charge automatiquement",
            Assert.Single(result.Verifications, check => check.Component == UpdateComponent.Store).Status);
    }

    private static MethodInfo ScanMethod()
    {
        var method = typeof(IUpdateSequenceService).GetMethod("ScanSteamAsync", [typeof(CancellationToken)]);
        Assert.NotNull(method);
        return method;
    }

    private static Task<IReadOnlyList<UpdateSequenceStepResult>> Scan(MethodInfo method, UpdateSequenceService service) =>
        Assert.IsAssignableFrom<Task<IReadOnlyList<UpdateSequenceStepResult>>>(method.Invoke(service, [CancellationToken.None])).WaitAsync(Deadline);

    private static UpdateSequenceStepResult Launcher(IEnumerable<UpdateSequenceStepResult> cards) =>
        Assert.Single(cards, card => card.Step.Id == "Steam");

    private static Task<UpdateSequenceResult> Execute(UpdateSequenceService service, ConfirmationService authority, UpdateSequenceStep[] steps) =>
        service.ExecuteAsync(steps, authority.Issue(service.CreatePreview(steps), new(ConfirmationLevel.Reinforced, null)), null, default)
            .WaitAsync(Deadline);

    private sealed class ReadAccess : ISteamReadAccess
    {
        public bool ReadHasActiveUser() => true;
        public string ReadExecutablePath() => @"C:\Steam\steam.exe";
        public bool FileExists(string path) => true;
        public string ReadText(string path) => Path.GetFileName(path) switch
        {
            "libraryfolders.vdf" => """ "libraryfolders" { "0" { "path" "C:\\Steam" } "1" { "path" "D:\\SteamLibrary" } } """,
            "appmanifest_10.acf" => """ "AppState" { "name" "Portal" "StateFlags" "4" "buildid" "123" "LastUpdated" "1763890200" } """,
            "appmanifest_20.acf" => """ "AppState" { "name" "Half-Life" "StateFlags" "1026" } """,
            _ => throw new IOException(path)
        };
        public IReadOnlyList<string> EnumerateManifests(string directory) =>
            [Path.Combine(directory, directory.StartsWith("D:", StringComparison.Ordinal) ? "appmanifest_10.acf" : "appmanifest_20.acf")];
    }

    private class OutcomeBackend : IUpdateSequenceBackend
    {
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) =>
            Task.FromResult(new UpdateSequenceStepResult(step, false, false, "Échec.")
            { WasUnavailable = step.Id == "unavailable" });
    }

    private sealed class StoreBackend : OutcomeBackend, IStoreUpdateBackend
    {
        public StoreUpdateService StoreUpdates { get; } = new(new StoreClient());
    }

    private sealed class StoreClient : IStoreUpdateClient
    {
        public Task<StoreSearch> SearchAsync(CancellationToken token) => Task.FromResult(new StoreSearch(true, "",
            [new("unsupported", "family-a", "A", "1.0"), new("failed", "family-b", "B", "1.0")]));
        public Task<IReadOnlyDictionary<string, string>> ReadVersionsAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["family-a"] = "1.0", ["family-b"] = "1.0" });
        public Task<StoreStart> StartAsync(StorePackage package, CancellationToken token) =>
            Task.FromResult(new StoreStart(false, "Échec.") { WasUnsupported = package.ProductId == "unsupported" });
    }

    public class RegistryProxy : DispatchProxy
    {
        protected override object Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null || targetMethod.Name != "ReadUninstallEntries") throw new InvalidOperationException();
            var entryType = targetMethod.ReturnType.GetGenericArguments()[0];
            var entries = Array.CreateInstance(entryType, 1);
            entries.SetValue(Activator.CreateInstance(entryType, "Application locale", "1.0", false), 0);
            return entries;
        }
    }

    private sealed class ManagedInventoryRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token) => Task.FromResult(new ProcessRunResult(0,
            JsonSerializer.Serialize(new OutsideWingetSnapshot([], ["Paquet géré"], true)), ""));
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token) => throw new InvalidOperationException();
    }

    private sealed class SteamBackend : IUpdateSequenceBackend, ISteamUpdateBackend, ISteamClient, ILauncherShutdownBackend
    {
        public ISteamClient SteamClient => this;
        public TimeProvider SteamClock { get; } = new StepClock();
        public TimeSpan SteamWaitLimit => TimeSpan.FromSeconds(30);
        public ConfirmationService Authority => _authority ??= new(SteamClock);
        private ConfirmationService? _authority;
        public SteamSession Session { get; set; } = new(true, "C:/Steam/steam.exe");
        public IReadOnlyList<SteamGameState> Games { get; set; } = [];
        public bool ChangedAfterSecondPoll { get; init; }
        public bool Detected { get; set; } = true;
        public int GameReads { get; private set; }

        public UpdateSequenceService CreateService() => new(this, Authority, launcherShutdown: new(this, SteamClock));
        public Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<string>>(Detected ? ["Steam"] : []);
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) =>
            Task.FromResult(new UpdateSequenceStepResult(step, true, false, ""));
        public Task<SteamSession> ReadSessionAsync(CancellationToken token) => Task.FromResult(Session);
        public Task<IReadOnlyList<SteamGameState>> ReadGamesAsync(CancellationToken token)
        {
            GameReads++;
            return Task.FromResult<IReadOnlyList<SteamGameState>>(ChangedAfterSecondPoll && GameReads >= 3
                ? [Games[0] with { StateFlags = 6 }, Games[1]] : Games);
        }
        public Task<SteamExecutableCheck> CheckExecutableAsync(string? path, CancellationToken token) =>
            Task.FromResult(new SteamExecutableCheck(true, ""));
        public Task<bool> LaunchSilentAsync(string path, CancellationToken token) => Task.FromResult(true);
        public Task<LauncherSnapshot> ReadAsync(string name, CancellationToken token) =>
            Task.FromResult(new LauncherSnapshot(true, [], ["C:/Games"]));
        public Task CloseAsync(string executable, bool force, CancellationToken token) => throw new InvalidOperationException();
    }

    private sealed class StepClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(1), "Test", "Test");
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Add(ref _ticks, dueTime.Ticks);
            return new Timer(callback, state, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }
}
