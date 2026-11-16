namespace Virgil.App.ViewModels;

public static class BugReportScreen
{
    public static IReadOnlyList<string> Known { get; } = Array.AsReadOnly(new[]
    {
        "Accueil", "Scan", "Nettoyage", "Diagnostic", "Ressources", "Applications",
        "Mises à jour", "Démarrage", "Réseau", "Réparation", "Rapports"
    });

    public static string From(IEnumerable<(string Name, bool IsActive)> buttons)
    {
        var active = buttons.Where(b => b.IsActive).Take(2).ToArray();
        return active.Length == 1 && Known.Contains(active[0].Name, StringComparer.Ordinal)
            ? active[0].Name : "non déterminé";
    }
}
