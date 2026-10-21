using Virgil.Core.Releases;
using Xunit;

namespace Virgil.Tests;
public sealed class SemanticVersionTests
{
    [Fact]
    public void OfficialSemVerPrecedenceAndAlphaChannelAreOrdered()
    {
        string[] ordered = ["2.0.0-alpha", "2.0.0-alpha.1", "2.0.0-alpha.2", "2.0.0-alpha.10", "2.0.0-alpha.beta", "2.0.0-beta", "2.0.0-beta.2", "2.0.0-beta.11", "2.0.0-rc.1", "2.0.0", "2.0.1", "2.1.0", "3.0.0"];
        for (var i = 0; i < ordered.Length - 1; i++)
        {
            var a = SemanticVersion.Parse(ordered[i])!;
            var b = SemanticVersion.Parse(ordered[i + 1])!;
            Assert.True(a.CompareTo(b) < 0);
            Assert.True(b.CompareTo(a) > 0);
            Assert.Equal(0, a.CompareTo(SemanticVersion.Parse(ordered[i])));
        }
    }
    [Theory]
    [InlineData("1.0.0+one", "1.0.0+two")]
    [InlineData("1.0.0-alpha+one", "1.0.0-alpha+two")]
    public void MetadataDoesNotAffectPrecedence(string a, string b) => Assert.Equal(0, SemanticVersion.Parse(a)!.CompareTo(SemanticVersion.Parse(b)));
    [Theory]
    [InlineData("01.0.0")][InlineData("1.00.0")][InlineData("1.0.00")]
    [InlineData("1.0")][InlineData("v1.0.0")][InlineData("1.0.0-")]
    [InlineData("1.0.0-alpha.01")][InlineData("1.0.0-alpha..2")]
    [InlineData("1.0.0+build..two")][InlineData("1.0.0\n")]
    [InlineData(" 1.0.0")][InlineData("1.0.0-é")]
    public void InvalidVersionsAreRejected(string version) => Assert.Null(SemanticVersion.Parse(version));
    [Theory]
    [InlineData("v2.0.0-alpha", true)][InlineData("v2.0.0", true)]
    [InlineData("v12.34.56-rc.1", true)][InlineData("V2.0.0", false)]
    [InlineData("v2.0.0+build", false)][InlineData("v02.0.0", false)]
    [InlineData("v2.0.0-alpha.01", false)][InlineData("v2.0.0\n", false)]
    public void DistributionTagsMatchPowerShellRules(string tag, bool valid) => Assert.Equal(valid, SemanticVersion.ParseTag(tag) is not null);
    [Fact]
    public void NumbersHaveNoMachineIntegerLimitAndIdentifiersAreOrdinal()
    {
        Assert.True(SemanticVersion.Parse("999999999999999999999.0.0")!.CompareTo(SemanticVersion.Parse("2.0.0")) > 0);
        Assert.True(SemanticVersion.Parse("2.0.0-999999999999999999999")!.CompareTo(SemanticVersion.Parse("2.0.0-1000000000000000000000")) < 0);
        Assert.True(SemanticVersion.Parse("2.0.0-Beta")!.CompareTo(SemanticVersion.Parse("2.0.0-alpha")) < 0);
    }
}
