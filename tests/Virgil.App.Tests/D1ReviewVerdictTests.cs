using Virgil.Contracts;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

public sealed class D1ReviewVerdictTests
{
    [Theory]
    [InlineData(UpdateComponent.WingetFinal, "reste à installer", "reste")]
    [InlineData(UpdateComponent.Store, "à vérifier", "à terminer dans le Microsoft Store")]
    [InlineData(UpdateComponent.Launcher, "non pris en charge automatiquement", "Steam n’était pas connecté")]
    public async Task Real_sequence_distinguishes_remaining_manual_completion_and_safety_blocks(UpdateComponent component, string status, string detail)
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new Scan { Remaining = true });
        f.Backend.Reboot = false;
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == component;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        if (component == UpdateComponent.Launcher)
        {
            var steam = Assert.Single(f.Vm.Operation.Steps, step => step.Key == "Launcher:Steam");
            var battle = Assert.Single(f.Vm.Operation.Steps, step => step.Key == "Launcher:Battle.net");
            Assert.Equal(status, steam.Status);
            Assert.Contains(detail, steam.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(status, battle.Status);
            Assert.Contains("le lanceur met ses jeux à jour lui-même à son ouverture", battle.Detail, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            var selected = Assert.Single(f.Vm.SequenceChoices, choice => choice.CanSelect && choice.IsSelected);
            var card = Assert.Single(f.Vm.Operation.Steps, step => step.Key == $"{component}:{selected.Step.Id}");
            Assert.Equal(status, card.Status);
            Assert.Contains(detail, card.Detail, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("tout est à jour", f.Vm.Operation.FinalSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Only_manual_sources_left_names_where_to_finish_in_every_conclusion(bool store, bool launcher)
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new Scan());
        f.Backend.Reboot = false;
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices)
            choice.IsSelected = choice.Step.Component == UpdateComponent.WingetFinal
                || (store && choice.Step.Component == UpdateComponent.Store)
                || (launcher && choice.Step.Component == UpdateComponent.Launcher);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        foreach (var conclusion in new[] { f.Vm.StatusText, f.Vm.Operation.FinalSummary, f.History.Report!.Summary })
        {
            if (store) Assert.Contains("à terminer dans le Microsoft Store", conclusion, StringComparison.OrdinalIgnoreCase);
            if (launcher) Assert.Contains("non pris en charge automatiquement", conclusion, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tout est à jour", conclusion, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("remaining")]
    [InlineData("unavailable")]
    [InlineData("scan-error")]
    [InlineData("store")]
    public async Task Non_success_cards_never_keep_a_finished_execution_detail(string scenario)
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new Scan
        { Remaining = scenario == "remaining", Available = scenario != "unavailable", Error = scenario == "scan-error" });
        f.Backend.Reboot = false;
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices)
            choice.IsSelected = choice.Step.Component == (scenario == "store" ? UpdateComponent.Store : UpdateComponent.WingetFinal);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        var cards = f.Vm.Operation.Steps.Where(step => step.Status is not "à jour" and not "mis à jour" and not "Terminée").ToArray();
        Assert.NotEmpty(cards);
        Assert.All(cards, card => Assert.DoesNotContain("Terminé", card.Detail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Restore_safety_failure_remains_a_real_block()
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new Scan());
        f.Backend.Handler = (step, _) => Task.FromResult(new UpdateSequenceStepResult(step, false, false,
            "Installation bloquée : point de restauration non vérifié.") { RestorePointVerified = false });
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component is UpdateComponent.Defender or UpdateComponent.Windows;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal("bloquée", Assert.Single(f.Vm.Operation.Steps, card => card.Key == "Defender:defender").Status);
        Assert.Equal("bloquée", Assert.Single(f.Vm.Operation.Steps, card => card.Key == "Windows:software").Status);
        Assert.DoesNotContain("tout est à jour", f.Vm.Operation.FinalSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Real_sequence_reboot_is_plain_french_in_cards_summary_and_report()
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new Scan());
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.WingetFinal;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        foreach (var text in new[] { f.Vm.Operation.FinalSummary, f.History.Report!.SimpleView,
            Assert.Single(f.Vm.Operation.Steps, card => card.Key == "PendingReboot:pending-reboot").Detail })
        {
            var reboot = Assert.Single(text.Split('\n'), line => line.StartsWith("Redémarrage nécessaire", StringComparison.Ordinal));
            Assert.Contains("après une installation", reboot);
            Assert.DoesNotContain("(", reboot);
            Assert.DoesNotContain(")", reboot);
        }
    }

    private sealed class Scan : IUpdateScanService
    {
        public bool Remaining { get; init; }
        public bool Available { get; init; } = true;
        public bool Error { get; init; }
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult(new UpdateScanReport { Winget = new WingetAvailability { IsAvailable = Available },
                Items = Remaining ? [new UpdateItem { Id = "app-left", Name = "Application restante" }] : [],
                Errors = Error ? ["Recherche impossible."] : [] });
    }
}
