using System.Text.RegularExpressions;
using Virgil.Contracts;
using Virgil.Core.Interventions;
using Virgil.ElevatedHelper;
using Xunit;

namespace Virgil.Tests;

public sealed class LotReparationWindowsUpdateResetTests
{
    private const InterventionId Reset = InterventionId.ResetWindowsUpdate;
    private const string Suffix = ".virgil-20261125-064900";

    [Fact]
    public void Catalog_and_allowlist_expose_a_separate_protected_reset()
    {
        var catalog = new InterventionCatalog();
        var reset = catalog.Get(Reset);
        Assert.Equal(27, (int)reset.Id);
        Assert.Equal("Remettre à zéro Windows Update", reset.Title);
        Assert.Equal(InterventionCategory.System, reset.Category);
        Assert.Equal(InterventionRiskLevel.Sensitive, reset.RiskLevel);
        Assert.Equal(20, reset.DisplayOrder);
        Assert.Equal("Quelques minutes", reset.EstimatedDuration);
        Assert.True(reset.RequiresAdministrator);
        Assert.False(reset.RebootPossible);
        Assert.False(reset.CanBeInterruptedAfterStart);
        Assert.True(InterventionSafety.RequiresRestorePoint(reset.Id));
        Assert.Contains("Aucun dossier n’est supprimé", reset.Description);
        Assert.Contains("Virgil ne redémarre pas le PC", reset.Description);
        var preview = Assert.Single(reset.CommandPreviews);
        Assert.Equal("virgil-update-reset", preview.Executable);
        Assert.Empty(preview.Arguments);
        Assert.True(preview.RunsElevated);

        var restart = catalog.Get(InterventionId.RestartWindowsUpdate);
        Assert.Equal("Redémarrer le service Windows Update", restart.Title);
        Assert.Equal(14, restart.DisplayOrder);
        var restartPreview = Assert.Single(restart.CommandPreviews);
        Assert.Equal("virgil-update", restartPreview.Executable);
        Assert.Empty(restartPreview.Arguments);

        var allowlist = new ElevatedActionAllowlist();
        Assert.True(allowlist.TryGet(Reset, "C:", out var spec));
        Assert.True(spec.RequiresAdministrator);
        Assert.False(spec.RebootRequiredOnSuccess);
        var command = Assert.Single(spec.Commands);
        Assert.Equal("virgil-update-reset", command.FileName);
        Assert.Empty(command.Arguments);
        Assert.True(command.NonInterruptible);

        Assert.True(allowlist.TryGet(restart.Id, "C:", out var restartSpec));
        var restartCommand = Assert.Single(restartSpec.Commands);
        Assert.Equal("virgil-update", restartCommand.FileName);
        Assert.Empty(restartCommand.Arguments);
        Assert.DoesNotContain(Reset, InterventionSafety.RepairsFor("VIRGIL|wuauserv|FINDING|Service désactivé"));
        Assert.Equal(TimeSpan.FromMinutes(45), ElevatedCommandRunner.TimeoutFor("sfc.exe"));
        Assert.Equal(TimeSpan.FromMinutes(60), ElevatedCommandRunner.TimeoutFor("dism.exe"));
        Assert.Equal(TimeSpan.FromMinutes(5), ElevatedCommandRunner.TimeoutFor("virgil-update-reset"));
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task Reset_checks_stops_renames_and_starts_in_order()
    {
        var host = new ResetHost(@"C:\Windows\System32");
        var result = await Run(host);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.StartsWith("Remise à zéro de Windows Update terminée.", result.StandardOutput);
        Assert.Contains("msiserver reste à démarrage à la demande", result.StandardOutput);
        Assert.Equal(new[]
        {
            "config:wuauserv", "config:bits", "config:cryptsvc", "config:msiserver", "état:TrustedInstaller", "redémarrage",
            @"attributs:C:\Windows\SoftwareDistribution", @"attributs:C:\Windows\System32\catroot2",
            @"attributs:C:\Windows\SoftwareDistribution.virgil-20261125-064900",
            @"attributs:C:\Windows\System32\catroot2.virgil-20261125-064900",
            "état:wuauserv", "arrêt:wuauserv", "état:wuauserv", "état:bits", "arrêt:bits", "état:bits",
            "état:cryptsvc", "arrêt:cryptsvc", "état:cryptsvc", "état:msiserver",
            @"renommage:C:\Windows\SoftwareDistribution → C:\Windows\SoftwareDistribution.virgil-20261125-064900",
            @"renommage:C:\Windows\System32\catroot2 → C:\Windows\System32\catroot2.virgil-20261125-064900",
            "état:cryptsvc", "démarrage:cryptsvc", "état:cryptsvc", "état:bits", "démarrage:bits", "état:bits",
            "état:wuauserv", "démarrage:wuauserv", "état:wuauserv"
        }, host.Operations);
        Assert.Equal(new[] { host.Software + Suffix, host.Catalog + Suffix }, host.Entries.Keys.Order());
    }

    [Theory]
    [InlineData("service absent", "wuauserv")]
    [InlineData("service désactivé", "désactivé")]
    [InlineData("installation MSI", "msiserver")]
    [InlineData("installation Windows", "TrustedInstaller")]
    [InlineData("redémarrage", "redémarrez d’abord le PC")]
    [InlineData("lien", "dossier ordinaire")]
    [InlineData("fichier", "dossier ordinaire")]
    [InlineData("destination", "existe déjà")]
    [InlineData("relatif", "chemin complet")]
    [InlineData("hors système", "System32")]
    public async Task Invalid_precondition_leaves_everything_untouched(string fault, string reason)
    {
        var host = CreateHost();
        var system = host.SystemDirectory;
        switch (fault)
        {
            case "service absent": host.Services.Remove("wuauserv"); break;
            case "service désactivé": host.Disabled = "bits"; break;
            case "installation MSI": host.Services["msiserver"] = WindowsUpdateServiceState.Running; break;
            case "installation Windows": host.Services["TrustedInstaller"] = WindowsUpdateServiceState.Running; break;
            case "redémarrage": host.PendingReboot = true; break;
            case "lien": host.Entries[host.Catalog] |= FileAttributes.ReparsePoint; break;
            case "fichier": host.Entries[host.Catalog] = FileAttributes.Normal; break;
            case "destination": host.Entries.Add(host.Catalog + Suffix, FileAttributes.Directory); break;
            case "relatif": system = "Windows/System32"; break;
            case "hors système": system = Path.GetDirectoryName(system)!; break;
        }

        var result = await new WindowsUpdateResetRunner(host, new ResetClock(), system).RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("Remise à zéro de Windows Update non lancée", result.StandardError);
        Assert.Contains(reason, result.StandardError);
        Assert.EndsWith("Aucune modification.", result.StandardError);
        Assert.DoesNotContain(host.Operations, operation => operation.StartsWith("arrêt:", StringComparison.Ordinal)
            || operation.StartsWith("renommage:", StringComparison.Ordinal) || operation.StartsWith("démarrage:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Absent_source_is_recorded_and_the_other_directory_is_renamed()
    {
        var host = CreateHost();
        host.Entries.Remove(host.Software);

        var result = await Run(host);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("SoftwareDistribution : absent", result.StandardOutput);
        Assert.Contains("absent, rien à renommer", result.StandardOutput);
        Assert.Equal(host.Catalog + Suffix, Assert.Single(host.Entries.Keys));
    }

    [Theory]
    [InlineData("arrêt:wuauserv")]
    [InlineData("arrêt:bits")]
    [InlineData("arrêt:cryptsvc")]
    [InlineData("état:msiserver")]
    [InlineData("premier renommage")]
    [InlineData("second renommage")]
    [InlineData("démarrage:cryptsvc")]
    [InlineData("démarrage:bits")]
    [InlineData("démarrage:wuauserv")]
    public async Task Failure_of_each_step_attempts_recovery(string fault)
    {
        var host = CreateHost();
        var operation = fault switch
        {
            "premier renommage" => Move(host.Software, host.Software + Suffix),
            "second renommage" => Move(host.Catalog, host.Catalog + Suffix),
            _ => fault
        };
        var failed = false;
        host.OnOperation = actual =>
        {
            if (!failed && actual == operation)
            {
                failed = true;
                throw new IOException("Échec simulé");
            }
        };

        var result = await Run(host);

        Assert.True(failed);
        AssertRecovered(host, result);
        Assert.Contains("Échec simulé", result.StandardOutput);
        if (fault.StartsWith("démarrage:", StringComparison.Ordinal))
        {
            var inverseMoves = host.Operations.Where(op => op.StartsWith("renommage:", StringComparison.Ordinal)).Skip(2);
            Assert.Equal(new[] { Move(host.Catalog + Suffix, host.Catalog), Move(host.Software + Suffix, host.Software) }, inverseMoves);
        }
    }

    [Theory]
    [InlineData("arrêt:bits", "arrêté", "en cours d’exécution")]
    [InlineData("démarrage:bits", "en cours d’exécution", "arrêté")]
    public async Task Service_deadline_reports_expected_and_observed_state(string fault, string expected, string observed)
    {
        var host = CreateHost();
        var clock = new ResetClock();
        host.IgnoreTransitionOnce = fault;

        var pending = Run(host, clock);
        Assert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(30));
        var result = await pending;

        AssertRecovered(host, result);
        Assert.Contains($"service bits : état attendu {expected} en 30 secondes, état observé {observed}", result.StandardOutput);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recovery_keeps_both_paths_on_conflict_or_failed_inverse_move(bool recreate)
    {
        var host = CreateHost();
        host.OnOperation = actual =>
        {
            if (actual == Move(host.Catalog, host.Catalog + Suffix))
            {
                if (recreate)
                    host.Entries.Add(host.Software, FileAttributes.Directory);
                throw new IOException("Échec du second renommage");
            }

            if (actual == Move(host.Software + Suffix, host.Software))
                throw new IOException("Échec du renommage inverse");
        };

        var result = await Run(host);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Rattrapage incomplet", result.StandardError);
        var paths = $"{host.Software + Suffix} vers {host.Software}";
        Assert.Contains(paths, result.StandardError);
        Assert.Contains(paths, result.StandardOutput);
        Assert.Contains(host.Software + Suffix, host.Entries.Keys);
        Assert.Contains(host.Catalog, host.Entries.Keys);
        Assert.Equal(recreate, host.Entries.ContainsKey(host.Software));
        Assert.Equal(!recreate, host.Operations.Contains(Move(host.Software + Suffix, host.Software)));
    }

    [Fact]
    public async Task Recovery_names_a_service_that_cannot_restart_and_continues()
    {
        var host = CreateHost();
        host.OnOperation = actual =>
        {
            if (actual == "démarrage:bits")
                throw new IOException("Échec du démarrage");
        };

        var result = await Run(host);

        Assert.Equal(2, result.ExitCode);
        Assert.EndsWith("Rattrapage incomplet :\nbits", result.StandardError);
        Assert.Contains("démarrage:wuauserv", host.Operations);
        Assert.Equal(new[] { host.Software, host.Catalog }, host.Entries.Keys.Order());
    }

    [Fact]
    public void Reset_source_and_host_have_no_destructive_or_external_command_apis()
    {
        var root = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src", "Virgil.ElevatedHelper");
        string[] forbidden =
        [
            "Delete", "CreateDirectory", "powershell", "cmd.exe", "ResetBase", "DISM", "SetValue", "CreateSubKey",
            "DeleteValue", "writable: true", "ChangeServiceConfig", "shutdown", "InitiateSystemShutdown",
            "ExitWindowsEx", "Restart-Computer", "Stop-Computer", ".Wait(", ".Result", "GetAwaiter().GetResult()",
            "Thread.Sleep", "takeown", "icacls"
        ];
        foreach (var file in new[] { "WindowsUpdateResetRunner.cs", "WindowsUpdateResetHost.cs" })
        {
            var source = File.ReadAllText(Path.Combine(root, file));
            Assert.DoesNotContain("Process", source, StringComparison.Ordinal);
            foreach (var term in forbidden)
                Assert.DoesNotContain(term, source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(new Regex(@"OpenSubKey\s*\([^\r\n]*,", RegexOptions.IgnoreCase), source);
        }

        Assert.Equal(new[] { "InspectService", "IsRebootPending", "MoveDirectory", "ReadAttributes", "ReadServiceState", "RequestStart", "RequestStop" },
            typeof(IWindowsUpdateResetHost).GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal));
    }

    private static void AssertRecovered(ResetHost host, ElevatedCommandResult result)
    {
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Rattrapage effectué : noms d'origine rétablis, services redémarrés.", result.StandardError);
        Assert.Equal(new[] { host.Software, host.Catalog }.Order(), host.Entries.Keys.Order());
        Assert.All(new[] { "cryptsvc", "bits", "wuauserv" },
            name => Assert.Equal(WindowsUpdateServiceState.Running, host.Services[name]));
        Assert.Equal(WindowsUpdateServiceState.Stopped, host.Services["msiserver"]);
    }

    private static ResetHost CreateHost() => new(Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "Windows", "System32"));

    private static Task<ElevatedCommandResult> Run(ResetHost host, ResetClock? clock = null) =>
        new WindowsUpdateResetRunner(host, clock ?? new ResetClock(), host.SystemDirectory).RunAsync();

    private static string Move(string source, string destination) => $"renommage:{source} → {destination}";

    private sealed class ResetHost : IWindowsUpdateResetHost
    {
        public ResetHost(string systemDirectory)
        {
            SystemDirectory = systemDirectory;
            Software = Path.Combine(Path.GetDirectoryName(systemDirectory)!, "SoftwareDistribution");
            Catalog = Path.Combine(systemDirectory, "catroot2");
            Entries = new() { [Software] = FileAttributes.Directory, [Catalog] = FileAttributes.Directory };
        }

        public string SystemDirectory { get; }
        public string Software { get; }
        public string Catalog { get; }
        public Dictionary<string, FileAttributes> Entries { get; }
        public Dictionary<string, WindowsUpdateServiceState> Services { get; } = new()
        {
            ["wuauserv"] = WindowsUpdateServiceState.Running,
            ["bits"] = WindowsUpdateServiceState.Running,
            ["cryptsvc"] = WindowsUpdateServiceState.Running,
            ["msiserver"] = WindowsUpdateServiceState.Stopped,
            ["TrustedInstaller"] = WindowsUpdateServiceState.Stopped
        };
        public List<string> Operations { get; } = [];
        public string? Disabled { get; set; }
        public bool PendingReboot { get; set; }
        public string? IgnoreTransitionOnce { get; set; }
        public Action<string>? OnOperation { get; set; }

        public WindowsUpdateServiceInfo InspectService(string name)
        {
            Record($"config:{name}");
            return new(Services.TryGetValue(name, out var state) ? state : throw new IOException($"Service {name} absent"), name == Disabled);
        }

        public WindowsUpdateServiceState ReadServiceState(string name)
        {
            Record($"état:{name}");
            return Services[name];
        }

        public void RequestStop(string name) => Transition($"arrêt:{name}", name, WindowsUpdateServiceState.Stopped);
        public void RequestStart(string name) => Transition($"démarrage:{name}", name, WindowsUpdateServiceState.Running);

        public bool IsRebootPending()
        {
            Record("redémarrage");
            return PendingReboot;
        }

        public FileAttributes? ReadAttributes(string path)
        {
            Record($"attributs:{path}");
            return Entries.TryGetValue(path, out var attributes) ? attributes : null;
        }

        public void MoveDirectory(string source, string destination)
        {
            Record(Move(source, destination));
            Entries.Add(destination, Entries[source]);
            Entries.Remove(source);
        }

        private void Transition(string operation, string name, WindowsUpdateServiceState state)
        {
            Record(operation);
            if (IgnoreTransitionOnce == operation)
                IgnoreTransitionOnce = null;
            else
                Services[name] = state;
        }

        private void Record(string operation)
        {
            Operations.Add(operation);
            OnOperation?.Invoke(operation);
        }
    }

    private sealed class ResetClock : TimeProvider
    {
        private Action? _fire;
        private long _ticks;

        public override DateTimeOffset GetUtcNow() => new(2026, 11, 25, 6, 49, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _fire = () => callback(state);
            return new FakeTimer();
        }

        public void Advance(TimeSpan elapsed)
        {
            _ticks += elapsed.Ticks;
            var fire = _fire ?? throw new InvalidOperationException("Attente d’un service attendue, aucune minuterie observée.");
            _fire = null;
            fire();
        }

        private sealed class FakeTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
