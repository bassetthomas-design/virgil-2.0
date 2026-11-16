using System.Reflection;
using Xunit;
using static Virgil.Tests.LotGLinkTests;

namespace Virgil.Tests;

public sealed class LotHLinkTests
{
    [Fact]
    public void Independently_fixed_truncation_boundaries_preserve_title_labels_and_whole_body()
    {
        var context = Context(); var full = Build(context);
        Assert.Equal(772, full.Length); Assert.Equal(full, Build(context, "Mises à jour", 2000));
        Assert.Equal(full, Build(context, "Mises à jour", 772));
        var cases = new[]
        {
            (771, 709, "## Informations\n- Version de Virgil : 0.8.0-alpha.8\n- Version de Windows : Windows 11 (10.0.22631)\n- Écran : Mises à jour\n\n## Ce qui s'est passé\n(Décrivez le problème en quelques mots.)\n\n## Ce qui était attendu\n(Décrivez ce que vous pensiez voir.)\n\n## Étapes pour reproduire\n1.\n2.\n3.\n\n(Texte raccourci : complétez directement sur GitHub.)"),
            (400, 384, "## Informations\n- Version de Virgil : 0.8.0-alpha.8\n- Version de Windows : Windows 11 (10.0.22631)\n\n(Texte raccourci : complétez directement sur GitHub.)"),
            (238, 238, "(Texte raccourci : complétez directement sur GitHub.)")
        };
        foreach (var (bound, length, body) in cases)
        {
            var url = Build(context, "Mises à jour", bound); Assert.Equal(length, url.Length);
            Assert.Equal(body, DecodedBody(url));
            Assert.Equal("labels=bug", Query(url)[0]); Assert.Equal(Query(full)[1], Query(url)[1]);
            Assert.Equal("Problème signalé depuis l'écran Mises à jour", Uri.UnescapeDataString(Query(url)[1][6..]));
            Assert.DoesNotMatch(@"^\n|\n$", DecodedBody(url));
        }
        var error = Assert.Throws<TargetInvocationException>(() => Build(context, "Mises à jour", 237));
        Assert.IsType<ArgumentOutOfRangeException>(error.InnerException);
    }
}
