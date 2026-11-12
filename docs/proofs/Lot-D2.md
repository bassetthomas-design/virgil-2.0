# Lot D2 — Mettre à jour un seul élément

Base vérifiée : `4cca37429270a96c56c012f4a1f1749db90a5954`. Branche `codex/lot-d2`, [PR #84](https://github.com/bassetthomas-design/virgil-2.0/pull/84), maintenue en brouillon, sans fusion. Les décisions de loops (relecture) priment sur le brief initial. Aucun feu vert de relecture n'est déduit des résultats CI.

## Preuves Windows et commits linéaires

| Run | Head | Résultat |
| --- | --- | --- |
| [43187497050](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43187497050) | `a464a8b60cc6d4c686d6ac360a9ba565b6d2b903` | Tests seuls, +601/−0, aucun fichier de production. Build : 0 avertissement / 0 erreur. Core 1239/1246 ; App 688/705. Échecs d'assertion des nouveaux tests, aucune erreur d'environnement. |
| [43188655405](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43188655405) | `f25be14bcb08b3c00f0c461d06c4bebc1375fe58` | Correctif séparé. Core 1245/1246 ; App 705/705. Seul le nouveau test du pilote absent échoue : le statut sérialisé n'était pas accompagné d'un message lisible. Build : 0 avertissement / 0 erreur. |
| [43189375981](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43189375981) | `9fec45a80e5045e57f63e19432839fc54da6e0ea` | Renforcement tests seuls : +19/−0, bornes de cadrage et distinction des scènes. Core 1245/1246 ; App 701/705. Quatre nouvelles assertions de texte hors cadre échouent. Build : 0 avertissement / 0 erreur. |
| [43190018156](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43190018156) | `24edd40aab4c1561a5224f79d549e480c6e2e73c` | Vert intermédiaire : Core 1246/1246 ; App 706/706 ; UI 14/14. Build : 0 avertissement / 0 erreur. La revue visuelle a montré que les bornes logiques ne prouvaient pas le dessin complet des PNG : cette capture n'est pas la preuve finale de cadrage. |
| [43191015851](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43191015851) | `98820567af8025344f291a441e7595a0d012609f` | Renforcement tests seuls : +82/−0, pixels effectivement rendus, raisons exactes des cartes sans bouton, Store natif sans version confirmée et doublon Store encore proposé. Core 1247/1247 ; App 698/708. Dix nouveaux échecs d'assertion, dont cinq sur les pixels et cinq sur les raisons. Build : 0 avertissement / 0 erreur. |
| [43191441642](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43191441642) | `26d6e3fabb0bb4523a550b369c5cdee7f37be5ef` | Core 1247/1247 ; App 707/708. Toutes les assertions D2, y compris les pixels, passent. Seul le test antérieur `InterfaceAccentTests.Interface_labels_preserve_french_accents_in_all_source_projects` échoue : le corpus ajoute « affiché » et confond alors le verbe « Affiche » avec un participe sans accent. Aucun fichier Interventions modifié. Build : 0 avertissement / 0 erreur ; UI smoke non exécuté sur ce head. |
| [43193885123](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43193885123) | `c5e9e5487af5853bface0b2e7b2074dad475188a` | Reformulation du libellé pour la garde d'accents : Core 1247/1247, App 708/708, UI 14/14 ; 0 avertissement / 0 erreur de build. |
| [43228487023](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43228487023) | `e0fa49bb3a2ed31a57dc7c5b876b8db43c6d65e3` | Relecture, tests seuls +222/−0 : Core 1247/1247, App 714/722 ; huit nouveaux échecs d'assertion ; 0 avertissement / 0 erreur de build. Aucune modification de production. |
| [43229609065](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43229609065) | `fca9325ebfdd7660fb65c4dbe2647a5d3d9218c4` | Correctif séparé : App 722/722, Core 1246/1247. Le garde D1-bis-c exige le mot firmware, absent du message BIOS unifié à ce stade. 0 avertissement / 0 erreur de build. |
| [43235131219](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43235131219) | `37f6b81ff424147840b1bf5866bdeda42271dadf` | Message firmware restauré sans modifier les tests : Core 1247/1247, App 720/722. Les deux attentes D2 exigent encore le texte BIOS sans firmware. 0 avertissement / 0 erreur de build. |
| [43238905156](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43238905156) | `10cd37466c1a6f95a4397c212c4997a757c2da6e` | **Vert final du correctif : Core 1247/1247, App 722/722, UI 14/14 ; 0 avertissement / 0 erreur de build.** Formulation commune et deux attentes D2 alignées. Publication et installateur réussis. |

Aucun amend, force-push, changement de workflow, de réglage, de secret, du moteur de confirmation ou de l'allowlist. Publication par le connecteur GitHub intégré ; les commits publiés ont été récupérés localement et leurs arbres comparés à l'index avant d'avancer la branche. Aucun nettoyage destructif.

## Canal et contrôle de chaque type

Toutes les actions D2 passent par `IUpdateSequenceService.ExecuteAsync`, avec une sélection d'un élément marquée `IsSingleItem`. Le marqueur, l'identité, les versions, la source et la session Steam sont sérialisés dans l'aperçu lié au `ConfirmedPlan`. Le plan est consommé avant tout effet ; les plans absents, modifiés, expirés ou réutilisés n'ajoutent aucun effet. Aucune sélection individuelle n'accepte `WingetFinal`, le jeton groupé `drivers`, une source Windows entière ou un élément informatif.

| Type / clé | Canal identique à D1/D1-bis | Commande / effet | Contrôle final | Statuts / tests |
| --- | --- | --- | --- | --- |
| Logiciel, `Winget:<id>` | `WindowsUpdateSequenceBackend` | Actualisation des sources puis `BuildUpgradeArguments(id)` : `upgrade --id <id> --exact --force`, options de capacités inchangées, `--version <version confirmée>` ; jamais `--all` | Scan applicatif en lecture seule ; correspondance sur son identité | mis à jour, reste à installer, en erreur, bloquée, indisponible, non lancée, redémarrage nécessaire ; `D2SingleChannelTests.Software_uses_exact_same_command_as_sequence_and_never_another_identity`, `D2SingleItemTests` |
| Paquet de source Microsoft Store géré par la voie logicielle | Même étape logicielle et dédoublonnage `StoreUpdateService.ScanAsync` | Une seule commande logicielle ; aucun appel natif pour le doublon | Même relecture applicative | `Store_duplicate_has_one_card_and_one_software_command_while_native_offer_is_still_present` |
| Store natif, `Store:<id>` | `StoreUpdateService`, paquet de l'aperçu figé | Un seul `StartAsync` ; les autres paquets ne sont pas installés | Versions avant/après et nouvelle recherche Store | Tests du vrai service : `Store_only_installs_the_frozen_package_and_deduplicates_software_route`. La voie native actuelle n'expose pas de version proposée confirmée : conformément à R3, sa carte D2 reste sans bouton, raison exacte « La version proposée ne peut pas être confirmée. » ; `Native_Store_without_confirmed_proposed_version_is_information_only` |
| Lanceur, `Launcher:<nom>` | `LauncherShutdownPolicy` puis, si proposé, son paquet logiciel | Fermeture vérifiée, jamais pendant un jeu ; même commande du paquet. Sans paquet et sans voie automatique : information seulement | Relecture applicative propre au paquet | non lancée si fermeture non prouvée ; résultat du paquet puis contrôle individuel. `Launcher_package_closes_before_command_and_verifies_its_own_result` ; politique inchangée et tests antérieurs conservés |
| Steam, `Launcher:Steam` ; jeux en lecture seule | Politique de fermeture et `SteamUpdateService` | Pour D2, succès de la fermeture propre à Steam remplace la condition de passe finale ; session ouverte figée, exécutable vérifié, relance exacte `-silent`. La séquence complète garde sa condition initiale | Manifestes Steam par le service existant, sans installation de jeu indépendante | Sans session : raison `LauncherCardSummary.NoSession` exacte, aucun bouton, fermeture ou relance. Jeu en cours : aucune fermeture ou relance. `Steam_alone_uses_real_shutdown_policy_without_final_application_pass`, `Steam_screen_labels_and_final_status_are_exact` ; tests Steam antérieurs conservés |
| Pilote Windows Update, `Driver:WU:<Guid:Revision>` | `InstallMicrosoftUpdates`, même agent/catalogue/recherche/téléchargement | Filtre fermé `driver:<Guid:Revision>` validé dans `UpdateMaintenanceRunner` ; une passe, une collection d'un élément, un seul `InstallAsync` ; filtre firmware conservé | `VerifyAsync(Driver)` ; si toujours proposé : reste à installer ; si contrôle indisponible : aucun succès prouvé ; identité disparue avant installation : à vérifier | `Targeted_driver_identity_is_strict_and_passes_unchanged_allowlist`, `Targeted_WUA_installs_one_collection_with_one_driver_or_nothing`, `Existing_driver_scan_exposes_individual_cards_without_startup_scan` |
| Paquet fabricant, `Driver:WinGet:<id>` | `DriverOfficialPackageCatalog.CanInstall` puis `IDriverPackageSource.InstallAsync` | AMD Cloud Edition seulement, même commande officielle et point de restauration ; assistant interactif/hors liste sans bouton | Relecture applicative propre au paquet, en plus du contrôle des pilotes | `Official_package_is_the_only_installable_offer_and_restore_failure_blocks_it` ; aucune installation si le point n'est pas vérifié |
| BIOS/firmware, fournisseur sans voie officielle, `OutsideWinget`, jeux des autres lanceurs | Information, aucune nouvelle voie | Aucun bouton ni installation. Raison existante conservée pour les propositions non prises en charge ; consigne BIOS/firmware explicite | Lecture seulement | Titre sans statut dans les nouvelles cartes ; `Bios_information_does_not_replace_current_driver_source_status`, tests de raisons exactes et de firmware |

`ElevatedActionAllowlist.IsMaintenanceAllowed` délègue déjà à `UpdateMaintenanceRunner.ValidIdentity` : aucun changement de l'allowlist n'a été nécessaire. Le chemin legacy `InstallDriverUpdate` n'est pas utilisé par D2. Un échec de recherche du pilote ciblé ne lance pas de recherche interactive de secours.

## Écran, statuts et annulation

- Aucun nouveau scan au démarrage. Les logiciels utilisent le scan actuel de l'écran ; les autres cartes utilisent uniquement le résultat de l'examen ou les données déjà obtenues lors du scan/bilan explicite de la séquence. L'action individuelle ne refait pas `ScanAsync` sur le service à état : l'aperçu Store et les offres officielles restent figés pendant l'action.
- Les cases `IsSelected`, `RunSelectedAsync`, `IsRunSelectedEnabled`, les lignes « ID : » / « Commande : » et toutes les assertions antérieures sont conservées conformément à la décision D4 de loops (relecture). Les textes nouveaux D2 n'affichent ni identifiant interne ni commande.
- `BeginOperation` protège le scan, l'examen, la séquence et l'action individuelle. Tous les boutons D2 et « Tout mettre à jour » sont désactivés pendant l'action ; un second appel ne lance rien. Une action refusée pendant la séquence ne remplace pas ses choix figés.
- Statut de chaque carte issu de son propre résultat et de sa propre relecture, puis résumé de source issu des enfants. Priorité : bloquée, en erreur, reste à installer, non lancée, puis résultat confirmé ; les états indisponibles, à vérifier et redémarrage empêchent aussi un succès global. Les informations BIOS ne prennent pas la place du statut de tête. Compteurs nuls omis et pluriels conservés.
- Le cas `Partial_applications_and_BIOS_share_one_screen_with_independent_source_lines` prouve sur le même écran : application A mise à jour, B en erreur, C non touchée/restante, BIOS informatif, Applications en erreur et Pilotes à jour. Aucun appel aux autres identités. Une action individuelle ne produit jamais « Tout est à jour. ».
- Annulation avant confirmation : non lancée, aucun plan/effet. Pendant une commande non interruptible : elle finit et son résultat réel est conservé ; aucune autre installation ne démarre. Le panneau `Operation` existant est réutilisé. L'attente Steam utilise toujours son arrêt d'attente, sans fermeture de Steam.

## Portée et limites

Les services, le ViewModel, la politique de fermeture, le Store et le helper sont réels ; les processus, agent de mise à jour, inventaire, BIOS, Store et Steam sont factices. Les tests Core ciblés ont aussi été exécutés dans un harnais Linux sans le runtime WPF : 16/16 ; ce harnais ne constitue pas une validation Windows. La compilation locale de la solution est sans avertissement. La CI Windows reste la seule référence de build et d'exécution WPF.

Aucune application, aucun pilote, aucun paquet Store ni jeu réellement installé en CI. Aucun BIOS ni firmware écrit. Aucune connexion à un compte. Preuve réelle prévue à l'alpha.8. Aucune préversion publiée. Windows Update logiciels, Defender et nouvelle version de Windows restent sans bouton individuel, selon la décision D5 de loops (relecture).

## Captures WPF STA contrôlées

[Virgil-ui-smoke, artefact 13953588420](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43238905156/artifacts/13953588420), head `10cd37466c1a6f95a4397c212c4997a757c2da6e`. Archive téléchargée par le connecteur intégré et relue localement. Digest ZIP : `sha256:1fc43c79fefb73e429f4fabb5f3017c133395a50b6a621c07ecb5d8b66379c0d`.

Le vrai `UpdatesView` est rendu sur STA dans une présentation WPF dédiée de 1600 × 2400 ; aucun agrandissement d'image après capture. Les assertions vérifient les bornes du panneau, les zones de défilement et les pixels clairs effectivement dessinés pour chaque texte exigé. Les deux scènes BIOS/non prise en charge sont distinctes. Les onze PNG sont distincts et proviennent de ce run vert du correctif. Les cartes pilotes et BIOS sont désormais sous « PILOTES ». Aucun fichier PNG généré n'est committé.

| PNG sous `lot-d2/` | Cas |
| --- | --- |
| `01-carte-avec-bouton.png` | Carte applicative, bouton exact, case existante conservée |
| `02-confirmation-logiciel.png` | Élément, versions et même méthode que la séquence |
| `03-confirmation-pilote.png` | Point de restauration et réutilisation récente explicités |
| `04-bilan-partiel.png` | Réussite, erreur, reste individuel |
| `05-bios-sans-bouton.png` | BIOS informatif sans action |
| `06-non-pris-en-charge.png` | Assistant interactif, raison exacte, sans action |
| `07-boutons-desactives.png` | Action en cours, tous les boutons individuels désactivés |
| `08-annulation.png` | Installation engagée finie, autres éléments non lancés |
| `09-bilan-partiel-avec-bios.png` | Applications A/B/C et BIOS dans le même cadre |
| `10-lanceur-controle-final.png` | Paquet du lanceur : fermeture vérifiée puis commande et contrôle final |
| `11-steam-controle-final.png` | Session Steam ouverte : relance silencieuse et statut final propre |

## Corrections de relecture et validation finale

Le libellé « Cet élément est présenté pour information ; aucune installation automatique n'est proposée. » a été reformulé pour respecter la garde d'accents existante. `InterventionCatalog.cs`, `InterfaceAccentTests.cs`, `MotsAccentues.txt` et `ValideSansAccent.txt` restent intacts.

La formulation commune est « Virgil n'installe jamais le BIOS ni le firmware ; passez par l'outil officiel du fabricant de l'ordinateur si une mise à jour est proposée. ». Une seule constante `UpdateSequenceService.BiosInformationMessage` est utilisée pour la carte BIOS, les éléments firmware connus ou relus, la passe finale logicielle et l'écran. Le helper, qui ne référence pas Core, conserve exactement ce texte dans son résultat informatif. `FirmwareInformationMessage` est supprimée. Les deux attentes exactes autorisées dans `D2SourceCardsTests.cs` suivent cette phrase ; aucun autre test n'est modifié par ce dernier correctif et aucune assertion n'est retirée.

L'aperçu de « Tout mettre à jour » consomme `SingleItems` produit par son scan existant et `KnownDriverUpdates`, un instantané en mémoire des résultats déjà obtenus. Il n'appelle plus `ReadSingleItemsAsync` ni `VerifyAsync(Driver)`. L'examen des pilotes utilise son unique rapport et préserve les cartes déjà connues ; il ne relance pas `IUpdateSequenceService.ScanAsync`. Sans identité de mise à jour disponible, aucun élément installable n'est inventé. La lecture explicite reste protégée par un délai ; en cas d'échec, elle fournit une carte informative « indisponible » avec le message exact « Les pilotes n'ont pas pu être lus. Relancez leur examen. ».

`Preview_uses_cached_cards_without_an_extra_driver_read` et `Driver_examination_uses_its_report_without_scanning_the_sequence` vérifient ces nombres d'appels avec le vrai service derrière un adaptateur de comptage. `Explicit_driver_read_failure_has_a_visible_simple_reason` couvre le message d'échec. Les six cas `Launcher_package_closes_before_command_and_verifies_its_own_result` passent par le vrai `LauncherShutdownPolicy`, pour les deux clés de carte : fermeture avant la commande, aucun lancement pendant un jeu, statut non lancé préservé et relecture de la proposition. Les deux cas `Steam_screen_labels_and_final_status_are_exact` vérifient titre, statut, détail, absence de bouton sans session et résultat final avec session ouverte. Le détail Steam seul ne parle plus d'une passe applicative ultérieure. `Unknown_card_status_requires_verification_instead_of_claiming_current` impose « à vérifier » pour un statut inconnu.

Le [run vert final 43238905156](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43238905156), head `10cd37466c1a6f95a4397c212c4997a757c2da6e`, valide Core **1247/1247**, App **722/722**, UI **14/14**, avec **0 avertissement / 0 erreur de build**. Son artefact `Virgil-ui-smoke` et ses onze PNG sont référencés ci-dessus. Les runs rouges précèdent leurs correctifs séparés ; aucun résultat local ne remplace cette validation Windows.

## Fichiers et hygiène du diff

Seul test présent à la base modifié : `tests/Virgil.App.Tests/LongOperationArchitectureTests.cs`, +1/−0 (inventaire de `RunSingleAsync`). `UpdatesViewModelTests.cs` et `UpdatesSequenceViewModelTests.cs` sont inchangés. Toutes les assertions antérieures restent intactes. Les trois fichiers D2 sont nouveaux par rapport à la base. Aucun fichier généré, réécriture globale ou changement de fins de ligne ; `git diff --check` sans erreur. Les onze PNG téléchargés ont onze SHA-256 distincts et mesurent chacun 1600 × 2400 pixels.

| Fichier | + | − |
| --- | ---: | ---: |
| `src/Virgil.App/ViewModels/UpdateTextCardViewModel.cs` | 27 | 1 |
| `src/Virgil.App/ViewModels/UpdatesItemViewModel.cs` | 1 | 0 |
| `src/Virgil.App/ViewModels/UpdatesViewModel.Sequence.cs` | 2 | 0 |
| `src/Virgil.App/ViewModels/UpdatesViewModel.Single.cs` | 242 | 0 |
| `src/Virgil.App/ViewModels/UpdatesViewModel.cs` | 7 | 1 |
| `src/Virgil.App/Views/UpdatesView.xaml` | 18 | 0 |
| `src/Virgil.Core/Updates/SteamUpdateService.cs` | 2 | 2 |
| `src/Virgil.Core/Updates/UpdateActionPreviewFactory.cs` | 7 | 0 |
| `src/Virgil.Core/Updates/UpdateSequenceService.cs` | 130 | 5 |
| `src/Virgil.ElevatedHelper/UpdateMaintenanceRunner.cs` | 21 | 5 |
| `src/Virgil.Infrastructure/Windows/WindowsUpdateSequenceBackend.cs` | 4 | 1 |
| `tests/Virgil.App.Tests/D2SingleItemTests.cs` | 306 | 0 |
| `tests/Virgil.App.Tests/D2SourceCardsTests.cs` | 420 | 0 |
| `tests/Virgil.App.Tests/LongOperationArchitectureTests.cs` | 1 | 0 |
| `tests/Virgil.Core.Tests/D2SingleChannelTests.cs` | 250 | 0 |
| `docs/proofs/Lot-D2.md` | 108 | 0 |
