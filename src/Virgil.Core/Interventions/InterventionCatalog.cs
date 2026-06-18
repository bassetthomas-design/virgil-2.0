using Virgil.Domain;

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
            "La barre des taches et le bureau disparaissent quelques secondes puis reviennent.",
            "Les applications ouvertes ne sont pas fermees.",
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
            "Supprime le cache de resolution DNS local.",
            "Force Windows a redemander les adresses DNS lors des prochaines connexions.",
            "Ne modifie ni les serveurs DNS, ni le routeur, ni les reseaux Wi-Fi.",
            InterventionRiskLevel.Low,
            admin: true,
            reboot: false,
            "Moins d'une minute",
            "Disponible si ipconfig.exe est present.",
            2,
            interruptible: true,
            Commands(true, "ipconfig.exe", "/flushdns")),
        Define(
            InterventionId.RenewIp,
            "Renouveler la configuration IP",
            InterventionCategory.Network,
            "Libere puis renouvelle les baux DHCP des interfaces actives compatibles.",
            "Peut restaurer une configuration IP obtenue par DHCP.",
            "Ne modifie pas une configuration IP statique volontaire.",
            InterventionRiskLevel.Moderate,
            admin: true,
            reboot: false,
            "Une a deux minutes",
            "Disponible seulement si une interface DHCP active non VPN est detectee.",
            3,
            interruptible: false,
            Commands(true, "ipconfig.exe", "/release", "&&", "ipconfig.exe", "/renew")),
        Define(
            InterventionId.SfcScan,
            "Analyser les fichiers systeme avec SFC",
            InterventionCategory.System,
            "Execute une verification des fichiers systeme proteges.",
            "Windows peut reparer certains fichiers systeme proteges.",
            "Ne supprime pas les fichiers personnels.",
            InterventionRiskLevel.Moderate,
            admin: true,
            reboot: true,
            "Plusieurs minutes",
            "Disponible si sfc.exe est present.",
            4,
            interruptible: false,
            Commands(true, "sfc.exe", "/scannow")),
        Define(
            InterventionId.DismScanHealth,
            "Analyser l'image Windows avec DISM",
            InterventionCategory.System,
            "Execute DISM ScanHealth en diagnostic approfondi.",
            "Detecte des corruptions possibles de l'image Windows.",
            "Ne lance pas RestoreHealth automatiquement.",
            InterventionRiskLevel.Moderate,
            admin: true,
            reboot: false,
            "Plusieurs minutes",
            "Disponible si dism.exe est present.",
            5,
            interruptible: false,
            Commands(true, "dism.exe", "/Online", "/Cleanup-Image", "/ScanHealth")),
        Define(
            InterventionId.DismRestoreHealth,
            "Reparer l'image Windows avec DISM",
            InterventionCategory.System,
            "Execute DISM RestoreHealth sans source externe personnalisee.",
            "Windows peut utiliser Windows Update pour reparer l'image.",
            "Ne supprime pas les fichiers personnels et n'utilise pas /ResetBase.",
            InterventionRiskLevel.Sensitive,
            admin: true,
            reboot: true,
            "Long",
            "Disponible si dism.exe est present.",
            6,
            interruptible: false,
            Commands(true, "dism.exe", "/Online", "/Cleanup-Image", "/RestoreHealth")),
        Define(
            InterventionId.ResetWinsock,
            "Reinitialiser Winsock",
            InterventionCategory.Network,
            "Reinitialise la pile Winsock.",
            "Peut resoudre certains problemes reseau bas niveau.",
            "Peut affecter des configurations reseau personnalisees.",
            InterventionRiskLevel.Sensitive,
            admin: true,
            reboot: true,
            "Moins d'une minute, puis redemarrage manuel probable",
            "Disponible si netsh.exe est present.",
            7,
            interruptible: false,
            Commands(true, "netsh.exe", "winsock", "reset")),
        Define(
            InterventionId.ResetTcpIp,
            "Reinitialiser TCP/IP",
            InterventionCategory.Network,
            "Reinitialise certains parametres TCP/IP.",
            "Peut restaurer une pile IP endommagee.",
            "Peut affecter des parametres reseau personnalises.",
            InterventionRiskLevel.Sensitive,
            admin: true,
            reboot: true,
            "Moins d'une minute, puis redemarrage manuel probable",
            "Disponible si netsh.exe est present.",
            8,
            interruptible: false,
            Commands(true, "netsh.exe", "int", "ip", "reset")),
        Define(
            InterventionId.ChkdskOnlineScan,
            "Analyser le disque systeme avec CHKDSK",
            InterventionCategory.Storage,
            "Execute CHKDSK en ligne sur le disque systeme avec /scan uniquement.",
            "Affiche le resultat de l'analyse sans planifier de reparation au redemarrage.",
            "N'utilise jamais /f, /r ou /x dans cette preview.",
            InterventionRiskLevel.Moderate,
            admin: true,
            reboot: false,
            "Plusieurs minutes",
            "Disponible si chkdsk.exe est present et si le disque systeme est valide.",
            9,
            interruptible: false,
            Commands(true, "chkdsk.exe", "<system-drive>", "/scan"))
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
