using System.Xml.Linq;
using Xunit;

namespace Virgil.Tests;

public sealed class ManifestTests
{
    [Fact]
    public void AppManifestDeclaresStandardPrivilegesAndPerMonitorV2()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Virgil.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var app = Path.Combine(directory.FullName, "src", "Virgil.App");
        var manifest = XDocument.Load(Path.Combine(app, "app.manifest"));
        XNamespace assembly = "urn:schemas-microsoft-com:asm.v1";
        XNamespace trust = "urn:schemas-microsoft-com:asm.v3";
        XNamespace compatibility = "urn:schemas-microsoft-com:compatibility.v1";
        XNamespace dpi2005 = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";
        XNamespace dpi2016 = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";
        var identity = Assert.Single(manifest.Descendants(assembly + "assemblyIdentity"));
        Assert.Equal("1.0.0.0", (string?)identity.Attribute("version"));
        Assert.Equal("Virgil.App", (string?)identity.Attribute("name"));
        var execution = Assert.Single(manifest.Descendants(trust + "requestedExecutionLevel"));
        Assert.Equal("asInvoker", (string?)execution.Attribute("level"));
        Assert.Equal("false", (string?)execution.Attribute("uiAccess"));
        Assert.Equal("{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}",
            (string?)Assert.Single(manifest.Descendants(compatibility + "supportedOS")).Attribute("Id"));
        Assert.Equal("true/PM", Assert.Single(manifest.Descendants(dpi2005 + "dpiAware")).Value);
        Assert.Equal("PerMonitorV2", Assert.Single(manifest.Descendants(dpi2016 + "dpiAwareness")).Value);
        var project = XDocument.Load(Path.Combine(app, "Virgil.App.csproj"));
        Assert.Equal("app.manifest", Assert.Single(project.Descendants("ApplicationManifest")).Value);
    }
}
