using Virgil.Core.Diagnostic;
using Xunit;

namespace Virgil.Tests;

public sealed class ProcessorInstanceOrderingTests
{
    [Fact]
    public void InstancesAreOrderedNumericallyByGroupThenIndex()
    {
        var result = ProcessorInstanceOrdering.Sort(new[] { "0,0", "0,1", "0,10", "0,11", "0,2", "1,0" });
        Assert.True(result.IsAvailable);
        Assert.Equal(new[] { "0,0", "0,1", "0,2", "0,10", "0,11", "1,0" }, result.Value);
        Assert.Equal(new[] { "2,1", "2,10", "10,0" }, ProcessorInstanceOrdering.Sort(new[] { "10,0", "2,10", "2,1" }).Value);
    }
    [Fact]
    public void MachineAndGroupTotalsAreExcluded()
    {
        var result = ProcessorInstanceOrdering.Sort(new[] { "_Total", "0,_Total", "1,_Total", "1,0", "0,2", "0,0" });
        Assert.True(result.IsAvailable);
        Assert.Equal(new[] { "0,0", "0,2", "1,0" }, result.Value);
        Assert.Empty(ProcessorInstanceOrdering.Sort(new[] { "_Total", "0,_Total" }).Value);
    }
    [Fact]
    public void UnparseableInstanceMakesCoreIdentificationUnavailableRatherThanGuessing()
    {
        foreach (var invalid in new[] { "invalid", "0,unknown", "0,-1", "-1,0", "0,1,2", "2147483648,0", "x,_Total" })
        {
            var result = ProcessorInstanceOrdering.Sort(new[] { "0,0", invalid, "0,2" });
            Assert.False(result.IsAvailable);
            Assert.Equal("Indisponible : identité des cœurs PDH non interprétable ou ambiguë", result.Reason);
            Assert.Throws<InvalidOperationException>(() => result.Value);
        }
    }
    [Fact]
    public void DuplicateNumericIdentityIsUnavailable()
    {
        Assert.False(ProcessorInstanceOrdering.Sort(new[] { "0,1", "0,1" }).IsAvailable);
        Assert.False(ProcessorInstanceOrdering.Sort(new[] { "0,1", "00,01" }).IsAvailable);
    }
}
