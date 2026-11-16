# Lot H — Correctifs avant alpha.8

PR de code [#88](https://github.com/bassetthomas-design/virgil-2.0/pull/88), branche `codex/lot-h`, fusionnée le 16/11/2026 (squash, main `411fc89228e5aa2be074a1e80c6ac320b16aaab9`). Base vérifiée : `2a204663494a47684ff46b71ce44c129ccddea86`. H-doc est exclu de cette PR. Aucun merge, amend ou force-push.

## Séquence de preuve Windows

La CI Windows est la référence ; le SDK .NET est absent de l'environnement Linux. Aucun build ni résultat de tests local n'est revendiqué.

| Étape | Head | Run Windows | Résultat |
| --- | --- | --- | --- |
| Tests seuls et E1–E5 | `19ab1826b73e0296bf492fe5139e432dc0fa4894` | [43878523455](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43878523455), tentatives 1 et 2 | Compilation réussie, 0 avertissement, 0 erreur. Tentative 1 : Core 1249/1250, App 819/839, dont un timeout extérieur au lot. Tentative 2 : Core 1249/1250, App 820/839 ; échecs d'assertion du lot seulement. |
| Préparation Pilotes, tests seuls | `d7346314a1662b0d2fd05728815ca9fd650f00c4` | [43884126798](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43884126798) | Compilation réussie, 0 avertissement, 0 erreur ; Core 1249/1250 et App 820/839, échecs d'assertion uniquement. |
| Correctif de production séparé | `1a349c66decd47659ed53f6f986930179b61e90f` | [43884641932](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43884641932) | Compilation réussie, 0 avertissement, 0 erreur ; Core 1250/1250, App 838/839. Seul le résumé Pilotes fixé dans P1 diffère ; décision M5 ci-dessous. |
| Correctif validé après décision M5, assertion seule | `228d91bd1f01237690563088b253ed0ab94c0b28` | [43895724670](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43895724670) | **Vert** : Core 1250/1250, App 839/839, UI 14/14 ; 0 avertissement, 0 erreur. Artefact Virgil-ui-smoke `14238289577`. |

Le run vert final sur le head documentaire final et son artefact `Virgil-ui-smoke` sont référencés dans la description de la PR #88, conformément à M12. Le run rouge n'est pas une preuve finale.

Au run rouge `43884126798`, les 20 cas en échec sont :

- `LotHBoundaryTests.Windows_port_and_lightweight_adapter_have_exact_contract_and_source_boundary` ; `Real_composition_reads_fake_port_once_only_on_resolution_and_launches_only_on_command` ; `Unreadable_screen_still_opens_exact_unknown_screen_link` ; `Backup_failure_is_visible_and_next_success_clears_it` (failure 0, 1 et 2).
- `LotHSequenceTests.Whole_word_application_identity_keeps_success_text_intact_on_real_sequence` ; `Single_source_counts_both_checked_launchers` ; `Real_driver_read_failure_produces_unavailable_card_and_source_summary`.
- `LotHWpfTests.View_name_survives_state_changes_while_status_and_unnamed_face_follow_profile` ; `Needs_review_real_panel_is_orange_with_theme_and_captured`.
- `LotHJargonTests.Real_scan_cards_and_validation_show_plain_action_without_changing_prepared_command`.
- `LotHLauncherSummaryTests.Steam_without_games_is_current_only_after_its_own_check_and_keeps_other_priorities`.
- E1 : `LotGViewModelTests.Production_sources_have_no_forbidden_effects_and_wiring_is_in_views_only`.
- E2 : `UpdatesViewModelTests.ScanPersistsRendersAndSelectionTracksAvailableCards` et `ExecutionRequiresDecisionForCurrentItem` (ValidationRequired, Sensitive, Safe).
- E3 : `D2SingleItemTests.Application_button_has_exact_new_labels_and_preserves_legacy_selection`.
- E4 : `LotEProgressColorTests.Typed_final_state_colors_the_real_bar_without_changing_completion` (NeedsReview).

La tentative 1 du run `43878523455` a aussi échoué dans `L6AppBehaviorTests.Escape_closes_diagnostic_validation_even_when_process_grid_has_focus`, `tests/Virgil.App.Tests/L6BehaviorTests.cs:80`, garde de 5 secondes sur `PresentationTask`. Ce test est repassé vert à la tentative 2 du même run, sans modifier son fichier ; il passe aussi au run du correctif `43884641932`. Le premier résultat, qui contenait un timeout, n'est pas présenté comme rouge exclusivement d'assertions.

## Exigences et vérifications

| Exigence | Test ou preuve |
| --- | --- |
| R1 et P4 : port, adaptateur léger, DI transitoire à fabrique explicite, lecture unique et absence de lancement à la résolution | `LotHBoundaryTests.Windows_port_and_lightweight_adapter_have_exact_contract_and_source_boundary` et `Real_composition_reads_fake_port_once_only_on_resolution_and_launches_only_on_command` ; vraie composition, faux port DispatchProxy avec compteur d'instance, sans contrôle WPF. E1 vérifie le câblage ; `CompositionRootTests` inchangé. |
| R2 : écran illisible → non déterminé, lien B1 exact | `LotHBoundaryTests.Unreadable_screen_still_opens_exact_unknown_screen_link` ; un seul lancement, aucune erreur affichée ni sentinelle dans l'URL. |
| R3 : trois échecs d'ouverture puis succès | `LotHBoundaryTests.Backup_failure_is_visible_and_next_success_clears_it` (trois cas), vrai store et priorité Sauvegarde, requête exacte sans navigation. |
| R4 : remplacement par mot entier | `LotHSequenceTests.Whole_word_application_identity_keeps_success_text_intact_on_real_sequence`, vraie séquence Winget et scan final ; statut et détail imposés conservés. Méthode privée unique pour Id et Scope, motif échappé, comparaison insensible à la casse et culture invariante. |
| R5 : nom de vue conservé, état dans ItemStatus, visage sans nom suivant le profil | `LotHWpfTests.View_name_survives_state_changes_while_status_and_unnamed_face_follow_profile`, vrai contrôle compact en STA, thème et vrai UpdatesView.xaml. |
| R6 : compte à jour selon Steam en mode un élément | `LotHSequenceTests.Single_source_counts_both_checked_launchers`, vraie fixture, deux cartes Steam/Battle.net, recalcul via InspectDriversCommand. |
| R7 : Steam seul contrôlé sans jeu, autres priorités conservées | `LotHLauncherSummaryTests.Steam_without_games_is_current_only_after_its_own_check_and_keeps_other_priorities`, quatre cas (Steam contrôlé, autre lanceur non pris en charge, Steam non contrôlé, liste vide). |
| R8 : cartes et validation sans jargon | `LotHJargonTests.Real_scan_cards_and_validation_show_plain_action_without_changing_prepared_command` ; vraie préparation du ViewModel, lignes et action A1 exactes, commande préparée inchangée ; E2 et E3. |
| R9 : barre orange | E4 et `LotHWpfTests.Needs_review_real_panel_is_orange_with_theme_and_captured`, vraie barre avec thème ; PNG lot-h/barre-a-verifier.png. Le visage attention conserve le jaune. |
| R10 : troncature indépendante | `LotHLinkTests.Independently_fixed_truncation_boundaries_preserve_title_labels_and_whole_body`, attendus B2–B4 écrits en dur, limites 2000/772/771/400/238/237, titre et labels conservés. Test de couverture vert dès le premier commit de tests seuls. `LotGLinkTests` reste inchangé. |
| R10 : visage chargé Error/Warning | `LotHWpfTests.Loaded_visible_error_and_warning_reject_glance`, vraie fenêtre montrée, IsLoaded/IsVisible affirmés, entrée interne bool appelée par réflexion sur la méthode. Test de couverture vert dès le premier commit de tests seuls ; `LotFOperationTests` reste inchangé. |
| R10 : rendu Pilotes indisponible | `LotHSequenceTests.Real_driver_read_failure_produces_unavailable_card_and_source_summary`, vrai service et cache chargé explicitement ; portée limitée et décisions M5 détaillées ci-dessous. |
| R11 : garde anti-blocage | E5 uniquement ; horloge factice et assertions inchangées. |
| R12–R13 : exclusions, sécurité, accents et régressions | Suite Core/App existante et Virgil.UiTests inchangée hors E1–E5 ; aucun AutomationId, workflow, vue, thème, contrat ou parcours de confirmation modifié. Résultats Windows dans la séquence ci-dessus et description finale de la PR. |
| R14 : preuves et statut | Ce document et l'unique ligne ajoutée à docs/PROJECT_STATUS.md. |

## R10 — portée du test Pilotes et décisions M5

Run vert FINAL : [run 43896869651](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43896869651), head `79d95cd348c6e92d402087d96f8e51504783502c`, Core 1250/1250, App 839/839, UI 14/14, artefact Virgil-ui-smoke 14240036127.

« ReadSingleItemsAsync n'est appelée par aucun code de l'appli ; ce test vérifie l'affichage d'une lecture des pilotes en échec, pas le parcours de l'aperçu » — décision « loops (relecture), 16/11 16h20 », option A (réponse M5b).

Le commit de tests seuls `d734631` conserve une référence au vrai `UpdateSequenceService` injecté via `wrap`, puis appelle explicitement `sequence.ReadSingleItemsAsync(default)` après RunAll/Skip. `InspectDriversCommand` recharge les cartes du cache. Aucun champ privé n'est manipulé, aucune fixture existante n'est modifiée et aucun appel de production n'est ajouté. Ce test constitue une couverture du rendu App depuis le cache, pas une preuve d'un parcours utilisateur d'aperçu.

Règle M5 : décision « loops (relecture), 16/11 15h35 ». Le `ReviewBackend` recopié lit toujours un BIOS : `IBiosInformationReader` rend `BiosInformation("OEM", "1.0")`. Le comportement D2 existant en fait une carte « non pris en charge automatiquement », comptée dans Pilotes. L'ancien attendu P1 omettait cette carte. L'attendu accepté est exactement « Pilotes : indisponible — 1 indisponible, 1 non pris en charge automatiquement ». Le commit séparé `228d91bd1f01237690563088b253ed0ab94c0b28` change uniquement cette assertion du nouveau test ; carte Driver `driver-read`, titre « Pilotes », statut « indisponible » et détail « Les pilotes n'ont pas pu être lus. Relancez leur examen. » restent vérifiés. Aucune sortie de production n'est changée pour faire passer cette assertion.

## Décision H1 et architecture livrée

Réponse à l'avis Codex P1 de la PR #87 : la version Windows passe par le port Core `IWindowsVersionSource` et l'adaptateur Infrastructure `EnvironmentWindowsVersionSource`. Celui-ci rend uniquement `Environment.OSVersion.Version`, lecture légère en mémoire ; il ne lit ni registre, fichier, WMI, processus ni réseau. L'enregistrement est transitoire, avec fabrique explicite, après `ApplicationVersionService`. La fabrique singleton de `BugReportViewModel` lit le port une fois. Il n'existe plus d'occurrence de `OSVersion` dans les sources App. `WindowsInfoReader` n'est pas utilisé ; les accès OS legacy sans rapport ne sont pas refactorés. Décision H1 du brief, loops (relecture).

## Exceptions E1–E5 sur les tests existants

Les seules modifications de tests existants sont les cinq exceptions ci-dessous, incluses dans le premier commit de tests seuls. Leur contenu et leurs lignes exactes ont été contrôlés contre la base ; aucun autre changement de test existant ni de fixture.

E1 tests/Virgil.App.Tests/LotGViewModelTests.cs, méthode `Production_sources_have_no_forbidden_effects_and_wiring_is_in_views_only` (conséquence directe de R1). Ligne 80 remplacée par :
`        Assert.DoesNotMatch(@"Environment\.OSVersion", composition);`
Ligne 83 remplacée par :
`        Assert.Contains("BugReportContext.From", views, StringComparison.Ordinal); Assert.Contains("sp.GetRequiredService<IWindowsVersionSource>().Read()", views, StringComparison.Ordinal);`
Rien d'autre dans le fichier (la liste l.70, dont « Environment », reste).
E2 tests/Virgil.App.Tests/UpdatesViewModelTests.cs (conséquence directe de R8, renvoi F9 du brief F). Ligne 101 remplacée par :
`        Assert.Equal(new[] { "Version installée : 1", "Version disponible : 2", "Source : Applications", "Risque : validation requise - Motif", "Statut : disponible" }, card.Lines);`
Ligne 169 remplacée par :
`        Assert.Contains("Application : app\nVersion : 1 -> 2\nSource : Applications\nRisque :", f.Vm.ValidationDetails);`
Ligne 171 remplacée par :
`        Assert.Equal("Mettre à jour app vers la version 2.", f.Vm.ValidationCommand);`
La ligne 170 et le reste du fichier ne changent pas.
E3 tests/Virgil.App.Tests/D2SingleItemTests.cs l.212 (R8) remplacée par :
`        Assert.DoesNotContain(legacy.Lines, line => line.StartsWith("ID :", StringComparison.Ordinal));`
Rien d'autre.
E4 tests/Virgil.App.Tests/LotEProgressColorTests.cs l.33 (R9, conformité au brief E R6 « orange ») remplacée par :
`    [InlineData("NeedsReview", "#FFFF8C1A")]`
Rien d'autre.
E5 tests/Virgil.Core.Tests/LauncherShutdownTests.cs l.148 (R11) remplacée par :
`        var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));`
Rien d'autre (l'horloge factice et toutes les assertions restent).
E1–E4 vont dans le commit de tests seuls, où elles sont rouges d'assertion tant que le correctif n'est pas là ; E5 y va aussi (verte, garde de temps seulement). Lot-H.md les cite mot pour mot.

## Captures PNG et limites

Les tests H produisent deux captures en STA avec les ressources du thème, dans `Virgil-ui-smoke/lot-h/` : `barre-a-verifier.png` (900×360, vrai OperationProgressPanel NeedsReview orange) et `carte-application.png` (1600×2400, vraie UpdatesView après scan, ligne « Source : Applications » visible et texte dans le cadre, sans lignes ID/Commande). Les images du run intermédiaire `43884641932`, artefact `14232634730`, ont été inspectées : lisibles, dimensions et octets différents. Leurs SHA-256 sont respectivement `6ecda1d14933c16b56cbdf4d8577927a411dbff5f8d01735f23c47f76c8deb56` et `d40641e7994a8ca73dc7724da9634480ea62530d3d3fa7d38ad743da28901bb7`. L'artefact du run vert et celui du run vert final sont référencés avec leurs heads.

Les backends de tests et le faux port n'attestent aucune mise à jour réelle sur un PC. L'affichage réel des noms accessibles par un lecteur d'écran et la couleur de la barre restent à vérifier sur le PC de référence à l'alpha.8. Aucune préversion n'est publiée par ce lot. Aucun changement de H-doc, aucune demande de relecture externe, aucun changement d'état brouillon.

Artefact du run vert du correctif : [Virgil-ui-smoke 14238289577](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43895724670/artifacts/14238289577), dossier `lot-h/`. Deux PNG rendus en STA avec le thème chargé, téléchargés et inspectés :

| PNG | Dimensions | SHA-256 |
| --- | --- | --- |
| `barre-a-verifier.png` | 900×360 | `6ecda1d14933c16b56cbdf4d8577927a411dbff5f8d01735f23c47f76c8deb56` |
| `carte-application.png` | 1600×2400 | `d40641e7994a8ca73dc7724da9634480ea62530d3d3fa7d38ad743da28901bb7` |

Les octets des deux captures diffèrent. La première montre la barre orange de NeedsReview ; la seconde montre la vraie UpdatesView, une carte d'application lisible dans le cadre et la ligne « Source : Applications », sans lignes « ID : » ni « Commande : ». Les noms des deux fichiers sont vérifiés dans l'archive. Les captures du head final et leur artefact sont référencés dans la description de la PR.

Exécuté : compilation et tests Windows Core/App/UI ; vérifications de diff, liste fermée et exceptions E1–E5 ; inspection des deux PNG. Non exécuté localement : build/tests .NET, SDK absent. Restant à vérifier sur PC à l'alpha.8 : noms effectivement lus par un lecteur d'écran et couleur de la barre en usage réel. Le test Pilotes ne prouve pas le parcours utilisateur d'aperçu, conformément à la décision M5b ci-dessus. Les essais réels Store, hors WinGet, Steam et navigateur restent les vérifications alpha prévues par le brief.

Aucun fichier H-doc, workflow, réglage ou secret n'est modifié. La PR a été fusionnée le 16/11/2026 après le feu vert de loops (relecture) sur `79d95cd348c6e92d402087d96f8e51504783502c`.

## Fichiers touchés et hygiène

Le contrôle de liste fermée et `git diff --check` contre la base main sont conformes. Aucun binaire ni fichier généré n'est ajouté ; les PNG restent dans les artefacts CI. Les fichiers touchés sont :

- `docs/PROJECT_STATUS.md`
- `docs/proofs/Lot-H.md`
- `src/Virgil.App/Controls/OperationProgressPanel.xaml`
- `src/Virgil.App/Controls/VirgilCoreControl.xaml.cs`
- `src/Virgil.App/Hosting/ServiceCollectionExtensions.cs`
- `src/Virgil.App/ViewModels/BugReportViewModel.cs`
- `src/Virgil.App/ViewModels/HomePrioritiesViewModel.cs`
- `src/Virgil.App/ViewModels/UpdatesItemViewModel.cs`
- `src/Virgil.App/ViewModels/UpdatesViewModel.Sequence.cs`
- `src/Virgil.App/ViewModels/UpdatesViewModel.Single.cs`
- `src/Virgil.App/ViewModels/UpdatesViewModel.cs`
- `src/Virgil.Core/Releases/IWindowsVersionSource.cs`
- `src/Virgil.Core/Updates/LauncherCardSummary.cs`
- `src/Virgil.Infrastructure/Releases/EnvironmentWindowsVersionSource.cs`
- `tests/Virgil.App.Tests/D2SingleItemTests.cs`
- `tests/Virgil.App.Tests/LotEProgressColorTests.cs`
- `tests/Virgil.App.Tests/LotGViewModelTests.cs`
- `tests/Virgil.App.Tests/LotHBoundaryTests.cs`
- `tests/Virgil.App.Tests/LotHJargonTests.cs`
- `tests/Virgil.App.Tests/LotHLinkTests.cs`
- `tests/Virgil.App.Tests/LotHSequenceTests.cs`
- `tests/Virgil.App.Tests/LotHWpfTests.cs`
- `tests/Virgil.App.Tests/UpdatesViewModelTests.cs`
- `tests/Virgil.Core.Tests/LauncherShutdownTests.cs`
- `tests/Virgil.Core.Tests/LotHLauncherSummaryTests.cs`
