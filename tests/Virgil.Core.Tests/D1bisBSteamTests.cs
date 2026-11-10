using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class D1bisBSteamTests
{
    [Fact]
    public async Task Partial_sequence_has_independent_launcher_and_game_results()
    {
        var f = new Fixture();
        var steps = await f.Scan();
        Assert.Equal(new[] { "Steam", "Epic Games" }, steps.Where(s => s.Component == UpdateComponent.Launcher).Select(s => s.Id));
        var result = await f.Execute(steps);
        Assert.Equal("mis à jour", Own(result, "SteamGame:10").VerifiedStatus);
        Assert.Equal("reste à installer", Own(result, "SteamGame:20").VerifiedStatus);
        Assert.Equal("à jour selon Steam", Own(result, "SteamGame:30").VerifiedStatus);
        Assert.Equal("non pris en charge automatiquement", Own(result, "Epic Games").VerifiedStatus);
        var source = Assert.Single(result.Verifications, s => s.Component == UpdateComponent.Launcher);
        Assert.Contains("1 mis à jour", source.Detail);
        Assert.Contains("1 reste à installer", source.Detail);
        Assert.Contains("1 lanceur non pris en charge", source.Detail);
        Assert.False(source.IsCurrent);
        Assert.Equal(new[] { "final", "-silent" }, f.Backend.Events);
        Assert.Single(f.Backend.Launches);
    }

    [Fact]
    public async Task Session_is_frozen_at_preview_and_no_session_never_launches()
    {
        var f = new Fixture();
        f.Backend.Session = false;
        var steps = await f.Scan();
        f.Backend.Session = true;
        var result = await f.Execute(steps);
        var steam = Own(result, "Steam");
        Assert.Equal("non pris en charge automatiquement", steam.VerifiedStatus);
        Assert.Contains("Steam n’était pas connecté", steam.Message);
        Assert.Empty(f.Backend.Launches);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Running_game_prevents_both_shutdown_and_restart()
    {
        var f = new Fixture();
        f.Backend.GameRunning = true;
        var result = await f.Execute(await f.Scan());
        Assert.Empty(f.Backend.Closed);
        Assert.Empty(f.Backend.Launches);
        Assert.Equal("non lancée", Own(result, "SteamGame:10").VerifiedStatus);
    }

    [Fact]
    public async Task Cancellation_during_wait_keeps_Steam_running_and_remaining_games_coherent()
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        f.Backend.OnPoll = cancellation.Cancel;
        var result = await f.Execute(await f.Scan(), cancellation.Token);
        Assert.True(result.WasCancelled);
        Assert.Single(f.Backend.Launches);
        Assert.Single(f.Backend.Closed);
        var remaining = Own(result, "SteamGame:20");
        Assert.Equal("reste à installer", remaining.VerifiedStatus);
        Assert.Contains("Steam continue en arrière-plan", remaining.Message);
        Assert.Contains("reste à installer", Assert.Single(result.Verifications, s => s.Component == UpdateComponent.Launcher).Detail);
    }

    [Theory, Trait("Category", "Safety")]
    [InlineData("path")]
    [InlineData("missing")]
    [InlineData("name")]
    [InlineData("signature")]
    public async Task Executable_guard_is_a_real_block(string failure)
    {
        var f = new Fixture();
        f.Backend.Guard = failure;
        var result = await f.Execute(await f.Scan());
        var steam = Own(result, "Steam");
        Assert.Equal("bloquée", steam.VerifiedStatus);
        Assert.True(UpdateSequenceService.IsSafetyBlocked(steam));
        Assert.Contains("Steam", steam.Message);
        Assert.Empty(f.Backend.Launches);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Missing_plan_has_no_effect_and_silent_arguments_are_exact()
    {
        var f = new Fixture();
        var steps = await f.Scan();
        Assert.True((await f.Service.ExecuteAsync(steps, null, null, default)).WasRefused);
        Assert.Empty(f.Backend.Closed);
        Assert.Empty(f.Backend.Launches);
        await f.Execute(steps);
        Assert.Equal(new[] { "-silent" }, f.Backend.Launches);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Refused_shutdown_cannot_become_a_restart_when_Steam_exits_independently()
    {
        var f = new Fixture();
        f.Backend.GameRunning = true;
        f.Backend.OnFinal = () => { f.Backend.GameRunning = false; f.Backend.ExitedIndependently = true; };
        var result = await f.Execute(await f.Scan());
        Assert.Empty(f.Backend.Closed);
        Assert.Empty(f.Backend.Launches);
        Assert.Equal("non lancée", Own(result, "SteamGame:10").VerifiedStatus);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Session_in_confirmed_preview_cannot_be_changed_before_execution()
    {
        var f = new Fixture();
        var steps = await f.Scan();
        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(f.Backend, authority, launcherShutdown: new(f.Backend, f.Backend.SteamClock));
        var plan = authority.Issue(service.CreatePreview(steps), new(ConfirmationLevel.Reinforced, null));
        var altered = steps.Select(step => step.Id == "Steam" ? step with { SteamSession = new(false, null) } : step).ToArray();
        Assert.True((await service.ExecuteAsync(altered, plan, null, default)).WasRefused);
        Assert.Empty(f.Backend.Closed);
        Assert.Empty(f.Backend.Launches);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Already_closed_Steam_can_restart_only_with_the_preview_session()
    {
        var f = new Fixture();
        var steps = await f.Scan();
        f.Backend.ExitedIndependently = true;
        await f.Execute(steps);
        Assert.Empty(f.Backend.Closed);
        Assert.Single(f.Backend.Launches);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Cancellation_before_restart_marks_games_not_started_without_reopening_Steam()
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        f.Backend.OnFinal = cancellation.Cancel;
        var result = await f.Execute(await f.Scan(), cancellation.Token);
        Assert.True(result.WasCancelled);
        Assert.Empty(f.Backend.Launches);
        Assert.Equal("non lancée", Own(result, "SteamGame:10").VerifiedStatus);
        Assert.False(Assert.Single(result.Verifications, check => check.Component == UpdateComponent.Launcher).IsCurrent);
    }

    [Theory]
    [InlineData(4u, false, "reste à installer")]
    [InlineData(6u, true, "reste à installer")]
    [InlineData(128u, true, "en erreur")]
    [InlineData(4u, true, "mis à jour")]
    public async Task Updated_requires_complete_installation_and_a_changed_build(uint flags, bool changes, string expected)
    {
        var f = new Fixture();
        f.Backend.FlagsAfterPoll = flags;
        f.Backend.BuildChanges = changes;
        var result = await f.Execute(await f.Scan());
        Assert.Equal(expected, Own(result, "SteamGame:10").VerifiedStatus);
        Assert.Equal("à jour selon Steam", Own(result, "SteamGame:30").VerifiedStatus);
    }

    [Fact]
    public void Steam_inventory_reads_registry_and_files_through_read_only_port()
    {
        var access = new ReadAccess();
        var games = new SteamInventoryReader(access).Read();
        Assert.Equal("Portal", Assert.Single(games).Name);
        Assert.Equal(4u, games[0].StateFlags);
        Assert.Equal("123", games[0].BuildId);
        Assert.Contains("registry", access.Reads);
        Assert.Contains(access.Reads, path => path.EndsWith("libraryfolders.vdf", StringComparison.Ordinal));
        Assert.Contains(access.Reads, path => path.EndsWith("appmanifest_10.acf", StringComparison.Ordinal));
        Assert.All(typeof(ISteamReadAccess).GetMethods(), method =>
            Assert.DoesNotContain(new[] { "Write", "Set", "Delete", "Create" }, verb => method.Name.StartsWith(verb, StringComparison.Ordinal)));
    }

    [Theory, Trait("Category", "Safety")]
    [InlineData("C:/Steam/steam.exe", true, true, true)]
    [InlineData("C:/Steam/other.exe", true, true, false)]
    [InlineData("C:/Steam/steam.exe", false, true, false)]
    [InlineData("C:/Steam/steam.exe", true, false, false)]
    [InlineData("", true, true, false)]
    public async Task Actual_launch_adapter_checks_guard_and_uses_exact_silent_argument(string path, bool exists, bool signature, bool expected)
    {
        var runner = new Runner();
        var launcher = new SteamProcessLauncher(runner, new(_ => exists, _ => signature));
        Assert.Equal(expected, await launcher.LaunchAsync(path, default));
        if (!expected) Assert.Empty(runner.Requests);
        else
        {
            var request = Assert.Single(runner.Requests);
            Assert.Equal(path, request.FileName);
            Assert.Equal(new[] { "-silent" }, request.Arguments);
            Assert.False(request.UseShellExecute);
            Assert.False(request.WaitForExit);
            Assert.Null(request.RawArguments);
        }
    }
    private sealed class Runner : Virgil.Core.Processes.IProcessRunner
    {
        public List<Virgil.Core.Processes.ProcessLaunchRequest> Requests { get; } = [];
        public Task<Virgil.Core.Processes.ProcessRunResult> RunAsync(Virgil.Core.Processes.ProcessRunRequest request, CancellationToken token) => throw new InvalidOperationException();
        public Task<Virgil.Core.Processes.ProcessLaunchResult> LaunchAsync(Virgil.Core.Processes.ProcessLaunchRequest request, CancellationToken token)
        { Requests.Add(request); return Task.FromResult(new Virgil.Core.Processes.ProcessLaunchResult(true, null)); }
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task Windows_session_adapter_requires_process_and_active_user(bool process, bool activeUser, bool expected)
    {
        var processes = new Backend();
        if (!process) await processes.CloseAsync("steam.exe", false, default);
        var access = new ReadAccess { ActiveUser = activeUser };
        var client = new SteamWindowsClient(new Runner(), access, processes, _ => true);
        Assert.Equal(expected, (await client.ReadSessionAsync(default)).IsOpen);
        Assert.DoesNotContain(typeof(SteamSession).GetProperties(), property => property.Name.Contains("User", StringComparison.Ordinal));
    }

    private static UpdateSequenceStepResult Own(UpdateSequenceResult result, string id) => Assert.Single(result.Steps, s => s.Step.Id == id);
    private sealed class Fixture
    {
        public Backend Backend { get; } = new();
        private readonly ConfirmationService _authority;
        public UpdateSequenceService Service { get; }
        public Fixture()
        {
            _authority = new(Backend.SteamClock);
            Service = new(Backend, _authority, new EmptyPendingRebootProbe(), new(Backend, Backend.SteamClock));
        }
        public async Task<UpdateSequenceStep[]> Scan() => (await Service.ScanAsync([], default))
            .Where(s => s.Component is UpdateComponent.Launcher or UpdateComponent.WingetFinal).ToArray();
        public Task<UpdateSequenceResult> Execute(UpdateSequenceStep[] steps, CancellationToken token = default) =>
            Service.ExecuteAsync(steps, _authority.Issue(Service.CreatePreview(steps), new(ConfirmationLevel.Reinforced, null)), null, token);
    }
    private sealed class Backend : IUpdateSequenceBackend, ILauncherShutdownBackend, ISteamUpdateBackend, ISteamClient
    {
        public ISteamClient SteamClient => this;
        public TimeProvider SteamClock { get; } = new Clock();
        public TimeSpan SteamWaitLimit => TimeSpan.FromSeconds(30);
        public bool Session { get; set; } = true;
        public bool GameRunning { get; set; }
        public string? Guard { get; set; }
        public Action? OnPoll { get; set; }
        public Action? OnFinal { get; set; }
        public bool ExitedIndependently { get; set; }
        public uint FlagsAfterPoll { get; set; } = 4;
        public bool BuildChanges { get; set; } = true;
        public List<string> Events { get; } = [];
        public List<string> Launches { get; } = [];
        public List<string> Closed { get; } = [];
        private bool _closed;
        private int _reads;
        public Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(["Steam", "Epic Games"]);
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token)
        { Events.Add("final"); OnFinal?.Invoke(); return Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Applications contrôlées.")); }
        public Task<LauncherSnapshot> ReadAsync(string name, CancellationToken token) => Task.FromResult(new LauncherSnapshot(true,
            GameRunning ? [new(1, 0, "C:/Steam/steam.exe"), new(2, 1, "C:/Games/Game.exe")]
            : name == "Steam" && !_closed && !ExitedIndependently ? [new(1, 0, "C:/Steam/steam.exe")] : [], ["C:/Games"]));
        public Task CloseAsync(string executable, bool force, CancellationToken token)
        { Closed.Add(executable); _closed = true; return Task.CompletedTask; }
        public Task<SteamSession> ReadSessionAsync(CancellationToken token) => Task.FromResult(new SteamSession(Session, Guard == "path" ? null : "C:/Steam/steam.exe"));
        public Task<IReadOnlyList<SteamGameState>> ReadGamesAsync(CancellationToken token)
        {
            var poll = ++_reads > 1;
            if (poll) OnPoll?.Invoke();
            return Task.FromResult<IReadOnlyList<SteamGameState>>([new("10", "Game A", poll ? FlagsAfterPoll : 6u, poll && BuildChanges ? "2" : "1"),
                new("20", "Game B", 6, "1"), new("30", "Game C", 4, "1")]);
        }
        public Task<SteamExecutableCheck> CheckExecutableAsync(string? path, CancellationToken token) =>
            Task.FromResult(new SteamExecutableGuard(_ => Guard != "missing", _ => Guard != "signature")
                .Check(Guard == "name" ? "C:/Steam/other.exe" : path));
        public Task<bool> LaunchSilentAsync(string path, CancellationToken token)
        { Launches.Add("-silent"); Events.Add("-silent"); return Task.FromResult(true); }
    }
    private sealed class ReadAccess : ISteamReadAccess
    {
        public List<string> Reads { get; } = [];
        public bool ActiveUser { get; init; } = true;
        public bool ReadHasActiveUser() => ActiveUser;
        public string? ReadExecutablePath() { Reads.Add("registry"); return "C:/Steam/steam.exe"; }
        public bool FileExists(string path) => true;
        public string ReadText(string path)
        {
            Reads.Add(path);
            return path.EndsWith(".vdf", StringComparison.Ordinal) ? "\"libraryfolders\" { \"0\" { \"path\" \"C:/Steam\" } }"
                : "\"AppState\" { \"appid\" \"10\" \"name\" \"Portal\" \"StateFlags\" \"4\" \"buildid\" \"123\" }";
        }
        public IReadOnlyList<string> EnumerateManifests(string directory) => [directory + "/appmanifest_10.acf"];
    }
    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Add(ref _ticks, dueTime.Ticks);
            var timer = new Timer(callback, state, TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan);
            return timer;
        }
    }
}
