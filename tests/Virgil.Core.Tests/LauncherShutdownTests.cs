using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

public sealed class LauncherShutdownTests
{
    [Theory, Trait("Category", "Safety")]
    [InlineData("child")]
    [InlineData("library")]
    [InlineData("library-forward-slashes")]
    [InlineData("unknown")]
    public async Task Running_game_blocks_launcher_shutdown(string kind)
    {
        var state = new Backend();
        state.Snapshot = kind switch
        {
            "child" => state.Snapshot with { Processes = [new(1, 0, "C:/Steam/steam.exe"), new(2, 1, "C:/Games/game.exe")] },
            "library-forward-slashes" => state.Snapshot with { Processes = [new(1, 0, "C:/Steam/steam.exe"), new(2, 0, @"C:\Steam\steamapps\common\Game\game.exe")], LibraryRoots = ["C:/Steam/steamapps/common/"] },
            "library" => state.Snapshot with { Processes = [new(1, 0, "C:/Steam/steam.exe"), new(2, 0, @"C:\Steam\steamapps\common\Game\game.exe")] },
            _ => state.Snapshot with { IsComplete = false }
        };
        var result = await Execute(state, new Clock());
        Assert.Empty(state.Commands);
        Assert.Contains(result.Steps, s => s.WasSkipped && !s.Succeeded && s.Message.Contains("mis à jour au prochain lancement", StringComparison.Ordinal));
    }

    [Theory, Trait("Category", "Safety")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Launcher_is_force_closed_only_after_15_seconds(bool exits)
    {
        var backend = new Backend();
        var clock = new Clock();
        var pending = Execute(backend, clock);
        Assert.Equal(new[] { false }, backend.Commands);
        clock.Advance(TimeSpan.FromSeconds(14));
        Assert.False(pending.IsCompleted);
        Assert.Single(backend.Commands);
        if (exits) backend.Snapshot = backend.Snapshot with { Processes = [] };
        clock.Advance(TimeSpan.FromSeconds(1));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(exits ? new[] { false } : new[] { false, true }, backend.Commands);
        if (!exits) Assert.Contains(result.Steps, s => s.Message.Contains("fermé de force", StringComparison.Ordinal));
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Launcher_shutdown_refuses_processes_outside_catalog()
    {
        var backend = new Backend();
        var result = await Execute(backend, new Clock(), "notepad");
        Assert.Empty(backend.Commands);
        Assert.Contains(result.Steps, s => s.Message.Contains("hors catalogue", StringComparison.Ordinal));
    }

    [Theory, Trait("Category", "Safety")]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("consumed")]
    public async Task Launcher_shutdown_requires_confirmed_plan(string kind)
    {
        var backend = new Backend { Snapshot = new(true, [], [@"C:\Steam\steamapps\common"]) };
        var clock = new Clock();
        var authority = new ConfirmationService(clock);
        var service = new UpdateSequenceService(new SequenceBackend(), authority, launcherShutdown: new(backend, clock));
        UpdateSequenceStep[] selected = [new("launchers", "Virgil fermera : Steam", UpdateComponent.Launcher)];
        var plan = kind == "missing" ? null : authority.Issue(service.CreatePreview(selected), new(ConfirmationLevel.Reinforced, null));
        if (kind == "expired") clock.Advance(TimeSpan.FromMinutes(11));
        if (kind == "consumed") await service.ExecuteAsync(selected, plan, null, default);
        var reads = backend.Reads;
        Assert.True((await service.ExecuteAsync(selected, plan, null, default)).WasRefused);
        Assert.Equal(reads, backend.Reads);
        Assert.Empty(backend.Commands);
    }

    [Theory, Trait("Category", "Safety")]
    [Trait("Category", "Windows")]
    [InlineData("steam.exe", @"C:\Steam\steam.exe", "-shutdown")]
    [InlineData("EpicGamesLauncher.exe", "taskkill.exe", "/IM EpicGamesLauncher.exe")]
    public async Task Graceful_shutdown_uses_catalogue_commands_without_force(string executable, string command, string arguments)
    {
        var runner = new RecordingRunner();
        var backend = new Virgil.Infrastructure.Windows.WindowsLauncherShutdownBackend(runner, (_, _) => (true, new[] { @"C:\Steam\steamapps\common" }));
        if (executable == "steam.exe") await backend.ReadAsync("Steam", default);
        await backend.CloseAsync(executable, false, default);
        var request = Assert.Single(runner.Requests, r => r.FileName != "powershell.exe");
        Assert.Equal(command, request.FileName);
        Assert.Equal(arguments, string.Join(" ", request.Arguments));
        Assert.DoesNotContain("/F", request.Arguments);
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.CloseAsync("notepad.exe", true, default));
        Assert.Equal(executable == "steam.exe" ? 2 : 1, runner.Requests.Count);
    }

    [Fact, Trait("Category", "Safety")]
    public void Process_protection_policy_still_refuses_launcher_close_outside_update_sequence()
        => new L6BehaviorTests().Tray_only_launcher_over_threshold_is_reported_without_close_action("steam");

    [Fact, Trait("Category", "Safety")]
    public void Launcher_shutdown_policy_has_only_sequence_and_composition_callers()
    {
        var source = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src");
        var references = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(file => File.ReadAllText(file).Contains("LauncherShutdownPolicy", StringComparison.Ordinal))
            .Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(new[] { "LauncherShutdownPolicy.cs", "ServiceCollectionExtensions.cs", "UpdateSequenceService.cs" }, references);
    }
    private sealed class RecordingRunner : Virgil.Core.Processes.IProcessRunner
    {
        private readonly object _requestsLock = new();
        private readonly List<Virgil.Core.Processes.ProcessRunRequest> _requests = [];
        public IReadOnlyList<Virgil.Core.Processes.ProcessRunRequest> Requests
        {
            get { lock (_requestsLock) return _requests.ToArray(); }
        }
        public bool MissingSystemPath { get; set; }
        public int SoftExitCode { get; set; }
        public Task<Virgil.Core.Processes.ProcessRunResult> RunAsync(Virgil.Core.Processes.ProcessRunRequest request, CancellationToken token)
        {
            lock (_requestsLock) _requests.Add(request);
            if (request.FileName == "powershell.exe")
            {
                var processes = new List<LauncherProcess> { new(1, 0, @"C:\Steam\steam.exe", "steam.exe"), new(2, 1, @"C:\Steam\bin\steamwebhelper.exe", "steamwebhelper.exe") };
                if (MissingSystemPath) processes.Add(new(99, 0, null!, "System"));
                return Task.FromResult(new Virgil.Core.Processes.ProcessRunResult(0, System.Text.Json.JsonSerializer.Serialize(processes), ""));
            }
            return Task.FromResult(new Virgil.Core.Processes.ProcessRunResult(request.Arguments.Contains("/F") ? 0 : SoftExitCode, "", ""));
        }
        public Task<Virgil.Core.Processes.ProcessLaunchResult> LaunchAsync(Virgil.Core.Processes.ProcessLaunchRequest request, CancellationToken token) => throw new InvalidOperationException();
    }

    [Theory, Trait("Category", "Safety")]
    [InlineData("Steam", "steamwebhelper.exe")]
    [InlineData("Steam", "steamservice.exe")]
    [InlineData("Epic Games", "EpicWebHelper.exe")]
    [InlineData("EA app", "EABackgroundService.exe")]
    [InlineData("Ubisoft Connect", "UplayWebCore.exe")]
    public async Task Known_launcher_helpers_allow_shutdown(string name, string helper)
    {
        var backend = new Backend { Snapshot = new(true,
            [new(1, 0, "C:/Launcher/" + LauncherCatalog.Executables[name]), new(2, 1, "C:/Launcher/" + helper)], ["C:/Games"]) };
        var clock = new Clock();
        var pending = Execute(backend, clock, name);
        Assert.Equal(new[] { false }, backend.Commands);
        clock.Advance(TimeSpan.FromSeconds(15));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { false, true }, backend.Commands);
        Assert.Contains(result.Steps, step => step.Message.Contains("fermé de force", StringComparison.Ordinal));
    }

    [Theory, Trait("Category", "Safety")]
    [InlineData("unknown-descendant")]
    [InlineData("helper-in-library")]
    [InlineData("other-launcher-helper")]
    public async Task Helper_allowlist_does_not_hide_games_or_unknown_descendants(string kind)
    {
        var processes = new List<LauncherProcess> { new(1, 0, "C:/Steam/steam.exe"), new(2, 1, "C:/Steam/bin/steamwebhelper.exe") };
        processes.Add(kind switch
        {
            "helper-in-library" => new(3, 2, "C:/Steam/steamapps/common/Game/steamwebhelper.exe"),
            "other-launcher-helper" => new(3, 2, "C:/Other/EpicWebHelper.exe"),
            _ => new(3, 2, "", "unknown.exe")
        });
        var backend = new Backend { Snapshot = new(true, processes, ["C:/Steam/steamapps/common"]) };
        await Execute(backend, new Clock());
        Assert.Empty(backend.Commands);
    }

    [Fact, Trait("Category", "Safety"), Trait("Category", "Windows")]
    public Task Empty_system_path_outside_launcher_tree_does_not_block_shutdown() => VerifyNativeShutdown(true, 0);
    [Fact, Trait("Category", "Safety"), Trait("Category", "Windows")]
    public Task Failed_soft_shutdown_reaches_force_only_after_15_seconds() => VerifyNativeShutdown(false, 1);
    private static async Task VerifyNativeShutdown(bool missingSystemPath, int softExitCode)
    {
        var runner = new RecordingRunner { MissingSystemPath = missingSystemPath, SoftExitCode = softExitCode };
        var backend = new Virgil.Infrastructure.Windows.WindowsLauncherShutdownBackend(runner, (_, _) => (true, new[] { @"C:\Steam\steamapps\common" }));
        var clock = new Clock();
        var authority = new ConfirmationService(clock);
        var service = new UpdateSequenceService(new SequenceBackend(), authority, launcherShutdown: new(backend, clock));
        UpdateSequenceStep[] selected = [new("launchers", "Virgil fermera : Steam", UpdateComponent.Launcher)];
        var pending = service.ExecuteAsync(selected, authority.Issue(service.CreatePreview(selected), new(ConfirmationLevel.Reinforced, null)), null, default);
        Assert.Equal(@"C:\Steam\steam.exe", Assert.Single(runner.Requests, r => r.Arguments.Contains("-shutdown")).FileName);
        clock.Advance(TimeSpan.FromSeconds(14));
        Assert.DoesNotContain(runner.Requests, r => r.Arguments.Contains("/F"));
        clock.Advance(TimeSpan.FromSeconds(1));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Contains(runner.Requests, r => r.Arguments.Contains("/F"));
        Assert.Contains(result.Steps, s => s.Message.Contains("fermé de force", StringComparison.Ordinal));
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Launcher_exit_before_deadline_does_not_wait_for_force()
    {
        var backend = new Backend();
        var clock = new Clock();
        var pending = Execute(backend, clock);
        backend.Snapshot = backend.Snapshot with { Processes = [] };
        clock.Advance(TimeSpan.FromSeconds(1));
        await pending.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { false }, backend.Commands);
    }

    private static Task<UpdateSequenceResult> Execute(Backend backend, Clock clock, string name = "Steam")
    {
        var authority = new ConfirmationService(clock);
        var service = new UpdateSequenceService(new SequenceBackend(), authority, launcherShutdown: new(backend, clock));
        UpdateSequenceStep[] selected = [new("launchers", "Virgil fermera : " + name, UpdateComponent.Launcher)];
        return service.ExecuteAsync(selected, authority.Issue(service.CreatePreview(selected), new(ConfirmationLevel.Reinforced, null)), null, default);
    }
    private sealed class SequenceBackend : IUpdateSequenceBackend
    {
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token) => throw new InvalidOperationException();
    }
    private sealed class Backend : ILauncherShutdownBackend
    {
        public int Reads { get; private set; }
        public List<bool> Commands { get; } = [];
        public LauncherSnapshot Snapshot { get; set; } = new(true, [new(1, 0, "C:/Steam/steam.exe")], [@"C:\Steam\steamapps\common"]);
        public Task<LauncherSnapshot> ReadAsync(string launcherName, CancellationToken token) { Reads++; return Task.FromResult(Snapshot); }
        public Task CloseAsync(string executable, bool force, CancellationToken token) { Commands.Add(force); return Task.CompletedTask; }
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        private long timestamp;
        public override long GetTimestamp() => timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        private readonly List<Timer> timers = [];
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state, now + dueTime);
            timers.Add(timer);
            return timer;
        }
        public void Advance(TimeSpan span)
        {
            now += span;
            timestamp += span.Ticks;
            foreach (var timer in timers.ToArray()) timer.Fire(now);
        }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state, DateTimeOffset due) : ITimer
        {
            private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) { due = clock.now + dueTime; return !disposed; }
            public void Fire(DateTimeOffset now) { if (!disposed && now >= due) { disposed = true; callback(state); } }
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
