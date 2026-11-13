using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using Virgil.App.Animation;
using Virgil.App.Controls;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LotEProgressColorTests
{
    internal static object Read(object owner, string name)
    {
        var property = owner.GetType().GetProperty(name);
        Assert.NotNull(property); var value = property.GetValue(owner); Assert.NotNull(value); return value;
    }
    private static void Complete(LongOperationViewModel operation, string state, bool cancelled = false)
    {
        var method = typeof(LongOperationViewModel).GetMethods().SingleOrDefault(m => m.Name == "Complete" && m.GetParameters().Length == 3);
        Assert.NotNull(method);
        var type = method.GetParameters()[2].ParameterType;
        Assert.True(type.IsEnum);
        method.Invoke(operation, new[] { (object)"Un message réussi ne prouve rien.", cancelled, Enum.Parse(type, state) });
    }

    [Theory]
    [InlineData("Neutral", "#FF9FB0C0")]
    [InlineData("Succeeded", "#FF5EE07A")]
    [InlineData("NeedsReview", "#FFFFC23D")]
    [InlineData("Failed", "#FFFF4D4D")]
    [InlineData("Cancelled", "#FF9FB0C0")]
    public Task Typed_final_state_colors_the_real_bar_without_changing_completion(string state, string color) => ThemeContrastTests.OnSta(() =>
    {
        var operation = new LongOperationViewModel(() => { }); operation.Start("Traitement", immediately: true);
        operation.Percent = 37;
        var panel = new OperationProgressPanel { DataContext = operation, Width = 600 };
        try
        {
            Assert.Equal("Neutral", Read(operation, "FinalState").ToString());
            Complete(operation, state, state == "Cancelled");
            Assert.Equal(state, Read(operation, "FinalState").ToString());
            Assert.Equal(state == "Cancelled" ? 37d : 100d, operation.Percent);
            LotEFaceTests.Capture(panel, "progression-" + state, 600, 160);
            var bar = Descendants(panel).OfType<ProgressBar>().Single();
            Assert.Equal(color, Assert.IsType<SolidColorBrush>(bar.Foreground).Color.ToString());
            Assert.Equal(operation.Percent, bar.Value);
        }
        finally { if (operation.IsRunning) operation.Complete("Fin"); }
        return Task.CompletedTask;
    });

    [Fact]
    public void Default_and_cancellation_remain_neutral_even_with_success_text()
    {
        var operation = new LongOperationViewModel(() => { }); operation.Start("Traitement");
        operation.Complete("Tout a réussi.");
        Assert.Equal("Neutral", Read(operation, "FinalState").ToString());
        operation.Start("Traitement"); operation.Percent = 42; operation.AcknowledgeCancellation();
        Complete(operation, "Succeeded");
        Assert.Equal("Cancelled", Read(operation, "FinalState").ToString()); Assert.Equal(42d, operation.Percent);
        operation.Start("Nouveau traitement");
        try { Assert.Equal("Neutral", Read(operation, "FinalState").ToString()); }
        finally { operation.Complete("Fin"); }
    }

    [Theory]
    [InlineData(false, "NeedsReview", VirgilCoreState.Warning)]
    [InlineData(true, "Failed", VirgilCoreState.Error)]
    public Task Partial_updates_use_individual_results_despite_success_words(bool fail, string expected, VirgilCoreState face) => ThemeContrastTests.OnSta(async () =>
    {
        var scan = new Scan(); var f = new UpdatesSequenceViewModelTests.Fixture(scan: scan); f.Backend.Reboot = false;
        f.Backend.Handler = (step, _) =>
        {
            if (step.Id == "a") scan.Remaining.Remove("a");
            return Task.FromResult(new UpdateSequenceStepResult(step, !fail || step.Id == "a", false, "Installation réussie."));
        };
        await f.Vm.ScanUpdatesCommand.ExecuteAsync(null);
        foreach (var id in fail ? new[] { "a", "b" } : new[] { "a" })
        {
            var card = Assert.Single(f.Vm.SingleCards, c => c.Key == "Winget:" + id);
            var pending = D2SingleItemTests.Command(f.Vm).ExecuteAsync(card.Step);
            f.Vm.ExecuteDecisionCommand.Execute(null); await pending;
        }
        Assert.Equal(expected, Read(f.Vm.Operation, "FinalState").ToString());
        Assert.Equal(face, f.Vm.MiniCoreState);
        Assert.Equal("mis à jour", Assert.Single(f.Vm.SingleCards, c => c.Key == "Winget:a").Status);
        Assert.Equal("reste à installer", Assert.Single(f.Vm.SingleCards, c => c.Key == "Winget:c").Status);
        Assert.Equal(fail ? 3 : 2, scan.Calls);
    });

    [Theory]
    [InlineData("en erreur")]
    [InlineData("à vérifier")]
    public Task Information_cards_never_color_the_bar_or_face(string status) => ThemeContrastTests.OnSta(() =>
    {
        var f = new UpdatesSequenceViewModelTests.Fixture();
        f.Vm.SingleCards.Add(new("") { Step = new("a", "Application A", UpdateComponent.Winget), Status = "mis à jour" });
        f.Vm.SingleCards.Add(new("") { Step = new("SteamGame:1", "Jeu Steam", UpdateComponent.Launcher) { InformationOnly = true }, Status = status });
        f.Vm.SingleCards.Add(new("") { Step = new("BIOS", "BIOS", UpdateComponent.Driver) { InformationOnly = true }, Status = status });
        f.Vm.Operation.Start("Traitement");
        var end = typeof(UpdatesViewModel).GetMethod("EndOperation", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(end); end.Invoke(f.Vm, null);
        Assert.Equal("Succeeded", Read(f.Vm.Operation, "FinalState").ToString());
        Assert.Equal(VirgilCoreState.Success, f.Vm.MiniCoreState);
        Assert.All(f.Vm.SingleCards.Where(c => c.Step!.IsInformational), c => Assert.Equal(status, c.Status));
        return Task.CompletedTask;
    });

    private sealed class Scan : IUpdateScanService
    {
        public HashSet<string> Remaining { get; } = ["a", "b", "c"];
        public int Calls { get; private set; }
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new UpdateScanReport { Winget = new() { IsAvailable = true }, Items = Remaining.Select(id =>
                new UpdateItem { Id = id, Name = "Application " + id, InstalledVersion = "1.0", AvailableVersion = "2.0" }).ToArray() });
        }
    }
    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
