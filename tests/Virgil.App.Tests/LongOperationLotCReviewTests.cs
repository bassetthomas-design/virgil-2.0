using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Virgil.App.Controls;
using Virgil.App.ViewModels;
using Virgil.Core.Operations;
using Xunit;

namespace Virgil.Tests;

public sealed class LongOperationLotCReviewTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Completed_bar_reaches_100_but_cancellation_preserves_reached_value(bool cancelled, bool determinate)
    {
        var operation = new LongOperationViewModel(() => { });
        operation.Start("Analyse");
        if (determinate) operation.Report(new OperationProgress("Zone", Percent: 9));
        var reached = operation.Percent;
        var wasIndeterminate = operation.IsIndeterminate;
        if (cancelled) operation.CancelCommand.Execute(null);
        operation.Complete(cancelled ? "Analyse annulée." : "Analyse terminée.");
        Assert.Equal(cancelled ? reached : 100, operation.Percent);
        Assert.Equal(cancelled && wasIndeterminate, operation.IsIndeterminate);
    }

    [Theory, Trait("Category", "Windows")]
    [InlineData(false)]
    [InlineData(true)]
    public Task Final_panel_displays_conclusion_exactly_once(bool cleanup) => ThemeContrastTests.OnSta(async () =>
    {
        LongOperationViewModel operation;
        var conclusion = cleanup ? "Nettoyage terminé." : "Séquence terminée.";
        if (cleanup)
        {
            var f = new CleanupViewModelTests.Fixture();
            await f.Analyze();
            var pending = f.Run();
            f.Vm.ExecuteDecisionCommand.Execute(null);
            await pending;
            operation = f.Vm.Operation;
        }
        else
        {
            var f = new UpdatesSequenceViewModelTests.Fixture(wrap: service => new UpdatesSequenceViewModelTests.RepeatingSequence(service));
            var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
            f.Vm.ExecuteDecisionCommand.Execute(null);
            await pending;
            operation = f.Vm.Operation;
        }
        await WithPanel(operation, panel =>
        {
            var visible = string.Join("\n", Descendants<TextBlock>(panel).Where(block => block.IsVisible).Select(block => block.Text));
            Assert.Single(Regex.Matches(visible, Regex.Escape(conclusion)).Cast<Match>());
        });
    });

    [Fact, Trait("Category", "Windows")]
    public Task Cancel_button_is_visible_only_while_operation_runs() => ThemeContrastTests.OnSta(async () =>
    {
        var operation = new LongOperationViewModel(() => { });
        operation.Start("Analyse", immediately: true);
        await WithPanel(operation, panel =>
        {
            var cancel = Assert.Single(Descendants<Button>(panel), button => ReferenceEquals(button.Command, operation.CancelCommand));
            Assert.True(cancel.IsVisible);
            operation.Complete("Analyse terminée.");
            panel.UpdateLayout();
            Assert.False(cancel.IsVisible);
            Assert.True(panel.IsVisible);
        });
    });

    [Theory, Trait("Category", "Windows")]
    [InlineData(0, "0 fichiers en quarantaine")]
    [InlineData(1, "1 fichier en quarantaine")]
    [InlineData(2, "2 fichiers en quarantaine")]
    public Task Quarantine_counts_use_correct_plural_in_final_summary_and_step(int count, string expected) => ThemeContrastTests.OnSta(async () =>
    {
        var f = new CleanupViewModelTests.Fixture();
        f.Actions.Handler = (zone, _) => Task.FromResult(new Virgil.Contracts.CleanupStepResult(zone.Definition,
            Virgil.Contracts.CleanupStepStatus.Completed, 0, 0, 0, 0, TimeSpan.Zero, []) { QuarantinedFiles = count });
        await f.Analyze();
        var pending = f.Run();
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Contains(expected, f.Vm.Operation.FinalSummary);
        Assert.Contains(expected, Assert.Single(f.Vm.Operation.Steps).Detail);
    });

    private static async Task WithPanel(LongOperationViewModel operation, Action<OperationProgressPanel> assertions)
    {
        var panel = new OperationProgressPanel { DataContext = operation };
        var window = new Window { Content = panel, Width = 1000, Height = 800, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            assertions(panel);
        }
        finally { window.Close(); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
