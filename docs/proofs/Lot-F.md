# Lot F — Répliques, texte progressif et Priorités

Base exacte : `cd7cd53d31232024117510c878e421371d945b7f`. Branche `codex/lot-f`, PR [#86](https://github.com/bassetthomas-design/virgil-2.0/pull/86), conservée en brouillon. Aucun merge, amend, rebase ou force-push. Les décisions F1–F11 de loops (relecture) et les exceptions ciblées du 16/11 priment sur le brief initial.

## Preuves Windows

| Étape | Run | SHA du head | Résultat constaté |
| --- | --- | --- | --- |
| Tests seuls initiaux | [405 — 43446518904](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43446518904) | `373f9d5fa6a8721392b8d2b070d08e4852e85d82` | Compilation 0 avertissement, 0 erreur ; Core 1247 réussis + 2 nouveaux échecs ; App 757 réussis + 49 nouveaux échecs d’assertion. UI non exécutée. |
| Tests seuls complétés | [406 — 43447219034](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43447219034) | `41b748229322a89174e0b5d4771d1caeae7498bf` | Compilation 0 avertissement, 0 erreur ; mêmes totaux et échecs d’assertion. UI non exécutée. |
| Correctif initial | [407 — 43828807319](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43828807319) | `3a24d27b587324b2180cb9ca25877b6ae137e77d` | Compilation 0 avertissement, 0 erreur ; Core 1248/1249, App 801/806. Rouge, six échecs ; UI non exécutée. |
| Tests de correction, décisions du 16/11 | [411 — 43830615560](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43830615560) | `451e45dfec7901710a8d6307a96c998b2472752a` | Compilation 0 avertissement, 0 erreur ; Core 1249/1249, App 803/807. Rouge sur quatre assertions App ; inventaire, limite du port et catalogue passent. |
| Correctif de production après relecture | [412 — 43831007548](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43831007548) | `5f1f15e029c616e647575eddb48bae0e4a226d0e` | Compilation 0 avertissement, 0 erreur ; Core 1249/1249, App 806/807. Seul le scénario Steam seul échoue ligne 27 sur une collection vide ; UI, publication et installateur non exécutés. |
| Préparation des cartes Steam après scan | [414 — 43832572292](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43832572292) | `59ef00a7d72bf4fcf078c5257782063f854caa96` | Compilation 0 avertissement, 0 erreur ; Core 1249/1249, App 805/807. Les deux cas Steam passent. Deux échecs existants : ligne Lanceurs indue et libellé d’annulation observé après progression. |
| Correctif Steam final, avant documentation | [415 — 43833285205](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43833285205) | `9965ef70027eccb7c479639f18c28133fb8d1240` | **VERT : compilation 0 avertissement, 0 erreur ; Core 1249/1249, App 807/807, UI 14/14. Publication et installateur réussis.** |

## Validation verte et protocole M7 du head final

Le run 415 valide tout le code livré, dont les deux parcours de `LotFSteamTests` : séquence complète et Steam seul. L’artefact vert [Virgil-ui-smoke, ID 14210395674](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43833285205/artifacts/14210395674) fait 5 951 471 octets ; digest publié `sha256:15b50b1e3258988ed46389b5152447c4841641e08162604d09ea90454afe8a8d`. Les logs constatent son upload réussi, avec 90 fichiers dans l’ensemble de l’artefact. Les artefacts de l’application et de l’installateur sont également publiés.

Le présent document est le dernier commit autorisé après cette validation verte. **Aucun nouveau push ne suivra ce commit documentaire.** Son propre run Windows sera vérifié ; conformément à M7 du brief, le numéro et l’ID du run vert FINAL sur le head final, le SHA exact de ce head et l’ID de son artefact Virgil-ui-smoke seront inscrits dans la description de la PR #86, après vérification réelle, sans réécrire ce document ni pousser un autre commit. Le run 415 n’est pas présenté comme celui du futur head documentaire.

Les runs 405/406, 407, 411, 412 et 414 restent des preuves rouges historiques ; aucun n’est présenté comme une validation finale verte. La ligne Lanceurs indue du run 414 est corrigée en limitant l’exposition après scan à Steam actionnable. L’échec d’annulation du même run ne s’est pas reproduit au run 415 ; ni le test ni le chemin d’annulation n’ont été modifiés pour le faire passer. La CI du head final vérifie à nouveau ce chemin.

### Décisions de loops (relecture), 16 novembre

- Exception F11 unique, commit dédié `ac143e3c224efec6d3fb3506b97d012d6d215374` : seule addition de `HomePrioritiesViewModel.cs` dans l’ordre alphabétique de la liste App de `LegacyEffectArchitectureTests.ProcessRunnerConsumersRemainExplicitlyInventoried`. Aucun autre octet de ce test ni aucun autre test préexistant n’est modifié. Le test ajouté `LotFBackupBoundaryTests` (commit `ee35c949749d0dd8e801ad659a079c26711b3afc`) prouve un lancement `ms-settings:backup` uniquement, sans argument, verbe d’élévation, attente de processus ni appel RunAsync. Les boutons d’écran émettent seulement la navigation ; un point étranger à la liste est refusé.
- Correction d’une attente erronée, décidée par loops (relecture) le 16/11 : commit dédié `451e45dfec7901710a8d6307a96c998b2472752a`, intitulé `test(lot-f): compare COM ordinally per review decision of 16 November`. Dans le seul nouveau `LotFSpeechTests.cs`, l’entrée interdite ` COM` est conservée et comparée en Ordinal, sensible à la casse. Les autres termes restent insensibles à la casse et l’Assert.Equal de chaque réplique est intact. Cela conserve exactement « Annulation prise en compte. Le détail est ci-dessus. » de l’annexe A, conformément à la décision et à M5.
- Aucune autorisation de modifier InterfaceAccentTests, ValideSansAccent ou MotsAccentues : ces fichiers restent strictement identiques à la base.

### Corrections de production

Commit séparé `5f1f15e029c616e647575eddb48bae0e4a226d0e`, après le run rouge 411, sans amend ni force-push :

1. `AppendVirgilMessage` normalise toujours la même chaîne, puis utilise exactement `"[VIRGIL]\n" + message`. Seul le nom de la variable locale change ; aucun message n’est reformulé.
2. Le TextBlock de phase du panneau est vide lorsque IsRunning est faux, en plus de rester masqué. Seule FinalSummary porte alors la conclusion. Les propriétés Step, Steps, Commentary, FinalSummary et leurs textes ne sont pas modifiés par ce réglage de présentation ; la réplique reste dans son unique TextBlock.
3. Les faux positifs d’accents proviennent de l’analyse de littéraux, qui construit son dictionnaire depuis les participes et traite ensuite des homographes corrects comme des fautes. Les quelques mots concernés sont représentés par des expressions littérales constantes, repliées par le compilateur C# : aucun assemblage de phrase à l’exécution. Les frontières de littéraux sont changées, pas les données compilées. Le contrôle statique comparatif a confirmé les 33 répliques et 22 entrées inchangées ; les Assert.Equal du catalogue et de l’annexe B restent la vérification en exécution. Les fichiers hors lot et le garde existant ne sont pas modifiés. Ce choix de représentation source est signalé explicitement dans la PR.

### Correction Steam en production, sans modification de test

La décision de loops (relecture) demande de préparer la carte Steam dès la fin du scan manuel, dans UpdatesViewModel et UpdatesViewModel.Single. Le test LotFSteamTests reste strictement identique à son état antérieur à cette correction : la préparation par aperçu puis annulation, précédemment proposée dans ce document, **n’a pas été appliquée**.

Le commit `59ef00a7d72bf4fcf078c5257782063f854caa96` ajoute un appel après RenderScanReport, au cours du scan explicite, et réutilise `IUpdateSequenceService.ScanAsync` pour préparer les résultats des sources en lecture seule. Aucune installation, fermeture de lanceur ou confirmation n’est déclenchée par cette préparation. Les résultats réels, dont la session Steam, sont conservés par le service existant. Aucun nouveau port, service ou accès OS App n’est ajouté.

Le complément `9965ef70027eccb7c479639f18c28133fb8d1240` ne fait apparaître après ce scan que la carte Steam actionnable ; les autres cartes de sources gardent le parcours existant d’aperçu. Un lanceur non pris en charge n’introduit donc plus une ligne « Lanceurs : à jour » sans preuve, et le test D2 existant reste inchangé. La carte Steam conserve l’identité `Step.Id = "Steam"` et son état de session pour la confirmation et l’exécution simple.

Dans AddSingleCard, toute carte `SteamGame:*` est explicitement InformationOnly, dans le parcours simple comme dans la séquence. La recherche d’une carte déjà présente utilise sa clé App existante `Component:Id`, sans changer cette clé ni les clés d’étapes du panneau. La fonction partagée SteamGameDetail reste l’unique règle de détail dans les deux parcours. Les exclusions des cartes d’information dans la source, les compteurs, la barre, le visage et la catégorie de réplique restent intactes. Les tests verts D2, Lot E et Lot F le vérifient.

Le complément de tests seuls conserve les identités Steam existantes, conformément à R10 (« les statuts, les clés, les compteurs et le résumé de la source Lanceurs ne changent pas ») : la carte simple a une clé `Launcher:SteamGame:<id>`, le panneau d’opération une clé `SteamGame:<id>`. Le nouveau test cherchait initialement la seconde dans les deux collections. Cette correction précède le premier run du correctif et ne change aucune attente de statut ou de détail. Le même commit complète le pilote de captures : texte partiel avec horloge manuelle, clic avant les captures finales et vrai panneau WPF pour les jeux Steam. Aucun test préexistant modifié dans ces deux premiers commits de tests seuls.

Conformément à M7, le run du head final contenant ce document, son SHA exact et l’ID de son artefact seront consignés dans la description de la PR après leur vérification, avant toute demande de relecture. Les runs rouges ci-dessus ne constituent pas une preuve finale verte.

## Répliques et états finaux

Le catalogue C# compilé reprend les 33 répliques de l’annexe A, mot pour mot. Une instance de sélection par propriétaire garde un compteur par liste ; index `(jour local de l’année + nombre déjà tiré) modulo taille`. L’horloge est le `TimeProvider` du propriétaire. Aucun hasard, réseau, lecture de catalogue à l’exécution ou génération de phrase.

| État / moment | Catégorie | Liste si humour autorisé | Liste sinon | Test |
| --- | --- | --- | --- | --- |
| Démarrage du chat | Accueil | `Welcome` (sarcastique) | Sans objet | `LotFRevealTests.Chat_preserves_the_old_message_and_adds_welcome_after_it` |
| Start | En cours | `RunningHumour` | `RunningSober` | `LotFSpeechTests.Typed_final_state_alone_selects_the_correct_list` |
| Succeeded | Réussi | `SucceededHumour` | `SucceededSober` | même test, état Succeeded |
| NeedsReview | À vérifier | `NeedsReview` (sobre) | `NeedsReview` (sobre) | même test, état NeedsReview |
| Failed | Échec | `Failed` (sobre) | `Failed` (sobre) | même test, état Failed |
| Cancelled | Annulé | `CancelledHumour` | `CancelledSober` | même test + `Cancellation_wins_over_requested_success` |
| Neutral | Neutre | `NeutralHumour` | `NeutralSober` | même test, état Neutral |

La sélection de fin ne lit que `FinalState`, après la règle d’annulation. Nettoyage, Applications et Réparation fixent `HumourAllowed = false` ; les trois autres panneaux conservent sa valeur vraie par défaut. Les textes existants de `FinalSummary`, `Commentary`, des étapes et des messages des modules sont conservés. La réplique est un `TextBlock` ordinaire après Commentary, avec `VirgilRemarkText` et la couleur existante blanche.

Les nouveaux tests utilisent le vrai `UpdatesViewModel` et le vrai `UpdateSequenceService` avec des backends factices : réussite, reste, redémarrage et cas partiel mêlant réussite, refus et reste. Les cartes informatives restent exclues de la catégorie et du visage. Le refus de confirmation passe à Neutral ; son texte reste exact.

## Affichage progressif et visage

`SpeechReveal` est le modèle pur, piloté par horloge : un pas de 40 ms, 40 pas au plus, plusieurs mots par pas au besoin. `SpeechText` est un comportement attaché au `TextBlock`, sans sous-classe ni attente synchrone. Le nom accessible contient immédiatement le texte complet. Un clic termine l’affichage ; un remplacement termine le texte précédent ; les animations Windows coupées donnent immédiatement le texte entier. Le déchargement termine le texte et arrête le minuteur.

Seuls les entrées du chat et la ligne Remark utilisent ce comportement. La normalisation existante des messages du chat reste appliquée avant l’ajout du préfixe `[VIRGIL]\n`. Le message de démarrage existant est conservé puis suivi d’une réplique Accueil.

`ShowSarcasticGlance` n’ajoute aucun état applicatif et ne modifie ni `SetState`, ni `NotifyCommunication`, ni `RefreshFace`, ni les noms accessibles. La méthode utilise la géométrie sarcastique déjà présente, pendant 1,2 s, au repos ou en communication seulement, contrôle chargé, visible, non compact et animations actives. Elle invalide le profil rendu pour que la prochaine demande d’état reprenne immédiatement sa forme, même si cet état est identique. Un jeton de génération empêche le retour différé d’écraser un état plus récent. Le lancement d’accueil est limité à une fois, après l’animation de démarrage ou le premier rendu si elle ne joue pas.

Couverture exécutée au run vert 415 : `LotFRevealTests` (horloge manuelle, vrai TextBlock STA, nom accessible, clic, désactivation), `LotFOperationTests.Real_panel_shows_each_remark_and_one_conclusion` et `Loaded_sarcastic_glance_restores_after_1200ms_and_state_change_cancels_it` (vrais contrôles WPF).

## Priorités : table de l’annexe B

La liste et le nombre sont publiés sous le même verrou et avec la même date : scan de gravité au moins Attention, tri stable décroissant de gravité ; Diagnostic d’impact strictement positif, ordre existant conservé. La mesure la plus récente gagne, y compris entre scan et Diagnostic. Une mesure ancienne n’écrase ni la liste ni le nombre.

| Identifiant (préfixe) | Phrase | Bouton | Destination | Test |
| --- | --- | --- | --- | --- |
| disk-{X} (scan) | Le disque {X} est presque plein. | Ouvrir Nettoyage | écran Nettoyage | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| space.{X} (Diagnostic) | Le disque {X} est presque plein. | Ouvrir Nettoyage | écran Nettoyage | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| cleanup-potential-warning, cleanup-potential-attention | Des fichiers temporaires prennent de la place ({valeur}). | Ouvrir Nettoyage | écran Nettoyage | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| updates-preview | Des mises à jour d'applications sont disponibles. | Ouvrir Mises à jour | écran Mises à jour | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| updates-sensitive-preview | Certaines mises à jour touchent des éléments sensibles : à regarder avant d'installer. | Ouvrir Mises à jour | écran Mises à jour | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| windows.errors | Windows a signalé des erreurs ces dernières 24 heures. | Ouvrir Mises à jour | écran Mises à jour | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| device.{X} | Un périphérique ne fonctionne pas correctement. | Ouvrir Mises à jour | écran Mises à jour | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| interventions-recommended | Virgil propose une réparation à examiner. | Ouvrir Réparation | écran Réparation | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| cpu-usage, resources-cpu-sustained | Le processeur est très chargé. | Ouvrir Ressources | écran Ressources | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| memory-usage, resources-memory-high | La mémoire est presque pleine. | Ouvrir Ressources | écran Ressources | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| process-memory, resources-heavy-processes | Des programmes consomment beaucoup de ressources. | Ouvrir Ressources | écran Ressources | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| applications-unknown | Des applications sont mal identifiées : vérifiez-les avant toute désinstallation. | Ouvrir Applications | écran Applications | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| network-active-interface | Aucune connexion réseau active. | Ouvrir Réseau | écran Réseau | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| cpu.hot | Le processeur chauffe (90 °C ou plus). | Ouvrir Diagnostic | écran Diagnostic | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| gpu.hot.{X} | La carte graphique chauffe (90 °C ou plus). | Ouvrir Diagnostic | écran Diagnostic | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| disk.failure.{X} | Windows prévoit une panne de disque : sauvegardez vos fichiers maintenant. | Ouvrir Sauvegarde Windows | ms-settings:backup | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| process.{X} | Un lanceur de jeux consomme beaucoup en arrière-plan. | Ouvrir Diagnostic | écran Diagnostic | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| partial-error-{X} | Certaines mesures n'ont pas pu être lues. | Ouvrir Diagnostic | écran Diagnostic | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |
| tout autre identifiant | titre d'origine de la constatation, inchangé | Ouvrir Diagnostic | écran Diagnostic | `LotFPrioritiesTests.Every_annex_B_identifier_has_exact_phrase_button_and_safe_destination` |

Le ViewModel n’a que trois dépendances : `ISystemStatusStore`, `IProcessRunner`, `TimeProvider`. La navigation émet seulement une demande ; MainWindow réutilise les gestionnaires existants et leurs gardes de scan. L’ouverture Réseau conserve sa relecture déjà existante. La seule cible shell autorisée est `ms-settings:backup`, sans argument, avec `UseShellExecute = true`. Aucun service de scan, Diagnostic ou maintenance ne peut être invoqué par ce ViewModel.

États vides vérifiés exactement : « Aucune analyse récente : lancez un scan pour voir les priorités. » puis, après une mesure vide, « Aucun point prioritaire au dernier contrôle. ». L’heure de mesure utilise le fuseau du TimeProvider. Tests du vrai magasin : `LotFPriorityStoreTests`, dont publication par le vrai `DiagnosticAnalyzer.Analyze`.

## Steam

Une seule fonction App `SteamGameDetail` est utilisée par les chemins séquence et action simple : détail vide pour « mis à jour » et « à jour selon Steam » ; « Steam continue en arrière-plan. » pour « reste à installer » ; raison existante pour « en erreur ». Les autres cartes sont renvoyées sans changement. `SteamUpdateService`, les statuts, les clés et la ligne de source restent inchangés. `LotFSteamTests` vérifie les quatre statuts avec le vrai service de séquence dans les deux parcours ; les deux cas passent au run vert 415, sans modifier ce test dans le correctif Steam.

## Captures WPF dans Virgil-ui-smoke/lot-f

Les tests verts enregistrent les 16 PNG ci-dessous dans `artifacts/ui-smoke/lot-f/`. Le workflow inchangé téléverse ce répertoire dans Virgil-ui-smoke après les tests. Les vrais contrôles WPF sont rendus en STA ; les captures des jeux utilisent les résultats du vrai service de séquence avec backends factices, après confirmation du scénario.

- `panneau-en-cours.png`, `panneau-Neutral.png`, `panneau-Succeeded.png`, `panneau-NeedsReview.png`, `panneau-Failed.png`, `panneau-Cancelled.png`, `panneau-nettoyage-sobre.png`.
- `texte-partiel.png`, `texte-apres-clic.png`.
- `priorites-sans-mesure.png`, `priorites-aucun-point.png`, `priorites-trois-points.png` (dont Ouvrir Sauvegarde Windows).
- `visage-repos.png`, `visage-coup-oeil.png`.
- `steam-quatre-statuts.png`, `steam-seul-quatre-statuts.png`.

Les métadonnées et les logs d’upload de l’artefact sont vérifiés. L’archive n’a pas été inspectée visuellement depuis ce poste : le transfert du lien de fichier fourni par l’intégration reçoit HTTP 403. Aucun paramètre réseau n’est modifié. Les tests qui enregistrent les captures passent ; cela ne constitue pas une validation humaine des pixels ni de la vitesse réelle du texte.

## Périmètre et fichiers

Un seul test préexistant modifié sous l’exception F11 du 16/11 : une entrée de l’inventaire des consommateurs. Aucun autre test préexistant, workflow, réglage, secret, paquet NuGet, helper, garde ou service d’installation modifié. La composition nouvelle reste dans `AddVirgilViews` et la fabrique MainWindow ; `AddVirgilServices` ne change pas. Les trois documents produit ne changent que les phrases d’état du ton autorisées par R14.

- `docs/ACTIONS.md`
- `docs/PROJECT_STATUS.md`
- `docs/modules/CHATBOX_MODULE.md`
- `src/Virgil.App/Controls/HomePrioritiesPanel.xaml`
- `src/Virgil.App/Controls/HomePrioritiesPanel.xaml.cs`
- `src/Virgil.App/Controls/OperationProgressPanel.xaml`
- `src/Virgil.App/Controls/VirgilCoreControl.xaml.cs`
- `src/Virgil.App/Hosting/ServiceCollectionExtensions.cs`
- `src/Virgil.App/MainWindow.xaml`
- `src/Virgil.App/MainWindow.xaml.cs`
- `src/Virgil.App/Speech/SpeechReveal.cs`
- `src/Virgil.App/Speech/SpeechText.cs`
- `src/Virgil.App/Speech/VirgilLinePicker.cs`
- `src/Virgil.App/Speech/VirgilLines.cs`
- `src/Virgil.App/ViewModels/ApplicationsViewModel.cs`
- `src/Virgil.App/ViewModels/CleanupViewModel.cs`
- `src/Virgil.App/ViewModels/HomePrioritiesViewModel.cs`
- `src/Virgil.App/ViewModels/HomePriorityCatalog.cs`
- `src/Virgil.App/ViewModels/InterventionsViewModel.cs`
- `src/Virgil.App/ViewModels/LongOperationViewModel.cs`
- `src/Virgil.App/ViewModels/UpdatesViewModel.Sequence.cs`
- `src/Virgil.App/ViewModels/UpdatesViewModel.Single.cs`
- `src/Virgil.App/ViewModels/UpdatesViewModel.cs` (préparation Steam après scan, explicitement autorisée)
- `src/Virgil.Core/Status/SystemStatusStore.cs`
- `tests/Virgil.App.Tests/LotFBackupBoundaryTests.cs`
- `tests/Virgil.App.Tests/LotFOperationTests.cs`
- `tests/Virgil.App.Tests/LotFPrioritiesTests.cs`
- `tests/Virgil.App.Tests/LotFRevealTests.cs`
- `tests/Virgil.App.Tests/LotFSpeechTests.cs`
- `tests/Virgil.App.Tests/LotFSteamTests.cs`
- `tests/Virgil.Core.Tests/LegacyEffectArchitectureTests.cs` (une seule entrée autorisée)
- `tests/Virgil.Core.Tests/LotFPriorityStoreTests.cs`
- `docs/proofs/Lot-F.md`

## Limites et vérifications restantes

La CI Windows est la référence ; la compilation croisée Linux antérieure n’est pas une validation WPF. Une tentative locale de test a été interrompue par l’absence de `Microsoft.WindowsDesktop.App` sous Linux : aucun résultat d’exécution local n’est revendiqué.

Le rendu et la vitesse du texte sur le PC cible restent à vérifier à l’alpha.8. Les captures automatisées des contrôles ne remplacent pas cet essai. La liste de contrôle externe `/workspace/overmind/revues/liste-controle-virgil.md` n’est pas présente ; sa version reproduite dans le brief a été utilisée. Aucun feu vert de loops (relecture) n’est revendiqué et aucune relecture n’est demandée avant la vérification finale M7.
