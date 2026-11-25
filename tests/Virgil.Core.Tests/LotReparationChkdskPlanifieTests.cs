using Virgil.Contracts;
using Virgil.Core.Actions;
using Virgil.Core.Interventions;
using Virgil.ElevatedHelper;
using Xunit;

namespace Virgil.Tests;

public sealed class LotReparationChkdskPlanifieTests
{
    private const InterventionId Schedule = InterventionId.ScheduleSystemDiskCheck;
    private const string RebootWarning = "Redémarrage nécessaire pour lancer la vérification. Virgil ne redémarre pas le PC.";
    private const string Planned = "Vérification du disque système planifiée au prochain redémarrage. Virgil ne redémarre pas le PC.";

    [Fact]
    public void Catalog_and_allowlist_define_one_protected_command()
    {
        var definition = new InterventionCatalog().Get(Schedule);
        Assert.Equal(25, (int)InterventionId.ResetStoreCache);
        Assert.Equal(26, (int)definition.Id);
        Assert.Equal("Planifier la vérification du disque système au prochain redémarrage", definition.Title);
        Assert.Equal(InterventionCategory.Storage, definition.Category);
        Assert.Equal(InterventionRiskLevel.Sensitive, definition.RiskLevel);
        Assert.Equal(19, definition.DisplayOrder);
        Assert.True(definition.RequiresAdministrator);
        Assert.True(definition.RebootPossible);
        Assert.False(definition.CanBeInterruptedAfterStart);
        Assert.True(InterventionSafety.RequiresRestorePoint(definition.Id));
        Assert.Contains("Un redémarrage sera nécessaire", definition.Description);
        Assert.Contains("Virgil ne redémarre pas le PC", definition.Description);
        Assert.Contains("Virgil ne peut pas l’annuler", definition.ExpectedEffect);
        var preview = Assert.Single(definition.CommandPreviews);
        Assert.Equal("fsutil.exe", preview.Executable);
        Assert.Equal(new[] { "dirty", "set", "<system-drive>" }, preview.Arguments);
        Assert.True(preview.RunsElevated);

        var allowlist = new ElevatedActionAllowlist();
        Assert.True(allowlist.TryGet(Schedule, "C:", out var spec));
        Assert.True(spec.RequiresAdministrator);
        Assert.True(spec.RebootRequiredOnSuccess);
        var command = Assert.Single(spec.Commands);
        Assert.Equal("fsutil.exe", command.FileName);
        Assert.Equal(new[] { "dirty", "set", "C:" }, command.Arguments);
        Assert.True(command.NonInterruptible);

        Assert.True(allowlist.TryGet(InterventionId.ChkdskOnlineScan, "C:", out var chkdsk));
        Assert.Equal(new[] { "C:", "/scan" }, Assert.Single(chkdsk.Commands).Arguments);
        Assert.DoesNotContain(Schedule, InterventionSafety.RepairsFor("VIRGIL|CHKDSK|FINDING|Corruption détectée"));
        Assert.Equal(TimeSpan.FromMinutes(5), ElevatedCommandRunner.TimeoutFor("fsutil.exe"));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData("c:")]
    [InlineData("")]
    [InlineData("C:;x")]
    public void Allowlist_rejects_invalid_drives(string drive)
    {
        Assert.False(new ElevatedActionAllowlist().TryGet(Schedule, drive, out var spec));
        Assert.Empty(spec.Commands);
    }

    [Theory]
    [Trait("Category", "Windows")]
    [InlineData("système", true)]
    [InlineData("parent", false)]
    [InlineData("courant", false)]
    [InlineData("absent", false)]
    public async Task Diagnostic_searches_for_fsutil_only_in_system_directory(string location, bool available)
    {
        const string windows = @"C:\Windows";
        var system = Path.Combine(windows, "System32");
        var existingFile = location switch
        {
            "système" => Path.Combine(system, "fsutil.exe"),
            "parent" => Path.Combine(windows, "fsutil.exe"),
            "courant" => "fsutil.exe",
            _ => null
        };
        var service = new InterventionDiagnosticService(new InterventionCatalog(),
            path => path == existingFile || path == Path.Combine(windows, "sfc.exe"), () => system, _ => [], () => []);

        var result = Assert.Single(await service.DiagnoseAllAsync(CancellationToken.None),
            action => action.Definition.Id == Schedule);
        Assert.Equal(available, result.IsAvailable);
        Assert.Equal(available ? InterventionStatus.Available : InterventionStatus.Unavailable, result.Status);
        Assert.Equal("C:", result.TechnicalData["SystemDrive"]);
        Assert.Contains(RebootWarning, result.Warnings);
        if (available)
            Assert.Empty(result.Errors);
        else
            Assert.Equal("fsutil.exe indisponible.", Assert.Single(result.Errors));

        Assert.True((await service.DiagnoseAsync(InterventionId.SfcVerifyOnly, CancellationToken.None)).IsAvailable);
        Assert.DoesNotContain(RebootWarning,
            (await service.DiagnoseAsync(InterventionId.ChkdskOnlineScan, CancellationToken.None)).Warnings);
    }

    [Fact]
    public async Task Diagnostic_rejects_invalid_system_drive()
    {
        var service = new InterventionDiagnosticService(new InterventionCatalog(), _ => true,
            () => "System32", _ => [], () => []);
        var result = await service.DiagnoseAsync(Schedule, CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal(InterventionStatus.Unavailable, result.Status);
        Assert.Contains("Lecteur système invalide pour la vérification planifiée.", result.Errors);
    }

    [Theory]
    [Trait("Category", "Windows")]
    [InlineData(0, "", true, InterventionStatus.RebootRequired)]
    [InlineData(7, "Accès refusé.", false, InterventionStatus.PartialFailure)]
    [InlineData(-1, "fsutil.exe indisponible.", false, InterventionStatus.PartialFailure)]
    public async Task Dispatcher_reports_actual_scheduling_outcome(int exitCode, string error, bool reboot, InterventionStatus status)
    {
        var runner = new RecordingRunner(new(exitCode, "Journal brut.", error));
        var progress = new RecordingProgress();
        var result = await Dispatcher(runner).ExecuteValidatedAsync(new() { ActionId = Schedule }, progress);

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(status, result.Status);
        Assert.Equal(reboot, result.RebootRequired);
        Assert.Contains(reboot ? Planned : "Vérification du disque système non planifiée.", result.SummaryOutput);
        Assert.Contains("Journal brut.", result.SummaryOutput);
        if (!reboot)
            Assert.Contains(error, result.ReadableError);
        Assert.Equal("Étape 1 sur 1 : Disque système (vérification planifiée au redémarrage)", Assert.Single(progress.Steps));
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task Dispatcher_refuses_scheduling_without_administrator()
    {
        var runner = new RecordingRunner(new(0, "", ""));
        var result = await Dispatcher(runner, administrator: false).ExecuteValidatedAsync(new() { ActionId = Schedule });

        Assert.Equal(InterventionStatus.Failed, result.Status);
        Assert.Contains("administrateur", result.ReadableError);
        Assert.False(result.RebootRequired);
        Assert.Empty(runner.Commands);
    }

    [Theory]
    [InlineData(InterventionId.SfcScan)]
    [InlineData(InterventionId.DismRestoreHealth)]
    [InlineData(InterventionId.ResetWinsock)]
    [InlineData(InterventionId.ResetTcpIp)]
    public async Task Existing_reboot_actions_keep_status_indicator_and_log(InterventionId action)
    {
        foreach (var exitCode in new[] { 0, 7 })
        {
            var runner = new RecordingRunner(new(exitCode, "Journal brut.", ""));
            var result = await Dispatcher(runner).ExecuteValidatedAsync(new() { ActionId = action });
            var command = Assert.Single(runner.Commands);

            Assert.Equal(exitCode == 0 ? InterventionStatus.RebootRequired : InterventionStatus.PartialFailure, result.Status);
            Assert.True(result.RebootRequired);
            Assert.Equal($"Étape : {command.FileName} {string.Join(" ", command.Arguments)}\nCode sortie : {exitCode}.\nJournal brut.", result.SummaryOutput);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Execution_requires_confirmation_and_refuses_unverified_restore_even_with_override(bool confirmed, bool overrideRestore)
    {
        var helper = new RecordingHelper(_ => Task.FromResult(new ElevatedInterventionResult { Status = InterventionStatus.Failed }));
        var diagnostic = Diagnostic(overrideRestore);
        var service = new InterventionExecutionService(helper, new UnexpectedExplorer(), TestPlans.Authority);

        var preview = InterventionActionPreviewFactory.Create(diagnostic.Definition);
        var plan = overrideRestore
            ? TestPlans.Authority.Issue(preview, new(Virgil.Contracts.Actions.ConfirmationLevel.Critical,
                ConfirmationService.ExpectedCriticalText(preview)))
            : confirmed ? TestPlans.Intervention(diagnostic) : null;
        var result = await service.ExecuteAsync(diagnostic, plan, CancellationToken.None);

        Assert.Equal(InterventionStatus.Failed, result.Status);
        if (confirmed)
        {
            Assert.Contains("point de restauration non créé ou non vérifié", result.ReadableError);
            Assert.Equal(InterventionId.CreateRestorePoint, Assert.Single(helper.Calls));
        }
        else
        {
            Assert.Contains("Intervention annulée.", result.ReadableError);
            Assert.Empty(helper.Calls);
        }
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task Execution_runs_fixed_command_after_verified_restore()
    {
        var runner = new RecordingRunner(new(0, "Journal brut.", ""));
        var dispatcher = Dispatcher(runner);
        var helper = new RecordingHelper(_ =>
        {
            Assert.Empty(runner.Commands);
            return Task.FromResult(new ElevatedInterventionResult { Status = InterventionStatus.Completed });
        }, dispatcher);
        var diagnostic = Diagnostic();
        var service = new InterventionExecutionService(helper, new UnexpectedExplorer(), TestPlans.Authority);

        var result = await service.ExecuteAsync(diagnostic, TestPlans.Intervention(diagnostic), CancellationToken.None);

        Assert.Equal(new[] { InterventionId.CreateRestorePoint, Schedule }, helper.Calls);
        var command = Assert.Single(runner.Commands);
        Assert.Equal("fsutil.exe", command.FileName);
        Assert.Equal(new[] { "dirty", "set", "C:" }, command.Arguments);
        Assert.True(result.WasConfirmed);
        Assert.True(result.WasElevated);
        Assert.True(result.RebootRequired);
        Assert.Contains(Planned, result.SummaryOutput);
    }

    [Theory]
    [InlineData(InterventionStatus.Completed)]
    [InlineData(InterventionStatus.Cancelled)]
    public async Task Cancellation_during_or_after_restore_prevents_scheduling(InterventionStatus restoreStatus)
    {
        using var cancellation = new CancellationTokenSource();
        var helper = new RecordingHelper(_ =>
        {
            cancellation.Cancel();
            return Task.FromResult(new ElevatedInterventionResult { Status = restoreStatus });
        });
        var diagnostic = Diagnostic();
        var service = new InterventionExecutionService(helper, new UnexpectedExplorer(), TestPlans.Authority);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteAsync(diagnostic, TestPlans.Intervention(diagnostic), cancellation.Token));

        Assert.Equal(InterventionId.CreateRestorePoint, Assert.Single(helper.Calls));
        Assert.Contains("annulé", service.Cancel(diagnostic).SummaryOutput);
    }

    private static InterventionDiagnostic Diagnostic(bool overrideRestore = false)
        => new()
        {
            Definition = new InterventionCatalog().Get(Schedule) with { AllowWithoutRestorePoint = overrideRestore },
            IsAvailable = true
        };

    private static ElevatedActionDispatcher Dispatcher(RecordingRunner runner, bool administrator = true)
        => new(new ElevatedRequestValidator(() => DateTimeOffset.UtcNow, Path.GetTempPath),
            new ElevatedActionAllowlist(), runner, new SecurityContext(administrator), () => @"C:\Windows\System32");

    private sealed class SecurityContext(bool administrator) : IElevatedSecurityContext
    {
        public bool IsAdministrator => administrator;
    }

    private sealed class RecordingRunner(ElevatedCommandResult result) : IElevatedCommandRunner
    {
        public List<ElevatedCommandSpec> Commands { get; } = [];

        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command)
        {
            Commands.Add(command);
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingHelper(Func<CancellationToken, Task<ElevatedInterventionResult>> restore,
        ElevatedActionDispatcher? dispatcher = null) : IInterventionElevatedHelperClient
    {
        public List<InterventionId> Calls { get; } = [];

        public Task<ElevatedInterventionResult> ExecuteAsync(InterventionDefinition definition, CancellationToken cancellationToken)
        {
            Calls.Add(definition.Id);
            return definition.Id == InterventionId.CreateRestorePoint
                ? restore(cancellationToken)
                : (dispatcher ?? throw new InvalidOperationException("Aucune commande système attendue."))
                    .ExecuteValidatedAsync(new() { ActionId = definition.Id }, null, cancellationToken);
        }
    }

    private sealed class RecordingProgress : IProgress<InterventionProgress>
    {
        public List<string> Steps { get; } = [];
        public void Report(InterventionProgress value) => Steps.Add(value.Step);
    }

    private sealed class UnexpectedExplorer : IExplorerRestarter
    {
        public Task<InterventionExecutionResult> RestartAsync(InterventionDiagnostic diagnostic, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Aucune relance de l’Explorateur attendue.");
    }
}
