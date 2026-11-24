using Virgil.Contracts;
using Virgil.Core.Interventions;
using Virgil.ElevatedHelper;
using Xunit;

namespace Virgil.Tests;

public sealed class LotReparationWsResetTests
{
    private const string TimeoutMessage = "wsreset n'a pas terminé en 5 minutes.";

    [Fact]
    public void Catalog_and_allowlist_define_one_protected_command()
    {
        var definition = new InterventionCatalog().Get(InterventionId.ResetStoreCache);
        Assert.Equal(24, (int)InterventionId.InstallMicrosoftUpdates);
        Assert.Equal(25, (int)definition.Id);
        Assert.Equal("Réinitialiser le cache du Microsoft Store", definition.Title);
        Assert.Contains("Réinitialise le cache du Microsoft Store", definition.ExpectedEffect);
        Assert.Contains("Le Store peut s’ouvrir", definition.ExpectedEffect);
        Assert.Equal(InterventionRiskLevel.Sensitive, definition.RiskLevel);
        Assert.Equal(18, definition.DisplayOrder);
        Assert.True(definition.RequiresAdministrator);
        Assert.False(definition.RebootPossible);
        Assert.False(definition.CanBeInterruptedAfterStart);
        Assert.True(InterventionSafety.RequiresRestorePoint(definition.Id));
        var preview = Assert.Single(definition.CommandPreviews);
        Assert.Equal("wsreset.exe", preview.Executable);
        Assert.Empty(preview.Arguments);
        Assert.True(preview.RunsElevated);

        var allowlist = new ElevatedActionAllowlist();
        Assert.True(allowlist.TryGet(definition.Id, "C:", out var spec));
        Assert.True(spec.RequiresAdministrator);
        Assert.False(spec.RebootRequiredOnSuccess);
        var command = Assert.Single(spec.Commands);
        Assert.Equal("wsreset.exe", command.FileName);
        Assert.Empty(command.Arguments);
        Assert.True(command.NonInterruptible);

        Assert.True(allowlist.TryGet(InterventionId.ChkdskOnlineScan, "C:", out var chkdsk));
        Assert.Equal(new[] { "C:", "/scan" }, Assert.Single(chkdsk.Commands).Arguments);
    }

    [Theory]
    [InlineData("système", true)]
    [InlineData("parent", false)]
    [InlineData("courant", false)]
    [InlineData("absent", false)]
    public async Task Analyze_requires_wsreset_in_system_directory(string location, bool available)
    {
        var windows = Path.Combine(Path.GetTempPath(), "Windows");
        var system = Path.Combine(windows, "System32");
        var existingFile = location switch
        {
            "système" => Path.Combine(system, "wsreset.exe"),
            "parent" => Path.Combine(windows, "wsreset.exe"),
            "courant" => "wsreset.exe",
            _ => null
        };
        var service = new InterventionDiagnosticService(new InterventionCatalog(), path => path == existingFile,
            () => system, _ => [], () => []);

        var actions = await service.DiagnoseAllAsync(CancellationToken.None);
        var result = Assert.Single(actions, action => action.Definition.Id == InterventionId.ResetStoreCache);

        Assert.Equal(available, result.IsAvailable);
        Assert.Equal(available ? InterventionStatus.Available : InterventionStatus.Unavailable, result.Status);
        Assert.Contains("Droits administrateur demandés uniquement après confirmation.", result.Warnings);
        if (available)
            Assert.Empty(result.Errors);
        else
            Assert.Equal("wsreset.exe indisponible.", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sfc_keeps_its_existing_fallback_lookup(bool parentOnly)
    {
        var windows = Path.Combine(Path.GetTempPath(), "Windows");
        var existingFile = parentOnly ? Path.Combine(windows, "sfc.exe") : "sfc.exe";
        var service = new InterventionDiagnosticService(new InterventionCatalog(), path => path == existingFile,
            () => Path.Combine(windows, "System32"), _ => [], () => []);

        var result = await service.DiagnoseAsync(InterventionId.SfcVerifyOnly, CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("AppXSvc")]
    [InlineData("ClipSVC")]
    public void Disabled_store_services_do_not_suggest_reset(string service)
        => Assert.Empty(InterventionSafety.RepairsFor($"VIRGIL|{service}|FINDING|Service désactivé."));

    [Fact]
    public async Task Wsreset_is_stopped_at_five_minutes_and_failure_is_logged()
    {
        var clock = new ManualClock();
        var duration = ElevatedCommandRunner.TimeoutFor("wsreset.exe");
        Assert.Equal(TimeSpan.FromMinutes(5), duration);
        using var timeout = new CancellationTokenSource(duration, clock);
        var process = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stops = 0;
        var runner = new RecordingRunner(command => ElevatedCommandRunner.WaitForWsResetAsync(
            token => process.Task.WaitAsync(token),
            () => { stops++; process.SetResult(); },
            Task.FromResult("Cache en cours de réinitialisation."),
            Task.FromResult(""),
            () => 0,
            timeout.Token));
        var dispatcher = CreateDispatcher(runner, administrator: true);

        var pending = dispatcher.ExecuteValidatedAsync(new() { ActionId = InterventionId.ResetStoreCache });
        clock.Advance(duration - TimeSpan.FromMilliseconds(1));
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, stops);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        var result = await pending;

        Assert.Equal(1, stops);
        Assert.Equal(InterventionStatus.PartialFailure, result.Status);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains(TimeoutMessage, result.ReadableError);
        Assert.Contains(TimeoutMessage, result.SummaryOutput);
        Assert.Contains("Cache en cours de réinitialisation.", result.SummaryOutput);
        Assert.False(result.RebootRequired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execution_requires_admin_and_preserves_runner_failure(bool administrator)
    {
        var runner = new RecordingRunner(_ => Task.FromResult(new ElevatedCommandResult(
            7, "Journal du cache.", "Réinitialisation du cache impossible.")));
        var dispatcher = CreateDispatcher(runner, administrator);

        var result = await dispatcher.ExecuteValidatedAsync(new() { ActionId = InterventionId.ResetStoreCache });

        if (!administrator)
        {
            Assert.Equal(InterventionStatus.Failed, result.Status);
            Assert.Contains("administrateur", result.ReadableError);
            Assert.Empty(runner.Commands);
            return;
        }

        Assert.Equal(InterventionStatus.PartialFailure, result.Status);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("Réinitialisation du cache impossible.", result.ReadableError);
        Assert.Contains("Journal du cache.", result.SummaryOutput);
        var command = Assert.Single(runner.Commands);
        Assert.Equal("wsreset.exe", command.FileName);
        Assert.Empty(command.Arguments);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Confirmation_and_verified_restore_point_gate_wsreset(bool confirmed, bool restoreVerified)
    {
        var runner = new RecordingRunner(_ => Task.FromResult(new ElevatedCommandResult(0, "Cache réinitialisé.", "")));
        var helper = new RecordingHelper(CreateDispatcher(runner, administrator: true), restoreVerified);
        var service = new InterventionExecutionService(helper, new UnexpectedExplorer(), TestPlans.Authority);
        var diagnostic = new InterventionDiagnostic
        {
            Definition = new InterventionCatalog().Get(InterventionId.ResetStoreCache),
            IsAvailable = true
        };

        var result = await service.ExecuteAsync(diagnostic, confirmed ? TestPlans.Intervention(diagnostic) : null,
            CancellationToken.None);

        if (!confirmed)
        {
            Assert.Equal(InterventionStatus.Failed, result.Status);
            Assert.Contains("Intervention annulée.", result.ReadableError);
            Assert.False(result.WasElevated);
            Assert.Empty(helper.Calls);
            Assert.Empty(runner.Commands);
        }
        else if (!restoreVerified)
        {
            Assert.Equal(InterventionStatus.Failed, result.Status);
            Assert.Contains("point de restauration non créé ou non vérifié", result.ReadableError);
            Assert.Equal(InterventionId.CreateRestorePoint, Assert.Single(helper.Calls));
            Assert.Empty(runner.Commands);
        }
        else
        {
            Assert.Equal(InterventionStatus.Completed, result.Status);
            Assert.True(result.WasConfirmed);
            Assert.True(result.WasElevated);
            Assert.Equal(new[] { InterventionId.CreateRestorePoint, InterventionId.ResetStoreCache }, helper.Calls);
            var command = Assert.Single(runner.Commands);
            Assert.Equal("wsreset.exe", command.FileName);
            Assert.Empty(command.Arguments);
            Assert.Contains("Cache réinitialisé.", result.SummaryOutput);
            Assert.False(result.RebootRequired);
        }
    }

    private static ElevatedActionDispatcher CreateDispatcher(RecordingRunner runner, bool administrator)
        => new(new ElevatedRequestValidator(() => DateTimeOffset.UtcNow, Path.GetTempPath),
            new ElevatedActionAllowlist(), runner, new SecurityContext(administrator), () => @"C:\Windows\System32");

    private sealed class SecurityContext(bool administrator) : IElevatedSecurityContext
    {
        public bool IsAdministrator => administrator;
    }

    private sealed class RecordingRunner(Func<ElevatedCommandSpec, Task<ElevatedCommandResult>> run) : IElevatedCommandRunner
    {
        public List<ElevatedCommandSpec> Commands { get; } = [];

        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command)
        {
            Commands.Add(command);
            return run(command);
        }
    }

    private sealed class RecordingHelper(ElevatedActionDispatcher dispatcher, bool restoreVerified) : IInterventionElevatedHelperClient
    {
        public List<InterventionId> Calls { get; } = [];

        public Task<ElevatedInterventionResult> ExecuteAsync(InterventionDefinition definition, CancellationToken cancellationToken)
        {
            Calls.Add(definition.Id);
            return definition.Id == InterventionId.CreateRestorePoint
                ? Task.FromResult(new ElevatedInterventionResult
                {
                    Status = restoreVerified ? InterventionStatus.Completed : InterventionStatus.Failed
                })
                : dispatcher.ExecuteValidatedAsync(new() { ActionId = definition.Id }, null, cancellationToken);
        }
    }

    private sealed class UnexpectedExplorer : IExplorerRestarter
    {
        public Task<InterventionExecutionResult> RestartAsync(InterventionDiagnostic diagnostic, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Aucune relance de l’Explorateur attendue.");
    }

    private sealed class ManualClock : TimeProvider
    {
        private ManualTimer? _timer;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _timer = new ManualTimer(() => callback(state), dueTime);
            return _timer;
        }

        public void Advance(TimeSpan elapsed) => _timer!.Advance(elapsed);

        private sealed class ManualTimer(Action fire, TimeSpan remaining) : ITimer
        {
            private bool _disposed;

            public void Advance(TimeSpan elapsed)
            {
                remaining -= elapsed;
                if (!_disposed && remaining <= TimeSpan.Zero)
                    fire();
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
