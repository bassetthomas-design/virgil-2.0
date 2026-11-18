# Lot T4 — Priorités par source

Le scan et le diagnostic conservent leurs propres constats. Un constat reste affiché « à revérifier » quand un nouveau contrôle de sa source ne le remesure pas. Si le contrôle le remesure et ne le signale plus, la liste affiche un retour à la normale jusqu’au contrôle suivant de cette source. La tuile compte les lignes actives et à revérifier, et sa commande ouvre la liste de l’accueil.

## CI Windows

- Tests seuls, [run #473](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44329470366), SHA `b7cbe6b1aae724943e0e98349d85c216da2137a6`. La compilation a réussi avec zéro avertissement et zéro erreur. Les neuf tests du tableau ont échoué sur leurs assertions. Les deux gardes ont réussi.
- Correctif, SHA `ec10627e8620e882371dab6d7ad397108f0c102e`. La première tentative a compilé sans avertissement ni erreur et les 2 191 tests hors smoke UI ont réussi. Trois smoke tests ont échoué. `Ui_thread_stays_responsive_during_navigation_and_complete_scan` a mesuré 825 ms pour une limite de 500 ms. `Smoke_run_has_no_destructive_side_effect` et `Cleanup_summary_is_captured_without_execution` ont signalé « Vue/contrôle UIA invisible : UpdatePlanStep.Drivers ». Échec connu de l’environnement, sans rapport avec T4 selon virgil (relecture). [Run #474, tentative 1](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44330366949/attempts/1)
- La relance sur le même SHA a réussi avec zéro avertissement et zéro erreur. Les 2 191 tests hors smoke UI et les 14 smoke tests ont réussi. L’artefact `Virgil-ui-smoke` de la relance porte l’ID `14440041847` et contient `lot-t4/priorites.txt`. [Run #474, tentative 2](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44330366949/attempts/2) · [Artefact](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44330366949/artifacts/14440041847)
- La description de la PR rassemble les références de validation du lot. [PR #97](https://github.com/bassetthomas-design/virgil-2.0/pull/97)

## Assertions du run de tests seuls

Les extraits reprennent la troncature du journal xUnit (`···`). Les tests S sont dans `LotPrioritesStoreTests`, les tests A dans `LotPrioritesTests`.

| Exigence | Test | Assertion du run #473 |
| --- | --- | --- |
| R-1, R-2 · Conserver les deux sources et résoudre lors du scan suivant | S1 `Scan_points_survive_a_diagnostic_and_return_to_normal_on_the_next_scan` | `Assert.Equal() Failure: Collections differ`<br>Expected `["disk-C:", "windows.errors"]`<br>Actual `["windows.errors"]` |
| R-2 · Conserver les constats hors couverture du scan rapide | S2 `Quick_scan_keeps_deep_only_points_to_recheck_and_deep_scan_resolves_only_what_it_measured` | `Assert.Equal() Failure: Collections differ`<br>Expected `["cleanup-potential-warning", "process-memory", "updates-preview"]`<br>Actual `[]` |
| R-2 · Distinguer un volume remesuré et un journal indisponible | S3 `Diagnostic_keeps_unmeasured_point_to_recheck_and_resolves_measured_one` | `Assert.Equal() Failure: Collections differ`<br>Expected `["windows.errors"]`<br>Actual `[]` |
| R-2 · Remplacer les identifiants positionnels et ignorer un scan plus ancien | S4 `Positional_points_are_replaced_unreadable_cpu_stays_to_recheck_and_older_scan_is_ignored` | `Assert.Equal() Failure: Collections differ`<br>Expected `["partial-error-1", "cpu-usage"]`<br>Actual `["partial-error-1"]` |
| R-3, R-4 · Source, heure et état de chaque ligne | A1 `Each_item_shows_source_measure_time_and_recheck_state` | `Assert.Equal() Failure: Collections differ`<br>Expected `["Le disque C: est presque plein.", "Windows a signalé des erreurs ces dernières 24 heu"···]`<br>Actual `["Windows a signalé des erreurs ces dernières 24 heu"···]` |
| R-3, R-4 · Texte de retour à la normale | A2 `Resolved_points_show_return_to_normal_with_source_and_time` | `Assert.Equal() Failure: Collections differ`<br>Expected `["Windows a signalé des erreurs ces dernières 24 heu"···]`<br>Actual `[]` |
| R-4 · Compteur, états vides et commande de la tuile | A3 `Tile_counts_the_displayed_list_and_separates_analysis_to_run_from_no_priority` | `Assert.Equal() Failure: Values differ`<br>Expected `2`<br>Actual `1` |
| R-5, R-6 · Rendu du panneau et commande du bouton | A4 `Rendered_panel_and_tile_show_source_time_return_to_normal_and_open_the_list` | `Assert.Contains() Failure: Item not found in collection`<br>Not found `"Le disque C: est presque plein."`<br>Collection `["PRIORITÉS", "Windows a signalé des erreurs ces dernières 24 heu"···, "Ouvrir Mises à jour", "Mesuré à 18:34"]` |
| R-7 · Hébergement et navigation dans MainWindow | A5 `Main_window_hosts_the_tile_and_routes_it_to_the_home_list` | `Assert.Equal() Failure: Collections differ`<br>Expected `["HomePriorityTile"]`<br>Actual `["Grid"]` |

## Tests verts dès le départ (gardes)

- G-1 `Older_publication_of_the_same_source_never_replaces_points` garde les constats et la même instance de liste après une publication plus ancienne de la même source.
- G-2 `Rendered_panel_distinguishes_analysis_to_run_from_no_priority` vérifie les deux états vides dans le panneau WPF.

## Exception E1

> E1 tests/Virgil.Core.Tests/LotFPriorityStoreTests.cs, test l.28 `Newer_real_diagnostic_replaces_scan_but_older_measure_does_not` (l.27-41). Il fige l'écrasement des constats du scan par le diagnostic, ce que le constat 4b du tri (l.22) demande de supprimer.
>   - l.28 devient : `public void Newer_real_diagnostic_keeps_scan_points_and_older_scan_does_not_replace()`
>   - l.37 devient : `Assert.Equal(new[] { "disk-C:" }, points.Select(p => (string)Read(p, "Id")));` (diagnostic sain : aucune issue d'impact > 0, comme DiagnosticAnalysisTests l.18-24 ; le constat du scan reste).
>   - l.39 devient : `store.PublishScan(Report(at.AddMinutes(-1), new ScanFinding("cpu-usage", "", "Ancien", "", "", ScanSeverity.Critical, null)));` Motif : l'ancienneté se juge désormais par source. Le premier scan est daté `at` : un scan à `at` est aussi récent, donc accepté. `at − 1 min` est plus ancien et doit être ignoré, ce que vérifie l.40 (`Assert.Same`), inchangée.
>   - l.38 et l.40 inchangées. Rien d'autre dans ce fichier.

## Réserves

- W1 Un même disque peut avoir une ligne du scan et une ligne du diagnostic.
- W2 Un résumé approfondi avec erreur garde ses constats « à revérifier » jusqu’à un contrôle approfondi sans erreur.
- W3 A5 couvre le câblage de MainWindow par lecture de source et A4 vérifie la commande du bouton dans une fenêtre isolée. Le défilement réel dans MainWindow reste à vérifier sous Windows.
- W4 Un identifiant dont la couverture est inconnue reste « à revérifier » jusqu’à ce qu’un contrôle de sa source le signale à nouveau ou le remplace.

## Liste de contrôle

1. Les références É1 à É7 correspondent au code de la base `857d217`.
2. Les attendus sont fixés à partir des fixtures. L’horloge UTC+2 donne 18 h 24 pour 16:24Z, et le diagnostic sain contient un volume C: lisible.
3. Les neuf échecs sont des assertions à valeurs fixes. G-1 et G-2 ont réussi.
4. Le diff couvre les 12 fichiers du lot. E1 modifie trois lignes du test existant.
5. Les constats et retours à la normale restent en mémoire. Les tests vérifient zéro appel au lanceur et écrivent leur preuve sous `artifacts/ui-smoke/lot-t4/`.
6. La CI exécute `InterfaceAccentTests` sur le corpus français.
7. A4 écrit `lot-t4/priorites.txt` dans `Virgil-ui-smoke` avant les assertions de rendu.
8. Le contexte et les décisions renvoient au retour de test alpha.8 et à virgil (relecture).

## Manipulations Windows

1. Lancer un scan rapide et noter les lignes de la liste, le compteur de la tuile Priorités et l’heure « Mesuré à ».
2. Lancer le diagnostic. Vérifier que les lignes du scan restent avec « Scan · mesuré à … » et que celles du diagnostic s’ajoutent avec « Diagnostic · mesuré à … ». Comparer le compteur de la tuile au nombre de constats dans la liste.
3. Si un disque était presque plein, libérer de la place et relancer un scan. Vérifier que le constat devient « Espace du disque … — retour à la normale lors du scan de … ».
4. Après un scan approfondi qui affiche des constats « Mises à jour » ou « Consommation des programmes », lancer un scan rapide. Vérifier que ces lignes restent avec « · à revérifier ».
5. Ouvrir Nettoyage, cliquer sur la tuile Priorités et vérifier le retour à l’accueil ainsi que le défilement jusqu’à la liste.
6. Au démarrage, avant tout contrôle, vérifier « -- » et « ANALYSE À LANCER » sur la tuile. Après un contrôle sans constat, vérifier « 0 » et « AUCUNE PRIORITÉ DÉTECTÉE ».
