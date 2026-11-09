using Virgil.App.ViewModels;
using Virgil.Core.Operations;
using Xunit;

namespace Virgil.Tests;

public sealed class LongOperationViewModelTests
{
    [Fact]
    public void Step_identity_updates_in_place_and_explicit_dismiss_or_restart_clears_final_state()
    {
        var operation = new LongOperationViewModel(() => { });
        operation.Start("Analyse");
        operation.SetStep("zone", "Zone", "En cours", "1/40");
        var first = Assert.Single(operation.Steps);
        operation.SetStep("zone", "Zone", "En cours", "12/40");
        Assert.Same(first, Assert.Single(operation.Steps));
        Assert.Equal("12/40", first.Detail);
        operation.Complete("Bilan");
        operation.SetStep("tardive", "Tardive", "En cours");
        Assert.Single(operation.Steps);
        operation.DismissCommand.Execute(null);
        Assert.False(operation.IsVisible);
        operation.Start("Nouvelle analyse");
        Assert.Empty(operation.Steps);
        Assert.Empty(operation.FinalSummary);
        Assert.Empty(operation.Commentary);
        operation.Complete("Terminée");
    }

    [Fact]
    public void DeterminateProgressCancellationAndFinalResultRemainVisible()
    {
        var cancellations = 0;
        var operation = new LongOperationViewModel(() => cancellations++);
        operation.Start("Analyse");
        Assert.True(operation.IsRunning);
        Assert.True(operation.IsIndeterminate);
        operation.Report(new OperationProgress("Fichiers", 25, 100));
        Assert.False(operation.IsIndeterminate);
        Assert.Equal(25, operation.Percent);
        Assert.Contains("Écoulé", operation.Timing);
        Assert.Contains("restant estimé", operation.Timing);
        operation.CancelCommand.Execute(null);
        Assert.Equal(1, cancellations);
        Assert.Contains("Annulation", operation.Step);
        operation.Complete("Opération annulée.");
        Assert.False(operation.IsRunning);
        Assert.True(operation.IsVisible);
        Assert.Equal("Opération annulée.", operation.FinalSummary);
        Assert.Equal("Opération annulée.", operation.Step);
        operation.Report(new OperationProgress("Ancien message", Percent: 50));
        Assert.Equal("Opération annulée.", operation.Step);
    }
}
