using System.Runtime.InteropServices;
using System.Text.Json;
using Virgil.Contracts;
using Virgil.Core.Updates;
using Virgil.ElevatedHelper;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Safety")]
public sealed class MicrosoftMaintenanceTests
{
    private const string Session = "f813d575-a123-4788-b34a-d547c15c5663";
    private const string Mu = "7971f918-a847-4430-9279-4a52d1efe18d";
    private const string StoreCommand = "Get-CimInstance -Namespace root\\cimv2\\mdm\\dmmap -ClassName MDM_EnterpriseModernAppManagement_AppManagement01 -ErrorAction Stop | Invoke-CimMethod -MethodName UpdateScanMethod -ErrorAction Stop";
    private static ElevatedInterventionRequest Request(InterventionId id = InterventionId.InstallMicrosoftUpdates, string tokens = "software,drivers,store,windows-feature-upgrade", int? sequence = 12)
        => new() { ActionId = id, UpdateIdentity = tokens, RestoreSessionId = Session, RestorePointSequenceNumber = sequence };
    private static string Points(bool session = true, int sequence = 12, int age = 1) => JsonSerializer.Serialize(new[] { new { SequenceNumber = sequence, Description = session ? "Virgil mises à jour " + Session : "Sauvegarde existante", CreationTime = DateTimeOffset.UtcNow.AddHours(-age) } });

    [Fact]
    public void Pnputil_french_accented_fields_are_read()
    {
        var item = Assert.Single(DriverInformationService.ParsePnPUtil("""
        Nom publié : oem12.inf
        Fournisseur du package de pilotes : NVIDIA
        Classe : Cartes graphiques
        Version du pilote : 31.0.15
        Date du pilote : 29/10/2026
        Signataire : Microsoft
        """));
        Assert.Equal("oem12.inf", item.PublishedName);
        Assert.Equal("NVIDIA", item.Provider);
        Assert.Equal("Cartes graphiques", item.ClassName);
        Assert.Equal("31.0.15", item.Version);
        Assert.Equal("29/10/2026", item.Date);
        Assert.Equal("Microsoft", item.Signer);
    }

    [Theory]
    [InlineData("Maintenance des composants")]
    [InlineData("Windows Update")]
    [InlineData("")]
    public void Pending_reboot_probe_reports_cbs_and_windows_update_sources(string source)
    {
        var result = new WindowsUpdateStatusService(new Probe(source)).ReadStatus();
        Assert.Equal(source.Length > 0, result.PendingRebootDetected);
        if (source.Length > 0) Assert.Contains(result.Notes, note => note.Contains(source, StringComparison.Ordinal));
    }
    private sealed class Probe(string source) : IPendingRebootProbe
    { public PendingRebootStatus Read() => new(true, source.Length == 0 ? [] : [source]); }

    [Fact]
    public async Task Install_microsoft_updates_runs_software_drivers_store_then_feature_upgrade()
    {
        var events = new List<string>(); var agent = new Agent(events); var commands = new Commands(events);
        var result = await UpdateMaintenanceRunner.ExecuteAsync(Request(), commands, agent);
        Assert.True(result.Success);
        Assert.Equal(new[] { "Software", "Driver", "Store", "Feature" }, events);
        Assert.Contains("VIRGIL|", result.Message);
    }

    [Fact]
    public async Task Microsoft_update_is_registered_once_with_script_flags()
    {
        var agent = new Agent([]) { Registered = false }; var commands = new Commands([]);
        var request = Request(InterventionId.PrepareMicrosoftUpdates, "defender,microsoft-update", null);
        Assert.True((await UpdateMaintenanceRunner.ExecuteAsync(request, commands, agent)).Success);
        Assert.Equal(new[] { (Mu, 7) }, agent.Added);
        await UpdateMaintenanceRunner.ExecuteAsync(request, commands, agent);
        Assert.Single(agent.Added);
        Assert.Contains(commands.Calls, c => c.Arguments.Last() == "Update-MpSignature -ErrorAction Stop");
        agent.Registered = false; agent.RejectRegistration = true;
        var prepare = await UpdateMaintenanceRunner.ExecuteAsync(request, commands, agent);
        Assert.Contains("Microsoft Update indisponible : Windows Update seul", prepare.Message);
        await UpdateMaintenanceRunner.ExecuteAsync(Request(tokens: "software"), commands, agent);
        Assert.All(agent.Searches, s => { Assert.Equal(0, s.Selection); Assert.Null(s.Service); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Windows_update_passes_repeat_until_nothing_installed_max_three(bool always)
    {
        var events = new List<string>(); var agent = new Agent(events) { Counts = always ? new Queue<int>([1,1,1,1,1,1,1]) : new Queue<int>([2,0,1,0,0,0]) };
        await UpdateMaintenanceRunner.ExecuteAsync(Request(tokens: "software,drivers"), new Commands(events), agent);
        Assert.Equal(new[] { "Software", "Driver", "Software", "Driver", "Software", "Driver" }, events);
        Assert.All(agent.Searches, s => { Assert.Equal(3, s.Selection); Assert.Equal(Mu, s.Service); Assert.Contains("IsInstalled=0 and IsHidden=0", s.Criteria); });
    }

    [Fact]
    public async Task Drivers_unchecked_means_no_driver_search()
    {
        var agent = new Agent([]);
        await UpdateMaintenanceRunner.ExecuteAsync(Request(tokens: "software"), new Commands([]), agent);
        Assert.Single(agent.Searches);
        Assert.DoesNotContain(agent.Searches, s => s.Criteria.Contains("Driver", StringComparison.Ordinal));
        Assert.DoesNotContain(agent.Events, e => e == "Feature");
    }

    [Fact]
    public async Task Store_scan_runs_the_fixed_cim_command()
    {
        var events = new List<string>(); var commands = new Commands(events) { StoreExitCode = 1 }; var agent = new Agent(events);
        var result = await UpdateMaintenanceRunner.ExecuteAsync(Request(tokens: "store,windows-feature-upgrade"), commands, agent);
        Assert.Equal(StoreCommand, Assert.Single(commands.Calls, c => c.Arguments.Last().Contains("Get-CimInstance", StringComparison.Ordinal)).Arguments.Last());
        Assert.Contains("Scan Store non disponible sur ce PC", result.Message);
        Assert.Contains("VIRGIL|store|UNAVAILABLE|", result.Message);
        Assert.Equal(new[] { "Store", "Feature" }, events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Feature_upgrade_runs_last_and_reports_nothing_to_install_when_absent(bool present)
    {
        var events = new List<string>(); var agent = new Agent(events) { FeaturePresent = present };
        var result = await UpdateMaintenanceRunner.ExecuteAsync(Request(), new Commands(events), agent);
        Assert.True(result.Success);
        Assert.Equal("Feature", events.Last());
        if (present) { Assert.Single(agent.Installed); Assert.True(agent.Installed[0].IsFeatureUpgrade); Assert.True(result.Reboot); }
        else Assert.Contains("Rien à installer", result.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Restore_point_four_cases_are_fail_closed(int scenario)
    {
        var commands = new Commands([]);
        commands.Reads = new Queue<string>(scenario switch { 1 => [Points()], 2 => ["[]", Points(sequence: 13)], 3 => [Points(false), Points(false)], _ => ["[]", "[]"] });
        commands.CreateExitCode = scenario >= 3 ? 1 : 0;
        var agent = new Agent([]);
        var result = await UpdateMaintenanceRunner.ExecuteAsync(Request(InterventionId.PrepareMicrosoftUpdates, "defender,microsoft-update", null), commands, agent);
        Assert.Equal(scenario != 4, result.Success);
        Assert.Equal(scenario == 1 ? 0 : 1, commands.Creates);
        if (scenario == 3) { Assert.Contains("point existant du", result.Message); Assert.Contains("(Sauvegarde existante) réutilisé", result.Message); }
        if (scenario != 4) Assert.Contains("VIRGIL|restore-point|OK|" + (scenario == 2 ? "13" : "12"), result.Message);
        else { Assert.Empty(agent.Added); Assert.Empty(agent.Searches); Assert.DoesNotContain(commands.Calls, c => c.Arguments.Last().Contains("Update-MpSignature", StringComparison.Ordinal)); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Legacy_action_reuses_recent_point_only_after_creation_refused_or_without_effect(int creationExit)
    {
        var commands = new Commands([]) { Reads = new([Points(false), Points(sequence: 13)]), CreateExitCode = 0 };
        var result = await UpdateMaintenanceRunner.ExecuteAsync(Request(InterventionId.InstallWindowsUpdates, "", null), commands, new Agent([]));
        Assert.True(result.Success); Assert.Equal(1, commands.Creates); Assert.DoesNotContain("réutilisé", result.Message); Assert.Contains("VIRGIL|restore-point|OK|13", result.Message);
        commands = new Commands([]) { Reads = new([Points(false), Points(false)]), CreateExitCode = creationExit };
        result = await UpdateMaintenanceRunner.ExecuteAsync(Request(InterventionId.InstallWindowsUpdates, "", null), commands, new Agent([]));
        Assert.True(result.Success); Assert.Equal(1, commands.Creates); Assert.Contains("réutilisé", result.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(99)]
    public async Task Install_requires_the_exact_valid_restore_sequence(int? sequence)
    {
        var agent = new Agent([]); var commands = new Commands([]);
        var result = await UpdateMaintenanceRunner.ExecuteAsync(Request(sequence: sequence), commands, agent);
        Assert.False(result.Success); Assert.Empty(agent.Searches); Assert.Equal(0, commands.Creates);
    }

    [Fact]
    public async Task Restore_point_failure_blocks_all_microsoft_steps()
    {
        var agent = new Agent([]); var commands = new Commands([]) { Reads = new(["[]", "[]"]), CreateExitCode = 1 };
        var result = await UpdateMaintenanceRunner.ExecuteAsync(Request(InterventionId.PrepareMicrosoftUpdates, "defender,microsoft-update", null), commands, agent);
        Assert.False(result.Success); Assert.Empty(agent.Events); Assert.Empty(agent.Added);
        Assert.All(commands.Calls, c => Assert.DoesNotContain("Update-MpSignature", c.Arguments.Last()));
    }

    [Theory]
    [InlineData(InterventionId.PrepareMicrosoftUpdates)]
    [InlineData(InterventionId.InstallWindowsUpdates)]
    public async Task Cancel_after_restore_starts_no_microsoft_step(InterventionId id)
    {
        using var cancel = new CancellationTokenSource(); var agent = new Agent([]); var commands = new Commands([]) { AfterRead = cancel.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UpdateMaintenanceRunner.ExecuteAsync(Request(id, id == InterventionId.PrepareMicrosoftUpdates ? "defender,microsoft-update" : "", null), commands, agent, cancel.Token));
        Assert.Empty(agent.Events); Assert.Empty(agent.Added); Assert.Single(commands.Calls);
    }

    [Fact]
    public async Task Cancel_during_windows_update_pass_finishes_current_call_and_starts_no_next_pass()
    {
        using var cancel = new CancellationTokenSource(); var agent = new Agent([]) { Counts = new([1]), BlockInstall = true };
        var task = UpdateMaintenanceRunner.ExecuteAsync(Request(), new Commands(agent.Events), agent, cancel.Token);
        await agent.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel(); Assert.False(task.IsCompleted);
        agent.Finish.SetResult(new(1, true, true));
        var result = await task;
        Assert.True(result.Reboot); Assert.Contains("terminée après annulation", result.Message);
        Assert.Equal(new[] { "Software" }, agent.Events); Assert.Single(agent.Searches);
    }

    private sealed class Commands(List<string> events) : IElevatedCommandRunner
    {
        public List<ElevatedCommandSpec> Calls { get; } = [];
        public Queue<string> Reads { get; set; } = new([Points()]);
        public int Creates { get; private set; }
        public int CreateExitCode { get; set; }
        public int StoreExitCode { get; set; }
        public Action? AfterRead { get; set; }
        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command)
        {
            Calls.Add(command); var script = command.Arguments.Last();
            if (script.Contains("Checkpoint-Computer", StringComparison.Ordinal)) { Creates++; return Task.FromResult(new ElevatedCommandResult(CreateExitCode, "", "")); }
            if (script.Contains("Get-ComputerRestorePoint", StringComparison.Ordinal)) { AfterRead?.Invoke(); return Task.FromResult(new ElevatedCommandResult(0, Reads.Count > 1 ? Reads.Dequeue() : Reads.Peek(), "")); }
            if (script.Contains("Get-CimInstance", StringComparison.Ordinal)) { events.Add("Store"); return Task.FromResult(new ElevatedCommandResult(StoreExitCode, "", "")); }
            return Task.FromResult(new ElevatedCommandResult(0, "", ""));
        }
    }
    private sealed class Item(bool feature = false) : IWindowsUpdateItem
    {
        public bool IsFeatureUpgrade => feature;
        public bool EulaAccepted { get; private set; }
        public bool IsDownloaded { get; set; }
        public void AcceptEula() => EulaAccepted = true;
    }
    private sealed class Agent(List<string> events) : IWindowsUpdateAgent
    {
        public List<string> Events => events;
        public bool Registered { get; set; } = true;
        public bool RejectRegistration { get; set; }
        public bool FeaturePresent { get; set; }
        public bool BlockInstall { get; set; }
        public Queue<int> Counts { get; set; } = new();
        public List<(string, int)> Added { get; } = [];
        public List<(string Criteria, int Selection, string? Service)> Searches { get; } = [];
        public List<IWindowsUpdateItem> Installed { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<WindowsUpdateInstallResult> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsServiceRegistered(string serviceId) { Assert.Equal(Mu, serviceId); return Registered; }
        public void AddService(string serviceId, int flags) { if (RejectRegistration) throw new COMException(); Added.Add((serviceId, flags)); Registered = true; }
        public Task<IReadOnlyList<IWindowsUpdateItem>> SearchAsync(string criteria, int serverSelection, string? serviceId)
        {
            Searches.Add((criteria, serverSelection, serviceId));
            var feature = criteria.Contains("3689bdc8", StringComparison.Ordinal);
            events.Add(feature ? "Feature" : criteria.Contains("Driver", StringComparison.Ordinal) ? "Driver" : "Software");
            var count = feature ? (FeaturePresent ? 1 : 0) : Counts.Count > 0 ? Counts.Dequeue() : 0;
            return Task.FromResult<IReadOnlyList<IWindowsUpdateItem>>(Enumerable.Range(0, count).Select(_ => new Item(feature)).ToArray());
        }
        public Task DownloadAsync(IReadOnlyList<IWindowsUpdateItem> updates) { foreach (var update in updates.Cast<Item>()) { Assert.True(update.EulaAccepted); update.IsDownloaded = true; } return Task.CompletedTask; }
        public Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<IWindowsUpdateItem> updates)
        { Assert.All(updates, u => Assert.True(u.IsDownloaded)); Installed.AddRange(updates); Started.TrySetResult(); return BlockInstall ? Finish.Task : Task.FromResult(new WindowsUpdateInstallResult(updates.Count, updates.Any(u => u.IsFeatureUpgrade), true)); }
    }
}
