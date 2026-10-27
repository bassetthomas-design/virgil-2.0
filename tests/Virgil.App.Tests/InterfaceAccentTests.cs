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
            "ACTIONS PROPOSEES", "ACTIONS EXECUTEES", "ACTIONS PASSEES", "DETAILS TECHNIQUES", "NON TOUCHE"];
        var root = Path.Combine(FindRepositoryRoot(), "src", "Virgil.App");
        var sources = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => Path.GetExtension(p) is ".xaml" or ".resx" or ".cs")
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"));
        var failures = sources.SelectMany(path => forbidden.Where(label => File.ReadAllText(path).Contains(label, StringComparison.Ordinal))
            .Select(label => $"{Path.GetRelativePath(root, path)} : {label}")).ToArray();
        Assert.True(failures.Length == 0, string.Join(Environment.NewLine, failures));
    }
}
