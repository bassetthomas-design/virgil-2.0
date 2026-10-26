using System.Xml.Linq;
using Xunit;
using static Virgil.Tests.TestRepositoryRoot;

namespace Virgil.Tests;

public sealed class ThemeReadabilityTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("CheckBox")]
    [InlineData("RadioButton")]
    public void Selection_controls_use_implicit_theme_styles_with_visible_states(string control)
    {
        var theme = Load("Themes", "TacticalHudTheme.xaml");
        var style = Assert.Single(theme.Root!.Elements(Presentation + "Style"),
            element => (string?)element.Attribute("TargetType") == $"{{x:Type {control}}}");
        Assert.Null(style.Attribute(Xaml + "Key"));
        Assert.Contains(style.Elements(Presentation + "Setter"), element =>
            (string?)element.Attribute("Property") == "Foreground" &&
            (string?)element.Attribute("Value") == "{DynamicResource App.TextPrimaryBrush}");
        Assert.Contains(style.Elements(Presentation + "Setter"), element =>
            (string?)element.Attribute("Property") == "FocusVisualStyle");
        var triggers = style.Descendants(Presentation + "Trigger").ToArray();
        Assert.Contains(triggers, element => (string?)element.Attribute("Property") == "IsChecked");
        Assert.Contains(triggers, element => (string?)element.Attribute("Property") == "IsKeyboardFocused");
        Assert.Contains(triggers, element => (string?)element.Attribute("Property") == "IsEnabled" &&
            (string?)element.Attribute("Value") == "False");
        if (control == "CheckBox")
            Assert.Contains(triggers, element => (string?)element.Attribute("Property") == "IsChecked" &&
                (string?)element.Attribute("Value") == "{x:Null}");
    }

    [Fact]
    public void Release_header_uses_theme_text_buttons_and_default_checkbox_style()
    {
        var view = Load("Views", "VirgilReleaseView.xaml");
        Assert.All(view.Descendants(Presentation + "TextBlock"), element =>
            Assert.Equal("{StaticResource VirgilHudSecondaryText}", (string?)element.Attribute("Style")));
        Assert.All(view.Descendants(Presentation + "Button"), element =>
            Assert.Equal("{StaticResource VirgilSecondaryButton}", (string?)element.Attribute("Style")));
        var checkBox = Assert.Single(view.Descendants(Presentation + "CheckBox"));
        Assert.Null(checkBox.Attribute("Style"));
        Assert.Null(checkBox.Attribute("Foreground"));
    }

    private static XDocument Load(string directory, string file) =>
        XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "Virgil.App", directory, file));
}
