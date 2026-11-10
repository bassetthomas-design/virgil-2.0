# Lot D1-bis-c — Pilotes officiels et BIOS en information seulement

Base : `main` / `origin/main` `1beb2d1fa0c22f4238f2b83652b5589ca4baf653`. Branche `codex/lot-d1bis-c`, [PR #83](https://github.com/bassetthomas-design/virgil-2.0/pull/83) maintenue en brouillon, sans fusion. Les deux commits de tests restent intacts.

## Preuves rouges Windows

| Run | Head | Build | Core | App |
| --- | --- | --- | --- | --- |
| [42828833832](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42828833832) | `d9c96a406268e09324fb8d39e5fef108fb7dd95b` | 0 avertissement / 0 erreur | 1210/1224, 14 échecs | 678/688, 10 échecs |
| [42832427054](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42832427054) | `03144bd0f569bc5ff73eaf49cef034fc3b76fe02` | 0 avertissement / 0 erreur | 1210/1227, 17 échecs | 678/688, 10 échecs |

Ce sont de vrais échecs d'assertion des nouveaux tests, sans échec de compilation ni d'environnement. Les tests UI sur l'exécutable publié sont ignorés après l'échec de Test. Le premier commit apporte seulement les tests, les ports et un lecteur BIOS vide. Le second ajoute trois cas : firmware + logiciel, inventaire vide et inventaire illisible. Le correctif de production est séparé : `389e324b16450859467edcb3e3d1c02a0a7ad2ee`.

## Comportement livré et sécurité

- Les titres et identités Windows Update sont remontés sans réécriture. Une carte par pilote porte son propre résultat ; la ligne Pilotes résume ces résultats avec priorité bloquée, en erreur, reste à installer, annulation, puis réussite. Les compteurs nuls sont omis et les pluriels sont testés.
- Clarification loops (relecture), 10/11/2026 14h40 : seulement dans Tout mettre à jour, un appel par pilote déjà téléchargé. L'appel en cours finit ; les suivants restent non lancés. La recherche, le catalogue Microsoft, le téléchargement et jusqu'à trois passes sont conservés. Les logiciels Windows et les mises à niveau restent groupés.
- Le plan confirmé est consommé avant les effets. Sans plan, après modification du plan ou après réutilisation, aucune installation supplémentaire. Le point de restauration protège aussi les paquets fabricants. Aucun redémarrage automatique.
- Inventaire existant en lecture seule ; lecture BIOS par `Get-CimInstance Win32_BIOS` (identité et version disponibles sur le PC). Le faux accès P5 refuse toute commande de modification, tout lancement, téléchargement et flash ; les lectures restent stables. La confirmation affiche une ligne BIOS sans case d'installation.
- Consigne loops (relecture), 15h08 : la passe finale relit les propositions avant tout groupe. Le classement BIOS/firmware/UEFI est celui d'UpdateRiskClassifier. Si un firmware est présent ou si l'ensemble n'est pas prouvé complet, aucune commande groupée ; les logiciels identifiés sont traités par les commandes individuelles existantes, inchangées. Le firmware a une étape et une carte d'information avec sa raison, sans commande d'installation.
- La complétude exige un scan réussi, une table lisible, des identités et versions valides, aucune identité tronquée ni ligne perdue, des compteurs cohérents et aucune erreur de lecture. Une table complète sans firmware autorise la commande groupée existante, inchangée. Une sortie vide, illisible ou douteuse ne l'autorise jamais. Aucun épinglage ni modification de réglage n'est effectué.

## Liste officielle versionnée

Catalogue : `src/Virgil.Core/Updates/DriverOfficialPackageCatalog.cs`. Source relue : [microsoft/winget-pkgs, commit db1e2f3a72d88b016050a00b5071c94a2ef70243](https://github.com/microsoft/winget-pkgs/tree/db1e2f3a72d88b016050a00b5071c94a2ef70243).

| Fabricant / identifiant vérifié | Décision et preuve de source |
| --- | --- |
| AMD — `AMD.AMDSoftwareCloudEdition` | Seul paquet de pilotes autorisé automatiquement, uniquement si réellement proposé sur le PC par la voie logicielle. Nom public : AMD Software: Cloud Edition. [Manifeste 23.Q3](https://github.com/microsoft/winget-pkgs/blob/db1e2f3a72d88b016050a00b5071c94a2ef70243/manifests/a/AMD/AMDSoftwareCloudEdition/23.Q3/AMD.AMDSoftwareCloudEdition.installer.yaml) : mode silent, commutateur `-install`. Pilotes spécialisés pour machines Azure NGads V620 ; aucun paquet AMD générique n'est déduit de ce cas. |
| Intel — `Intel.IntelDriverAndSupportAssistant` | Identifiant officiel vérifié, mais exclu de l'installation automatique des pilotes : installer silencieusement l'assistant ne prouve pas qu'il installe les pilotes tout seul. [Manifeste 26.2.0.7](https://github.com/microsoft/winget-pkgs/blob/db1e2f3a72d88b016050a00b5071c94a2ef70243/manifests/i/Intel/IntelDriverAndSupportAssistant/26.2.0.7/Intel.IntelDriverAndSupportAssistant.installer.yaml). |
| NVIDIA | Aucun paquet de pilote graphique silencieux établi dans le [dossier officiel relu](https://github.com/microsoft/winget-pkgs/tree/db1e2f3a72d88b016050a00b5071c94a2ef70243/manifests/n/Nvidia). Aucun identifiant de pilote inventé. Les pilotes Windows Update restent traités ; sinon le fournisseur détecté est informatif. |
| `Nvidia.UEFIFirmwareUpdater40xx` | Identifiant réel utilisé seulement comme contre-exemple interdit dans les tests, jamais accepté dans la liste installable. [Manifeste 1.2.0](https://github.com/microsoft/winget-pkgs/blob/db1e2f3a72d88b016050a00b5071c94a2ef70243/manifests/n/Nvidia/UEFIFirmwareUpdater40xx/1.2.0/Nvidia.UEFIFirmwareUpdater40xx.locale.en-US.yaml). |

Aucun paquet de chipset de carte mère ajouté faute de voie établie. Aucun téléchargement direct sur un site fabricant, outil tiers, paquet BIOS ou flash lancé.

## Portée des preuves

Les nouveaux tests passent par UpdateSequenceService, UpdatesViewModel et le helper réels avec des agents, sources logicielles, inventaires et BIOS factices. Les PNG sont ceux du vrai panneau WPF sur STA, avec données factices. Ils ne constituent pas des captures d'une installation réelle. Aucun pilote ni BIOS réellement installé en CI ; preuve réelle prévue à l'alpha.8. Le harnais local Linux sans WPF a passé 95 cas ciblés et ne constitue pas une validation Windows. Aucun feu vert de loops (relecture) n'est déduit de la CI.

## Adaptation étroite d'un test existant

`tests/Virgil.Core.Tests/L6PlusExecutionTests.cs` : les deux cas de `Refused_UAC_or_failed_restore_blocks_only_microsoft_steps` ont réellement échoué au [run 42834617811](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42834617811), head `389e324b16450859467edcb3e3d1c02a0a7ad2ee` (Core 1225/1227 ; App 688/688 ; 0 avertissement / 0 erreur). Le faux backend ne fournissait pas l'inventaire requis avant la passe finale. La fixture est complétée avec une source d'inventaire séparée et une assertion de lecture. Toutes les assertions antérieures sont conservées, notamment les étapes Microsoft bloquées, les deux réussites logicielles, les deux commandes d'installation et l'unique lecture de fermeture. Aucun autre test existant modifié.

Le [run intermédiaire 42835548879](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42835548879), head `e763b69c18d2a20d968c2c8eeb521a703d1cec87`, a ensuite entièrement réussi. La relecture a demandé de reconnaître réellement une table lisible comme complète : un nouveau commit de tests distinct protège ce cas avant le correctif final.

La correction de complétude possède sa propre preuve rouge : [run 42836421712](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42836421712), head `828fc0f86549e26bfdc2e5285d955211192ff057`, build 0 avertissement / 0 erreur, Core 1229/1230 (seul le nouveau test de table lisible échoue), App 688/688. Les cas identité tronquée et ligne malformée complètent la protection des inventaires douteux. La fixture L6Plus passe ensuite par le reader réel avec une table logicielle lisible, sur un faux accès séparé ; la lecture de cet accès et son absence de commandes d'installation sont aussi assertées.

La comparaison des lignes d’assertion de L6PlusExecutionTests avec origin/main conserve les 68 lignes initiales et en ajoute trois (71 au total). Diff de ce seul test existant : +23/−2, concernant uniquement la fixture, sa table d’inventaire et les assertions supplémentaires ; aucune assertion retirée ni remplacée.

## Fichiers et hygiène du diff

Hors ce document : 20 fichiers, +1318/−22 contre origin/main. Aucun fichier généré, workflow, réglage, secret ni moteur de confirmation modifié ; git diff --check sans erreur.

| Chemin | + | − |
| --- | ---: | ---: |
| `src/Virgil.App/ViewModels/UpdatesViewModel.Sequence.cs` | 23 | 3 |
| `src/Virgil.Contracts/Updates/UpdateModels.cs` | 2 | 0 |
| `src/Virgil.Core/Updates/BiosInformation.cs` | 8 | 0 |
| `src/Virgil.Core/Updates/DriverCardSummary.cs` | 26 | 0 |
| `src/Virgil.Core/Updates/DriverInformationService.cs` | 3 | 3 |
| `src/Virgil.Core/Updates/DriverOfficialPackageCatalog.cs` | 22 | 0 |
| `src/Virgil.Core/Updates/DriverSequenceContracts.cs` | 31 | 0 |
| `src/Virgil.Core/Updates/UpdateSequenceService.cs` | 105 | 8 |
| `src/Virgil.ElevatedHelper/ComWindowsUpdateAgent.cs` | 13 | 2 |
| `src/Virgil.ElevatedHelper/IWindowsUpdateAgent.cs` | 9 | 1 |
| `src/Virgil.ElevatedHelper/UpdateMaintenanceRunner.cs` | 37 | 1 |
| `src/Virgil.Infrastructure/Windows/BiosWindowsReader.cs` | 22 | 0 |
| `src/Virgil.Infrastructure/Windows/DriverFinalPassWindowsReader.cs` | 55 | 0 |
| `src/Virgil.Infrastructure/Windows/DriverWindowsSources.cs` | 38 | 0 |
| `src/Virgil.Infrastructure/Windows/WindowsUpdateSequenceBackend.cs` | 37 | 2 |
| `tests/Virgil.App.Tests/D1bisCPanelTests.cs` | 291 | 0 |
| `tests/Virgil.Core.Tests/D1bisCFirmwarePassTests.cs` | 116 | 0 |
| `tests/Virgil.Core.Tests/D1bisCHelperTests.cs` | 188 | 0 |
| `tests/Virgil.Core.Tests/D1bisCSequenceTests.cs` | 269 | 0 |
| `tests/Virgil.Core.Tests/L6PlusExecutionTests.cs` | 23 | 2 |

## Validation Windows finale du code

[Run 42837488174](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42837488174), head `646e38a807ad7fb7ac9db60eb412db13e5825b4b`, job `129903462576` : entièrement vert, build 0 avertissement / 0 erreur, Core **1230/1230**, App **688/688**, UI **14/14**. Publication et installateur réussis. Ce run est distinct de tous les runs rouges précédents. Le correctif de complétude est un commit séparé du test rouge `828fc0f` ; aucun commit antérieur amendé ni écrasé.

## Captures WPF STA factices

[Virgil-ui-smoke — artefact 13774927319](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42837488174/artifacts/13774927319), régénéré sur le head vert ci-dessus. Digest ZIP fourni par GitHub : `sha256:68709bc358c19455b7b63255391fee8f0ae277c0d444c5959df3d8a08609e543`.

| PNG sous `lot-d1bis-c/` | Scénario du vrai panneau WPF |
| --- | --- |
| `confirmation.png` | Une confirmation, paquets officiels, BIOS en ligne d'information sans case |
| `partial.png` | Pilote installé, autre pilote restant, paquet officiel installé, fournisseur non pris en charge, BIOS informatif |
| `no-drivers.png` | Aucun pilote Windows Update proposé, carte source à jour, aucun paquet inventé |
| `bios-information.png` | Identité/version disponibles en lecture seule, aucune installation BIOS |
| `cancellation.png` | Premier pilote terminé, suivants non lancés, bilan cohérent |

Les dix cas App D1bisC et les captures RenderTargetBitmap sur STA ont réussi. Le proxy refuse le téléchargement local du fichier d'artefact (403 lors du CONNECT vers le stockage) : aucune revue visuelle locale des PNG ni aucun hash individuel n'est revendiqué. L'artefact Windows est disponible pour la revue. Aucun réglage réseau modifié pour contourner ce refus.
