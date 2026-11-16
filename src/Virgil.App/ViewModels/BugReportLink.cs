using System.Globalization;
using System.Text.RegularExpressions;
using Virgil.Core.Releases;

namespace Virgil.App.ViewModels;

public sealed record BugReportContext(string AppVersion, string WindowsVersion)
{
    public static BugReportContext From(ApplicationVersionService version, Version windows)
    {
        var value = version.Current?.Value;
        var app = value is not null && Regex.IsMatch(value, @"\A[0-9A-Za-z.+-]{1,64}\z", RegexOptions.CultureInvariant)
            ? value : "indisponible";
        var minor = windows.Major == 10 ? 0 : windows.Minor;
        var numbers = string.Create(CultureInfo.InvariantCulture, $"{windows.Major}.{minor}.{windows.Build}");
        var name = windows.Major == 10 ? windows.Build >= 22000 ? "Windows 11" : "Windows 10" : "Windows";
        return new BugReportContext(app, $"{name} ({numbers})");
    }
}

public static class BugReportLink
{
    public const int MaxUrlLength = 2000;
    private const string Prefix = "https://github.com/bassetthomas-design/virgil-2.0/issues/new?labels=bug&title=";
    private const string Shortened = "(Texte raccourci : complétez directement sur GitHub.)";

    public static string Build(BugReportContext context, string screen) => Build(context, screen, MaxUrlLength);

    public static string Build(BugReportContext context, string screen, int maxLength)
    {
        screen = BugReportScreen.From([(screen, true)]);
        var title = Uri.EscapeDataString($"Problème signalé depuis l'écran {screen}");
        var head = Prefix + title + "&body=";
        var body = $"## Informations\n- Version de Virgil : {context.AppVersion}\n- Version de Windows : {context.WindowsVersion}\n- Écran : {screen}\n\n## Ce qui s'est passé\n(Décrivez le problème en quelques mots.)\n\n## Ce qui était attendu\n(Décrivez ce que vous pensiez voir.)\n\n## Étapes pour reproduire\n1.\n2.\n3.\n\nAvant d'envoyer : ne collez ni mot de passe, ni chemin de dossier personnel, ni nom d'ordinateur.";
        var full = head + Uri.EscapeDataString(body);
        if (full.Length <= maxLength) return full;
        var lines = body.Split('\n').ToList();
        while (lines.Count > 0)
        {
            lines.RemoveAt(lines.Count - 1);
            while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            var shortened = lines.Count == 0 ? Shortened : string.Join("\n", lines) + "\n\n" + Shortened;
            var candidate = head + Uri.EscapeDataString(shortened);
            if (candidate.Length <= maxLength) return candidate;
        }
        throw new ArgumentOutOfRangeException(nameof(maxLength));
    }
}
