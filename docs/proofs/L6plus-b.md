# L6+-b — preuves de régression

Base exacte : `ee72c94a18331ca003b2f441ebf8e94bd9c10381` (L6+-a fusionné). Une seule PR : [#73](https://github.com/bassetthomas-design/virgil-2.0/pull/73), vers `main`.

## Premier commit rouge

`dbd4b47f5ce5c3701dda3716ffe517a32579ca26` contient uniquement trois fichiers de tests, sans point d'injection ni changement de production. [CI Windows rouge #283](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/41419590077) : **compilation réussie, 0 warning, 0 erreur**, puis tests rouges.

| Projet | Total | Réussites | Échecs |
| --- | ---: | ---: | ---: |
| Core.Tests | 1059 | 1055 | 4 |
| App.Tests | 507 | 506 | 1 |
| UiTests | Aucun test correspondant à `Category!=UI` | — | 0 |

Les tests `SafeUiTests` sont liés dans Core.Tests : leur échec compte donc dans les quatre échecs Core, sans exécution FlaUI.

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
Core.Tests: Total tests: 1059; Passed: 1055; Failed: 4
App.Tests:  Total tests: 507; Passed: 506; Failed: 1
```

| Test rouge sur la base | Défaut observé | Commit de correction |
| --- | --- | --- |
| `Opening_update_plan_never_searches_Windows_Update` | Appel du pré-scan WUA et absence des catégories | `67c8715` |
| `Winget_upgrade_arguments_match_script_flags` (1.4 et 1.5, deux cas) | `--force` absent | `67c8715` |
| `Update_plan_lists_script_steps_in_order_with_drivers_checked` | Pilotes décochés | `67c8715` |
| `Update_preview_is_allowed_without_confirmation_or_execution` | `RunSelectedButton` refusé par SafeUi | `67c8715` |

## Correspondance avec le script

| Étape du script | Étape Virgil (plan) | Élévation | Test qui la prouve |
| --- | --- | --- | --- |
| Point de restauration | Point de restauration obligatoire si un composant élevé est sélectionné | élévation 1 | `Update_sequence_never_requests_more_than_two_elevations` |
| Defender | Signatures Microsoft Defender | élévation 1 | `Update_plan_lists_script_steps_in_order_with_drivers_checked`, `Update_sequence_never_requests_more_than_two_elevations` |
| Microsoft Update | Enregistrer Microsoft Update (Office et produits Microsoft) | élévation 1 | mêmes tests, requête `PrepareMicrosoftUpdates` |
| Lanceurs | Virgil fermera : liste détectée | sans élévation | `Launcher_shutdown_plan_lists_detected_launchers_and_requires_confirmation`, `Running_game_blocks_launcher_shutdown`, `Launcher_is_force_closed_only_after_15_seconds` |
| `winget source update`, puis paquets | Applications WinGet, identités et versions confirmées | sans élévation | `Winget_source_update_runs_before_first_and_final_pass`, `Winget_upgrade_arguments_match_script_flags` |
| WU logiciels | Windows Update : logiciels (jusqu'à 3 passes) | élévation 2 | `Update_sequence_never_requests_more_than_two_elevations` |
| WU pilotes | Windows Update : pilotes (jusqu'à 3 passes), cochés | élévation 2 | `Update_plan_lists_script_steps_in_order_with_drivers_checked`, `Excluding_category_changes_plan_hash_and_removes_helper_token` |
| Store | Rechercher les mises à jour du Microsoft Store | élévation 2 | `Update_sequence_never_requests_more_than_two_elevations` |
| Nouvelle version de Windows | Nouvelle version de Windows (si disponible), cochée, dernière étape de l'élévation WU | élévation 2 | `Update_plan_lists_script_steps_in_order_with_drivers_checked`, `Excluding_category_changes_plan_hash_and_removes_helper_token` |
| Passe finale | Passe finale : tout ce que winget trouve | sans élévation | `Final_winget_pass_uses_exact_script_command` (1.4 et 1.5) |
| Redémarrage | Vérifier le redémarrage en attente, même après annulation | sans élévation | `Pending_reboot_is_read_after_sequence_even_when_steps_report_none`, `Pending_reboot_is_read_after_cancellation`, `Pending_reboot_probe_drives_banner_and_report` |

La préparation transmet la session et le numéro `VIRGIL|restore-point|OK|…` du helper à `InstallMicrosoftUpdates`. En cas de réutilisation, son texte exact reste dans le rapport. Aucune réimplémentation des passes WU, du Store, de la fonctionnalité ou du choix du point de restauration : le helper L6+-a reste inchangé.

La passe finale est exactement :

```text
upgrade --all --include-unknown --include-pinned --force --accept-source-agreements --accept-package-agreements --silent --disable-interactivity
```

Avec WinGet < 1.5, `--include-pinned` est retiré et cette limite est rapportée. Les commandes individuelles restent épinglées sur `--id`, `--exact` et la version confirmée ; elles partagent le constructeur avec la validation individuelle. `--ignore-security-hash` reste absent et testé. Un échec d'actualisation des sources est rapporté et ne bloque pas la passe.

## Tests et sécurité

Les tests supplémentaires couvrent les jetons `drivers` et `windows-feature-upgrade` retirés du plan et de la requête, le refus d'un plan absent/expiré/consommé avant toute fermeture, les commandes Steam et taskkill sans `/F`, le délai de 15 secondes par horloge factice, la survie ou l'arrêt du lanceur, les enfants et bibliothèques de jeux ainsi que le cas inconnu. Aucun vrai processus n'est fermé par ces tests.

`Process_protection_policy_still_refuses_launcher_close_outside_update_sequence` réexerce le test L6-1 `Tray_only_launcher_over_threshold_is_reported_without_close_action`. `Launcher_shutdown_policy_has_only_sequence_and_composition_callers` vérifie les seuls consommateurs de la nouvelle politique dans `src/`.

`Cancel_during_winget_pass_skips_windows_update_and_final_pass` conserve l'installation en cours, bloque les paquets suivants, Windows Update et la passe finale ; la sonde du redémarrage reste exécutée. `Cancel_during_Windows_Update_acknowledges_non_interruptible_step_immediately` vérifie l'acquittement immédiat et le message « ne peut pas être interrompue », sans attendre la fin de l'étape factice.

Vérification complémentaire Linux : compilation croisée Release, 0 warning ; **108 tests ciblés réussis, 0 échec**, sans tests Windows. Le runtimeconfig généré de Core.Tests a été adapté uniquement dans `bin/` pour cette exécution de logique pure ; aucun fichier de projet ni workflow ne l'est. Cette vérification ne remplace pas la CI Windows.

### Greps B2

```text
rg -n 'CanInstallDrivers|InstallButtonVisibilityReason|IsInstallDrivers|InstallDriversCommand|ScanDriversAsync|décochés par défaut' src tests
0 occurrence (code de retour 1)
```

L'inventaire `InspectDriversButton` / `pnputil` est conservé. Les tests d'inventaire vérifient toujours la requête en lecture seule, les dix pilotes rendus, les erreurs, la préservation des applications et la lecture de `oem12.inf` ; seuls les contrats morts d'installation disparaissent.

### Diff de la règle AGENTS.md

Une seule phrase est ajoutée à la règle existante, sans retrait :

> Décision produit du 28/10 : LauncherShutdownPolicy constitue une exception étroite réservée à UpdateSequenceService après validation du ConfirmedPlan, pour les seuls exécutables de LauncherCatalog et sans jeu en cours ni doute.

### Diff Diagnostic

La consigne conserve « Quittez-le depuis son icône » et devient :

> Il tourne dans la zone de notification. Quittez-le depuis son icône (clic droit → Quitter) si vous ne l'utilisez pas, ou laissez « Tout mettre à jour » s'en charger.

`ProcessProtectionPolicy` et `ProcessActionService` ont un diff vide, comme `.github/workflows` et `src/Virgil.ElevatedHelper`.

`Blocked_launcher_is_reported_as_not_executed` vérifie aussi qu'un lanceur laissé ouvert figure parmi les actions non exécutées, sans être compté comme une fermeture réussie. `Preparation_launch_failure_is_not_reported_as_success_and_still_probes_reboot` vérifie qu'un refus de lancement de la préparation apparaît comme un échec et conserve la lecture finale du redémarrage.

## CI Windows finale et captures

[CI Windows verte #289](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/41425759740), sur le code final `0c1f034` : build Release, tests, publication, smoke et installateur **réussis, 0 warning, 0 erreur**. Le dernier commit recopie seulement ces preuves ; la [CI de la tête de branche](https://github.com/bassetthomas-design/virgil-2.0/actions/workflows/build.yml?query=branch%3Acodex%2Fl6plus-b) permet de vérifier aussi cette tête documentaire.

| Projet / étape | Réussites | Échecs |
| --- | ---: | ---: |
| Core.Tests (`Category!=UI`) | 1090 | 0 |
| App.Tests (`Category!=UI`) | 514 | 0 |
| UiTests (`Category=UI`) | 14 | 0 |
| Build Release | 0 warning, 0 erreur | 0 |
| Publication et installateur | Réussis | 0 |

`InterfaceAccentTests` est **vert**, y compris les quatre cas distinguant le nom « passes » du participe. Latence UI maximale observée : **66 ms**, sous le plafond de 500 ms.

Artefacts effectivement produits : [Virgil-ui-smoke](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/41425759740/artifacts/13131323366), [Virgil-2.0-win-x64](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/41425759740/artifacts/13132431052), [Virgil-win-x64-setup](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/41425759740/artifacts/13132386791). Les assertions UIA prouvent la présence visible de `UpdatePlanStep.Drivers` cochée avant la capture et la liste exacte des **15 PNG** ; aucune revue visuelle manuelle des pixels n'est revendiquée.

Extrait vert des tests B1–B6 (noms et paramètres conservés, durées omises) :

```text
Passed Virgil.Tests.L6PlusSequenceTests.Winget_upgrade_arguments_match_script_flags(version: "1.4.0", pinned: False)
Passed Virgil.Tests.L6PlusSequenceTests.Winget_upgrade_arguments_match_script_flags(version: "1.5.0", pinned: True)
Passed Virgil.Tests.L6PlusSequenceTests.Opening_update_plan_never_searches_Windows_Update
Passed Virgil.Tests.L6PlusExecutionTests.Update_sequence_never_requests_more_than_two_elevations(wingetOnly: True)
Passed Virgil.Tests.L6PlusExecutionTests.Update_sequence_never_requests_more_than_two_elevations(wingetOnly: False)
Passed Virgil.Tests.L6PlusExecutionTests.Preparation_launch_failure_is_not_reported_as_success_and_still_probes_reboot
Passed Virgil.Tests.L6PlusExecutionTests.Final_winget_pass_uses_exact_script_command(version: "1.4.0", pinned: False)
Passed Virgil.Tests.L6PlusExecutionTests.Final_winget_pass_uses_exact_script_command(version: "1.5.0", pinned: True)
Passed Virgil.Tests.L6PlusExecutionTests.Pending_reboot_is_read_after_sequence_even_when_steps_report_none
Passed Virgil.Tests.L6PlusExecutionTests.Winget_source_update_runs_before_first_and_final_pass
Passed Virgil.Tests.L6PlusExecutionTests.Pending_reboot_is_read_after_cancellation
Passed Virgil.Tests.L6PlusExecutionTests.Excluding_category_changes_plan_hash_and_removes_helper_token(excluded: "windows-feature-upgrade")
Passed Virgil.Tests.L6PlusExecutionTests.Excluding_category_changes_plan_hash_and_removes_helper_token(excluded: "drivers")
Passed Virgil.Tests.UpdatesSequenceViewModelTests.Pending_reboot_probe_drives_banner_and_report
Passed   Passed Virgil.Tests.UpdatesSequenceViewModelTests.Cancel_during_Windows_Update_acknowledges_non_interruptible_step_immediately
Passed Virgil.Tests.UpdatesSequenceViewModelTests.Blocked_launcher_is_reported_as_not_executed
Passed Virgil.Tests.OperationCancellationSafetyTests.Cancel_during_winget_pass_skips_windows_update_and_final_pass
Passed Virgil.UiTests.SafeUiTests.Winget_listing_is_allowed_but_installation_is_always_refused(command: "winget.exe install Safe.Package", allowed: False)
Passed Virgil.UiTests.SafeUiTests.Winget_listing_is_allowed_but_installation_is_always_refused(command: "winget.exe upgrade Safe.Package", allowed: False)
Passed Virgil.UiTests.SafeUiTests.Winget_listing_is_allowed_but_installation_is_always_refused(command: "winget.exe upgrade --include-unknown --include-pin"···, allowed: True)
Passed Virgil.UiTests.SafeUiTests.Winget_listing_is_allowed_but_installation_is_always_refused(command: "winget.exe upgrade --all --silent", allowed: False)
Passed Virgil.UiTests.SafeUiTests.Winget_listing_is_allowed_but_installation_is_always_refused(command: "winget.exe list --id Safe.Package", allowed: True)
Passed Virgil.UiTests.SafeUiTests.Winget_listing_is_allowed_but_installation_is_always_refused(command: "winget.exe upgrade --id Safe.Package", allowed: False)
Passed Virgil.UiTests.SafeUiTests.Winget_listing_is_allowed_but_installation_is_always_refused(command: "winget.exe --version", allowed: True)
Passed Virgil.UiTests.SafeUiTests.Winget_listing_is_allowed_but_installation_is_always_refused(command: "winget.exe upgrade --include-unknown --force", allowed: False)
Passed Virgil.UiTests.SafeUiTests.Update_preview_is_allowed_without_confirmation_or_execution
Passed Virgil.Tests.LauncherShutdownTests.Graceful_shutdown_uses_catalogue_commands_without_force(executable: "steam.exe", command: "steam.exe", arguments: "-shutdown")
Passed Virgil.Tests.LauncherShutdownTests.Graceful_shutdown_uses_catalogue_commands_without_force(executable: "EpicGamesLauncher.exe", command: "taskkill.exe", arguments: "/IM EpicGamesLauncher.exe")
Passed Virgil.Tests.LauncherShutdownTests.Running_game_blocks_launcher_shutdown(kind: "library")
Passed Virgil.Tests.LauncherShutdownTests.Running_game_blocks_launcher_shutdown(kind: "unknown")
Passed Virgil.Tests.LauncherShutdownTests.Running_game_blocks_launcher_shutdown(kind: "child")
Passed Virgil.Tests.LauncherShutdownTests.Process_protection_policy_still_refuses_launcher_close_outside_update_sequence
Passed Virgil.Tests.LauncherShutdownTests.Launcher_shutdown_policy_has_only_sequence_and_composition_callers
Passed Virgil.Tests.LauncherShutdownTests.Launcher_is_force_closed_only_after_15_seconds(exits: False)
Passed Virgil.Tests.LauncherShutdownTests.Launcher_is_force_closed_only_after_15_seconds(exits: True)
Passed Virgil.Tests.LauncherShutdownTests.Launcher_shutdown_requires_confirmed_plan(kind: "consumed")
Passed Virgil.Tests.LauncherShutdownTests.Launcher_shutdown_requires_confirmed_plan(kind: "missing")
Passed Virgil.Tests.LauncherShutdownTests.Launcher_shutdown_requires_confirmed_plan(kind: "expired")
Passed Virgil.Tests.LauncherShutdownTests.Launcher_shutdown_refuses_processes_outside_catalog
Passed Virgil.UiTests.UiSmokeTests.Ui_thread_stays_responsive_during_navigation_and_complete_scan
Passed Virgil.UiTests.UiSmokeTests.Smoke_run_has_no_destructive_side_effect
Passed Virgil.UiTests.UiSmokeTests.Cleanup_summary_is_captured_without_execution
```

Le parcours smoke conserve `ExecuteUpdateButton` explicitement interdit. Il autorise seulement les deux identifiants supplémentaires `RunSelectedButton` et `SkipUpdateButton`, vérifie `UpdatePlanStep.Drivers` avec `ToggleState.On`, capture, annule puis compare les rapports. La surveillance refuse le helper élevé, `winget install` et toute commande `winget upgrade` avec cible ou installation globale. Le seul `upgrade` toléré est la liste sans cible, bornée aux options fermées du scanner existant ; huit cas de sécurité distinguent cette lecture des installations. La ligne de commande est lue via une API native Windows, sans dépendance WMI dans le smoke. Le scan approfondi est borné à 45 secondes avant repli sur l'inventaire déjà disponible pour ouvrir l'aperçu dans la limite du smoke ; la passe finale annoncée reste sélectionnable.

Liste contrôlée et produite des **15 PNG** de `Virgil-ui-smoke` :

```text
01-accueil.png
02-scan.png
03-nettoyage.png
04-diagnostic.png
05-ressources.png
06-applications.png
07-mises-a-jour.png
08-demarrage.png
09-reseau.png
10-reparation.png
11-rapports.png
12-recap-nettoyage.png
13-scan-progression.png
14-plan-mises-a-jour.png
session-desktop.png
```

`07-mises-a-jour.png` est régénéré par le parcours existant. La liste exacte est contrôlée par `AssertNoSideEffects`.

## Accents et identifiants

Aucun identifiant C#, binding, x:Name/AutomationId, clé de ressource, clé JSON, jeton ni nom PNG n'est accentué. Les AutomationId sont `UpdatePlanStep.Defender`, `.MicrosoftUpdate`, `.Store`, `.Winget.<id>`, `.WindowsSoftware`, `.Drivers`, `.WindowsFeatureUpgrade`, `.WingetFinal` ; les lignes obligatoires et lanceurs ont aussi un identifiant stable ASCII.

Les libellés imposés « jusqu'à 3 passes » restent exactement inchangés. Le garde d'accents distingue le nom « passes » après un nombre du participe « passés », avec contre-tests qui continuent à rejeter « Les essais sont passes ». Aucun ajout à `ValideSansAccent.txt`, aucune exemption par fichier ou dossier. `InterfaceAccentTests` est vert dans la CI Windows #289.

## Essai manuel sur le PC de thoms

**En attente.** Aucun accès à ce PC, aucune installation manuelle ni fin de rapport réelle n'est revendiquée.

## Limites connues

- Les appels WUA et l'installation WinGet déjà commencés ne sont pas interrompus ; l'annulation bloque la suite. Une fonctionnalité Windows peut être longue et imposer un redémarrage, annoncé avant confirmation.
- Les bibliothèques Steam sont lues dans `libraryfolders.vdf`, Epic dans ses manifests et les autres lanceurs dans leurs emplacements de registre reconnus. Une configuration absente, inaccessible, personnalisée ou indécidable entraîne « mis à jour au prochain lancement ». Tout enfant du lanceur bloque aussi la fermeture, y compris un éventuel utilitaire du lanceur : choix conservateur conforme à la règle produit.
- La fermeture forcée peut perdre un état du lanceur ; elle exige un nouvel inventaire sûr après 15 secondes. Aucune relance automatique.
- `taskkill /IM` concerne toutes les instances de l'exécutable du catalogue. Aucun processus hors catalogue n'est accepté.
- Le repli après 45 secondes ne promet pas une liste exhaustive des paquets avant confirmation ; la passe finale globale est explicitement annoncée dans le plan.
- Office Click-to-Run conserve son mécanisme autonome. Aucun redémarrage automatique, reprise automatique ou changement des workflows.
- L'essai réel du PC de thoms reste en attente. Seules les preuves CI effectivement observées seront marquées réussies.
