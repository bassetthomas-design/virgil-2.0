# Lot F — Répliques, texte progressif et Priorités

Base exacte : `cd7cd53d31232024117510c878e421371d945b7f`. Branche `codex/lot-f`, PR [#86](https://github.com/bassetthomas-design/virgil-2.0/pull/86), conservée en brouillon. Historique linéaire, aucun merge, amend ni force-push. Les décisions de loops (relecture) du 16/11 couvrent le scénario Steam seul et les corrections B1/B2 ci-dessous.

## Corrections B1/B2 décidées le 16/11

L’ancienne preuve de coup d’œil était insuffisante : sur le runner sans animations, le test acceptait Rest et les PNG repos/coup d’œil étaient identiques. Les runs verts 419/420 ne prouvaient donc pas ce comportement. Cette affirmation antérieure est corrigée ici.

- Tests seuls B1, commit `6aed8746fedaa0ce50d616f23b7c644273b66e5f` : appel forcé avec true, Sarcastic sans condition, retour Rest à 1 200 ms avec horloge manuelle (1199 puis 1 ms, sans Sleep), comparaison des pixels rendus des deux PNG. Le run rouge 421 échoue réellement sur Assert.NotNull : la surcharge interne bool manque encore ; compilation réussie, aucun échec d’environnement.
- Correctif B1 séparé, commit `428b8dac845f425e1a9b46d408aa59be36218f53` : la méthode publique appelle `ShowSarcasticGlance(UserAnimationsEnabled())`. La surcharge interne bool permet le test indépendant des préférences CI ; GlanceClock, un TimeProvider interne, pilote le minuteur unique de 1,2 s. Les gardes chargé/visible/compact/état et le jeton de génération sont conservés. Le run propre 422 est vert et l’assertion de différence des pixels passe.
- B2 séparé, commit `766cb525c5acbdae80d9043103870fbe2449f5d0` : tous les littéraux découpés de VirgilLines et HomePriorityCatalog sont remis en entier. Exception décidée par loops (relecture) le **16/11** : cinq entrées motivées dans ValideSansAccent.txt seulement, `garde`, `fonctionne`, `note`, `charge`, `mesures`, pour distinguer noms/présents des participes accentués. InterfaceAccentTests et MotsAccentues.txt restent identiques à la base. Les Assert.Equal gardent les annexes A 33/33 et B 22/22 exactes.

`texte-partiel.png` utilisait un TextBlock de test sans le style du chat, d’où le noir sur noir. L’interface réelle emploie déjà VirgilChatMessageText, hérité de VirgilHudSecondaryText, et la réplique du panneau est blanche. Le test applique maintenant le style réel du chat et vérifie le rapport de contraste texte/fond ≥ 4,5:1 avant la capture. Ce test passe dès le run rouge B1 ; aucune couleur de production n’a été changée.

## Décision finale Steam du 16/11 : production restaurée, préparation du test corrigée

Depuis D2, ScanUpdatesCommand ne doit lancer ni scan de séquence ni nouvelle détection de lanceurs. MergeSingleApplications conserve son fonctionnement. La carte Launcher:Steam provient des SingleItems du vrai UpdateSequenceService, préparés par l’aperçu de séquence. L’échec du test Steam seul à la ligne 27 était une erreur de préparation du nouveau test, pas un motif pour ajouter cette lecture en production.

Les deux changements de production rejetés sont annulés entièrement, dans deux commits séparés générés par `git revert --no-edit`, puis publiés par l’action GitHub intégrée :

| Commit annulé | Commit de revert publié | Opération |
| --- | --- | --- |
| `9965ef70027eccb7c479639f18c28133fb8d1240` | `c42c47163a4d715c8888dd3b9ef28fefaf2600a9` | Revert de LoadScannedSteamCardAsync et de son appel dans ScanUpdatesAsync |
| `59ef00a7d72bf4fcf078c5257782063f854caa96` | `7cf628f313cfa13ecadf280737645afd9a94206d` | Revert complet de la préparation des sources après scan et des autres changements de ce commit |

**UpdatesViewModel.cs et UpdatesViewModel.Single.cs sont strictement identiques à leur état de `2aed3c5`.** La comparaison `git diff 2aed3c5 HEAD --` sur ces deux chemins est vide. Les blobs vérifiés sont :

| Fichier | Blob à 2aed3c5 et dans la version corrigée |
| --- | --- |
| `src/Virgil.App/ViewModels/UpdatesViewModel.cs` | `212f0e7407285df6dae0edd2503532f60622cf7e` |
| `src/Virgil.App/ViewModels/UpdatesViewModel.Single.cs` | `3b1cc2ead44b26a071923f746c70212501c6c828` |

Commit dédié `test(lot-f): prepare Steam-only scenario through the existing sequence preview`, SHA `b5b97cce4100d806d91c50854223aa5e016bf807` : conformément à la décision de loops (relecture) du **16/11**, seule la préparation de la branche single du nouveau LotFSteamTests est corrigée. Motif : contexte de test erroné ; la carte Steam est préparée par l’aperçu existant de D2, pas par le scan simple.

```diff
--- tests/Virgil.App.Tests/LotFSteamTests.cs
+++ tests/Virgil.App.Tests/LotFSteamTests.cs
@@ branche single uniquement
-            await vm.ScanUpdatesCommand.ExecuteAsync(null);
+            var preview = vm.RunAllCommand.ExecuteAsync(null);
+            vm.SkipDecisionCommand.Execute(null);
+            await preview;
```

Rien d’autre ne change dans ce test : Assert.Single sur Step.Id == "Steam", quatre statuts et détails exacts, Launches == 1, Closes == 1 et captures sont conservés. Le cas séquence complète est inchangé. Les deux cas passent au run 419 ; aucun nouveau correctif de production n’est donc nécessaire pour les compteurs. Aucune exclusion des cartes d’information n’est retirée.

## Preuves Windows

| Étape | Run | SHA du head | Résultat constaté |
| --- | --- | --- | --- |
| Tests seuls initiaux | [405 — 43446518904](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43446518904) | `373f9d5fa6a8721392b8d2b070d08e4852e85d82` | Compilation 0 avertissement, 0 erreur ; Core 1247 réussis + 2 nouveaux échecs ; App 757 réussis + 49 nouveaux échecs d’assertion. UI non exécutée. |
| Tests seuls complétés | [406 — 43447219034](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43447219034) | `41b748229322a89174e0b5d4771d1caeae7498bf` | Compilation 0 avertissement, 0 erreur ; mêmes totaux et échecs d’assertion. UI non exécutée. |
| Correctif initial rouge | [407 — 43828807319](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43828807319) | `3a24d27b587324b2180cb9ca25877b6ae137e77d` | Compilation 0 avertissement, 0 erreur ; Core 1248/1249, App 801/806. Six échecs ; UI non exécutée. |
| Tests de correction, décisions du 16/11 | [411 — 43830615560](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43830615560) | `451e45dfec7901710a8d6307a96c998b2472752a` | Compilation 0 avertissement, 0 erreur ; Core 1249/1249, App 803/807. Quatre échecs App. |
| Rouge avant correction de la préparation Steam | [412 — 43831007548](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43831007548) | `5f1f15e029c616e647575eddb48bae0e4a226d0e` | Compilation 0 avertissement, 0 erreur ; Core 1249/1249, App 806/807. Steam seul échoue sur la collection vide. |
| Changement de production rejeté | [414 — 43832572292](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43832572292) | `59ef00a7d72bf4fcf078c5257782063f854caa96` | Core 1249/1249, App 805/807. Deux tests existants en échec : ligne Lanceurs indue et acquittement d’annulation. Commit annulé. |
| Version corrigée, avant documentation finale | [419 — 43835834477](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43835834477) | `b5b97cce4100d806d91c50854223aa5e016bf807` | **VERT : compilation 0 avertissement, 0 erreur ; Core 1249/1249, App 807/807, UI 14/14. Publication et installateur réussis.** |
| Ancien head documentaire, avant B1/B2 | [420 — 43836841119](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43836841119) | `cb070d8f90ec61f722a268375459e6e06c426b5f` | VERT, sans preuve effective du coup d’œil ; remplacé par B1. |
| B1 tests seuls, rouge | [421 — 43839594162](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43839594162) | `6aed8746fedaa0ce50d616f23b7c644273b66e5f` | Compilation 0 avertissement, 0 erreur ; Core 1249/1249, App 806/807. Unique échec : surcharge bool absente (Assert.NotNull). |
| B1 correctif, run propre | [422 — 43840180160](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43840180160) | `428b8dac845f425e1a9b46d408aa59be36218f53` | **VERT : 0 avertissement, 0 erreur ; Core 1249/1249, App 807/807, UI 14/14.** |
| B1+B2, avant dernier commit documentaire | [423 — 43840327391](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43840327391) | `766cb525c5acbdae80d9043103870fbe2449f5d0` | **VERT : 0 avertissement, 0 erreur ; Core 1249/1249, App 807/807, UI 14/14 ; publication et installateur réussis.** |

Les anciens runs verts 415/416 portaient les scans de séquence ajoutés à ScanUpdatesCommand. Leur résultat de CI ne validait pas le respect du périmètre : les deux commits concernés sont annulés. **Ces anciens runs ne sont pas les preuves finales de la version restaurée.** Les runs rouges ci-dessus restent des preuves historiques d’échec.

Le run 419 valide la production restaurée et la préparation de test décidée le 16/11. Artefact vert [Virgil-ui-smoke, ID 14211410685](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43835834477/artifacts/14211410685), 5 948 228 octets ; digest publié `sha256:d049514eff683f854725d5baacd8a17bc10eeab64683b5231d25bce6bdae7bb8`. Les logs confirment l’upload de 90 fichiers au total. Application et installateur sont également publiés.

Artefact propre B1 : [Virgil-ui-smoke 14212842854](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43840180160/artifacts/14212842854), digest `sha256:8b54bb977bde8a7a2d054083e93662a4e01f8bf0a3bf521d3520b65efa2a5a57`.

Artefact B1+B2 : [Virgil-ui-smoke 14212298578](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43840327391/artifacts/14212298578), 5 941 473 octets, digest `sha256:6298e0bd343eae1b4120314a7295c1e63fa02679b6090c1618a6c5d8e54fbd88`. Les tests enregistrent les PNG lot-f, avec différence des pixels repos/coup d’œil et contraste du texte vérifiés par assertions.

### M7 : dernier commit documentaire, puis vérification du head final

Le présent document est publié seulement après la validation verte du run 423. **Aucun autre push ne suivra ce dernier commit documentaire.** Son propre run Windows sera vérifié jusqu’à sa conclusion réelle. Conformément à M7 du brief, le numéro et l’ID du run vert FINAL sur ce head final, le SHA exact du head et l’ID de son artefact Virgil-ui-smoke seront inscrits dans la description de la PR #86, sans réécrire ce document ni pousser un autre commit. Le run 423 n’est pas présenté comme le run du futur head documentaire. Le SHA final et son run ne peuvent être autoréférencés dans ce commit ; la description de PR constitue la référence finale après vérification, comme prévu par M7.

### Autres exceptions et corrections décidées par loops (relecture), 16/11

- Exception F11 unique sur un test préexistant, commit dédié `ac143e3c224efec6d3fb3506b97d012d6d215374` : seul ajout alphabétique de HomePrioritiesViewModel.cs dans la liste App de LegacyEffectArchitectureTests.ProcessRunnerConsumersRemainExplicitlyInventoried. Aucun autre octet de ce test ni autre test préexistant n’est modifié. Le nouveau LotFBackupBoundaryTests, commit `ee35c949749d0dd8e801ad659a079c26711b3afc`, prouve uniquement ms-settings:backup, sans argument, élévation, attente de processus ni RunAsync ; navigation d’écran sans lancement et point étranger refusé.
- Correction d’une attente erronée décidée par loops (relecture) le 16/11, commit dédié `451e45dfec7901710a8d6307a96c998b2472752a` : dans le seul nouveau LotFSpeechTests, entrée interdite " COM" conservée et comparaison Ordinal sensible à la casse. Les autres termes restent insensibles à la casse et chaque Assert.Equal du catalogue est conservé. La réplique « Annulation prise en compte. Le détail est ci-dessus. » reste exacte ; cette correction est justifiée selon M5.
- Exception B2 du 16/11 : ValideSansAccent.txt reçoit uniquement les cinq homographes motivés ci-dessus. Les littéraux sont entiers ; aucun contournement du contrôle d’accents ne subsiste. InterfaceAccentTests et MotsAccentues.txt ne changent pas.
- Le commit de production `5f1f15e029c616e647575eddb48bae0e4a226d0e` conserve la normalisation du chat, puis la chaîne exacte `"[VIRGIL]\n" + message`, et vide le TextBlock de phase masqué à la fin pour ne garder qu’une conclusion dans le panneau. Les propriétés Step, Steps, Commentary et FinalSummary conservent leurs textes et règles.

Le complément initial de tests seuls corrigeait l’identification des cartes Steam sans changer leurs clés : Launcher:SteamGame:<id> pour la carte App, SteamGame:<id> pour l’étape du panneau. Cette correction précède les runs du correctif de production et ne modifie aucune attente de statut ou de détail.

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

Couverture exécutée aux runs verts 422 et 423 : `LotFRevealTests` (horloge manuelle, vrai TextBlock STA, nom accessible, clic, désactivation), `LotFOperationTests.Real_panel_shows_each_remark_and_one_conclusion` et `Loaded_sarcastic_glance_restores_after_1200ms_and_state_change_cancels_it` (vrais contrôles WPF, horloge manuelle et pixels différents pour le coup d’œil).

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

Une seule fonction App `SteamGameDetail` est utilisée par les chemins séquence et action simple : détail vide pour « mis à jour » et « à jour selon Steam » ; « Steam continue en arrière-plan. » pour « reste à installer » ; raison existante pour « en erreur ». Les autres cartes sont renvoyées sans changement. `SteamUpdateService`, les statuts, les clés et la ligne de source restent inchangés. `LotFSteamTests` vérifie les quatre statuts avec le vrai service de séquence dans les deux parcours ; les deux cas passent au run vert 419 avec la seule correction de préparation autorisée dans la branche single.

## Captures WPF dans Virgil-ui-smoke/lot-f

Les tests verts enregistrent les 16 PNG ci-dessous dans `artifacts/ui-smoke/lot-f/`. Le workflow inchangé téléverse ce répertoire dans Virgil-ui-smoke après les tests. Les vrais contrôles WPF sont rendus en STA ; les captures des jeux utilisent les résultats du vrai service de séquence avec backends factices, après confirmation du scénario.

- `panneau-en-cours.png`, `panneau-Neutral.png`, `panneau-Succeeded.png`, `panneau-NeedsReview.png`, `panneau-Failed.png`, `panneau-Cancelled.png`, `panneau-nettoyage-sobre.png`.
- `texte-partiel.png`, `texte-apres-clic.png`.
- `priorites-sans-mesure.png`, `priorites-aucun-point.png`, `priorites-trois-points.png` (dont Ouvrir Sauvegarde Windows).
- `visage-repos.png`, `visage-coup-oeil.png`.
- `steam-quatre-statuts.png`, `steam-seul-quatre-statuts.png`.

Les métadonnées et les logs d’upload de l’artefact sont vérifiés. L’archive n’a pas été inspectée visuellement depuis ce poste : le transfert du lien de fichier fourni par l’intégration reçoit HTTP 403. Aucun paramètre réseau n’est modifié. Les tests qui enregistrent les captures passent ; cela ne constitue pas une validation humaine des pixels ni de la vitesse réelle du texte.

## Périmètre et fichiers

Un seul fichier C# de test préexistant modifié sous l’exception F11 du 16/11 : une entrée de l’inventaire des consommateurs. La ressource ValideSansAccent.txt reçoit les cinq entrées de l’exception B2. Aucun autre test préexistant, workflow, réglage, secret, paquet NuGet, helper, garde ou service d’installation modifié. La composition nouvelle reste dans `AddVirgilViews` et la fabrique MainWindow ; `AddVirgilServices` ne change pas. Les trois documents produit ne changent que les phrases d’état du ton autorisées par R14.

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
- `src/Virgil.Core/Status/SystemStatusStore.cs`
- `tests/Virgil.App.Tests/Accents/ValideSansAccent.txt` (cinq homographes motivés, exception B2)
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

Le rendu et la vitesse du texte sur le PC cible restent à vérifier à l’alpha.8. Les captures automatisées des contrôles ne remplacent pas cet essai. La liste de contrôle externe `/workspace/overmind/revues/liste-controle-virgil.md` existe, comme précisé par loops (relecture). Sa copie n’est pas montée/accessible dans cet environnement Cloud ; sa version reproduite dans le brief a été utilisée. L’ancienne affirmation générale d’inexistence est retirée. Aucun feu vert de loops (relecture) n’est revendiqué et aucune relecture n’est demandée avant la vérification finale M7.
