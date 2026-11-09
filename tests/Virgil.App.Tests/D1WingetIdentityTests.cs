using Virgil.Contracts;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

public sealed class D1WingetIdentityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Real_service_cards_use_own_execution_and_own_remaining_identity(bool guardRefusal, bool cancelThird)
    {
        var scan = new IdentityScan(cancelThird);
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: scan);
        var executed = new List<string>();
        f.Backend.Handler = (step, _) =>
        {
            executed.Add(step.Id);
            if (cancelThird && step.Id == "app-b") f.Vm.CancelActiveOperation();
            return Task.FromResult(new UpdateSequenceStepResult(step, step.Id == "app-a", false,
                step.Id == "app-a" ? "Installation réussie pour app-a." : guardRefusal
                    ? "Installation refusée par une garde de sécurité pour app-b." : "Échec d'installation pour app-b."));
        };
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Winget;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.InRange(scan.Calls, 2, int.MaxValue);
        Assert.Equal(new[] { "app-a", "app-b" }, executed);
        var a = Assert.Single(f.Vm.Operation.Steps, card => card.Key == "Winget:app-a");
        var b = Assert.Single(f.Vm.Operation.Steps, card => card.Key == "Winget:app-b");
        Assert.Equal("mis à jour", a.Status);
        Assert.Contains("Installation réussie pour app-a", a.Detail);
        Assert.DoesNotContain("bloquée", a.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("en erreur", a.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reste à installer", a.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("app-b", a.Detail);
        Assert.DoesNotContain("app-c", a.Detail);
        Assert.Equal(guardRefusal ? "bloquée" : "en erreur", b.Status);
        Assert.Contains("app-b", b.Detail);
        Assert.Contains(guardRefusal ? "garde de sécurité" : "Échec d'installation", b.Detail);
        Assert.DoesNotContain("réussie", b.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Terminé", b.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("app-a", b.Detail);
        Assert.DoesNotContain("app-c", b.Detail);
        if (cancelThird)
        {
            var c = Assert.Single(f.Vm.Operation.Steps, card => card.Key == "Winget:app-c");
            Assert.Equal("reste à installer", c.Status);
            Assert.Contains("app-c", c.Detail);
            Assert.Contains("non lancée", c.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Étape exécutée", c.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("réussie", c.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Terminé", c.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("app-a", c.Detail);
            Assert.DoesNotContain("app-b", c.Detail);
        }
        Assert.DoesNotContain("tout est à jour", f.Vm.Operation.FinalSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tout est à jour", f.History.Report!.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, false, "reste à installer")]
    [InlineData(true, false, "reste à installer")]
    [InlineData(true, true, "en erreur")]
    [InlineData(false, true, "à jour")]
    public async Task Partly_executed_source_uses_final_remains_and_short_summary(bool failSecond, bool emptyFinal, string status)
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new IdentityScan(true, emptyFinal));
        var executed = new List<string>();
        f.Backend.Handler = (step, _) =>
        {
            executed.Add(step.Id);
            if (step.Id == (failSecond ? "app-b" : "app-a")) f.Vm.CancelActiveOperation();
            return Task.FromResult(new UpdateSequenceStepResult(step, step.Id == "app-a", false,
                step.Id == "app-a" ? "Installation réussie pour app-a." : "Échec brut d'installation pour app-b."));
        };
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Winget;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(failSecond ? new[] { "app-a", "app-b" } : ["app-a"], executed);
        foreach (var text in new[] { f.Vm.Operation.FinalSummary, f.History.Report!.SimpleView })
        {
            var source = Assert.Single(text.Split('\n'), line => line.StartsWith("WinGet / applications", StringComparison.Ordinal));
            Assert.Contains(": " + status + " — ", source);
            Assert.DoesNotContain("non lancée", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("app-b", source);
            Assert.DoesNotContain("Étape terminée après annulation", source);
            Assert.DoesNotContain("Échec brut", source);
            Assert.InRange(source.Length, 1, 180);
        }
    }

    internal sealed class IdentityScan(bool includeThird, bool emptyFinal = false) : IUpdateScanService
    {
        public int Calls { get; private set; }
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token)
        {
            Calls++;
            var ids = Calls == 1 ? (includeThird ? new[] { "app-a", "app-b", "app-c" } : ["app-a", "app-b"])
                : emptyFinal ? Array.Empty<string>() : includeThird ? new[] { "app-b", "app-c" } : ["app-b"];
            return Task.FromResult(new UpdateScanReport { Winget = new WingetAvailability { IsAvailable = true },
                Items = ids.Select(id => new UpdateItem { Id = id, Name = "Même application", Source = UpdateSource.Winget }).ToArray() });
        }
    }
}
