using System.Globalization;
using Virgil.Core.Status;

namespace Virgil.App.ViewModels;

public sealed record HomePriorityItem(string Id, string Text, string Button, string Destination)
{
    public string Detail { get; init; } = string.Empty;
}

public static class HomePriorityCatalog
{
    private sealed record Entry(string Id, string Phrase, string Button, string Destination, bool Prefix, string Subject);
    private static readonly Entry[] Entries =
    [
        new("disk-", "Le disque {X} est presque plein.", "Ouvrir Nettoyage", "Nettoyage", true, "Espace du disque {X}"),
        new("space.", "Le disque {X} est presque plein.", "Ouvrir Nettoyage", "Nettoyage", true, "Espace du disque {X}"),
        new("cleanup-potential-warning", "Des fichiers temporaires prennent de la place ({valeur}).", "Ouvrir Nettoyage", "Nettoyage", false, "Fichiers temporaires"),
        new("cleanup-potential-attention", "Des fichiers temporaires prennent de la place ({valeur}).", "Ouvrir Nettoyage", "Nettoyage", false, "Fichiers temporaires"),
        new("updates-preview", "Des mises à jour d'applications sont disponibles.", "Ouvrir Mises à jour", "Mises à jour", false, "Mises à jour d'applications"),
        new("updates-sensitive-preview", "Certaines mises à jour touchent des éléments sensibles : à regarder avant d'installer.", "Ouvrir Mises à jour", "Mises à jour", false, "Mises à jour sensibles"),
        new("windows.errors", "Windows a signalé des erreurs ces dernières 24 heures.", "Ouvrir Mises à jour", "Mises à jour", false, "Erreurs Windows"),
        new("device.", "Un périphérique ne fonctionne pas correctement.", "Ouvrir Mises à jour", "Mises à jour", true, "Périphériques"),
        new("interventions-recommended", "Virgil propose une réparation à examiner.", "Ouvrir Réparation", "Réparation", false, "Réparations"),
        new("cpu-usage", "Le processeur est très chargé.", "Ouvrir Ressources", "Ressources", false, "Charge du processeur"),
        new("resources-cpu-sustained", "Le processeur est très chargé.", "Ouvrir Ressources", "Ressources", false, "Charge du processeur"),
        new("memory-usage", "La mémoire est presque pleine.", "Ouvrir Ressources", "Ressources", false, "Mémoire"),
        new("resources-memory-high", "La mémoire est presque pleine.", "Ouvrir Ressources", "Ressources", false, "Mémoire"),
        new("process-memory", "Des programmes consomment beaucoup de ressources.", "Ouvrir Ressources", "Ressources", false, "Consommation des programmes"),
        new("resources-heavy-processes", "Des programmes consomment beaucoup de ressources.", "Ouvrir Ressources", "Ressources", false, "Consommation des programmes"),
        new("applications-unknown", "Des applications sont mal identifiées : vérifiez-les avant toute désinstallation.", "Ouvrir Applications", "Applications", false, "Identification des applications"),
        new("network-active-interface", "Aucune connexion réseau active.", "Ouvrir Réseau", "Réseau", false, "Connexion réseau"),
        new("cpu.hot", "Le processeur chauffe (90 °C ou plus).", "Ouvrir Diagnostic", "Diagnostic", false, "Température du processeur"),
        new("gpu.hot.", "La carte graphique chauffe (90 °C ou plus).", "Ouvrir Diagnostic", "Diagnostic", true, "Température de la carte graphique"),
        new("disk.failure.", "Windows prévoit une panne de disque : sauvegardez vos fichiers maintenant.", "Ouvrir Sauvegarde Windows", "ms-settings:backup", true, "Santé du disque {X}"),
        new("process.", "Un lanceur de jeux consomme beaucoup en arrière-plan.", "Ouvrir Diagnostic", "Diagnostic", true, "Lanceur de jeux"),
        new("partial-error-", "Certaines mesures n'ont pas pu être lues.", "Ouvrir Diagnostic", "Diagnostic", true, "Mesures du scan"),
    ];
    public static HomePriorityItem For(PriorityPoint point, TimeZoneInfo zone)
    {
        var entry = Find(point);
        var item = entry is null ? new HomePriorityItem(point.Id, point.Title, "Ouvrir Diagnostic", "Diagnostic")
            : new HomePriorityItem(point.Id, Expand(entry.Phrase, entry, point)
                .Replace("{valeur}", point.Value, StringComparison.Ordinal), entry.Button, entry.Destination);
        var source = point.Source == PrioritySource.Scan ? "Scan" : "Diagnostic";
        var detail = $"{source} · mesuré à {MeasureTime(point, zone)}";
        if (point.State == PriorityState.ToRecheck) detail += " · à revérifier";
        return item with { Detail = detail };
    }

    public static string ResolvedText(PriorityPoint point, TimeZoneInfo zone)
    {
        var entry = Find(point);
        var subject = entry is null ? point.Title : Expand(entry.Subject, entry, point);
        var source = point.Source == PrioritySource.Scan ? "scan" : "diagnostic";
        return $"{subject} — retour à la normale lors du {source} de {MeasureTime(point, zone)}.";
    }

    private static Entry? Find(PriorityPoint point) =>
        Entries.FirstOrDefault(e => e.Prefix ? point.Id.StartsWith(e.Id, StringComparison.Ordinal) : point.Id == e.Id);

    private static string Expand(string text, Entry entry, PriorityPoint point) =>
        text.Replace("{X}", point.Id[entry.Id.Length..], StringComparison.Ordinal);

    private static string MeasureTime(PriorityPoint point, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(point.MeasuredAt, zone).ToString("H' h 'mm", CultureInfo.GetCultureInfo("fr-FR"));
}
