using Virgil.Contracts.Diagnostic;

namespace Virgil.Core.Diagnostic;

public static class ProcessExplanationCatalog
{
    private static readonly Dictionary<string, string> Entries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["System"] = "Noyau et pilotes de Windows.",
        ["svchost"] = "Hôte de services Windows ; plusieurs instances sont habituelles.",
        ["explorer"] = "Bureau, barre des tâches et explorateur de fichiers Windows.",
        ["dwm"] = "Composition et affichage des fenêtres Windows.",
        ["csrss"] = "Composant essentiel des sessions Windows.",
        ["smss"] = "Gestion des sessions Windows.",
        ["wininit"] = "Initialisation des services Windows.",
        ["winlogon"] = "Ouverture et verrouillage de la session Windows.",
        ["lsass"] = "Authentification et sécurité de Windows.",
        ["services"] = "Gestionnaire des services Windows.",
        ["MsMpEng"] = "Moteur de protection antivirus Microsoft Defender.",
        ["SearchIndexer"] = "Indexation Windows pour accélérer la recherche.",
        ["RuntimeBroker"] = "Gestion des autorisations des applications Windows.",
        ["chrome"] = "Navigateur Google Chrome ; plusieurs processus sont habituels.",
        ["msedge"] = "Navigateur Microsoft Edge ; plusieurs processus sont habituels.",
        ["firefox"] = "Navigateur Mozilla Firefox.",
        ["brave"] = "Navigateur Brave.",
        ["steam"] = "Lanceur et bibliothèque de jeux Steam.",
        ["steamwebhelper"] = "Interface web du lanceur Steam.",
        ["EpicGamesLauncher"] = "Lanceur et bibliothèque Epic Games.",
        ["EpicWebHelper"] = "Interface web du lanceur Epic Games.",
        ["Battle.net"] = "Lanceur de jeux Battle.net.",
        ["Agent"] = "Programme nommé Agent ; son rôle dépend de son éditeur et de son emplacement.",
        ["Discord"] = "Messagerie et appels Discord.",
        ["nvcontainer"] = "Conteneur de services et réglages du pilote NVIDIA.",
        ["NVIDIA Share"] = "Fonctions de partage et de capture NVIDIA.",
        ["RadeonSoftware"] = "Interface de réglage du pilote graphique AMD.",
        ["AMDRSServ"] = "Service de capture et de diffusion AMD Radeon.",
        ["igfxEM"] = "Composant de gestion de l'affichage Intel.",
        ["igfxCUIService"] = "Service de l'interface graphique Intel.",
        ["OneDrive"] = "Synchronisation de fichiers Microsoft OneDrive."
    };

    public static string Explain(string name, DiagnosticValue<string> publisher, DiagnosticValue<string> path)
    {
        var key = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        if (Entries.TryGetValue(key, out var explanation)) return explanation + " Identification par nom, éditeur et emplacement à vérifier.";
        if (publisher.IsAvailable && path.IsAvailable && !string.IsNullOrWhiteSpace(publisher.Value))
        {
            var index = Math.Max(path.Value.LastIndexOf('\\'), path.Value.LastIndexOf('/'));
            if (index > 0) return $"Programme de {publisher.Value}, situé dans {path.Value[..index]}";
        }
        return "Programme inconnu : à vérifier";
    }
}
