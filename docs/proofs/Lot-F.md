# Lot F — Répliques, texte progressif et Priorités

Base exacte : `cd7cd53d31232024117510c878e421371d945b7f`. Branche `codex/lot-f`, PR [#86](https://github.com/bassetthomas-design/virgil-2.0/pull/86), conservée en brouillon. Aucun merge, amend, rebase ou force-push. Les décisions F1–F11 de loops (relecture) priment sur le brief initial.

## Preuves Windows

| Étape | Run | SHA du head | Résultat constaté |
| --- | --- | --- | --- |
| Tests seuls initiaux | [405 — 43446518904](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43446518904) | `373f9d5fa6a8721392b8d2b070d08e4852e85d82` | Compilation 0 avertissement, 0 erreur ; Core 1247 réussis + 2 nouveaux échecs ; App 757 réussis + 49 nouveaux échecs d’assertion. UI non exécutée. |
| Tests seuls complétés | [406 — 43447219034](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43447219034) | `41b748229322a89174e0b5d4771d1caeae7498bf` | Compilation 0 avertissement, 0 erreur ; mêmes totaux et échecs d’assertion. UI non exécutée. |
| Correctif séparé | [407 — 43828807319](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43828807319) | `3a24d27b587324b2180cb9ca25877b6ae137e77d` | Compilation 0 avertissement, 0 erreur ; Core 1248/1249, App 801/806. Run rouge. UI et publication non exécutées. |

**Validation finale bloquée : aucun run vert final n’existe à ce stade.** Le run 407 est rouge, même si sa compilation est sans avertissement. Son artefact partiel [Virgil-ui-smoke, ID 14207921750](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43828807319/artifacts/14207921750) a été publié ; métadonnées contrôlées : 3 131 208 octets, digest `sha256:b7bfe8b3e988645437d4e6a3a3daed1a9c8365cd9f4b41bb69055fb84c4b11f3`. Le contenu des PNG n’a pas été validé ; cet artefact rouge ne constitue pas la preuve finale P3/M7.

### Arrêt imposé par F11 : deux tests existants

1. `tests/Virgil.Core.Tests/LegacyEffectArchitectureTests.cs:92`, test `ProcessRunnerConsumersRemainExplicitlyInventoried` : la liste fermée des consommateurs App ne contient pas `HomePrioritiesViewModel.cs`. Or R9 exige explicitement que ce ViewModel reçoive `IProcessRunner`. Déplacer ou dissimuler cette référence pour contourner le garde ne respecterait pas son intention. Diff proposé, non appliqué : ajouter ce seul fichier à l’inventaire attendu, sans retirer les autres consommateurs.
2. `tests/Virgil.App.Tests/InterfaceAccentTests.cs:151`, test `Interface_labels_preserve_french_accents_in_all_source_projects` : son dictionnaire alimenté par le corpus ajoute automatiquement `charge → chargé`, `note → noté`, `fonctionne → fonctionné`, `mesures → mesurés`, `garde → gardé`. Les annexes A/B exactes introduisent les participes ; le test rejette ensuite des noms ou présents corrects, y compris « Un périphérique ne fonctionne pas correctement. » et « Certaines mesures n’ont pas pu être lues. ». Il signale également des phrases de fichiers hors périmètre, dont DiagnosticViewModel, DiagnosticAnalysis et SystemScanService. Réécrire les annexes ou ces textes existants enfreindrait F1, R13 ou la liste fermée.

Proposition précise pour la relecture, non appliquée :

```diff
--- tests/Virgil.Core.Tests/LegacyEffectArchitectureTests.cs
+++ tests/Virgil.Core.Tests/LegacyEffectArchitectureTests.cs
@@
-            "ServiceCollectionExtensions.cs", "UpdatesViewModel.cs", "VirgilReleaseViewModel.cs" }, app);
+            "HomePrioritiesViewModel.cs", "ServiceCollectionExtensions.cs", "UpdatesViewModel.cs", "VirgilReleaseViewModel.cs" }, app);
```

```diff
--- tests/Virgil.App.Tests/InterfaceAccentTests.cs
+++ tests/Virgil.App.Tests/InterfaceAccentTests.cs
@@ BuildDictionary
         var result = new Dictionary<string, string>(dictionary, StringComparer.OrdinalIgnoreCase);
+        // Homographes français : ne pas déduire une faute du seul participe rencontré.
+        var homographs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
+            { "charge", "note", "fonctionne", "mesures", "garde" };
@@
-            if (!plain.Equals(word, StringComparison.OrdinalIgnoreCase) && !valid.ContainsKey(plain))
+            if (!plain.Equals(word, StringComparison.OrdinalIgnoreCase) && !valid.ContainsKey(plain)
+                && !homographs.Contains(plain))
                 result.TryAdd(plain, word);
```

Cette seconde proposition limite seulement l’inférence nouvelle depuis le corpus ; le dictionnaire explicite, les contrôles contextuels existants et les fichiers Accents restent inchangés. Des nouveaux tests LotF devront distinguer homographes corrects et véritables accents manquants, avec preuve rouge puis verte. Ces deux modifications de tests existants requièrent une exception explicite à F11 et à la liste fermée. Elles ne sont ni écrites ni publiées.

### Quatre échecs des nouveaux tests, également à traiter

- `LotFSpeechTests.Catalogue_is_exact_complete_short_and_contains_no_forbidden_claim` : le contrôle par sous-chaîne ` COM` rejette à tort « en compte » de l’annexe A. Selon F1/R1, vérifier le terme technique COM comme mot entier, sans changer le catalogue ; correction d’attente à isoler et justifier selon M5.
- `LotFRevealTests.Chat_preserves_the_old_message_and_adds_welcome_after_it` : le test cherche le nom de variable `message` alors que le préfixe est ajouté à `cleanMessage`, après la normalisation existante. Les textes ne diffèrent pas. R6/R13 demandent une égalité du texte accessible, pas le nom d’une variable ; correction du nouveau test à isoler selon M5 ou renommage local sans changement de comportement.
- `LotFOperationTests.Real_panel_shows_each_remark_and_one_conclusion` : le comptage inclut un TextBlock masqué portant Step, d’où 2 au lieu de 1 ; R5/F11 et le test Lot C existant visent le texte visible. Corriger le nouveau test en contrôlant la visibilité réelle dans une fenêtre STA, sans changer les bilans ; M5 s’applique.
- `LotFSteamTests.Real_sequence_and_Steam_only_share_game_detail_rule(single: true)` : la recherche de carte Steam à la ligne 27 échoue avant toute action : le test ne prépare pas la liste des sources, chargée par le parcours existant d’aperçu. Préparer cet aperçu puis l’annuler dans le test avant l’action Steam seule, sans ajouter une lecture de production ni changer les statuts. Le cas séquence complète passe.

Aucune correction de code supplémentaire n’est entreprise après le constat des échecs existants : arrêt demandé par F11. Le présent document publie les preuves et le diff proposé avant la demande d’exception. La validation complète M7 reste à obtenir après déblocage.

Le complément de tests seuls conserve les identités Steam existantes, conformément à R10 (« les statuts, les clés, les compteurs et le résumé de la source Lanceurs ne changent pas ») : la carte simple a une clé `Launcher:SteamGame:<id>`, le panneau d’opération une clé `SteamGame:<id>`. Le nouveau test cherchait initialement la seconde dans les deux collections. Cette correction précède le premier run du correctif et ne change aucune attente de statut ou de détail. Le même commit complète le pilote de captures : texte partiel avec horloge manuelle, clic avant les captures finales et vrai panneau WPF pour les jeux Steam. Aucun test préexistant modifié.

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

Couverture prévue par les tests (voir les échecs et limites ci-dessus) : `LotFRevealTests` (horloge manuelle, vrai TextBlock STA, nom accessible, clic, désactivation), `LotFOperationTests.Real_panel_shows_each_remark_and_one_conclusion` et `Loaded_sarcastic_glance_restores_after_1200ms_and_state_change_cancels_it` (vrais contrôles WPF).

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

Une seule fonction App `SteamGameDetail` est utilisée par les chemins séquence et action simple : détail vide pour « mis à jour » et « à jour selon Steam » ; « Steam continue en arrière-plan. » pour « reste à installer » ; raison existante pour « en erreur ». Les autres cartes sont renvoyées sans changement. `SteamUpdateService`, les statuts, les clés et la ligne de source restent inchangés. `LotFSteamTests` vise les quatre statuts avec le vrai service de séquence dans les deux parcours : le cas séquence passe au run 407 ; la préparation de la liste des sources du cas Steam seul doit être complétée, comme indiqué ci-dessus.

## Périmètre et fichiers

Aucun test préexistant, workflow, réglage, secret, paquet NuGet, helper, garde ou service d’installation modifié. La composition nouvelle reste dans `AddVirgilViews` et la fabrique MainWindow ; `AddVirgilServices` ne change pas. Les trois documents produit ne changent que les phrases d’état du ton autorisées par R14.

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
- `tests/Virgil.App.Tests/LotFOperationTests.cs`
- `tests/Virgil.App.Tests/LotFPrioritiesTests.cs`
- `tests/Virgil.App.Tests/LotFRevealTests.cs`
- `tests/Virgil.App.Tests/LotFSpeechTests.cs`
- `tests/Virgil.App.Tests/LotFSteamTests.cs`
- `tests/Virgil.Core.Tests/LotFPriorityStoreTests.cs`
- `docs/proofs/Lot-F.md`

## Limites et vérifications restantes

La CI Windows est la référence ; la compilation croisée Linux antérieure n’est pas une validation WPF. Une tentative locale de test a été interrompue par l’absence de `Microsoft.WindowsDesktop.App` sous Linux : aucun résultat d’exécution local n’est revendiqué.

Le rendu et la vitesse du texte sur le PC cible restent à vérifier à l’alpha.8. Les captures automatisées des contrôles ne remplacent pas cet essai. La liste de contrôle externe `/workspace/overmind/revues/liste-controle-virgil.md` n’est pas présente ; sa version reproduite dans le brief a été utilisée. Aucun feu vert de loops (relecture) n’est revendiqué et aucune relecture n’est demandée avant la vérification finale M7.
