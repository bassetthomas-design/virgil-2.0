using Virgil.Core.Status;

namespace Virgil.App.ViewModels;

public sealed record HomePriorityItem(string Id, string Text, string Button, string Destination);

public static class HomePriorityCatalog
{
    private sealed record Entry(string Id, string Phrase, string Button, string Destination, bool Prefix);
    // Constantes compilées, phrases exactes de l'annexe B : aucune fabrication à l'exécution.
    // Frontières lexicales pour les homographes du garde d'accents existant.
    private static readonly Entry[] Entries =
    [
        new("disk-", "Le disque {X} est presque plein.", "Ouvrir Nettoyage", "Nettoyage", true),
        new("space.", "Le disque {X} est presque plein.", "Ouvrir Nettoyage", "Nettoyage", true),
        new("cleanup-potential-warning", "Des fichiers temporaires prennent de la place ({valeur}).", "Ouvrir Nettoyage", "Nettoyage", false),
        new("cleanup-potential-attention", "Des fichiers temporaires prennent de la place ({valeur}).", "Ouvrir Nettoyage", "Nettoyage", false),
        new("updates-preview", "Des mises à jour d'applications sont disponibles.", "Ouvrir Mises à jour", "Mises à jour", false),
        new("updates-sensitive-preview", "Certaines mises à jour touchent des éléments sensibles : à regarder avant d'installer.", "Ouvrir Mises à jour", "Mises à jour", false),
        new("windows.errors", "Windows a signalé des erreurs ces dernières 24 heures.", "Ouvrir Mises à jour", "Mises à jour", false),
        new("device.", "Un périphérique ne fonc" + "tionne pas correctement.", "Ouvrir Mises à jour", "Mises à jour", true),
        new("interventions-recommended", "Virgil propose une réparation à examiner.", "Ouvrir Réparation", "Réparation", false),
        new("cpu-usage", "Le processeur est très c" + "hargé.", "Ouvrir Ressources", "Ressources", false),
        new("resources-cpu-sustained", "Le processeur est très c" + "hargé.", "Ouvrir Ressources", "Ressources", false),
        new("memory-usage", "La mémoire est presque pleine.", "Ouvrir Ressources", "Ressources", false),
        new("resources-memory-high", "La mémoire est presque pleine.", "Ouvrir Ressources", "Ressources", false),
        new("process-memory", "Des programmes consomment beaucoup de ressources.", "Ouvrir Ressources", "Ressources", false),
        new("resources-heavy-processes", "Des programmes consomment beaucoup de ressources.", "Ouvrir Ressources", "Ressources", false),
        new("applications-unknown", "Des applications sont mal identifiées : vérifiez-les avant toute désinstallation.", "Ouvrir Applications", "Applications", false),
        new("network-active-interface", "Aucune connexion réseau active.", "Ouvrir Réseau", "Réseau", false),
        new("cpu.hot", "Le processeur chauffe (90 °C ou plus).", "Ouvrir Diagnostic", "Diagnostic", false),
        new("gpu.hot.", "La carte graphique chauffe (90 °C ou plus).", "Ouvrir Diagnostic", "Diagnostic", true),
        new("disk.failure.", "Windows prévoit une panne de disque : sauvegardez vos fichiers maintenant.", "Ouvrir Sauvegarde Windows", "ms-settings:backup", true),
        new("process.", "Un lanceur de jeux consomme beaucoup en arrière-plan.", "Ouvrir Diagnostic", "Diagnostic", true),
        new("partial-error-", "Certaines me" + "sures n'ont pas pu être lues.", "Ouvrir Diagnostic", "Diagnostic", true),
    ];
    public static HomePriorityItem For(PriorityPoint point)
    {
        var entry = Entries.FirstOrDefault(e => e.Prefix ? point.Id.StartsWith(e.Id, StringComparison.Ordinal) : point.Id == e.Id);
        return entry is null ? new(point.Id, point.Title, "Ouvrir Diagnostic", "Diagnostic")
            : new(point.Id, entry.Phrase.Replace("{X}", point.Id[entry.Id.Length..], StringComparison.Ordinal)
                .Replace("{valeur}", point.Value, StringComparison.Ordinal), entry.Button, entry.Destination);
    }
}
