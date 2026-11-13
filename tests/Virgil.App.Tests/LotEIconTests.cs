using System.Xml.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LotEIconTests
{
    [Fact]
    public void Committed_icon_contains_all_required_sizes()
    {
        var path = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "assets", "virgil.ico");
        Assert.True(File.Exists(path), "assets/virgil.ico doit être commité.");
        using var reader = new BinaryReader(File.OpenRead(path));
        Assert.Equal((ushort)0, reader.ReadUInt16()); Assert.Equal((ushort)1, reader.ReadUInt16());
        var count = reader.ReadUInt16(); var sizes = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            var width = reader.ReadByte(); var height = reader.ReadByte();
            Assert.Equal(width, height); sizes.Add(width == 0 ? 256 : width);
            reader.ReadBytes(6); var length = reader.ReadUInt32(); var offset = reader.ReadUInt32();
            Assert.True(length > 0 && offset + length <= reader.BaseStream.Length);
        }
        foreach (var size in new[] { 16, 32, 48, 64, 128, 256 }) Assert.Contains(size, sizes);
    }

    [Fact]
    public void Executable_window_and_installer_use_the_same_committed_icon()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "src/Virgil.App/Virgil.App.csproj"));
        Assert.EndsWith("assets/virgil.ico", Assert.Single(project.Descendants("ApplicationIcon")).Value.Replace('\\', '/'));
        var window = XDocument.Load(Path.Combine(root, "src/Virgil.App/MainWindow.xaml"));
        Assert.EndsWith("virgil.ico", window.Root?.Attribute("Icon")?.Value);
        var script = File.ReadAllText(Path.Combine(root, "installer/Virgil.iss"));
        Assert.Contains("SetupIconFile=..\\assets\\virgil.ico", script);
        Assert.Contains("UninstallDisplayIcon={app}\\virgil.ico", script);
        Assert.Contains("Source: \"..\\assets\\virgil.ico\"; DestDir: \"{app}\"", script);
        foreach (var shortcut in new[] { "{group}", "{autodesktop}" })
            Assert.Contains("IconFilename: \"{app}\\virgil.ico\"", Assert.Single(script.Split('\n'), line => line.StartsWith("Name: \"" + shortcut, StringComparison.Ordinal)));
        Assert.True(File.Exists(Path.Combine(root, "assets/virgil-visage.svg")));
        Assert.True(File.Exists(Path.Combine(root, "build/Build-Icon.ps1")));
    }

    [Fact]
    public Task Icon_is_rendered_at_native_sizes_on_light_and_dark_backgrounds() => ThemeContrastTests.OnSta(() =>
    {
        using var stream = File.OpenRead(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "assets", "virgil.ico"));
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        foreach (var size in new[] { 16, 32, 256 })
        foreach (var light in new[] { false, true })
        {
            var frame = Assert.Single(decoder.Frames, frame => frame.PixelWidth == size);
            var border = new Border { Background = light ? Brushes.White : new SolidColorBrush(Color.FromRgb(11, 15, 20)),
                Child = new Image { Source = frame, Width = size, Height = size } };
            LotEFaceTests.Capture(border, $"icone-{size}-{(light ? "clair" : "sombre")}", size, size);
        }
        return Task.CompletedTask;
    });
}
