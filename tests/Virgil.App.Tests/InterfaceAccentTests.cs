using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;
using static Virgil.Tests.TestRepositoryRoot;

namespace Virgil.Tests;

public sealed class InterfaceAccentTests
{
    private static readonly string[] Forbidden = ["MISES A JOUR", "A REVOIR MANUELLEMENT", "ETAT VIRGIL", "MEMOIRE",
            "DISQUE SYSTEME", "PRIORITES", "NON ANALYSE", "Systeme pret", "securise",
            "MARQUER A REVOIR", "RESSOURCES SYSTEME", "AGENT TACTIQUE SYSTEME", "LIBERER", "Mises a jour", "SYSTEME PRET", "ETAT GLOBAL", "SELECTION DU PROTOCOLE",
            "ACTIONS RECOMMANDEES", "HELPER ELEVE", "COMMANDE AUTORISEE", "CONFIDENTIALITE",
            "ACTIONS PROPOSEES", "ACTIONS EXECUTEES", "ACTIONS PASSEES", "DETAILS TECHNIQUES", "NON TOUCHE",
            "Previsualisation terminee", "zones analysees", "potentiellement recuperables",
            "elements personnels ou ambigus a revoir, aucune suppression proposee",
            "Aucune action executee", "Reseau :", "Ressources systeme :", "ciblees", "Interventions ciblées pretes", "Memoire inactive :",
            "Diagnostic interventions lance.", "Diagnostic annule.", "Intervention passee.",
            "Analyse ressources lancee.", "Analyse ressources annulee.",
            "Analyse annulee. Aucune action exécutée.", "Processus ignore."];
    private static readonly string[] ForbiddenWords = [@"systeme", @"memoire", @"reseau", @"executees?", @"terminee", @"termine(?=[.!…]?(?:\s*""|\s*$))", @"proposees?", @"ciblees?", @"annulee?", @"effectuee", @"deja", @"initialise", @"detecte(?:e)?s?", @"desinstall\w*", @"redemarrage", @"parametres", @"previsualisation", @"donnees", @"etre", @"installees", @"utilises", @"recommandees", @"priorites?", @"etat", @"echec", @"operation", @"sequence", @"separee", @"conserve", @"genere", @"selectionnee", @"a verifier", @"mises? a jour"];

    [Fact]
    public void Interface_labels_preserve_french_accents_in_xaml_resources_and_code()
    {
        var root = Path.Combine(FindRepositoryRoot(), "src");
        var sources = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => new[] { "Virgil.App", "Virgil.Core", "Virgil.ElevatedHelper" }.Contains(Path.GetRelativePath(root, p).Split(Path.DirectorySeparatorChar)[0]))
            .Where(p => Path.GetExtension(p) is ".xaml" or ".resx" or ".cs")
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"));
        // Only literal strings and XAML text are UI candidates: C# identifiers and comments are not labels.
        var failures = sources.SelectMany(path =>
        {
            var code = File.ReadAllText(path);
            var literals = Path.GetExtension(path) == ".cs"
                ? Regex.Matches(code, @"""(?:\\.|[^""\\])*""").Select(match => match.Value)
                : XmlLabels(code);
            // Exact protocol identifier bound into ConfirmedPlan hashes, never a displayed label.
            if (Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/') == "Virgil.Core/Updates/UpdateSequenceService.cs")
                literals = literals.Where(literal => literal != "\"updates.sequence\"");
            return literals.SelectMany(literal => Violations(literal)
                .Select(label => $"{Path.GetRelativePath(root, path)} : {label} : {literal}"));
        }).ToArray();
        Assert.True(failures.Length == 0, string.Join(Environment.NewLine, failures));
    }
    private static IEnumerable<string> XmlLabels(string code) => XDocument.Parse(code).Descendants().SelectMany(element =>
        // Binding expressions, x:Name and other technical XML attributes are identifiers, not labels.
        element.Attributes().Where(attribute => attribute.Name.NamespaceName.Length == 0 &&
                attribute.Name.LocalName is "Text" or "Content" or "Header" or "ToolTip" or "Title" or "AutomationProperties.Name" or "AutomationProperties.HelpText")
            .Select(attribute => attribute.Value)
            .Concat(element.Nodes().OfType<XText>().Select(text => text.Value)))
        .Where(value => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("{", StringComparison.Ordinal));

    private static IEnumerable<string> Violations(string literal)
    {
        literal = literal.Replace(@"\n", " ").Replace(@"\r", " ").Replace(@"\t", " ");
        return Forbidden.Where(label => literal.Contains(label, StringComparison.OrdinalIgnoreCase))
            .Concat(ForbiddenWords.Where(word => Regex.IsMatch(literal, @"\b(?:" + word + @")\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));
    }

    [Theory]
    [InlineData("<TextBlock>systeme</TextBlock>")]
    [InlineData("<Button AutomationProperties.Name=\"systeme\" />")]
    [InlineData("<data><value>systeme</value></data>")]
    public void XmlTextAccessibilityAndResourceLabelsAreChecked(string xml) =>
        Assert.Contains(XmlLabels(xml).SelectMany(Violations), label => label == "systeme");

    // Verbatim messages verified with git show 4af1cce:<path> (App and Core).
    [Theory]
    [InlineData("Protocole d'analyse initialise.", "initialise")]
    [InlineData("Analyse systeme en cours.", "systeme")]
    [InlineData("Scan initialise.", "initialise")]
    [InlineData("Non analyse", "non analyse")]
    [InlineData("NON ANALYSE", "non analyse")]
    public void Baseline4af1cceCommunicationAndCoreLabelsAreRejected(string literal, string word) =>
        Assert.Contains(Violations(literal), label => string.Equals(label, word, StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData("Protocole d'analyse initialisé.", "initialise")]
    [InlineData("Analyse système en cours.", "systeme")]
    [InlineData("opération", "operation")]
    [InlineData("operational", "operation")]
    [InlineData("sequences", "sequence")]
    public void AccentedWordsAndWordBoundariesArePreserved(string literal, string word) =>
        Assert.DoesNotContain(Violations(literal), label => string.Equals(label, word, StringComparison.OrdinalIgnoreCase));

}

