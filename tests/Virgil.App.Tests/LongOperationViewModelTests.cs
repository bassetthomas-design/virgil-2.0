using Virgil.App.ViewModels;
using Virgil.Core.Operations;
using Xunit;

namespace Virgil.Tests;

public sealed class LongOperationViewModelTests
{
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
        Assert.Equal("Opération annulée.", operation.Step);
        operation.Report(new OperationProgress("Ancien message", Percent: 50));
        Assert.Equal("Opération annulée.", operation.Step);
    }
}
