using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public static class RepairCheckSummary
{
    public static string Build(InterventionExecutionResult result)
    {
        var checks = new List<string>();
        var unknown = 0;
        var findings = 0;
        var controlCount = 0;
        var catalog = new InterventionCatalog();
        foreach (var rawLine in result.SummaryOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("VIRGIL|", StringComparison.Ordinal)) continue;
            var fields = line.Split('|');
            if (fields.Length != 4) continue;
            controlCount++;
            var label = Label(fields[1]);
            var detail = fields[3];
            var sentence = detail.EndsWith(".", StringComparison.Ordinal) ? detail : detail + ".";
            switch (fields[2])
            {
                case "OK":
                case "INFO":
                    checks.Add(label + " : terminé, aucune anomalie.");
                    break;
                case "FINDING":
                    findings++;
                    var title = InterventionSafety.RepairsFor(line).Select(id => catalog.Get(id).Title).FirstOrDefault();
                    checks.Add(label + " : terminé, constat : " + sentence
                        + (title is null ? string.Empty : " Action proposée : " + title + "."));
                    break;
                case "UNKNOWN":
                    unknown++;
                    checks.Add(label + " : échec, " + sentence);
                    break;
            }
        }

        var summary = result.Status == InterventionStatus.Cancelled
            ? "Vérification annulée : les contrôles suivants n'ont pas été lancés."
            : result.Status == InterventionStatus.Failed && controlCount == 0
                ? "Vérification en échec : " + (result.ReadableError ?? "cause inconnue.")
                : unknown > 0
                    ? $"Vérification partielle : {unknown} contrôle(s) en échec, {findings} constat(s)."
                    : findings > 0
                        ? $"Vérification terminée : {findings} constat(s)."
                        : "Vérification terminée : aucune anomalie.";
        checks.Insert(0, summary);
        checks.Add("Journal brut : « Détails techniques » et rapport.");
        return string.Join("\n", checks);
    }

    private static string Label(string key) => key switch
    {
        "SFC" => "Fichiers système (SFC)",
        "DISM" => "Image Windows (DISM)",
        "CHKDSK" => "Disque système (CHKDSK)",
        "Services" => "Contrôle des services",
        "wuauserv" => "Service Windows Update",
        "AppXSvc" => "Service de déploiement des applications (AppXSvc)",
        "ClipSVC" => "Service de licences du Microsoft Store (ClipSVC)",
        "Audiosrv" => "Service audio Windows",
        "AudioEndpointBuilder" => "Générateur de points de terminaison audio",
        "AudioEndpoint" => "Sorties audio",
        "DefaultAudioEndpoint" => "Sortie audio par défaut",
        "Spooler" => "Spouleur d'impression",
        "PrintQueue" => "File d'impression",
        "Reboot" => "Redémarrage de Windows",
        "Gateway" => "Passerelle réseau",
        "DNS" => "Résolution DNS",
        "restore-point" => "Point de restauration",
        _ => key
    };
}
