using System.Text.Json;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Processes;
using Virgil.Core.Updates;
using Virgil.ElevatedHelper;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class D2SingleChannelTests
{
    internal static UpdateSequenceStep Single(UpdateSequenceStep step)
    {
        typeof(UpdateSequenceStep).GetProperty("IsSingleItem")?.SetValue(step, true);
        return step;
    }

    [Fact]
    public async Task Software_uses_exact_same_command_as_sequence_and_never_another_identity()
    {
        var item = new UpdateItem { Id = "Example.App", Name = "Exemple", InstalledVersion = "1.0", AvailableVersion = "2.0" };
        var one = new Runner(); var all = new Runner();
        var authority = new ConfirmationService(TimeProvider.System);
        var singleBackend = new WindowsUpdateSequenceBackend(one, null!, null!, () => "helper", new WingetAvailabilityService(one, ["winget.exe"]));
        var fullBackend = new WindowsUpdateSequenceBackend(all, null!, null!, () => "helper", new WingetAvailabilityService(all, ["winget.exe"]));
        var service = new UpdateSequenceService(singleBackend, authority, new EmptyPendingRebootProbe());
        var step = Single(new(item.Id, item.Name, UpdateComponent.Winget, item));
        var selected = new[] { step };
        var preview = service.CreatePreview(selected);
        Assert.Equal("Mettre à jour Exemple", preview.Title);
        var plan = authority.Issue(preview, new(ConfirmationLevel.Reinforced, null));
        await service.ExecuteAsync(selected, plan, null, default);
        await fullBackend.ExecuteAsync(step with { }, default);
        var command = Assert.Single(one.Requests, r => r.Arguments.FirstOrDefault() == "upgrade");
        Assert.Equal(Assert.Single(all.Requests, r => r.Arguments.FirstOrDefault() == "upgrade").Arguments, command.Arguments);
        Assert.DoesNotContain("--all", command.Arguments);
        Assert.Equal("Example.App", command.Arguments[command.Arguments.ToList().IndexOf("--id") + 1]);
        Assert.Equal("2.0", command.Arguments[command.Arguments.ToList().IndexOf("--version") + 1]);
        Assert.True((await service.ExecuteAsync(selected, plan, null, default)).WasRefused);
        Assert.Single(one.Requests, r => r.Arguments.FirstOrDefault() == "upgrade");
    }

    [Fact]
    public async Task Missing_modified_expired_and_reused_plans_have_no_new_effect()
    {
        var clock = new Clock(); var authority = new ConfirmationService(clock); var backend = new Backend();
        var service = new UpdateSequenceService(backend, authority, new EmptyPendingRebootProbe());
        var selected = new[] { Single(new UpdateSequenceStep("a", "Application A", UpdateComponent.Winget,
            new UpdateItem { Id = "a", Name = "Application A", AvailableVersion = "2.0" })) };
        Assert.True((await service.ExecuteAsync(selected, null, null, default)).WasRefused);
        var plan = authority.Issue(service.CreatePreview(selected), new(ConfirmationLevel.Reinforced, null));
        Assert.True((await service.ExecuteAsync([selected[0] with { Id = "b" }], plan, null, default)).WasRefused);
        clock.Now += TimeSpan.FromMinutes(11);
        Assert.True((await service.ExecuteAsync(selected, plan, null, default)).WasRefused);
        Assert.Empty(backend.Calls);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public async Task Steam_alone_uses_real_shutdown_policy_without_final_application_pass(bool session, bool game, bool launches)
    {
        var backend = new Backend { Session = session, Game = game };
        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(backend, authority, new EmptyPendingRebootProbe(), new(backend, TimeProvider.System));
        var steps = await service.ScanAsync([], default);
        var step = Single(Assert.Single(steps, s => s.Component == UpdateComponent.Launcher && s.Id == "Steam"));
        var result = await service.ExecuteAsync([step], authority.Issue(service.CreatePreview([step]), new(ConfirmationLevel.Reinforced, null)), null, default);
        Assert.Empty(backend.Calls);
        Assert.Equal(launches ? new[] { "-silent" } : [], backend.Launches);
        Assert.Equal(launches ? new[] { "steam.exe" } : [], backend.Closed);
        if (!session)
        {
            var own = Assert.Single(result.Steps, r => r.Step.Id == "Steam");
            Assert.Equal("non pris en charge automatiquement", own.VerifiedStatus);
            Assert.Contains(LauncherCardSummary.NoSession, own.Message);
        }
    }

    [Fact]
    public async Task Store_only_installs_the_frozen_package_and_deduplicates_software_route()
    {
        var client = new StoreClient(); var backend = new Backend(client);
        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(backend, authority, new EmptyPendingRebootProbe());
        var steps = await service.ScanAsync([], default);
        var step = Single(Assert.Single(steps, s => s.Component == UpdateComponent.Store && s.Id == "a"));
        var preview = service.CreatePreview([step]);
        Assert.Equal("Mettre à jour Store A", preview.Title);
        var result = await service.ExecuteAsync([step], authority.Issue(preview, new(ConfirmationLevel.Reinforced, null)), null, default);
        Assert.Equal(new[] { "a" }, client.Started);
        Assert.Equal("mis à jour", Assert.Single(result.Steps, r => r.Step.Id == "a").VerifiedStatus);
        var duplicate = new UpdateItem { Id = "a", Name = "Store A", Source = UpdateSource.MicrosoftStore, AvailableVersion = "2.0" };
        steps = await service.ScanAsync([duplicate], default);
        Assert.Single(steps, s => s.Application?.Id == "a");
        Assert.DoesNotContain(steps, s => s.Component == UpdateComponent.Store && s.Id == "a");
        var refusal = await backend.StoreUpdates.ExecuteAsync(step with { Id = "invented" }, default);
        Assert.Equal("bloquée", refusal.VerifiedStatus);
        Assert.Equal(new[] { "a" }, client.Started);
    }

    [Theory]
    [InlineData("f813d575-a123-4788-b34a-d547c15c5663:7", true)]
    [InlineData("f813d575-a123-4788-b34a-d547c15c5663", false)]
    [InlineData("f813d575-a123-4788-b34a-d547c15c5663:-1", false)]
    [InlineData("f813d575-a123-4788-b34a-d547c15c5663:1,drivers", false)]
    [InlineData("f813d575-a123-4788-b34a-d547c15c5663:2147483648", false)]
    [InlineData("' or IsInstalled=1:2", false)]
    public void Targeted_driver_identity_is_strict_and_passes_unchanged_allowlist(string identity, bool valid) =>
        Assert.Equal(valid, new ElevatedActionAllowlist().IsMaintenanceAllowed(InterventionId.InstallMicrosoftUpdates, "driver:" + identity));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Targeted_WUA_installs_one_collection_with_one_driver_or_nothing(bool firmware, bool offered)
    {
        const string identity = "f813d575-a123-4788-b34a-d547c15c5663:7";
        var agent = new Agent(firmware, offered); var runner = new RestoreRunner();
        var result = await UpdateMaintenanceRunner.ExecuteAsync(new() { ActionId = InterventionId.InstallMicrosoftUpdates,
            UpdateIdentity = "driver:" + identity, RestoreSessionId = RestoreRunner.Session, RestorePointSequenceNumber = 12 }, runner, agent, timeProvider: new Clock());
        Assert.Single(agent.Searches);
        Assert.Equal(("IsInstalled=0 and IsHidden=0 and Type='Driver'", 3, "7971f918-a847-4430-9279-4a52d1efe18d"), agent.Searches[0]);
        if (!firmware && offered)
        {
            Assert.Equal(identity, Assert.Single(Assert.Single(agent.Installed)).Identity);
            Assert.Equal(identity, Assert.Single(Assert.Single(agent.Downloaded)).Identity);
            Assert.Contains("driver-item|RESULT|", result.Message);
        }
        else
        {
            Assert.Empty(agent.Installed); Assert.Empty(agent.Downloaded);
            Assert.Contains(firmware ? "non pris en charge automatiquement" : "à vérifier", result.Message);
        }
    }

    private sealed class Runner : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessRunResult(0, request.Arguments.Contains("--version") && request.Arguments.Count == 1 ? "v1.12.0" : "", ""));
        }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token) => throw new InvalidOperationException("Aucun autre canal.");
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Backend : IUpdateSequenceBackend, ILauncherShutdownBackend, ISteamUpdateBackend, ISteamClient, IStoreUpdateBackend
    {
        public Backend(StoreClient? store = null) { StoreUpdates = new(store ?? new StoreClient()); }
        public StoreUpdateService StoreUpdates { get; }
        public ISteamClient SteamClient => this;
        public TimeProvider SteamClock => TimeProvider.System;
        public TimeSpan SteamWaitLimit => TimeSpan.Zero;
        public bool Session { get; init; } = true;
        public bool Game { get; init; }
        private bool _closed;
        public List<string> Calls { get; } = [];
        public List<string> Closed { get; } = [];
        public List<string> Launches { get; } = [];
        public Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(["Steam"]);
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token)
        { Calls.Add(step.Id); throw new InvalidOperationException("Appel inattendu : " + step.Id); }
        public Task<LauncherSnapshot> ReadAsync(string name, CancellationToken token) => Task.FromResult(new LauncherSnapshot(true,
            Game ? [new(1, 0, "C:/Steam/steam.exe"), new(2, 1, "C:/Games/game.exe")]
                : Session && !_closed ? [new(1, 0, "C:/Steam/steam.exe")] : [], ["C:/Games"]));
        public Task CloseAsync(string executable, bool force, CancellationToken token)
        { Closed.Add(executable); _closed = true; return Task.CompletedTask; }
        public Task<SteamSession> ReadSessionAsync(CancellationToken token) => Task.FromResult(new SteamSession(Session, "C:/Steam/steam.exe"));
        public Task<IReadOnlyList<SteamGameState>> ReadGamesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<SteamGameState>>([]);
        public Task<SteamExecutableCheck> CheckExecutableAsync(string? path, CancellationToken token) => Task.FromResult(new SteamExecutableCheck(true, ""));
        public Task<bool> LaunchSilentAsync(string path, CancellationToken token) { Launches.Add("-silent"); return Task.FromResult(true); }
    }
    private sealed class StoreClient : IStoreUpdateClient
    {
        public List<string> Started { get; } = [];
        public Task<StoreSearch> SearchAsync(CancellationToken token) => Task.FromResult(new StoreSearch(true, "", new[] {
            new StorePackage("a", "family-a", "Store A", "1.0"), new StorePackage("b", "family-b", "Store B", "1.0") }.Where(p => !Started.Contains(p.ProductId)).ToArray()));
        public Task<IReadOnlyDictionary<string, string>> ReadVersionsAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["family-a"] = Started.Contains("a") ? "2.0" : "1.0", ["family-b"] = "1.0" });
        public Task<StoreStart> StartAsync(StorePackage package, CancellationToken token) { Started.Add(package.ProductId); return Task.FromResult(new StoreStart(true, "")); }
    }
    private sealed class RestoreRunner : IElevatedCommandRunner
    {
        public const string Session = "f813d575-a123-4788-b34a-d547c15c5663";
        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command) => Task.FromResult(new ElevatedCommandResult(0,
            JsonSerializer.Serialize(new[] { new { SequenceNumber = 12, Description = "Virgil mises à jour " + Session, CreationTime = new Clock().Now.AddHours(-1) } }), ""));
    }
    private sealed class Item(string identity, bool firmware = false) : IWindowsUpdateItem
    {
        public string Identity => identity;
        public string Title => firmware ? "System Firmware" : "Pilote officiel";
        public bool IsFirmware => firmware;
        public bool IsFeatureUpgrade => false;
        public bool EulaAccepted { get; private set; }
        public bool IsDownloaded { get; set; }
        public void AcceptEula() => EulaAccepted = true;
    }
    private sealed class Agent(bool firmware, bool offered) : IWindowsUpdateAgent
    {
        private readonly Item _own = new("f813d575-a123-4788-b34a-d547c15c5663:7", firmware);
        public List<(string, int, string?)> Searches { get; } = [];
        public List<IReadOnlyList<IWindowsUpdateItem>> Installed { get; } = [];
        public List<IReadOnlyList<IWindowsUpdateItem>> Downloaded { get; } = [];
        public bool IsServiceRegistered(string serviceId) => true;
        public void AddService(string serviceId, int flags) => throw new InvalidOperationException();
        public Task<IReadOnlyList<IWindowsUpdateItem>> SearchAsync(string criteria, int serverSelection, string? serviceId)
        { Searches.Add((criteria, serverSelection, serviceId)); return Task.FromResult<IReadOnlyList<IWindowsUpdateItem>>(offered ? [_own, new Item("a313d575-a123-4788-b34a-d547c15c5663:1")] : [new Item("a313d575-a123-4788-b34a-d547c15c5663:1")]); }
        public Task DownloadAsync(IReadOnlyList<IWindowsUpdateItem> updates)
        { Downloaded.Add(updates); foreach (var item in updates.Cast<Item>()) item.IsDownloaded = true; return Task.CompletedTask; }
        public Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<IWindowsUpdateItem> updates)
        { Installed.Add(updates); return Task.FromResult(new WindowsUpdateInstallResult(1, false, true)); }
    }
}
