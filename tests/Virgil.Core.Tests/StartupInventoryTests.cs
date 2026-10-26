using Virgil.Infrastructure.Windows.Startup;
using Xunit;

namespace Virgil.Core.Tests;

public sealed class StartupInventoryTests
{
    [Theory]
    [InlineData(2, "Activé")]
    [InlineData(6, "Activé")]
    [InlineData(3, "Désactivé")]
    [InlineData(7, "Désactivé")]
    [InlineData(99, "État inconnu")]
    public void Approval_state_distinguishes_enabled_disabled_and_unknown(byte state, string expected)
    {
        var value = new byte[12];
        value[0] = state;
        Assert.Equal(expected, WindowsStartupInventorySource.State(value));
    }
    [Fact]
    public void Missing_or_truncated_approval_is_not_reported_enabled()
    {
        Assert.Equal("État inconnu", WindowsStartupInventorySource.State(null));
        Assert.Equal("État inconnu", WindowsStartupInventorySource.State(new byte[] { 2 }));
    }
}
