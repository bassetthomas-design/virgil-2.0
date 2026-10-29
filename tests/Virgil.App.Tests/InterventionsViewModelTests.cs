using Virgil.Core.Actions;
using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Animation;
using Virgil.App.Hosting;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Interventions;
using Virgil.Core.Reports;
using Xunit;

namespace Virgil.Tests;

public sealed class InterventionsViewModelTests
{
    [Theory]
    [Trait("Category", "Safety")]
    [InlineData("Analyze")]
    [InlineData("VerifyHealth")]
    [InlineData("ScanRepair")]
    [InlineData("RunGuided")]
    public async Task CancelEachRepairOperationAcknowledgesWithoutStartingNext(string operation)
    {
        var f = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostics = new TaskCompletionSource<IReadOnlyList<InterventionDiagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<InterventionExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (operation == "Analyze")
            f.Diagnostics.Handler = async token => { started.SetResult(); return await diagnostics.Task.WaitAsync(token); };
        else
        {
            await f.Analyze();
            foreach (var item in f.Items) item.IsSelected = true;
            f.Actions.Handler = (diagnostic, token) => { started.SetResult(); return finish.Task; };
        }
        var task = operation switch
        {
            "Analyze" => f.Analyze(),
            "VerifyHealth" => f.Vm.VerifyHealthCommand.ExecuteAsync(null),
            "ScanRepair" => f.Vm.ScanRepairCommand.ExecuteAsync(null),
            _ => f.Run()
        };
        if (f.Vm.IsValidationOpen) f.Vm.ExecuteDecisionCommand.Execute(null);
        Assert.False(task.IsCompleted);
        await started.Task;
        f.Vm.Operation.CancelCommand.Execute(null);
        Assert.True(!f.Vm.IsBusy || f.Vm.IsCancelling);
        var count = f.Actions.Executed.Count;
        finish.TrySetResult(new InterventionExecutionResult { Action = Diagnostic().Definition, Status = InterventionStatus.Completed });
        await task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(count, f.Actions.Executed.Count);
        Assert.False(f.Vm.IsBusy);
    }

    [Theory]
    [InlineData(InterventionId.FlushDns, InterventionRiskLevel.Low, "EXÉCUTER")]
    [InlineData(InterventionId.ResetWinsock, InterventionRiskLevel.Sensitive, "JE CONFIRME")]
    [InlineData(InterventionId.RestartExplorer, InterventionRiskLevel.Low, "EXÉCUTER")]
    public async Task ExistingOverlayIssuesPlanThenRealServiceReachesExactlyOneAdapter(
        InterventionId id, InterventionRiskLevel risk, string label)
    {
        var confirmation = new RecordingConfirmation();
        var helper = new RecordingHelper { ThrowOnCall = false };
        var explorer = new RecordingExplorer { ThrowOnCall = false };
        var service = new InterventionExecutionService(helper, explorer, confirmation.Authority);
        var f = new Fixture(service, confirmation);
        var diagnostic = Diagnostic(id, risk: risk);
        f.Diagnostics.Items = new[] { diagnostic with { Definition = diagnostic.Definition with { RequiresAdministrator = id != InterventionId.RestartExplorer } } };
        await f.Analyze();
        Assert.Single(f.Items).IsSelected = true;
        var pending = f.Run();
        Assert.Equal(label, f.Vm.ExecuteActionLabel);
        Assert.Equal(0, confirmation.Calls); Assert.Equal(0, helper.Calls + explorer.Calls);
        f.Vm.ExecuteDecisionCommand.Execute(null); await pending;
        Assert.Equal(1, confirmation.Calls);
        Assert.Equal(id == InterventionId.RestartExplorer ? 0 : 2, helper.Calls);
        if (id != InterventionId.RestartExplorer) Assert.Equal(new[] { InterventionId.CreateRestorePoint, id }, helper.Actions);
        Assert.Equal(id == InterventionId.RestartExplorer ? 1 : 0, explorer.Calls);
        Assert.Single(f.History.Saved!.ExecutedActions);
    }

    [Fact]
    public async Task RefusedIssuanceUsesCancelAllPath()
    {
        var f = new Fixture(); f.Confirmation.Refuse = true;
        await f.Analyze(); var pending = f.Run();
        f.Vm.ExecuteDecisionCommand.Execute(null); await pending;
        Assert.Equal(1, f.Confirmation.Calls);
        Assert.Empty(f.Actions.Executed); Assert.Single(f.Actions.Cancelled);
        Assert.Equal("Parcours annulé par l'utilisateur.", Assert.Single(f.Actions.Report!.Errors));
    }

    [Fact]
    public void InitialStateMatchesView()
    {
        var vm = new Fixture().Vm;
        Assert.Equal("DIAGNOSTIC, VALIDATION ET EXÉCUTION", vm.StatusText);
        Assert.Equal("NON ANALYSÉ", vm.OverallText);
        Assert.Equal("0", vm.RecommendedCountText);
        Assert.Equal("Lancez une analyse pour afficher les actions système.", vm.SystemNotice);
        Assert.Equal("Lancez une analyse pour afficher les actions réseau.", vm.NetworkNotice);
        Assert.Equal("Lancez une analyse pour afficher les actions stockage.", vm.StorageNotice);
        Assert.Equal("Lancez une analyse pour afficher les actions interface.", vm.InterfaceNotice);
        Assert.Empty(vm.SystemItems.Concat(vm.NetworkItems).Concat(vm.StorageItems).Concat(vm.InterfaceItems));
        Assert.True(vm.IsIdle);
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsRunGuidedEnabled);
        Assert.False(vm.IsReportButtonEnabled);
        Assert.False(vm.IsValidationOpen);
        Assert.Equal("VALIDATION REQUISE", vm.ValidationTitle);
        Assert.Empty(vm.ValidationDetails);
        Assert.Empty(vm.ValidationCommand);
        Assert.Empty(vm.ValidationSafety);
        Assert.Equal("EXÉCUTER", vm.ExecuteActionLabel);
        Assert.False(vm.IsReportOpen);
        Assert.Empty(vm.ReportText);
        Assert.Equal(VirgilCoreState.Idle, vm.MiniCoreState);
        Assert.Equal(VirgilCoreState.Idle, vm.ValidationCoreState);
        Assert.False(vm.TryCloseOverlay());
    }

    [Fact]
    public async Task AnalyzeSortsFourCategoriesCountsAndResetsSelection()
    {
        var f = new Fixture();
        f.Diagnostics.Items = new[]
        {
            Diagnostic(InterventionId.SfcScan, InterventionCategory.System, 8),
            Diagnostic(InterventionId.RenewIp, InterventionCategory.Network, 7),
            Diagnostic(InterventionId.ChkdskOnlineScan, InterventionCategory.Storage, 6),
            Diagnostic(InterventionId.RestartExplorer, InterventionCategory.Interface, 5),
            Diagnostic(InterventionId.DismScanHealth, InterventionCategory.System, 4),
            Diagnostic(InterventionId.FlushDns, InterventionCategory.Network, 3),
            Diagnostic(InterventionId.DismRestoreHealth, InterventionCategory.Storage, 2, available: false),
            Diagnostic(InterventionId.ResetWinsock, InterventionCategory.Interface, 1),
            Diagnostic(InterventionId.ResetTcpIp, (InterventionCategory)999, 0)
        };
        await f.Analyze();
        Assert.Equal(new[] { 0, 4, 8 }, f.Vm.SystemItems.Select(i => i.Diagnostic.Definition.DisplayOrder));
        Assert.Equal(new[] { 3, 7 }, f.Vm.NetworkItems.Select(i => i.Diagnostic.Definition.DisplayOrder));
        Assert.Equal(new[] { 2, 6 }, f.Vm.StorageItems.Select(i => i.Diagnostic.Definition.DisplayOrder));
        Assert.Equal(new[] { 1, 5 }, f.Vm.InterfaceItems.Select(i => i.Diagnostic.Definition.DisplayOrder));
        Assert.All(f.Items, i => Assert.False(i.IsSelected));
        Assert.Equal("8 disponibles. Aucune exécution automatique.", f.Vm.OverallText);
        Assert.Equal("9", f.Vm.RecommendedCountText);
        Assert.Equal("Diagnostic terminé.\n8 actions disponibles.\n9 recommandées.", f.Message);
        Assert.Equal((VirgilCoreState.Success, "PRÊT"), f.State);
        Assert.Empty(f.Vm.SystemNotice + f.Vm.NetworkNotice + f.Vm.StorageNotice + f.Vm.InterfaceNotice);
        Assert.True(f.Vm.IsIdle);
        Assert.True(f.Vm.IsRunGuidedEnabled);
        Assert.False(f.Vm.IsReportButtonEnabled);
        Assert.Equal(0, f.History.Calls);
        f.Vm.SystemItems[0].IsSelected = true;
        await f.Analyze();
        Assert.All(f.Items, i => Assert.False(i.IsSelected));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnalyzeCancellationOrFailureEndsOperation(bool cancelled)
    {
        var f = new Fixture();
        f.Diagnostics.Handler = _ => Task.FromException<IReadOnlyList<InterventionDiagnostic>>(
            cancelled ? new OperationCanceledException() : new InvalidOperationException());
        await f.Analyze();
        Assert.True(f.Vm.IsIdle);
        Assert.Equal(cancelled ? "Diagnostic annulé." : "Diagnostic indisponible.", f.Vm.StatusText);
        Assert.Equal(cancelled ? VirgilCoreState.Idle : VirgilCoreState.Error, f.Vm.MiniCoreState);
        Assert.Equal(cancelled ? "Diagnostic annulé.\nAucune action exécutée." : "Diagnostic indisponible.\nAucune action exécutée.", f.Message);
        Assert.Equal(0, f.History.Calls);
    }

    [Fact]
    public async Task EmptyAnalysisKeepsNotAnalyzedNotices()
    {
        var f = new Fixture();
        f.Diagnostics.Items = Array.Empty<InterventionDiagnostic>();
        await f.Analyze();
        Assert.Equal("NON ANALYSÉ", f.Vm.OverallText);
        Assert.Equal("0", f.Vm.RecommendedCountText);
        Assert.StartsWith("Lancez une analyse", f.Vm.SystemNotice);
        Assert.False(f.Vm.IsRunGuidedEnabled);
    }

    [Theory]
    [InlineData("selected", 1)]
    [InlineData("fallback", 2)]
    [InlineData("unavailable-selected", 2)]
    [InlineData("sensitive-only", 0)]
    [InlineData("nothing", 0)]
    public async Task TargetingPreservesSelectionPriorityAndExactFallback(string scenario, int expected)
    {
        var f = new Fixture();
        var recommended = Diagnostic(InterventionId.FlushDns, order: 3);
        var unavailable = Diagnostic(InterventionId.SfcScan, order: 2, available: false);
        var sensitive = Diagnostic(InterventionId.ResetWinsock, order: 1, risk: InterventionRiskLevel.Sensitive);
        var optional = Diagnostic(InterventionId.RestartExplorer, order: 4, status: InterventionStatus.Available);
        f.Diagnostics.Items = scenario switch
        {
            "sensitive-only" => new[] { sensitive },
            "nothing" => new[] { optional },
            _ => new[] { recommended, unavailable, sensitive, optional }
        };
        await f.Analyze();
        if (scenario == "selected") f.Items.Single(i => i.Diagnostic == optional).IsSelected = true;
        if (scenario == "unavailable-selected") f.Items.Single(i => i.Diagnostic == unavailable).IsSelected = true;
        var pending = f.Run();
        if (expected == 0)
        {
            await pending;
            Assert.Equal("Aucune intervention sélectionnée.", f.Message);
            Assert.Equal("Diagnostic interventions en cours.", f.Vm.StatusText);
            Assert.True(f.Vm.IsIdle);
            Assert.False(f.Vm.IsValidationOpen);
            Assert.Equal(0, f.History.Calls);
            Assert.Null(f.Actions.Report);
        }
        else
        {
            var titles = new List<string>();
            for (var i = 0; i < expected; i++)
            {
                Assert.True(f.Vm.IsValidationOpen);
                titles.Add(f.Vm.ValidationDetails.Split('\n')[0]);
                f.Vm.SkipDecisionCommand.Execute(null);
            }
            await pending;
            Assert.Equal(scenario == "selected" ? new[] { optional.Definition.Title } : new[] { unavailable.Definition.Title, recommended.Definition.Title }, titles);
            Assert.Equal(scenario == "selected" ? new[] { optional } : new[] { recommended, unavailable }, f.Actions.Report!.ProposedActions);
            Assert.Equal(expected, f.Actions.Skipped.Count);
            Assert.DoesNotContain(sensitive, f.Actions.Report.ProposedActions);
        }
        Assert.Empty(f.Actions.Executed);
    }

    [Fact]
    public async Task RunEnabledFollowsCheckboxChangesAndBusyState()
    {
        var f = new Fixture();
        f.Diagnostics.Items = new[] { Diagnostic(status: InterventionStatus.Available), Diagnostic(InterventionId.SfcScan, available: false, status: InterventionStatus.Available) };
        await f.Analyze();
        Assert.False(f.Vm.IsRunGuidedEnabled);
        var available = f.Items.Single(i => i.IsAvailable);
        var unavailable = f.Items.Single(i => !i.IsAvailable);
        unavailable.IsSelected = true;
        Assert.False(f.Vm.IsRunGuidedEnabled);
        available.IsSelected = true;
        Assert.True(f.Vm.IsRunGuidedEnabled);
        available.IsSelected = false;
        Assert.False(f.Vm.IsRunGuidedEnabled);
        available.IsSelected = true;
        var pending = f.Run();
        Assert.False(f.Vm.IsRunGuidedEnabled);
        Assert.False(f.Vm.IsReportButtonEnabled);
        available.IsSelected = false;
        available.IsSelected = true;
        Assert.False(f.Vm.IsRunGuidedEnabled);
        f.Vm.SkipDecisionCommand.Execute(null);
        await pending;
        Assert.True(f.Vm.IsRunGuidedEnabled);
        Assert.True(f.Vm.IsReportButtonEnabled);
    }

    [Theory]
    [InlineData(InterventionRiskLevel.Low)]
    [InlineData(InterventionRiskLevel.Sensitive)]
    public async Task OverlayShowsExactPreviewAndExecutesOnlyAfterDecision(InterventionRiskLevel risk)
    {
        var f = new Fixture();
        var d = Diagnostic(risk: risk) with
        {
            Warnings = new[] { "Attention réseau" },
            TechnicalData = new Dictionary<string, string> { ["SystemDrive"] = "C:" },
            Definition = Diagnostic(risk: risk).Definition with
            {
                Description = "Description", ExpectedEffect = "Effet", NotTouched = "Fichiers", RequiresAdministrator = true,
                RebootPossible = true, CommandPreviews = new[] { new InterventionCommandPreview
                { Executable = "outil.exe", Arguments = new[] { "<system-drive>", "deux mots", "/scan" } } }
            }
        };
        f.Diagnostics.Items = new[] { d };
        await f.Analyze();
        Assert.Single(f.Items).IsSelected = true;
        var pending = f.Run();
        Assert.True(f.Vm.IsValidationOpen);
        Assert.False(pending.IsCompleted);
        Assert.Empty(f.Actions.Executed);
        var sensitive = risk == InterventionRiskLevel.Sensitive;
        Assert.Equal(sensitive ? "ACTION SENSIBLE" : "VALIDATION REQUISE", f.Vm.ValidationTitle);
        Assert.Equal(sensitive ? "JE CONFIRME" : "EXÉCUTER", f.Vm.ExecuteActionLabel);
        Assert.Equal(sensitive ? VirgilCoreState.SensitiveAction : VirgilCoreState.Warning, f.Vm.ValidationCoreState);
        Assert.Equal((f.Vm.ValidationCoreState, "VALIDATION"), f.State);
        Assert.Equal("outil.exe C: \"deux mots\" /scan", f.Vm.ValidationCommand);
        Assert.Equal(string.Join("\n", new[] { d.Definition.Title, "Description", "Effet attendu : Effet", "Non touché : Fichiers",
            sensitive ? "Risque : sensible" : "Risque : faible", "Admin : oui", "Redémarrage possible : oui", "Diagnostic : État avant", "Recommandation : Conseil" }), f.Vm.ValidationDetails);
        Assert.Equal("Certaines étapes ne peuvent pas être interrompues une fois lancées.\nAttention réseau\nValidation séparée obligatoire.\nAucune commande libre.\nAucun redémarrage automatique.\nAucun Take Ownership dans cette PR.", f.Vm.ValidationSafety);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        var call = Assert.Single(f.Actions.Executed);
        Assert.Same(d, call.Diagnostic);
        Assert.NotNull(call.Plan);
        Assert.False(f.Vm.IsValidationOpen);
        Assert.Equal(VirgilCoreState.Idle, f.Vm.ValidationCoreState);
        Assert.True(f.Vm.IsReportOpen);
        Assert.Equal(1, f.Persisted);
    }

    [Fact]
    public async Task ThreeInlineDecisionsPreserveNextDecisionAndReportOrder()
    {
        var f = new Fixture();
        f.Diagnostics.Items = new[] { Diagnostic(InterventionId.SfcScan, order: 3), Diagnostic(InterventionId.FlushDns, order: 1), Diagnostic(InterventionId.RestartExplorer, order: 2) };
        await f.Analyze();
        var pending = f.Run();
        Assert.StartsWith("FlushDns\n", f.Vm.ValidationDetails);
        f.Vm.SkipDecisionCommand.Execute(null);
        Assert.StartsWith("RestartExplorer\n", f.Vm.ValidationDetails);
        Assert.True(f.Vm.IsValidationOpen);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        Assert.StartsWith("SfcScan\n", f.Vm.ValidationDetails);
        Assert.True(f.Vm.IsValidationOpen);
        f.Vm.CancelDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(1, f.Confirmation.Calls);
        Assert.Equal(new[] { "Skip:FlushDns", "Execute:RestartExplorer", "Cancel:SfcScan" }, f.Actions.Order);
        Assert.Equal(f.Diagnostics.Items, f.Actions.Report!.ProposedActions);
        Assert.Equal(new[] { InterventionStatus.Skipped, InterventionStatus.Completed, InterventionStatus.Cancelled }, f.Actions.Report.Results.Select(r => r.Status));
        Assert.Equal(new[] { "Parcours annulé par l'utilisateur." }, f.Actions.Report.Errors);
        Assert.Equal("Parcours annulé.", f.Vm.StatusText);
        Assert.Equal(VirgilCoreState.Idle, f.Vm.MiniCoreState);
        Assert.True(f.Vm.IsReportOpen);
        Assert.Equal(new InterventionReportBuilder().Build(f.Actions.Report), f.Vm.ReportText);
        Assert.Equal(ReportKind.Interventions, f.History.Saved!.Kind);
    }

    [Theory]
    [InlineData("SkipDecision")]
    [InlineData("CancelDecision")]
    [InlineData("TryCloseOverlay")]
    [InlineData("CancelActiveOperation")]
    public async Task AbandoningConfirmationNeverExecutesAndPersists(string decision)
    {
        var f = new Fixture();
        await f.Analyze();
        var pending = f.Run();
        Assert.Empty(f.Actions.Executed);
        Decide(f.Vm, decision);
        await pending;
        Assert.Empty(f.Actions.Executed);
        var skip = decision is "SkipDecision" or "TryCloseOverlay";
        Assert.Equal(skip ? 1 : 0, f.Actions.Skipped.Count);
        Assert.Equal(skip ? 0 : 1, f.Actions.Cancelled.Count);
        Assert.Equal(skip ? InterventionStatus.Skipped : InterventionStatus.Cancelled, Assert.Single(f.Actions.Report!.Results).Status);
        Assert.False(f.Vm.IsValidationOpen);
        Assert.Equal(VirgilCoreState.Idle, f.Vm.ValidationCoreState);
        Assert.True(f.Vm.IsReportOpen);
        Assert.Equal(1, f.History.Calls);
        Assert.Equal(1, f.Persisted);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        Assert.Empty(f.Actions.Executed);
    }

    public static IEnumerable<object[]> RealAbandonedActions =>
        from action in new[] { InterventionId.FlushDns, InterventionId.RestartExplorer }
        from decision in new[] { "SkipDecision", "CancelDecision", "TryCloseOverlay", "CancelActiveOperation" }
        select new object[] { action, decision };

    [Theory]
    [MemberData(nameof(RealAbandonedActions))]
    public async Task RealExecutionServiceNeverReachesHelperOrExplorerWithoutExecute(InterventionId action, string decision)
    {
        var helper = new RecordingHelper();
        var explorer = new RecordingExplorer();
        var real = new InterventionExecutionService(helper, explorer, TestPlans.Authority);
        var f = new Fixture(real);
        f.Diagnostics.Items = new[] { Diagnostic(action) with { Definition = Diagnostic(action).Definition with { RequiresAdministrator = true } } };
        await f.Analyze();
        var pending = f.Run();
        Assert.Equal(0, helper.Calls + explorer.Calls);
        Decide(f.Vm, decision);
        await pending;
        Assert.Equal(0, helper.Calls);
        Assert.Equal(0, explorer.Calls);
        Assert.False(f.Vm.IsValidationOpen);
        Assert.True(f.Vm.IsReportOpen);
        Assert.Equal(1, f.Persisted);
        var saved = f.History.Saved!;
        Assert.Empty(saved.ExecutedActions);
        Assert.Single(saved.SkippedActions);
        Assert.Equal(decision is "SkipDecision" or "TryCloseOverlay" ? ReportActionStatus.Skipped : ReportActionStatus.Cancelled, saved.SkippedActions[0].Status);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        Assert.Equal(0, helper.Calls + explorer.Calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationAlwaysReachesHelperForSafeStop(bool interruptible, bool escape)
    {
        var f = new Fixture();
        f.Diagnostics.Items = new[] { Diagnostic() with { Definition = Diagnostic().Definition with { CanBeInterruptedAfterStart = interruptible } } };
        var gate = new TaskCompletionSource<InterventionExecutionResult>();
        f.Actions.Handler = (_, token) => gate.Task.WaitAsync(token);
        await f.Analyze();
        var pending = f.Run();
        f.Vm.ExecuteDecisionCommand.Execute(null);
        var token = f.Actions.Token;
        Assert.True(f.Vm.IsBusy);
        Assert.False(f.Vm.IsValidationOpen);
        Assert.False(f.Vm.IsReportButtonEnabled);
        if (escape) Assert.True(f.Vm.TryCloseOverlay());
        else f.Vm.CancelActiveOperation();
        Assert.True(token.IsCancellationRequested);
        await pending;
        Assert.True(f.Vm.IsIdle);
        Assert.True(f.Vm.IsReportOpen);
        Assert.Equal(1, f.Persisted);
        Assert.Contains("Parcours annulé.", f.Actions.Report!.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BusyOperationRejectsAnalyzeAndRunGuided(bool duringAnalysis)
    {
        var f = new Fixture();
        Task pending;
        if (duringAnalysis)
        {
            var gate = new TaskCompletionSource<IReadOnlyList<InterventionDiagnostic>>();
            f.Diagnostics.Handler = token => gate.Task.WaitAsync(token);
            pending = f.Analyze();
        }
        else
        {
            await f.Analyze();
            pending = f.Run();
        }
        await f.Analyze();
        Assert.Equal("Opération déjà en cours.", f.Message);
        await f.Run();
        Assert.Equal("Opération déjà en cours.", f.Message);
        Assert.Equal(1, f.Diagnostics.Calls);
        Assert.Empty(f.Actions.Executed);
        f.Vm.CancelActiveOperation();
        await pending;
        Assert.True(f.Vm.IsIdle);
    }

    [Fact]
    public async Task ReportAndReturnHomeCommandsAndOverlayPriority()
    {
        var f = new Fixture();
        f.Vm.ViewReportCommand.Execute(null);
        Assert.Equal("Aucun rapport interventions disponible.", f.Message);
        Assert.False(f.Vm.IsReportOpen);
        var home = 0;
        f.Vm.ReturnHomeRequested += (_, _) => home++;
        f.Vm.ReturnHomeCommand.Execute(null);
        Assert.Equal(1, home);
        await f.Analyze();
        var pending = f.Run();
        Assert.Equal("Aucune commande externe.", f.Vm.ValidationCommand);
        f.Vm.SkipDecisionCommand.Execute(null);
        await pending;
        Assert.True(f.Vm.TryCloseOverlay());
        Assert.False(f.Vm.IsReportOpen);
        Assert.False(f.Vm.TryCloseOverlay());
        pending = f.Run();
        f.Vm.ViewReportCommand.Execute(null);
        Assert.True(f.Vm.TryCloseOverlay());
        Assert.True(f.Vm.IsValidationOpen);
        Assert.False(pending.IsCompleted);
        Assert.True(f.Vm.TryCloseOverlay());
        await pending;
        f.Vm.CloseReportCommand.Execute(null);
        Assert.False(f.Vm.IsReportOpen);
        Assert.Empty(f.Actions.Executed);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "Indisponible")]
    [InlineData(true, "Avertissement")]
    public async Task PersistenceResultControlsMessageAndEvent(bool success, string? error)
    {
        var f = new Fixture();
        f.History.Result = new() { Success = success, ReadableError = error };
        await f.Analyze();
        var pending = f.Run();
        f.Vm.SkipDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(success ? 1 : 0, f.Persisted);
        Assert.Equal(error ?? "Historique local indisponible. Rapport conservé en mémoire.", f.Message);
        Assert.False(f.History.Token.CanBeCanceled);
        Assert.True(f.Vm.IsReportOpen);
    }

    [Theory]
    [InlineData(InterventionStatus.Completed, "Intervention terminée.\nRapport mis à jour.", "Parcours terminé.", VirgilCoreState.Success)]
    [InlineData(InterventionStatus.RebootRequired, "Intervention terminée.\nRedémarrage manuel probablement requis.", "Parcours terminé.", VirgilCoreState.Success)]
    [InlineData(InterventionStatus.PartialFailure, "Intervention partielle.\nRapport disponible.", "Parcours terminé avec erreurs.", VirgilCoreState.Warning)]
    [InlineData(InterventionStatus.Failed, "Intervention en échec.\nRapport disponible.", "Parcours terminé avec erreurs.", VirgilCoreState.Warning)]
    [InlineData(InterventionStatus.Unavailable, "Intervention traitee.\nRapport disponible.", "Parcours terminé.", VirgilCoreState.Success)]
    public async Task ExecutionResultKeepsMessagesAndSessionState(InterventionStatus status, string message, string session, VirgilCoreState state)
    {
        var f = new Fixture();
        f.Actions.ResultStatus = status;
        await f.Analyze();
        var pending = f.Run();
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(message, f.Message);
        Assert.Equal(session, f.Vm.StatusText);
        Assert.Equal(state, f.Vm.MiniCoreState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ItemRetainsSevenLinesAndFirstTwoErrors(bool errors)
    {
        var d = Diagnostic(risk: InterventionRiskLevel.Moderate) with
        {
            Errors = errors ? new[] { "un", "deux", "trois" } : Array.Empty<string>(),
            Definition = Diagnostic(risk: InterventionRiskLevel.Moderate).Definition with
            { Description = "Description", EstimatedDuration = "2 min", RequiresAdministrator = true, RebootPossible = true }
        };
        var changes = 0;
        var item = new InterventionItemViewModel(d, () => changes++);
        Assert.Same(d, item.Diagnostic);
        Assert.Equal(d.Definition.Title, item.Title);
        Assert.True(item.IsAvailable);
        Assert.False(item.IsSelected);
        Assert.Equal(new[] { "Description", "Risque : modéré", "Admin : oui", "Redémarrage possible : oui", "Durée : 2 min", "Statut : recommandé", "Diagnostic : État avant" }, item.Lines.Take(7));
        Assert.Equal(errors ? 8 : 7, item.Lines.Count);
        if (errors) Assert.Equal("Erreur : un | deux", item.Lines[7]);
        item.IsSelected = true;
        item.IsSelected = true;
        item.IsSelected = false;
        Assert.Equal(2, changes);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void CompositionRegistersExplicitSingletonFactoriesAndSharesViewModel()
    {
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var registration = Assert.Single(services, d => d.ServiceType == typeof(InterventionsViewModel));
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
        Assert.NotNull(registration.ImplementationFactory);
        var vm = provider.GetRequiredService<InterventionsViewModel>();
        Assert.Same(vm, provider.GetRequiredService<InterventionsViewModel>());
        var viewRegistration = Assert.Single(services, d => d.ServiceType == typeof(Virgil.App.Views.InterventionsView));
        Assert.Equal(ServiceLifetime.Singleton, viewRegistration.Lifetime);
        Assert.NotNull(viewRegistration.ImplementationFactory);

    }

    private static void Decide(InterventionsViewModel vm, string decision)
    {
        switch (decision)
        {
            case "SkipDecision": vm.SkipDecisionCommand.Execute(null); break;
            case "CancelDecision": vm.CancelDecisionCommand.Execute(null); break;
            case "TryCloseOverlay": Assert.True(vm.TryCloseOverlay()); break;
            case "CancelActiveOperation": vm.CancelActiveOperation(); break;
            default: throw new ArgumentOutOfRangeException(nameof(decision));
        }
    }

    private static InterventionDiagnostic Diagnostic(InterventionId id = InterventionId.FlushDns,
        InterventionCategory category = InterventionCategory.System, int order = 0,
        bool available = true, InterventionStatus status = InterventionStatus.Recommended,
        InterventionRiskLevel risk = InterventionRiskLevel.Low) => new()
    {
        Definition = new() { Id = id, Title = id.ToString(), Category = category, DisplayOrder = order, RiskLevel = risk, CanBeInterruptedAfterStart = true },
        IsAvailable = available, Status = status, StateBefore = "État avant", Recommendation = "Conseil"
    };

    private sealed class Fixture
    {
        public FakeDiagnostics Diagnostics { get; } = new();
        public RecordingConfirmation Confirmation { get; } = new();
        public FakeActions Actions { get; } = new();
        public FakeHistory History { get; } = new();
        public InterventionsViewModel Vm { get; }
        public string? Message { get; private set; }
        public (VirgilCoreState State, string Label)? State { get; private set; }
        public int Persisted { get; private set; }
        public IEnumerable<InterventionItemViewModel> Items => Vm.SystemItems.Concat(Vm.NetworkItems).Concat(Vm.StorageItems).Concat(Vm.InterfaceItems);
        public Fixture(IInterventionExecutionService? execution = null, RecordingConfirmation? confirmation = null)
        {
            Confirmation = confirmation ?? new RecordingConfirmation();
            Actions.Authority = Confirmation.Authority;
            Vm = new(Diagnostics, execution ?? Actions, new InterventionReportBuilder(), History, Confirmation);
            Vm.VirgilMessageRequested += message => Message = message;
            Vm.VirgilStateRequested += (state, label) => State = (state, label);
            Vm.ReportPersisted += (_, _) => Persisted++;
        }
        public Task Analyze() => Vm.AnalyzeCommand.ExecuteAsync(null);
        public Task Run() => Vm.RunGuidedCommand.ExecuteAsync(null);
    }

    private sealed class FakeDiagnostics : IInterventionDiagnosticService
    {
        public IReadOnlyList<InterventionDiagnostic> Items { get; set; } = new[] { Diagnostic() };
        public Func<CancellationToken, Task<IReadOnlyList<InterventionDiagnostic>>>? Handler { get; set; }
        public int Calls { get; private set; }
        public Task<InterventionDiagnostic> DiagnoseAsync(InterventionId id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<InterventionDiagnostic>> DiagnoseAllAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Handler?.Invoke(cancellationToken) ?? Task.FromResult(Items);
        }
    }

    private sealed class FakeActions : IInterventionExecutionService
    {
        public ConfirmationService Authority { get; set; } = null!;
        public List<(InterventionDiagnostic Diagnostic, ConfirmedPlan? Plan)> Executed { get; } = new();
        public List<InterventionDiagnostic> Skipped { get; } = new();
        public List<InterventionDiagnostic> Cancelled { get; } = new();
        public List<string> Order { get; } = new();
        public InterventionSessionReport? Report { get; private set; }
        public InterventionStatus ResultStatus { get; set; } = InterventionStatus.Completed;
        public Func<InterventionDiagnostic, CancellationToken, Task<InterventionExecutionResult>>? Handler { get; set; }
        public CancellationToken Token { get; private set; }
        public Task<InterventionExecutionResult> ExecuteAsync(InterventionDiagnostic diagnostic, ConfirmedPlan? plan, CancellationToken cancellationToken)
        {
            Assert.NotNull(plan);
            Assert.True(Authority.TryConsume(plan, InterventionActionPreviewFactory.Create(diagnostic.Definition)).IsAccepted);
            Executed.Add((diagnostic, plan));
            Order.Add("Execute:" + diagnostic.Definition.Id);
            Token = cancellationToken;
            return Handler?.Invoke(diagnostic, cancellationToken) ?? Task.FromResult(new InterventionExecutionResult
            { Action = diagnostic.Definition, Status = ResultStatus, WasConfirmed = plan is not null });
        }
        public InterventionExecutionResult Skip(InterventionDiagnostic diagnostic)
        {
            Skipped.Add(diagnostic);
            Order.Add("Skip:" + diagnostic.Definition.Id);
            return new() { Action = diagnostic.Definition, Status = InterventionStatus.Skipped };
        }
        public InterventionExecutionResult Cancel(InterventionDiagnostic diagnostic)
        {
            Cancelled.Add(diagnostic);
            Order.Add("Cancel:" + diagnostic.Definition.Id);
            return new() { Action = diagnostic.Definition, Status = InterventionStatus.Cancelled };
        }
        public InterventionSessionReport CreateReport(DateTimeOffset startedAt, IReadOnlyList<InterventionDiagnostic> proposedActions,
            IReadOnlyList<InterventionExecutionResult> results, IReadOnlyList<string> errors)
        {
            Report = new() { StartedAt = startedAt, ProposedActions = proposedActions, Results = results, Errors = errors };
            return Report;
        }
    }

    private sealed class RecordingHelper : IInterventionElevatedHelperClient
    {
        public bool ThrowOnCall { get; init; } = true;
        public int Calls { get; private set; }
        public List<InterventionId> Actions { get; } = new();
        public Task<ElevatedInterventionResult> ExecuteAsync(InterventionDefinition definition, CancellationToken cancellationToken)
        {
            Calls++;
            Actions.Add(definition.Id);
            if (ThrowOnCall) throw new InvalidOperationException("Le helper ne doit jamais être appele sans EXECUTER.");
            return Task.FromResult(new ElevatedInterventionResult { Status = InterventionStatus.Completed });
        }
    }

    private sealed class RecordingExplorer : IExplorerRestarter
    {
        public bool ThrowOnCall { get; init; } = true;
        public int Calls { get; private set; }
        public Task<InterventionExecutionResult> RestartAsync(InterventionDiagnostic diagnostic, CancellationToken cancellationToken)
        {
            Calls++;
            if (ThrowOnCall) throw new InvalidOperationException("Explorer ne doit jamais être relance sans EXECUTER.");
            return Task.FromResult(new InterventionExecutionResult { Action = diagnostic.Definition, Status = InterventionStatus.Completed, WasConfirmed = true });
        }
    }

    private sealed class FakeHistory : IReportHistoryService
    {
        public ReportSaveResult Result { get; set; } = new() { Success = true };
        public ReportEntry? Saved { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken cancellationToken)
        {
            Saved = report;
            Token = cancellationToken;
            Calls++;
            return Task.FromResult(Result);
        }
        public Task<ReportHistoryLoadResult> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetLatestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

