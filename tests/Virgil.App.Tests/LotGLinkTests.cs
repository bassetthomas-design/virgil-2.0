using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Virgil.App.ViewModels;
using Virgil.Core.Releases;
using Xunit;

namespace Virgil.Tests;

public sealed class LotGLinkTests
{
    internal const string Prefix = "https://github.com/bassetthomas-design/virgil-2.0/issues/new?labels=bug&title=";
    internal const string Shortened = "(Texte raccourci : complétez directement sur GitHub.)";
    internal const string Failure = "Le navigateur ne s'est pas ouvert. Copiez ce lien dans votre navigateur pour signaler le problème :";
    internal static readonly string[] Screens = ["Accueil", "Scan", "Nettoyage", "Diagnostic", "Ressources", "Applications", "Mises à jour", "Démarrage", "Réseau", "Réparation", "Rapports"];
    internal static Type Type(string name)
    {
        var type = typeof(LongOperationViewModel).Assembly.GetType("Virgil.App.ViewModels." + name);
        Assert.NotNull(type); return type;
    }
    internal static object Invoke(Type type, string method, params object[] args)
    {
        var member = type.GetMethods().Single(m => m.Name == method && m.GetParameters().Length == args.Length);
        return member.Invoke(null, args)!;
    }
    internal static object Read(object value, string name)
    {
        var property = value.GetType().GetProperty(name); Assert.NotNull(property);
        return property.GetValue(value)!;
    }
    private sealed class Source(string? value) : IApplicationVersionSource
    { public string? ReadInformationalVersion() => value; }
    internal static object Context(string? version = "0.8.0-alpha.8", Version? windows = null) =>
        Invoke(Type("BugReportContext"), "From", new ApplicationVersionService(new Source(version)), windows ?? new Version(10, 0, 22631, 4460));
    internal static string Build(object context, string screen = "Mises à jour", int? max = null) => (string)(max.HasValue
        ? Invoke(Type("BugReportLink"), "Build", context, screen, max.Value)
        : Invoke(Type("BugReportLink"), "Build", context, screen));
    internal static string Body(string version = "0.8.0-alpha.8", string windows = "Windows 11 (10.0.22631)", string screen = "Mises à jour") =>
        $"## Informations\n- Version de Virgil : {version}\n- Version de Windows : {windows}\n- Écran : {screen}\n\n## Ce qui s'est passé\n(Décrivez le problème en quelques mots.)\n\n## Ce qui était attendu\n(Décrivez ce que vous pensiez voir.)\n\n## Étapes pour reproduire\n1.\n2.\n3.\n\nAvant d'envoyer : ne collez ni mot de passe, ni chemin de dossier personnel, ni nom d'ordinateur.";
    internal static string[] Query(string url) => url[(url.IndexOf('?') + 1)..].Split('&');
    internal static string DecodedBody(string url) => Uri.UnescapeDataString(Query(url)[2][5..]);

    [Fact]
    public void Exact_url_title_body_and_encoding()
    {
        var url = Build(Context()); Assert.StartsWith(Prefix, url, StringComparison.Ordinal);
        var parts = Query(url); Assert.Equal(3, parts.Length); Assert.Equal("labels=bug", parts[0]);
        Assert.StartsWith("title=", parts[1], StringComparison.Ordinal); Assert.StartsWith("body=", parts[2], StringComparison.Ordinal);
        Assert.Equal("Problème signalé depuis l'écran Mises à jour", Uri.UnescapeDataString(parts[1][6..]));
        Assert.Equal(Body(), DecodedBody(url));
        Assert.Equal(Prefix + Uri.EscapeDataString("Problème signalé depuis l'écran Mises à jour") + "&body=" + Uri.EscapeDataString(Body()), url);
        foreach (var part in parts)
        foreach (var forbidden in new[] { " ", "+", "\n", "#", "&" }) Assert.DoesNotContain(forbidden, part, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(10, 0, 19045, 1, "Windows 10 (10.0.19045)")]
    [InlineData(10, 0, 22000, 0, "Windows 11 (10.0.22000)")]
    [InlineData(6, 3, 9600, 0, "Windows (6.3.9600)")]
    public void Windows_format_omits_revision(int major, int minor, int build, int revision, string expected) =>
        Assert.Equal(expected, Read(Context(windows: new Version(major, minor, build, revision)), "WindowsVersion"));
    [Theory]
    [InlineData(null, "indisponible")]
    [InlineData(@"C:\Users\SENTINELLE-UTILISATEUR-7Q4\x", "indisponible")]
    [InlineData("0.8.0-alpha.8", "0.8.0-alpha.8")]
    public void Version_uses_real_service_and_filters(string? input, string expected) => Assert.Equal(expected, Read(Context(input), "AppVersion"));
    [Fact]
    public void Long_version_is_unavailable() => Assert.Equal("indisponible", Read(Context("0.8.0-" + new string('a', 59)), "AppVersion"));
    [Fact]
    public void Screen_list_matches_real_navigation_and_requires_exactly_one_known_active()
    {
        var type = Type("BugReportScreen");
        var known = Assert.IsAssignableFrom<IReadOnlyList<string>>(type.GetProperty("Known")!.GetValue(null));
        Assert.Equal(Screens, known);
        var xml = XDocument.Load(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src/Virgil.App/MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var panel = xml.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "NavigationPanel");
        Assert.Equal(known, panel.Elements().Where(e => e.Name.LocalName == "Button").Select(e => (string)e.Attribute("AutomationProperties.Name")!));
        foreach (var screen in Screens) Assert.Equal(screen, Invoke(type, "From", Screens.Select(s => (s, s == screen)).ToArray()));
        Assert.Equal("non déterminé", Invoke(type, "From", Array.Empty<(string, bool)>()));
        Assert.Equal("non déterminé", Invoke(type, "From", new[] { ("Accueil", true), ("Scan", true) }));
        Assert.Equal("non déterminé", Invoke(type, "From", new[] { ("SENTINELLE-ECRAN-8Z9", true) }));
        Assert.Equal("non déterminé", Invoke(type, "From", new[] { ("accueil", true) }));
    }
    [Fact]
    public void No_identifying_values_in_decoded_url()
    {
        var decoded = Uri.UnescapeDataString(Build(Context()));
        foreach (var value in new[] { Environment.UserName, Environment.MachineName, Environment.UserDomainName, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SENTINELLE-UTILISATEUR-7Q4" })
            if (value.Length >= 4) Assert.DoesNotContain(value, decoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"(?<![\d.])\d{1,3}(?:\.\d{1,3}){3}(?![\d.])", decoded);
        Assert.DoesNotMatch(@"\b[0-9A-Fa-f]{2}(?:[:-][0-9A-Fa-f]{2}){5}\b", decoded);
        Assert.DoesNotContain(":\\", decoded, StringComparison.Ordinal); Assert.DoesNotContain("\\\\", decoded, StringComparison.Ordinal);
    }
    [Fact]
    public void Truncation_preserves_whole_lines_title_and_labels()
    {
        var type = Type("BugReportLink"); Assert.Equal(2000, type.GetField("MaxUrlLength")!.GetRawConstantValue());
        var context = Context("0.8.0-" + new string('a', 58));
        Assert.Equal(64, ((string)Read(context, "AppVersion")).Length);
        var full = Build(context, "non déterminé"); Assert.True(full.Length <= 2000);
        Assert.Equal(full, Build(context, "non déterminé", 2000)); Assert.Equal(full, Build(context, "non déterminé", full.Length));
        foreach (var bound in new[] { full.Length - 1, 400 })
        {
            var actual = Build(context, "non déterminé", bound); Assert.True(actual.Length <= bound); Assert.NotEqual(full, actual);
            Assert.Equal(Query(full).Take(2), Query(actual).Take(2)); Assert.DoesNotMatch("%(?![0-9A-F]{2})", actual);
            var lines = DecodedBody(full).Split('\n').ToList(); string expected;
            do
            {
                lines.RemoveAt(lines.Count - 1); while (lines.Count > 0 && lines[^1] == "") lines.RemoveAt(lines.Count - 1);
                expected = lines.Count == 0 ? Shortened : string.Join("\n", lines) + "\n\n" + Shortened;
            } while ((Prefix + Query(full)[1][6..] + "&body=" + Uri.EscapeDataString(expected)).Length > bound);
            Assert.Equal(expected, DecodedBody(actual));
        }
        var error = Assert.Throws<TargetInvocationException>(() => Build(context, "non déterminé", 1));
        Assert.IsType<ArgumentOutOfRangeException>(error.InnerException);
    }
}
