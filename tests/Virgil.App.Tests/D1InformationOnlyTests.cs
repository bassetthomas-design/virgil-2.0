using System.Reflection;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Actions;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

public sealed class D1InformationOnlyTests
{
    [Theory]
    [InlineData("truncated", false)]
    [InlineData("firmware", false)]
    [InlineData("same-id", false)]
    [InlineData("truncated", true)]
    [InlineData("firmware", true)]
    [InlineData("same-id", true)]
    public async Task Information_only_final_scan_cannot_prove_source_current(string scenario, bool checkRemainingFlag)
    {
        Capture? capture = null;
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new InformationScan(scenario), wrap: service => capture = new Capture(service));
        f.Backend.Handler = (step, _) => Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Installation réussie pour app-a."));
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Winget;
        Assert.False(Assert.Single(f.Vm.SequenceChoices, choice => choice.Step.Component == UpdateComponent.WingetFinal).IsSelected);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(UpdateComponent.Winget, Assert.Single(f.Backend.Executed));
        var result = Assert.IsType<UpdateSequenceResult>(capture?.Result);
        Assert.True(Assert.Single(result.Steps, step => step.Step.Component == UpdateComponent.Winget).Succeeded);
        if (checkRemainingFlag)
        {
            // Le verdict interne n'est pas une API UI ; vérifier son indicateur sans exposer un port de test en production.
            var verify = typeof(UpdatesViewModel).GetMethod("ReadFinalVerificationsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(verify);
            var task = Assert.IsAssignableFrom<Task<(IReadOnlyList<UpdateSourceVerification> Sources, UpdateScanReport? Applications)>>(verify.Invoke(f.Vm, [result]));
            var verdict = Assert.Single((await task).Sources, check => check.Component == UpdateComponent.Winget);
            Assert.True(verdict.HasRemaining);
            Assert.False(verdict.IsCurrent);
            Assert.Equal("reste à vérifier", verdict.Status);
        }
        else
        {
            var report = f.History.Report;
            Assert.NotNull(report);
            foreach (var text in new[] { f.Vm.Operation.FinalSummary, f.Vm.StatusText, f.Vm.Operation.Commentary,
                report.Summary, report.SimpleView })
                Assert.DoesNotContain("Tout est à jour", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("reste à vérifier", f.Vm.Operation.FinalSummary);
            var card = Assert.Single(f.Vm.Operation.Steps, step => step.Key == "Winget:app-a");
            Assert.Equal(scenario == "same-id" ? "reste à vérifier" : "mis à jour", card.Status);
            if (scenario == "same-id") Assert.DoesNotContain("n'est plus proposée", card.Detail);
        }
    }

    internal sealed class InformationScan(string scenario) : IUpdateScanService
    {
        private int _calls;
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult(new UpdateScanReport { Winget = new WingetAvailability { IsAvailable = true },
                Items = ++_calls == 1 ? [new UpdateItem { Id = "app-a", Name = "Application A" }]
                    : [new UpdateItem { Id = scenario == "same-id" ? "app-a" : scenario == "firmware" ? "Vendor.Firmware" : "Vendor.App…",
                        Name = "Mise à jour à examiner", IsIdTruncated = scenario == "truncated",
                        Source = scenario == "firmware" ? UpdateSource.FirmwareInformation : UpdateSource.Winget,
                        RiskLevel = UpdateRiskLevel.CriticalInformationOnly, Status = UpdateItemStatus.InformationOnly }] });
    }

    private sealed class Capture(IUpdateSequenceService service) : IUpdateSequenceService
    {
        public UpdateSequenceResult? Result { get; private set; }
        public Task<IReadOnlyList<UpdateSequenceStep>> ScanAsync(IReadOnlyList<UpdateItem> apps, CancellationToken token) => service.ScanAsync(apps, token);
        public Virgil.Contracts.Actions.ActionPreview CreatePreview(IReadOnlyList<UpdateSequenceStep> selected) => service.CreatePreview(selected);
        public async Task<UpdateSequenceResult> ExecuteAsync(IReadOnlyList<UpdateSequenceStep> selected, ConfirmedPlan? plan, IProgress<UpdateSequenceProgress>? progress, CancellationToken token)
        { Result = await service.ExecuteAsync(selected, plan, progress, token); return Result; }
    }
}
