using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public sealed class InterventionCatalog : IInterventionCatalog
{
    private static readonly IReadOnlyList<InterventionDefinition> Definitions = new[]
    {
        Define(
            InterventionId.RestartExplorer,
            "Relancer l'Explorateur Windows",
            InterventionCategory.Interface,
            "Relance uniquement explorer.exe.",
            "La barre des tâches et le bureau disparaissent quelques secondes puis reviennent.",
            "Les applications ouvertes ne sont pas fermées.",
            InterventionRiskLevel.Low,
            admin: false,
            reboot: false,
            "Quelques secondes",
            "Disponible si Windows peut lancer explorer.exe.",
            1,
            interruptible: true,
            Commands(false, "explorer.exe")),
        Define(
            InterventionId.FlushDns,
            "Vider le cache DNS",
            InterventionCategory.Network,
            "Supprime le cache de résolution DNS local.",
            "Force Windows à redemander les adresses DNS lors des prochaines connexions.",
            "Ne modifie ni les serveurs DNS, ni le routeur, ni les réseaux Wi-Fi.",
            InterventionRiskLevel.Low,
            admin: true,
            reboot: false,
            "Moins d'une minute",
            "Disponible si ipconfig.exe est présent.",
            2,
            interruptible: true,
            Commands(true, "ipconfig.exe", "/flushdns")),
        Define(
            InterventionId.RenewIp,
            "Renouveler la configuration IP",
            InterventionCategory.Network,
            "Libère puis renouvelle les baux DHCP des interfaces actives compatibles.",
            "Peut restaurer une configuration IP obtenue par DHCP.",
            "Ne modifie pas une configuration IP statique volontaire.",
            InterventionRiskLevel.Moderate,
            admin: true,
            reboot: false,
            "Une à deux minutes",
            "Disponible seulement si une interface DHCP active non VPN est détectée.",
            3,
            interruptible: false,
            Commands(true, "ipconfig.exe", "/release", "&&", "ipconfig.exe", "/renew")),
        Define(
            InterventionId.SfcScan,
            "Analyser les fichiers système avec SFC",
            InterventionCategory.System,
            "Exécute une vérification des fichiers système protégés.",
            "Windows peut réparer certains fichiers système protégés.",
            "Ne supprime pas les fichiers personnels.",
            InterventionRiskLevel.Moderate,
            admin: true,
            reboot: true,
            "Plusieurs minutes",
            "Disponible si sfc.exe est présent.",
            4,
            interruptible: false,
            Commands(true, "sfc.exe", "/scannow")),
        Define(
            InterventionId.DismScanHealth,
            "Analyser l'image Windows avec DISM",
            InterventionCategory.System,
            "Exécute DISM ScanHealth en diagnostic approfondi.",
            "Détecte des corruptions possibles de l'image Windows.",
            "Ne lance pas RestoreHealth automatiquement.",
            InterventionRiskLevel.Moderate,
            admin: true,
            reboot: false,
            "Plusieurs minutes",
            "Disponible si dism.exe est présent.",
            5,
            interruptible: false,
            Commands(true, "dism.exe", "/Online", "/Cleanup-Image", "/ScanHealth")),
        Define(
            InterventionId.DismRestoreHealth,
            "Réparer l'image Windows avec DISM",
            InterventionCategory.System,
            "Exécute DISM RestoreHealth sans source externe personnalisée.",
            "Windows peut utiliser Windows Update pour réparer l'image.",
            "Ne supprime pas les fichiers personnels et n'utilise pas /ResetBase.",
            InterventionRiskLevel.Sensitive,
            admin: true,
            reboot: true,
            "Long",
            "Disponible si dism.exe est présent.",
            6,
            interruptible: false,
            Commands(true, "dism.exe", "/Online", "/Cleanup-Image", "/RestoreHealth")),
        Define(
            InterventionId.ResetWinsock,
            "Réinitialiser Winsock",
            InterventionCategory.Network,
            "Réinitialise la pile Winsock.",
            "Peut résoudre certains problèmes réseau bas niveau.",
            "Peut affecter des configurations réseau personnalisées.",
            InterventionRiskLevel.Sensitive,
            admin: true,
            reboot: true,
            "Moins d'une minute, puis redémarrage manuel probable",
            "Disponible si netsh.exe est présent.",
            7,
            interruptible: false,
            Commands(true, "netsh.exe", "winsock", "reset")),
        Define(
            InterventionId.ResetTcpIp,
            "Réinitialiser TCP/IP",
            InterventionCategory.Network,
            "Réinitialise certains paramètres TCP/IP.",
            "Peut restaurer une pile IP endommagée.",
            "Peut affecter des paramètres réseau personnalisés.",
            InterventionRiskLevel.Sensitive,
            admin: true,
            reboot: true,
            "Moins d'une minute, puis redémarrage manuel probable",
            "Disponible si netsh.exe est présent.",
            8,
            interruptible: false,
            Commands(true, "netsh.exe", "int", "ip", "reset")),
        Define(
            InterventionId.ChkdskOnlineScan,
            "Analyser le disque système avec CHKDSK",
            InterventionCategory.Storage,
            "Exécute CHKDSK en ligne sur le disque système avec /scan uniquement.",
            "Affiche le résultat de l'analyse sans planifier de réparation au redémarrage.",
            "N'utilise jamais /f, /r ou /x dans cette preview.",
            InterventionRiskLevel.Moderate,
            admin: true,
            reboot: false,
            "Plusieurs minutes",
            "Disponible si chkdsk.exe est présent et si le disque système est valide.",
            9,
            interruptible: false,
            Commands(true, "chkdsk.exe", "<system-drive>", "/scan"))
    }.Concat(new[]
    {
        ReadOnly(InterventionId.VerifyHealth, "Vérifier Windows et les services", "virgil-services", 0) with
        {
            CommandPreviews = new[]
            {
                new InterventionCommandPreview { Executable = "sfc.exe", Arguments = ["/verifyonly"], RunsElevated = true },
                new InterventionCommandPreview { Executable = "dism.exe", Arguments = ["/Online", "/Cleanup-Image", "/CheckHealth", "/English"], RunsElevated = true },
                new InterventionCommandPreview { Executable = "chkdsk.exe", Arguments = ["<system-drive>", "/scan"], RunsElevated = true },
                new InterventionCommandPreview { Executable = "virgil-services", RunsElevated = true }
            }
        },
        ReadOnly(InterventionId.SfcVerifyOnly, "Vérifier les fichiers système sans réparation", "sfc.exe", 10, "/verifyonly"),
        ReadOnly(InterventionId.DismCheckHealth, "Vérifier l’image Windows", "dism.exe", 11, "/Online", "/Cleanup-Image", "/CheckHealth", "/English"),
        ReadOnly(InterventionId.CheckServices, "Vérifier les services et les connexions", "virgil-services", 12),
        ReadOnly(InterventionId.CreateRestorePoint, "Créer et vérifier un point de restauration", "virgil-restore-point", 13),
        Repair(InterventionId.RestartWindowsUpdate, "Redémarrer le service Windows Update", "virgil-update", 14),
        Repair(InterventionId.RestartAudio, "Redémarrer les services audio", "virgil-audio", 15),
        Repair(InterventionId.ResetPrintQueue, "Réinitialiser les tâches d’impression bloquées", "virgil-print", 16)
    }).Concat(new[]
    {
        ReadOnly(InterventionId.VerifyHealthDeep, "Vérifier Windows avec analyse approfondie de l’image", "virgil-services", 17) with
        {
            CommandPreviews = new[]
            {
                new InterventionCommandPreview { Executable = "sfc.exe", Arguments = ["/verifyonly"], RunsElevated = true },
                new InterventionCommandPreview { Executable = "dism.exe", Arguments = ["/Online", "/Cleanup-Image", "/CheckHealth", "/English"], RunsElevated = true },
                new InterventionCommandPreview { Executable = "dism.exe", Arguments = ["/Online", "/Cleanup-Image", "/ScanHealth", "/English"], RunsElevated = true },
                new InterventionCommandPreview { Executable = "chkdsk.exe", Arguments = ["<system-drive>", "/scan"], RunsElevated = true },
                new InterventionCommandPreview { Executable = "virgil-services", RunsElevated = true }
            }
        },
        Define(
            InterventionId.ResetStoreCache,
            "Réinitialiser le cache du Microsoft Store",
            InterventionCategory.System,
            "Exécute wsreset.exe après création et vérification d’un point de restauration. Le Microsoft Store peut s’ouvrir.",
            "Réinitialise le cache du Microsoft Store. Le Store peut s’ouvrir et Virgil le laisse ouvert. L’action ne réactive pas les services Store désactivés et ne réinstalle pas les applications.",
            "Fichiers personnels, applications installées et autres caches Windows.",
            InterventionRiskLevel.Sensitive,
            admin: true,
            reboot: false,
            "Jusqu’à cinq minutes",
            "Disponible si wsreset.exe est présent dans le répertoire système.",
            18,
            interruptible: false,
            Commands(true, "wsreset.exe")),
        Define(
            InterventionId.ScheduleSystemDiskCheck,
            "Planifier la vérification du disque système au prochain redémarrage",
            InterventionCategory.Storage,
            "Planifie la vérification du disque système après création et vérification d’un point de restauration. Un redémarrage sera nécessaire. Virgil ne redémarre pas le PC.",
            "Au prochain redémarrage, Windows vérifie et répare le système de fichiers du disque système (équivalent de CHKDSK /F). La vérification peut durer de quelques minutes à plus d’une heure selon le disque. N’éteignez pas le PC pendant la vérification. Une fois la vérification planifiée, Virgil ne peut pas l’annuler.",
            "Fichiers personnels, applications, autres disques. Aucun redémarrage lancé par Virgil.",
            InterventionRiskLevel.Sensitive,
            admin: true,
            reboot: true,
            "Quelques secondes, puis vérification au redémarrage",
            "Disponible si fsutil.exe est présent dans le répertoire système et que le lecteur système est valide.",
            19,
            interruptible: false,
            Commands(true, "fsutil.exe", "dirty", "set", "<system-drive>")),
        Define(
            InterventionId.ResetWindowsUpdate,
            "Remettre à zéro Windows Update",
            InterventionCategory.System,
            "Arrête wuauserv, bits, cryptsvc et msiserver, renomme SoftwareDistribution et catroot2 avec un suffixe daté, puis redémarre les services Windows Update. Aucun dossier n’est supprimé. Virgil ne redémarre pas le PC.",
            "Windows recrée le cache de Windows Update. Les téléchargements en attente sont à refaire et l’historique affiché peut apparaître vide. Les mises à jour déjà installées restent installées. En cas d’échec, Virgil remet les noms d’origine quand c’est possible et redémarre les services.",
            "Mises à jour installées, fichiers personnels, applications, pilotes, magasin de composants Windows et type de démarrage des services.",
            InterventionRiskLevel.Sensitive,
            admin: true,
            reboot: false,
            "Quelques minutes",
            "Les préconditions sont vérifiées au lancement.",
            20,
            interruptible: false,
            Commands(true, "virgil-update-reset"))
    }).Select(definition => InterventionSafety.RequiresRestorePoint(definition.Id)
        ? definition with { RiskLevel = InterventionRiskLevel.Sensitive } : definition).ToArray();

    private static InterventionDefinition ReadOnly(InterventionId id, string title, string executable, int order, params string[] args)
        => Define(id, title, InterventionCategory.System, "Vérification en lecture seule, sauf création du point de restauration explicitement annoncée.",
            id == InterventionId.CreateRestorePoint ? "Créer un point de restauration et vérifier sa présence." : "Rapport des contrôles et des éléments indisponibles, sans réparation.",
            "Vos fichiers personnels et vos paramètres.", InterventionRiskLevel.Low, true, false,
            "Jusqu’à 45 minutes pour SFC, 60 minutes pour DISM", "Outils Windows disponibles", order, true, Commands(true, executable, args));

    private static InterventionDefinition Repair(InterventionId id, string title, string executable, int order)
        => ReadOnly(id, title, executable, order) with
        {
            RiskLevel = InterventionRiskLevel.Sensitive,
            Description = "Changement système après création et vérification d’un point de restauration.",
            ExpectedEffect = id == InterventionId.ResetPrintQueue
                ? "Annuler les tâches bloquées depuis plus d’une heure ou en erreur, puis redémarrer le service d’impression."
                : "Redémarrer uniquement les services concernés."
        };

    public IReadOnlyList<InterventionDefinition> GetAll()
    {
        return Definitions;
    }

    public InterventionDefinition Get(InterventionId id)
    {
        return Definitions.First(definition => definition.Id == id);
    }

    private static InterventionDefinition Define(
        InterventionId id,
        string title,
        InterventionCategory category,
        string description,
        string effect,
        string notTouched,
        InterventionRiskLevel risk,
        bool admin,
        bool reboot,
        string duration,
        string availability,
        int order,
        bool interruptible,
        IReadOnlyList<InterventionCommandPreview> previews)
    {
        return new InterventionDefinition
        {
            Id = id,
            Title = title,
            Category = category,
            Description = description,
            ExpectedEffect = effect,
            NotTouched = notTouched,
            RiskLevel = risk,
            RequiresAdministrator = admin,
            RebootPossible = reboot,
            EstimatedDuration = duration,
            AvailabilityCondition = availability,
            DisplayOrder = order,
            CanBeInterruptedAfterStart = interruptible,
            CommandPreviews = previews
        };
    }

    private static IReadOnlyList<InterventionCommandPreview> Commands(
        bool elevated,
        string executable,
        params string[] arguments)
    {
        if (arguments.Contains("&&", StringComparer.OrdinalIgnoreCase))
        {
            return new[]
            {
                new InterventionCommandPreview
                {
                    Executable = executable,
                    Arguments = arguments.TakeWhile(argument => argument != "&&").ToList(),
                    RunsElevated = elevated
                },
                new InterventionCommandPreview
                {
                    Executable = arguments.SkipWhile(argument => argument != "&&").Skip(1).First(),
                    Arguments = arguments.SkipWhile(argument => argument != "&&").Skip(2).ToList(),
                    RunsElevated = elevated
                }
            };
        }

        return new[]
        {
            new InterventionCommandPreview
            {
                Executable = executable,
                Arguments = arguments,
                RunsElevated = elevated
            }
        };
    }
}
