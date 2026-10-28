using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;
using static Virgil.Tests.TestRepositoryRoot;

namespace Virgil.Tests;

public sealed class InterfaceAccentTests
{
    [Fact]
    public void Interface_labels_preserve_french_accents_in_xaml_resources_and_code()
    {
        string[] forbidden = ["MISES A JOUR", "A REVOIR MANUELLEMENT", "ETAT VIRGIL", "MEMOIRE",
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
        string[] words = [@"systeme", @"memoire", @"reseau", @"executees?", @"terminee", @"termine(?=[.!…]?(?:\s*""|\s*$))", @"proposees?", @"ciblees?", @"annulee?", @"effectuee", @"deja", @"initialise", @"detecte(?:e)?s?", @"desinstall\w*", @"redemarrage", @"parametres", @"previsualisation", @"donnees", @"etre", @"installees", @"utilises", @"recommandees", @"priorites?", @"etat", @"echec", @"operation", @"sequence", @"separee", @"conserve", @"genere", @"selectionnee", @"a verifier", @"mises? a jour"];
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
                : XDocument.Parse(code).Descendants().SelectMany(element =>
                    element.Attributes().Where(attribute => attribute.Name.LocalName is "Text" or "Content" or "Header" or "ToolTip" or "Name")
                        .Select(attribute => attribute.Value)
                        .Concat(element.Name.LocalName is "value" or "String" ? new[] { element.Value } : Array.Empty<string>()))
                    .Where(value => !value.StartsWith("{", StringComparison.Ordinal));
            // Exact protocol identifier bound into ConfirmedPlan hashes, never a displayed label.
            if (Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/') == "Virgil.Core/Updates/UpdateSequenceService.cs")
                literals = literals.Where(literal => literal != "\"updates.sequence\"");
            return literals.Select(literal => literal.Replace(@"\n", " ").Replace(@"\r", " ").Replace(@"\t", " ")).SelectMany(literal => forbidden.Where(label => literal.Contains(label, StringComparison.OrdinalIgnoreCase))
                .Concat(words.Where(word => Regex.IsMatch(literal, @"\b(?:" + word + @")\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
                .Select(label => $"{Path.GetRelativePath(root, path)} : {label} : {literal}"));
        }).ToArray();
        Assert.True(failures.Length == 0, string.Join(Environment.NewLine, failures));
    }
}

