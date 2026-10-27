using Virgil.Core.Actions;
using Virgil.Contracts.Actions;
using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Animation;
using Virgil.App.Hosting;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Reports;
using Virgil.Core.Resources;
using Virgil.Core.Scanning;
using Xunit;

namespace Virgil.Tests;

public sealed class ResourcesViewModelTests
{
    [Fact]
    public void InitialStateMatchesView()
    {
        var vm = new Fixture().ViewModel;
        Assert.Equal("CPU, RAM ET PROCESSUS EN LECTURE SEULE", vm.StatusText);
        Assert.Equal("NON ANALYSÉ", vm.CpuSummaryText);
        Assert.Equal("NON ANALYSÉ", vm.MemorySummaryText);
        Assert.Equal("NON ANALYSÉ", vm.SessionSummaryText);
        Assert.Equal("Lancez une analyse. Aucune action ne sera executee.", vm.RecommendationsText);
        Assert.Equal("Lancez une analyse pour identifier les principaux consommateurs CPU et RAM.", vm.ProcessSummaryText);
        Assert.Equal("Analyse requise. Les processus systeme critiques, securite, VPN et materiel resteront proteges.", vm.ProcessesNotice);
        Assert.Empty(vm.Items);
        Assert.True(vm.IsIdle);
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsReportButtonEnabled);
        Assert.False(vm.IsValidationOpen);
        Assert.False(vm.IsReportOpen);
        Assert.Equal("VALIDATION REQUISE", vm.ValidationTitle);
        Assert.Empty(vm.ValidationDetails);
        Assert.Empty(vm.ValidationSafety);
        Assert.Equal("EXECUTER", vm.ExecuteActionLabel);
        Assert.Empty(vm.ReportText);
        Assert.Equal(VirgilCoreState.Idle, vm.MiniCoreState);
        Assert.Equal(VirgilCoreState.Idle, vm.ValidationCoreState);
        Assert.False(vm.TryCloseOverlay());
    }

    [Theory]
    [InlineData(false, "MÉMOIRE INACTIVE - INFO")]
    [InlineData(true, "LIBÉRER MÉMOIRE INACTIVE")]
    public void MemoryLabelReadsCapabilityAtConstruction(bool capability, string expected)
    {
        var f = new Fixture(capability);
        Assert.Equal(expected, f.ViewModel.ReleaseInactiveMemoryLabel);
        f.Actions.CanReleaseInactiveMemory = !capability;
        Assert.Equal(expected, f.ViewModel.ReleaseInactiveMemoryLabel);
    }

    [Theory]
    [InlineData(ResourceHealthLevel.Stable, VirgilCoreState.Success)]
    [InlineData(ResourceHealthLevel.Watch, VirgilCoreState.Success)]
    [InlineData(ResourceHealthLevel.InterventionRecommended, VirgilCoreState.Warning)]
    [InlineData(ResourceHealthLevel.Critical, VirgilCoreState.Warning)]
    public async Task AnalyzeUsesInteractiveRequestRendersAndPersists(ResourceHealthLevel health, VirgilCoreState expected)
    {
        var f = new Fixture();
        f.Monitor.Report = f.Monitor.Report with { OverallHealth = health };
        await f.Analyze();
        Assert.Same(ResourceAnalysisRequest.Interactive, f.Monitor.Request);
        Assert.Equal(1, f.Monitor.Calls);
        Assert.Equal($"Analyse terminee en {2.5:0.0} s.", f.ViewModel.StatusText);
        Assert.Equal($"Moyenne {12.5:0.0} % - maximum {25.0:0.0} % - STABLE", f.ViewModel.CpuSummaryText);
        Assert.Equal($"{50.0:0.0} % utilisee - A SURVEILLER - 2 Go / 4 Go", f.ViewModel.MemorySummaryText);
        Assert.Equal("Session 8 j 3 h - 42 processus - redemarrage manuel conseille", f.ViewModel.SessionSummaryText);
        Assert.Equal("- Examiner la RAM", f.ViewModel.RecommendationsText);
        Assert.Equal("1 processus lourds detectes. Aucune fermeture automatique.", f.ViewModel.ProcessSummaryText);
        Assert.Equal($"Analyse ressources terminee.\nRAM : {50.0:0.0} %\nCPU moyen : {12.5:0.0} %\n1 processus lourds detectes.", f.Message);
        Assert.Equal((expected, "ANALYSE TERMINEE"), f.State);
        Assert.Equal(expected, f.ViewModel.MiniCoreState);
        Assert.True(f.ViewModel.IsIdle);
        Assert.True(f.ViewModel.IsReportButtonEnabled);
        Assert.Empty(f.ViewModel.ProcessesNotice);
        Assert.Equal(1, f.Persisted);
        Assert.Equal(1, f.History.Calls);
        Assert.Equal(ReportKind.Resources, f.History.Saved!.Kind);
        Assert.True(f.History.Saved.RestartRequired);
        Assert.Contains(f.History.Saved.ProposedActions, a => a.Name == "Examiner Bloc-notes (PID 7)");
        Assert.Equal(0, f.Confirmation.Calls);
        Assert.Empty(f.Actions.Calls);
    }

    [Fact]
    public async Task ProcessesAreDeduplicatedHeavyFirstDescendingMemoryAndLimitedToTwelve()
    {
        var f = new Fixture();
        var processes = Enumerable.Range(1, 16).Select(i => f.Process with
        {
            ProcessId = i, WorkingSetBytes = i * 1024L,
            Status = i % 3 == 0 ? ProcessResourceStatus.Heavy : ProcessResourceStatus.Normal
        }).ToArray();
        f.Monitor.Report = f.Monitor.Report with
        {
            TopMemoryProcesses = processes,
            TopCpuProcesses = new[] { processes[0] with { WorkingSetBytes = long.MaxValue }, processes[2] }
        };
        await f.Analyze();
        Assert.Equal(new[] { 15, 12, 9, 6, 3, 16, 14, 13, 11, 10, 8, 7 }, f.ViewModel.Items.Select(i => i.Process.ProcessId));
        Assert.Equal("5 processus lourds detectes. Aucune fermeture automatique.", f.ViewModel.ProcessSummaryText);
        await f.ViewModel.Items[0].IgnoreCommand.ExecuteAsync(null);
        Assert.DoesNotContain(f.ViewModel.Items, i => i.Process.ProcessId == 15);
        Assert.Equal(12, f.ViewModel.Items.Count);
    }

    [Fact]
    public async Task EmptyAnalysisShowsNoticeAndStableRecommendations()
    {
        var f = new Fixture();
        f.Monitor.Report = new();
        await f.Analyze();
        Assert.Empty(f.ViewModel.Items);
        Assert.Equal("Aucun processus accessible a afficher.", f.ViewModel.ProcessesNotice);
        Assert.Equal("Aucun processus lourd selon les seuils V1. Principaux consommateurs affiches pour information.", f.ViewModel.ProcessSummaryText);
        Assert.Equal("Ressources stables. Aucune action automatique.", f.ViewModel.RecommendationsText);
        Assert.Equal("Lecture RAM indisponible.", f.ViewModel.MemorySummaryText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnalyzeCancellationAndFailureRestoreIdle(bool cancel)
    {
        var f = new Fixture();
        f.Monitor.Handler = _ => Task.FromException<ResourceAnalysisReport>(cancel ? new OperationCanceledException() : new InvalidOperationException());
        await f.Analyze();
        Assert.True(f.ViewModel.IsIdle);
        Assert.False(f.ViewModel.IsBusy);
        Assert.Equal(cancel ? VirgilCoreState.Idle : VirgilCoreState.Error, f.ViewModel.MiniCoreState);
        Assert.Equal(cancel ? "Analyse annulee. Aucune action executee." : "Analyse ressources indisponible.", f.ViewModel.StatusText);
        Assert.Equal(cancel ? "Analyse ressources annulee.\nAucune action executee." : "Analyse ressources indisponible.\nAucune action executee.", f.Message);
        Assert.Equal(!cancel, f.ViewModel.IsReportButtonEnabled);
        Assert.Equal(0, f.History.Calls);
    }

    [Theory]
    [InlineData(ProcessActionKind.KillProcess)]
    [InlineData(ProcessActionKind.CloseMainWindow)]
    [InlineData(ProcessActionKind.RestartExplorer)]
    public async Task ConfirmationExecutesOnlyAfterDecision(ProcessActionKind action)
    {
        var f = new Fixture();
        await f.Analyze();
        var pending = f.Request(action);
        var reinforced = action == ProcessActionKind.KillProcess;
        Assert.True(f.ViewModel.IsValidationOpen);
        Assert.False(pending.IsCompleted);
        Assert.Equal(reinforced ? "CONFIRMATION RENFORCEE" : "VALIDATION REQUISE", f.ViewModel.ValidationTitle);
        Assert.Equal(reinforced ? "JE CONFIRME" : "EXECUTER", f.ViewModel.ExecuteActionLabel);
        Assert.Equal(reinforced ? VirgilCoreState.SensitiveAction : VirgilCoreState.Warning, f.ViewModel.ValidationCoreState);
        Assert.Equal((f.ViewModel.ValidationCoreState, "VALIDATION"), f.State);
        Assert.Contains(action == ProcessActionKind.RestartExplorer ? "Explorer Windows" : "Bloc-notes (PID 7)", f.ViewModel.ValidationDetails);
        Assert.Contains(reinforced ? "perte de donnees" : action == ProcessActionKind.RestartExplorer ? "ne seront pas fermees" : "demande de fermeture", f.ViewModel.ValidationDetails);
        Assert.Contains(reinforced ? "Confirmation renforcee obligatoire." : "Validation explicite obligatoire.", f.ViewModel.ValidationSafety);
        Assert.Equal(0, f.Confirmation.Calls);
        Assert.Empty(f.Actions.Calls);
        f.ViewModel.ExecuteDecisionCommand.Execute(null);
        await pending;
        var call = Assert.Single(f.Actions.Calls);
        Assert.Equal(action, call.Action);
        Assert.Same(action == ProcessActionKind.RestartExplorer ? null : f.Process, call.Target);
        Assert.NotNull(call.Plan);
        Assert.Equal(reinforced ? ConfirmationLevel.Reinforced : ConfirmationLevel.Simple, call.Plan.ConfirmationLevel);
        Assert.True(f.Confirmation.Authority.Validate(call.Plan, ProcessActionPreviewFactory.Create(action, call.Target)).IsAccepted);
        Assert.Equal(1, f.Confirmation.Calls);
        Assert.False(f.ViewModel.IsValidationOpen);
        Assert.Equal(VirgilCoreState.Idle, f.ViewModel.ValidationCoreState);
        Assert.Equal(VirgilCoreState.Success, f.ViewModel.MiniCoreState);
        if (action == ProcessActionKind.RestartExplorer) Assert.Single(f.ViewModel.Items);
        else Assert.Empty(f.ViewModel.Items);
        Assert.Equal(2, f.Persisted);
    }

    public static IEnumerable<object[]> AbandonedActions =>
        from action in new[] { ProcessActionKind.CloseMainWindow, ProcessActionKind.KillProcess, ProcessActionKind.RestartExplorer }
        from decision in new[] { "SkipDecision", "CancelDecision", "TryCloseOverlay", "CancelActiveOperation" }
        select new object[] { action, decision };

    [Theory]
    [MemberData(nameof(AbandonedActions))]
    public async Task AbandonedConfirmationNeverCallsServiceAndPersistsSkippedEntry(ProcessActionKind action, string decision)
    {
        var f = new Fixture();
        await f.Analyze();
        var pending = f.Request(action);
        Assert.Equal(0, f.Confirmation.Calls);
        Assert.Empty(f.Actions.Calls);
        switch (decision)
        {
            case "SkipDecision": f.ViewModel.SkipDecisionCommand.Execute(null); break;
            case "CancelDecision": f.ViewModel.CancelDecisionCommand.Execute(null); break;
            case "TryCloseOverlay": Assert.True(f.ViewModel.TryCloseOverlay()); break;
            case "CancelActiveOperation": f.ViewModel.CancelActiveOperation(); break;
        }
        await pending;
        Assert.Equal(0, f.Confirmation.Calls);
        Assert.Empty(f.Actions.Calls);
        Assert.Equal(0, f.Actions.SkipCalls);
        Assert.False(f.ViewModel.IsValidationOpen);
        Assert.Equal(VirgilCoreState.Idle, f.ViewModel.ValidationCoreState);
        Assert.Equal(decision is "SkipDecision" or "TryCloseOverlay" ? "Action passee." : "Action annulee.", f.ViewModel.StatusText);
        Assert.Equal("Action non executee.", f.Message);
        Assert.True(f.ViewModel.IsReportButtonEnabled);
        Assert.Single(f.ViewModel.Items);
        var skipped = Assert.Single(f.History.Saved!.SkippedActions);
        var label = action switch { ProcessActionKind.CloseMainWindow => "Fermeture propre", ProcessActionKind.KillProcess => "Fermeture forcee", _ => "Relance Explorer" };
        Assert.Equal(label + " - " + (action == ProcessActionKind.RestartExplorer ? "Explorer Windows" : "Bloc-notes (PID 7)"), skipped.Name);
        Assert.Equal(ReportActionStatus.Skipped, skipped.Status);
        Assert.Empty(f.History.Saved.ExecutedActions);
        Assert.Equal(2, f.Persisted);
        // A stale execute decision cannot resurrect an abandoned request.
        f.ViewModel.ExecuteDecisionCommand.Execute(null);
        Assert.Equal(0, f.Confirmation.Calls);
        Assert.Empty(f.Actions.Calls);
    }

    [Theory]
    [InlineData(ProcessActionKind.CloseMainWindow)]
    [InlineData(ProcessActionKind.KillProcess)]
    [InlineData(ProcessActionKind.RestartExplorer)]
    public async Task BusyAnalysisRejectsActionWithoutOverlay(ProcessActionKind action)
    {
        var f = new Fixture();
        await f.Analyze();
        var gate = new TaskCompletionSource<ResourceAnalysisReport>();
        f.Monitor.Handler = token => gate.Task.WaitAsync(token);
        var pending = f.Analyze();
        Assert.True(f.ViewModel.IsBusy);
        Assert.False(f.ViewModel.IsIdle);
        Assert.False(f.ViewModel.IsReportButtonEnabled);
        await f.Request(action);
        Assert.Equal("Operation deja en cours.", f.Message);
        Assert.False(f.ViewModel.IsValidationOpen);
        Assert.Equal(0, f.Confirmation.Calls);
        Assert.Empty(f.Actions.Calls);
        Assert.True(f.ViewModel.TryCloseOverlay());
        await pending;
        Assert.True(f.ViewModel.IsIdle);
        Assert.Equal("Analyse annulee. Aucune action executee.", f.ViewModel.StatusText);
    }

    [Theory]
    [InlineData(ProcessActionKind.ReleaseInactiveMemory)]
    [InlineData(ProcessActionKind.OpenLocation)]
    public async Task InformationalActionsExecuteWithoutOverlay(ProcessActionKind action)
    {
        var f = new Fixture();
        await f.Analyze();
        await (action == ProcessActionKind.OpenLocation
            ? f.ViewModel.Items[0].OpenLocationCommand.ExecuteAsync(null)
            : f.ViewModel.ReleaseInactiveMemoryCommand.ExecuteAsync(null));
        var call = Assert.Single(f.Actions.Calls);
        Assert.Equal(action, call.Action);
        Assert.Same(action == ProcessActionKind.OpenLocation ? f.Process : null, call.Target);
        Assert.Null(call.Plan);
        Assert.Equal(0, f.Confirmation.Calls);
        Assert.False(f.ViewModel.IsValidationOpen);
        Assert.Equal(2, f.Persisted);
        Assert.Single(f.ViewModel.Items);
        if (action == ProcessActionKind.ReleaseInactiveMemory)
        {
            Assert.Equal("Information seulement. Aucune action memoire executee.", f.ViewModel.StatusText);
            Assert.Equal("Memoire inactive : information seulement.\nAucun boost RAM magique.\nAucune action executee.", f.Message);
            Assert.Equal((VirgilCoreState.Idle, "INFORMATION"), f.State);
        }
    }

    [Fact]
    public async Task IgnoreRemovesCardAndPersistsWithoutCallingService()
    {
        var f = new Fixture();
        await f.Analyze();
        await f.ViewModel.Items[0].IgnoreCommand.ExecuteAsync(null);
        Assert.Equal(0, f.Confirmation.Calls);
        Assert.Empty(f.Actions.Calls);
        Assert.Equal(0, f.Actions.SkipCalls);
        Assert.Empty(f.ViewModel.Items);
        Assert.False(f.ViewModel.IsValidationOpen);
        Assert.Equal("Processus ignore.\nAucune action executee.", f.Message);
        Assert.Equal("Processus ignore - Bloc-notes (PID 7)", Assert.Single(f.History.Saved!.SkippedActions).Name);
        Assert.True(f.ViewModel.IsReportButtonEnabled);
        // A fresh analysis resets the treated PID set, as before.
        await f.Analyze();
        Assert.Single(f.ViewModel.Items);
    }

    [Fact]
    public async Task ViewReportRequiresDataThenBuildsSimpleReportAndCloses()
    {
        var f = new Fixture();
        f.ViewModel.ViewReportCommand.Execute(null);
        Assert.Equal("Aucun rapport ressources disponible.", f.Message);
        Assert.False(f.ViewModel.IsReportOpen);
        await f.Analyze();
        f.ViewModel.ViewReportCommand.Execute(null);
        Assert.True(f.ViewModel.IsReportOpen);
        // CapturedAt is generated for each build; compare everything after its date line.
        var expected = new ResourceReportBuilder().Build(new ResourceSessionReport
        {
            Analyses = new[] { f.Monitor.Report },
            ProposedActions = new[] { "Examiner la RAM", "Examiner Bloc-notes (PID 7)" },
            RestartRecommended = true
        }, false);
        Assert.Equal(WithoutDate(expected), WithoutDate(f.ViewModel.ReportText));
        Assert.DoesNotContain(f.Process.Path!, f.ViewModel.ReportText);
        f.ViewModel.CloseReportCommand.Execute(null);
        Assert.False(f.ViewModel.IsReportOpen);
    }

    [Fact]
    public async Task OverlayCloseOrderIsReportThenValidationThenOperation()
    {
        var f = new Fixture();
        await f.Analyze();
        var pending = f.Request(ProcessActionKind.KillProcess);
        f.ViewModel.ViewReportCommand.Execute(null);
        Assert.True(f.ViewModel.TryCloseOverlay());
        Assert.False(f.ViewModel.IsReportOpen);
        Assert.True(f.ViewModel.IsValidationOpen);
        Assert.False(pending.IsCompleted);
        Assert.True(f.ViewModel.TryCloseOverlay());
        await pending;
        Assert.False(f.ViewModel.TryCloseOverlay());
        Assert.Equal(0, f.Confirmation.Calls);
        Assert.Empty(f.Actions.Calls);
    }

    [Fact]
    public async Task HeavyProcessesAnalyzesOnceThenOnlyRequestsScroll()
    {
        var f = new Fixture();
        var scrolls = 0;
        f.ViewModel.ProcessListScrollRequested += (_, _) => scrolls++;
        await f.ViewModel.HeavyProcessesCommand.ExecuteAsync(null);
        Assert.Equal(1, f.Monitor.Calls);
        Assert.Equal(1, scrolls);
        await f.ViewModel.HeavyProcessesCommand.ExecuteAsync(null);
        Assert.Equal(1, f.Monitor.Calls);
        Assert.Equal(2, scrolls);
    }

    [Theory]
    [InlineData(ProcessActionStatus.Failed)]
    [InlineData(ProcessActionStatus.PartialFailure)]
    public async Task FailedActionRequestsAttentionAndKeepsCard(ProcessActionStatus status)
    {
        var f = new Fixture();
        await f.Analyze();
        f.Actions.Result = new() { Status = status, Summary = "Refus", ReadableError = "Protege" };
        var pending = f.Request(ProcessActionKind.KillProcess);
        f.ViewModel.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal((VirgilCoreState.Warning, "ATTENTION"), f.State);
        Assert.Equal("Refus Protege", f.ViewModel.StatusText);
        Assert.Equal("Refus\nProtege", f.Message);
        Assert.Single(f.ViewModel.Items);
        Assert.Contains("Protege", f.History.Saved!.Errors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActionCancellationAndExceptionRestoreIdle(bool cancel)
    {
        var f = new Fixture();
        await f.Analyze();
        f.Actions.Exception = cancel ? new OperationCanceledException() : new InvalidOperationException();
        await f.ViewModel.Items[0].OpenLocationCommand.ExecuteAsync(null);
        Assert.True(f.ViewModel.IsIdle);
        Assert.Equal(cancel ? VirgilCoreState.Idle : VirgilCoreState.Error, f.ViewModel.MiniCoreState);
        Assert.Equal(cancel ? "Action annulee." : "Action impossible. Aucune autre action executee.", f.ViewModel.StatusText);
    }

    [Fact]
    public async Task MemoryOnlyCatchesCancellationAndAlwaysEndsOperation()
    {
        var f = new Fixture();
        f.Actions.Exception = new OperationCanceledException();
        await f.ViewModel.ReleaseInactiveMemoryCommand.ExecuteAsync(null);
        Assert.Equal("Information annulee.", f.ViewModel.StatusText);
        Assert.True(f.ViewModel.IsIdle);
        f.Actions.Exception = new InvalidOperationException();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.ViewModel.ReleaseInactiveMemoryCommand.ExecuteAsync(null));
        Assert.True(f.ViewModel.IsIdle);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "Disque indisponible")]
    [InlineData(true, "Avertissement")]
    public async Task PersistenceMessagesAndEventFollowSaveResult(bool success, string? error)
    {
        var f = new Fixture();
        f.History.Result = new() { Success = success, ReadableError = error };
        await f.Analyze();
        Assert.Equal(success ? 1 : 0, f.Persisted);
        Assert.Equal(error ?? "Historique local indisponible. Rapport conserve en memoire.", f.Message);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    [InlineData("application.exe", true)]
    public async Task ItemExposesOriginalCardTextAndAvailability(string? path, bool canOpen)
    {
        var f = new Fixture();
        f.Monitor.Report = f.Monitor.Report with { TopMemoryProcesses = new[] { f.Process with { Path = path, Publisher = null, CanForceClose = false } } };
        await f.Analyze();
        var item = Assert.Single(f.ViewModel.Items);
        Assert.Equal("Bloc-notes (PID 7) - LOURD", item.Header);
        Assert.Equal($"RAM {ScanRules.FormatBytes(f.Process.WorkingSetBytes)} - CPU {1.5:0.0} %", item.UsageLine);
        Assert.Equal("Editeur : non disponible", item.PublisherLine);
        Assert.Equal("Application utilisateur", item.UserMessage);
        Assert.Equal(canOpen, item.CanOpenLocation);
        Assert.True(item.CanCloseGracefully);
        Assert.False(item.CanForceClose);
    }

    [Fact]
    public void ReturnHomeRelaysEvent()
    {
        var f = new Fixture();
        var calls = 0;
        f.ViewModel.ReturnHomeRequested += (_, _) => calls++;
        f.ViewModel.ReturnHomeCommand.Execute(null);
        Assert.Equal(1, calls);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void CompositionRegistersViewModelWithExplicitSingletonFactory()
    {
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var registration = Assert.Single(services, d => d.ServiceType == typeof(ResourcesViewModel));
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
        Assert.NotNull(registration.ImplementationFactory);
        Assert.Same(provider.GetRequiredService<ResourcesViewModel>(), provider.GetRequiredService<ResourcesViewModel>());
    }

    [Fact]
    public async Task ConfirmationRefusalFollowsExistingCancelPath()
    {
        var f = new Fixture(); await f.Analyze(); f.Confirmation.Refuse = true;
        var pending = f.Request(ProcessActionKind.CloseMainWindow);
        f.ViewModel.ExecuteDecisionCommand.Execute(null); await pending;
        Assert.Equal(1, f.Confirmation.Calls); Assert.Empty(f.Actions.Calls);
        Assert.Equal("Action annulee.", f.ViewModel.StatusText);
        Assert.Equal("Action non executee.", f.Message);
        Assert.False(f.ViewModel.IsValidationOpen);
    }

    private static string WithoutDate(string text) => text[(text.IndexOf('\n') + 1)..];

    private sealed class Fixture
    {
        public ProcessResourceInfo Process { get; } = new()
        {
            ProcessId = 7, Name = "Bloc-notes", Path = "bloc-notes.exe", Publisher = "Editeur",
            WorkingSetBytes = 1024 * 1024, CpuPercent = 1.5, Status = ProcessResourceStatus.Heavy,
            CanCloseGracefully = true, CanForceClose = true, UserMessage = "Application utilisateur"
        };
        public FakeMonitor Monitor { get; } = new();
        public FakeActions Actions { get; } = new();
        public FakeHistory History { get; } = new();
        public RecordingConfirmation Confirmation { get; } = new();
        public ResourcesViewModel ViewModel { get; }
        public string? Message { get; private set; }
        public (VirgilCoreState State, string Label)? State { get; private set; }
        public int Persisted { get; private set; }
        public Fixture(bool capability = false)
        {
            Actions.CanReleaseInactiveMemory = capability;
            Monitor.Report = new()
            {
                Duration = TimeSpan.FromSeconds(2.5), AverageCpuPercent = 12.5, MaximumCpuPercent = 25,
                AverageMemoryPercent = 50, MaximumMemoryPercent = 60, CpuHealth = ResourceHealthLevel.Stable,
                MemoryHealth = ResourceHealthLevel.Watch, OverallHealth = ResourceHealthLevel.Watch,
                TopMemoryProcesses = new[] { Process }, Recommendations = new[] { "Examiner la RAM" },
                Samples = new[] { new ResourceSample { TotalMemoryBytes = 4UL * 1024 * 1024 * 1024,
                    UsedMemoryBytes = 2UL * 1024 * 1024 * 1024, Uptime = TimeSpan.FromDays(8) + TimeSpan.FromHours(3), ProcessCount = 42 } }
            };
            ViewModel = new(Monitor, Actions, new ResourceReportBuilder(), History, Confirmation);
            ViewModel.VirgilMessageRequested += message => Message = message;
            ViewModel.VirgilStateRequested += (state, label) => State = (state, label);
            ViewModel.ReportPersisted += (_, _) => Persisted++;
        }
        public Task Analyze() => ViewModel.AnalyzeCommand.ExecuteAsync(null);
        public Task Request(ProcessActionKind action) => action switch
        {
            ProcessActionKind.RestartExplorer => ViewModel.RestartExplorerCommand.ExecuteAsync(null),
            ProcessActionKind.CloseMainWindow => ViewModel.Items[0].CloseGracefullyCommand.ExecuteAsync(null),
            _ => ViewModel.Items[0].ForceCloseCommand.ExecuteAsync(null)
        };
    }

    // Deliberately do not report progress: Progress<T> posts callbacks asynchronously.
    private sealed class FakeMonitor : IResourceMonitoringService
    {
        public ResourceAnalysisReport Report { get; set; } = new();
        public ResourceAnalysisRequest? Request { get; private set; }
        public int Calls { get; private set; }
        public Func<CancellationToken, Task<ResourceAnalysisReport>>? Handler { get; set; }
        public Task<ResourceAnalysisReport> AnalyzeAsync(ResourceAnalysisRequest request, IProgress<ResourceProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            return Handler?.Invoke(cancellationToken) ?? Task.FromResult(Report);
        }
    }

    private sealed class FakeActions : IProcessActionService
    {
        public bool CanReleaseInactiveMemory { get; set; }
        public List<(ProcessActionKind Action, ProcessResourceInfo? Target, ConfirmedPlan? Plan)> Calls { get; } = new();
        public int SkipCalls { get; private set; }
        public ProcessActionResult Result { get; set; } = new() { Status = ProcessActionStatus.Completed, Summary = "Termine" };
        public Exception? Exception { get; set; }
        public Task<ProcessActionResult> ExecuteAsync(ProcessActionKind action, ProcessResourceInfo? target, ConfirmedPlan? plan, CancellationToken cancellationToken)
        {
            Calls.Add((action, target, plan));
            return Exception is null ? Task.FromResult(Result with { Action = action }) : Task.FromException<ProcessActionResult>(Exception);
        }
        public ProcessActionResult Skip(ProcessActionKind action, string target)
        {
            SkipCalls++;
            throw new InvalidOperationException("No service call is allowed for an abandoned confirmation.");
        }
    }

    private sealed class FakeHistory : IReportHistoryService
    {
        public ReportSaveResult Result { get; set; } = new() { Success = true };
        public ReportEntry? Saved { get; private set; }
        public int Calls { get; private set; }
        public Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken cancellationToken)
        {
            Calls++;
            Saved = report;
            return Task.FromResult(Result);
        }
        public Task<ReportHistoryLoadResult> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetLatestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
