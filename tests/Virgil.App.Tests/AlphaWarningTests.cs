using Virgil.App;
using Xunit;

namespace Virgil.Tests;

public sealed class AlphaWarningTests
{
    [Fact]
    public void Warning_text_is_exact()
    {
        Assert.Equal("Attention, ceci est une version alpha. Vous êtes seul responsable de ce que vous faites. En cas de doute, ne lancez rien. Merci.",
            AlphaWarningWindow.WarningText);
    }

    [Fact]
    public void Every_launch_requires_warning_before_main_window_and_capture_does_not_block()
    {
        var events = new List<string>();
        for (var launch = 0; launch < 2; launch++)
            AlphaWarningWindow.RunStartup(false, () => events.Add("warning"), () => events.Add("main"));
        Assert.Equal(new[] { "warning", "main", "warning", "main" }, events);

        events.Clear();
        AlphaWarningWindow.RunStartup(true,
            () => throw new InvalidOperationException("Capture must not open a modal."),
            () => events.Add("main"));
        Assert.Equal(new[] { "main" }, events);
    }

    [Fact]
    public void Bootstrap_uses_existing_capture_marker_and_defers_main_window_show()
    {
        var source = File.ReadAllText(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src", "Virgil.App", "AppBootstrap.cs"));
        Assert.Contains("AlphaWarningWindow.RunStartup(", source);
        Assert.Contains("Environment.GetEnvironmentVariable(\"VIRGIL_APP_EXE\")", source);
        Assert.Contains("() => new AlphaWarningWindow().ShowDialog()", source);
        Assert.DoesNotContain("Owner = window", source);
        Assert.Contains("() => window.Show()", source);
    }
}
