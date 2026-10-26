using Virgil.Contracts;
using Virgil.Core.Interventions;
using Virgil.Infrastructure.Windows.Startup;
using Xunit;

namespace Virgil.Tests;

public sealed class StartupRestorePointTests
{
    [Theory]
    [InlineData(InterventionStatus.Completed, 0, "Code sortie : 0.\nVIRGIL_RESTORE_POINT_VERIFIED", true)]
    [InlineData(InterventionStatus.Completed, 0, "Point créé", false)]
    [InlineData(InterventionStatus.Completed, 0, "NOT_VIRGIL_RESTORE_POINT_VERIFIED", false)]
    [InlineData(InterventionStatus.Failed, 0, "VIRGIL_RESTORE_POINT_VERIFIED", false)]
    [InlineData(InterventionStatus.Completed, 1, "VIRGIL_RESTORE_POINT_VERIFIED", false)]
    public async Task RequiresSuccessfulElevatedResultAndExactVerificationMarker(InterventionStatus status, int exitCode, string output, bool expected)
    {
        var helper = new Helper(new() { Status = status, ExitCode = exitCode, SummaryOutput = output });
        var service = new WindowsStartupRestorePointService(helper);

        Assert.Equal(expected, await service.CreateAndVerifyAsync(CancellationToken.None));
        Assert.Equal(InterventionId.CreateStartupRestorePoint, helper.Definition!.Id);
        Assert.True(helper.Definition.RequiresAdministrator);
    }

    [Fact]
    public async Task CancellationPropagatesWithoutAcceptingRestoreProtection()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var helper = new Helper(new());
        var service = new WindowsStartupRestorePointService(helper);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAndVerifyAsync(cancellation.Token));
        Assert.Null(helper.Definition);
    }

    private sealed class Helper(ElevatedInterventionResult result) : IInterventionElevatedHelperClient
    {
        public InterventionDefinition? Definition { get; private set; }
        public Task<ElevatedInterventionResult> ExecuteAsync(InterventionDefinition definition, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Definition = definition;
            return Task.FromResult(result);
        }
    }
}
