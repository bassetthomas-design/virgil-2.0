using Virgil.Contracts;
using Xunit;
using Virgil.ElevatedHelper;

namespace Virgil.Tests;

public sealed class StartupApprovalPayloadTests
{
    [Theory]
    [InlineData("../Run")]
    [InlineData("a\\b")]
    [InlineData("")]
    [InlineData("a\n")]
    public void RejectsUnsafeNames(string name) => Assert.False(new StartupApprovalWrite(StartupApprovalKind.Run, name,
        "020000000000000000000000", "030000000000000000000000").IsValid());

    [Theory]
    [InlineData("010000000000000000000000")]
    [InlineData("020100000000000000000000")]
    [InlineData("02")]
    [InlineData("zz0000000000000000000000")]
    public void RejectsUnknownOrMalformedState(string hex) => Assert.False(new StartupApprovalWrite(StartupApprovalKind.Run, "App", hex,
        "030000000000000000000000").IsValid());

    [Fact]
    public void RejectsUnknownKind() => Assert.False(new StartupApprovalWrite((StartupApprovalKind)99, "App",
        "020000000000000000000000", "030000000000000000000000").IsValid());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidPayloadNeverRunsRestorePointOrRegistryWrite(bool admin)
    {
        var runner = new Runner();
        var dispatcher = new ElevatedActionDispatcher(new ElevatedRequestValidator(), new ElevatedActionAllowlist(),
            runner, new Security(admin), () => @"C:\Windows\System32");
        var result = await dispatcher.ExecuteValidatedAsync(new ElevatedInterventionRequest
        {
            ActionId = InterventionId.SetStartupApproval,
            StartupApproval = new(StartupApprovalKind.Run, "../Run", "020000000000000000000000", "030000000000000000000000")
        });
        Assert.Equal(InterventionStatus.Failed, result.Status);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task FailedRestorePointBlocksRegistryWrite()
    {
        var runner = new Runner();
        var dispatcher = new ElevatedActionDispatcher(new ElevatedRequestValidator(), new ElevatedActionAllowlist(),
            runner, new Security(true), () => @"C:\Windows\System32");
        var result = await dispatcher.ExecuteValidatedAsync(new ElevatedInterventionRequest
        {
            ActionId = InterventionId.SetStartupApproval,
            StartupApproval = new(StartupApprovalKind.Run, "App", "020000000000000000000000", "030000000000000000000000")
        });
        Assert.Equal(InterventionStatus.Failed, result.Status);
        Assert.Equal(1, runner.Calls);
        Assert.Contains("restauration", result.ReadableError);
    }

    private sealed class Security(bool administrator) : IElevatedSecurityContext
    {
        public bool IsAdministrator => administrator;
    }
    private sealed class Runner : IElevatedCommandRunner
    {
        public int Calls { get; private set; }
        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command)
        {
            Calls++;
            return Task.FromResult(new ElevatedCommandResult(1, "", "Échec"));
        }
    }

    [Fact]
    public void AcceptsReversibleKnownState() => Assert.True(new StartupApprovalWrite(StartupApprovalKind.Run32, "App",
        "020000000000000000000000", "030000000000000000000000").IsValid());
}
