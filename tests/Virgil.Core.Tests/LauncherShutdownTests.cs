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
    [InlineData("steam.exe", "steam.exe", "-shutdown")]
    [InlineData("EpicGamesLauncher.exe", "taskkill.exe", "/IM EpicGamesLauncher.exe")]
    public async Task Graceful_shutdown_uses_catalogue_commands_without_force(string executable, string command, string arguments)
    {
        var runner = new RecordingRunner();
        var backend = new Virgil.Infrastructure.Windows.WindowsLauncherShutdownBackend(runner);
        await backend.CloseAsync(executable, false, default);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(command, request.FileName);
        Assert.Equal(arguments, string.Join(" ", request.Arguments));
        Assert.DoesNotContain("/F", request.Arguments);
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.CloseAsync("notepad.exe", true, default));
        Assert.Single(runner.Requests);
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
        public List<Virgil.Core.Processes.ProcessRunRequest> Requests { get; } = [];
        public Task<Virgil.Core.Processes.ProcessRunResult> RunAsync(Virgil.Core.Processes.ProcessRunRequest request, CancellationToken token)
        { Requests.Add(request); return Task.FromResult(new Virgil.Core.Processes.ProcessRunResult(0, "", "")); }
        public Task<Virgil.Core.Processes.ProcessLaunchResult> LaunchAsync(Virgil.Core.Processes.ProcessLaunchRequest request, CancellationToken token) => throw new InvalidOperationException();
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
