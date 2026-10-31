# L6+-a — preuves de régression

Base exacte : `2e2850104fcf327ffcf824e5d67f11e91a97478f` (main après #71). Une seule PR : [#72](https://github.com/bassetthomas-design/virgil-2.0/pull/72), vers `main`.

## Reprise sur 28bf9a3 — messages des anciennes actions

Un seul commit supplémentaire, limité à `UpdateMaintenanceRunner.cs`, `MicrosoftMaintenanceTests.cs` et ce document. `InstallWindowsUpdates` et `InstallDriverUpdate` renvoient de nouveau exactement « Installation terminée. » ou « Aucune mise à jour nécessaire. » ; `UpdateDefenderSignatures` renvoie « Signatures Defender à jour. ». Les erreurs restent lisibles et accentuées. Les anciennes actions ne renvoient aucune ligne `VIRGIL|` dans leur message final transmis à `SummaryOutput`. Le cas 3 ajoute uniquement la mention « point existant du <date> (<description>) réutilisé » ; le test existant vérifie aussi son absence quand la création réussit.

`PrepareMicrosoftUpdates` / `InstallMicrosoftUpdates` conservent leurs lignes de protocole, y compris le numéro de séquence. Un `ThrowIfCancellationRequested` précède désormais `agent.IsServiceRegistered` dans le chemin Install. Aucun changement du backend, des vues ou de la séquence « Tout mettre à jour ».

Preuve Safety : `Legacy_actions_keep_original_readable_summary_without_protocol_lines` (5 cas : Windows succès/vide, pilote succès/vide, Defender) compare les messages exacts de `2e28501` et refuse toute occurrence de `VIRGIL|`. `Legacy_action_reuses_recent_point_only_after_creation_refused_or_without_effect` est adapté au message lisible, conserve ses deux variantes (création refusée ou sans effet) et refuse aussi les lignes de protocole. Les tests des nouvelles actions continuent à exiger leurs traces.

La [CI Windows de la tête de cette reprise](https://github.com/bassetthomas-design/virgil-2.0/pull/72/checks) fournit le verdict de référence : build Release sans warning, tests Core/App et InterfaceAccentTests, smoke inchangé de 14 tests avec assertion des 14 noms de PNG, publication et installeur. Les résultats #279 ci-dessous sont les preuves historiques avant cette reprise. Les différences sur `.github/workflows`, `AGENTS.md`, les fichiers de séquence et `tests/Virgil.UiTests` restent vides ; aucun élément L6+-b n’est introduit.

## Premier commit et CI rouge

Le premier commit `c8067f0` contient uniquement les tests et des contrats/points d’injection neutres : valeurs d’énumération ajoutées en fin de liste, champ entier nullable `RestorePointSequenceNumber`, ports WUA et sonde, surcharges initialement neutres. Le comportement antérieur des points d’entrée existants est conservé.

[CI Windows rouge #277](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/41210222975) : compilation Release réussie, **0 warning, 0 erreur** ; Core **1 045 tests, 1 020 réussites, 25 échecs** ; App **507 réussites, 0 échec** ; UiTests : aucun cas correspondant au filtre `Category!=UI`, smoke non lancé après l’échec.

Extrait :
```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
Total tests: 1045
     Passed: 1020
     Failed: 25
Pnputil_french_accented_fields_are_read :
Expected: "oem12.inf"
Actual:   ""
```

**Limite de cette preuve rouge :** les surcharges neutres ne consommaient pas encore les factices. Les trois cas hérités (annulation et deux variantes de réutilisation) ont atteint un scan WUA réel du runner ; les deux variantes de réutilisation ont répondu « Aucune mise à jour nécessaire. ». La sonde neutre conservait également les lectures registre de la base. Cette première exécution ne respecte donc pas entièrement l’exigence d’isolation OS des tests sans trait Windows. Aucune commande système réelle ni point de restauration réel n’a été lancé par le runner de commandes factice ; aucune installation n’est rapportée par ces scans. L’historique n’est pas réécrit. La tête corrigée utilise effectivement les ports factices et ne reproduit plus ces accès.

Noms des 25 cas rouges :
```text
UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: PrepareMicrosoftUpdates, identity: "defender,microsoft-update", expected: True)
UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: PrepareMicrosoftUpdates, identity: "", expected: True)
UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: InstallMicrosoftUpdates, identity: "software,drivers,store,windows-feature-upgrade", expected: True)
MicrosoftMaintenanceTests.Cancel_during_windows_update_pass_finishes_current_call_and_starts_no_next_pass
MicrosoftMaintenanceTests.Pending_reboot_probe_reports_cbs_and_windows_update_sources(source: "Maintenance des composants")
MicrosoftMaintenanceTests.Pending_reboot_probe_reports_cbs_and_windows_update_sources(source: "Windows Update")
MicrosoftMaintenanceTests.Feature_upgrade_runs_last_and_reports_nothing_to_install_when_absent(present: True)
MicrosoftMaintenanceTests.Feature_upgrade_runs_last_and_reports_nothing_to_install_when_absent(present: False)
MicrosoftMaintenanceTests.Drivers_unchecked_means_no_driver_search
MicrosoftMaintenanceTests.Windows_update_passes_repeat_until_nothing_installed_max_three(always: False)
MicrosoftMaintenanceTests.Windows_update_passes_repeat_until_nothing_installed_max_three(always: True)
MicrosoftMaintenanceTests.Cancel_after_restore_starts_no_microsoft_step(id: PrepareMicrosoftUpdates)
InterventionsModuleTests.Elevated_allowlist_uses_fixed_safe_commands_only
InterventionsModuleTests.Catalog_exposes_only_targeted_interventions_without_take_ownership
MicrosoftMaintenanceTests.Cancel_after_restore_starts_no_microsoft_step(id: InstallWindowsUpdates)
MicrosoftMaintenanceTests.Legacy_action_reuses_recent_point_only_after_creation_refused_or_without_effect(creationExit: 0)
MicrosoftMaintenanceTests.Legacy_action_reuses_recent_point_only_after_creation_refused_or_without_effect(creationExit: 1)
MicrosoftMaintenanceTests.Restore_point_four_cases_are_fail_closed(scenario: 2)
MicrosoftMaintenanceTests.Restore_point_four_cases_are_fail_closed(scenario: 4)
MicrosoftMaintenanceTests.Restore_point_four_cases_are_fail_closed(scenario: 3)
MicrosoftMaintenanceTests.Restore_point_four_cases_are_fail_closed(scenario: 1)
MicrosoftMaintenanceTests.Install_microsoft_updates_runs_software_drivers_store_then_feature_upgrade
MicrosoftMaintenanceTests.Store_scan_runs_the_fixed_cim_command
MicrosoftMaintenanceTests.Microsoft_update_is_registered_once_with_script_flags
MicrosoftMaintenanceTests.Pnputil_french_accented_fields_are_read
```

## Test rouge sur 2e28501 → commit de correction

Il s’agit de la base augmentée des contrats neutres de `c8067f0`, comme dans le précédent L6-1, et non d’une compilation des nouvelles signatures sur le SHA inchangé.

| Test rouge (variantes ci-dessus) | Correction |
| --- | --- |
| `Maintenance_allowlist_accepts_only_known_tokens` (3 cas) | `f350b01` : ensemble fermé de jetons, refus sans effet |
| `Elevated_allowlist_uses_fixed_safe_commands_only`, `Catalog_exposes_only_targeted_interventions_without_take_ownership` | `f350b01` : reconnaissance des nouvelles actions de maintenance, hors catalogue de réparation |
| `Install_microsoft_updates_runs_software_drivers_store_then_feature_upgrade` | `f350b01` : ordre interne fixe |
| `Microsoft_update_is_registered_once_with_script_flags` | `f350b01` : MU, drapeau 7 et repli explicite |
| `Windows_update_passes_repeat_until_nothing_installed_max_three` (2 cas), `Drivers_unchecked_means_no_driver_search` | `f350b01` : passes bornées et sélection |
| `Store_scan_runs_the_fixed_cim_command`, `Feature_upgrade_runs_last_and_reports_nothing_to_install_when_absent` (2 cas) | `f350b01` : Store best effort et fonctionnalité en dernier |
| `Restore_point_four_cases_are_fail_closed` (4 cas), `Legacy_action_reuses_recent_point_only_after_creation_refused_or_without_effect` (2 cas) | `f350b01` : protection partagée, création puis repli visible |
| `Cancel_after_restore_starts_no_microsoft_step` (2 cas), `Cancel_during_windows_update_pass_finishes_current_call_and_starts_no_next_pass` | `f350b01` : frontières d’annulation et conservation du verdict de redémarrage |
| `Pending_reboot_probe_reports_cbs_and_windows_update_sources` (2 cas rouges) | `f350b01` : sonde injectée, registre dans Infrastructure |
| `Pnputil_french_accented_fields_are_read` | `f350b01` : FormD, retrait des NonSpacingMark, espaces réduits et casse ignorée |

La CI #278 a ensuite confirmé **1 050/1 050 tests Core verts** et identifié un seul échec App : `CoreServicesExceptConfirmationAuthorityAreTransientAndHaveExplicitFactories`. `a682454` rétablit les fabriques DI explicites pour les deux inscriptions ajoutées, sans affaiblir le test.

## Couverture du script

| Étape du script | Action / jeton du helper ou port | Test |
| --- | --- | --- |
| Defender | `PrepareMicrosoftUpdates / defender` | `Microsoft_update_is_registered_once_with_script_flags`, `Restore_point_failure_blocks_all_microsoft_steps` |
| Microsoft Update | `PrepareMicrosoftUpdates / microsoft-update` | `Microsoft_update_is_registered_once_with_script_flags` |
| Passes logiciels | `InstallMicrosoftUpdates / software` | `Windows_update_passes_repeat_until_nothing_installed_max_three` |
| Passes pilotes | `InstallMicrosoftUpdates / drivers` | même test ; `Drivers_unchecked_means_no_driver_search` |
| Store | `InstallMicrosoftUpdates / store` | `Store_scan_runs_the_fixed_cim_command` |
| Mise à niveau de fonctionnalité | `InstallMicrosoftUpdates / windows-feature-upgrade` | `Feature_upgrade_runs_last_and_reports_nothing_to_install_when_absent` |
| Redémarrage | `RebootRequired` WUA + `IPendingRebootProbe` (sans élévation) | `Cancel_during_windows_update_pass_finishes_current_call_and_starts_no_next_pass`, `Pending_reboot_probe_reports_cbs_and_windows_update_sources` |

Les deux actions couvrent la partie Microsoft sans action élevée supplémentaire. La preuve du compte UAC de la séquence relève de L6+-b. Le numéro retenu est renvoyé dans `VIRGIL|restore-point|OK|<SequenceNumber>` ; Install refuse un numéro absent, négatif, inconnu, futur ou expiré avant toute recherche et ne crée aucun point. Les jetons sont séparés par des virgules, sans espace ni doublon.

## Vérification verte

[CI Windows verte #279](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/41211005967), commit `a682454` : **1 050/1 050 Core, 507/507 App, 14/14 UI ; 0 warning, 0 erreur**. Publication win-x64 et installeur réussis. [Contrôles de la tête finale de la PR](https://github.com/bassetthomas-design/virgil-2.0/pull/72/checks) : le dernier commit ne modifie que ces preuves et l’architecture ; aucun code ni test n’est modifié après cette validation.

Extraits Windows des tests nommés :
```text
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: InstallWindowsUpdates, identity: "unexpected", expected: False) [3 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: InstallDriverUpdate, identity: "' or IsInstalled=1", expected: False) [< 1 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: PrepareMicrosoftUpdates, identity: "defender,microsoft-update", expected: True) [< 1 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: PrepareMicrosoftUpdates, identity: "", expected: True) [< 1 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: FlushDns, identity: "", expected: False) [< 1 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: InstallMicrosoftUpdates, identity: "software,drivers,store,windows-feature-upgrade", expected: True) [< 1 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: InstallMicrosoftUpdates, identity: "", expected: False) [< 1 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: InstallDriverUpdate, identity: "f813d575-a123-4788-b34a-d547c15c5663", expected: True) [< 1 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: InstallMicrosoftUpdates, identity: "' or IsInstalled=1", expected: False) [< 1 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: InstallMicrosoftUpdates, identity: "software,software", expected: False) [< 1 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: UpdateDefenderSignatures, identity: "", expected: True) [< 1 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: InstallWindowsUpdates, identity: "", expected: True) [< 1 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: PrepareMicrosoftUpdates, identity: "store", expected: False) [< 1 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: InstallMicrosoftUpdates, identity: "unknown", expected: False) [< 1 ms]
Passed Virgil.Tests.UpdateSequenceTests.Maintenance_allowlist_accepts_only_known_tokens(id: InstallMicrosoftUpdates, identity: "defender", expected: False) [< 1 ms]
Passed Virgil.Tests.UpdatesModuleTests.Driver_inventory_parser_is_read_only_and_hides_install [< 1 ms]
Passed Virgil.Tests.OperationCancellationSafetyTests.CancelAfterRestorePointPreventsWindowsUpdateSearchFromStarting [3 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Install_requires_the_exact_valid_restore_sequence(sequence: 99) [7 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Install_requires_the_exact_valid_restore_sequence(sequence: -1) [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Install_requires_the_exact_valid_restore_sequence(sequence: null) [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Cancel_during_windows_update_pass_finishes_current_call_and_starts_no_next_pass [13 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Pending_reboot_probe_reports_cbs_and_windows_update_sources(source: "Maintenance des composants") [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Pending_reboot_probe_reports_cbs_and_windows_update_sources(source: "Windows Update") [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Pending_reboot_probe_reports_cbs_and_windows_update_sources(source: "") [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Feature_upgrade_runs_last_and_reports_nothing_to_install_when_absent(present: True) [1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Feature_upgrade_runs_last_and_reports_nothing_to_install_when_absent(present: False) [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Drivers_unchecked_means_no_driver_search [1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Windows_update_passes_repeat_until_nothing_installed_max_three(always: False) [2 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Windows_update_passes_repeat_until_nothing_installed_max_three(always: True) [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.UsoClient_fallback_is_limited_to_search_failure(searchFailure: True) [1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.UsoClient_fallback_is_limited_to_search_failure(searchFailure: False) [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Expired_or_future_restore_point_blocks_installation(age: 25) [2 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Expired_or_future_restore_point_blocks_installation(age: -1) [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Cancel_after_restore_starts_no_microsoft_step(id: PrepareMicrosoftUpdates) [3 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Cancel_after_restore_starts_no_microsoft_step(id: InstallWindowsUpdates) [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Legacy_action_reuses_recent_point_only_after_creation_refused_or_without_effect(creationExit: 0) [13 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Legacy_action_reuses_recent_point_only_after_creation_refused_or_without_effect(creationExit: 1) [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Restore_point_four_cases_are_fail_closed(scenario: 2) [1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Restore_point_four_cases_are_fail_closed(scenario: 4) [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Restore_point_four_cases_are_fail_closed(scenario: 3) [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Restore_point_four_cases_are_fail_closed(scenario: 1) [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Install_microsoft_updates_runs_software_drivers_store_then_feature_upgrade [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Store_scan_runs_the_fixed_cim_command [< 1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Microsoft_update_is_registered_once_with_script_flags [4 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Pnputil_french_accented_fields_are_read [4 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Restore_point_failure_blocks_all_microsoft_steps [1 ms]
Passed Virgil.Tests.MicrosoftMaintenanceTests.Quality_pass_excludes_feature_items_and_installs_only_downloaded_updates [1 ms]
```

`InterfaceAccentTests` : les 77 cas sont verts dans App. Complément local uniquement : compilation Release sans warning et harnais de logique pure **107/107** (30 maintenance + 77 accents), sans registre/WUA réels ; cela ne remplace pas la CI Windows.

Artefacts de #279 : [Virgil-ui-smoke](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/41211005967/artifacts/13046407171), [application](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/41211005967/artifacts/13046223072), [installeur](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/41211005967/artifacts/13046461956). Le smoke vert `Smoke_run_has_no_destructive_side_effect` et `Cleanup_summary_is_captured_without_execution` vérifie exactement les **14 PNG**, liste inchangée :
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
session-desktop.png
```

| Critère de fusion | Preuve / verdict |
| --- | --- |
| Premier commit : compilation puis tests rouges, dont pnputil FR | #277, 25 échecs Core ; **isolation OS incomplète dans cette preuve historique**, détaillée plus haut |
| Tests A1–A7 et gardes | #279 : Core 1 050/1 050, App 507/507 |
| Build Release, 0 warning | #279 : 0 Warning(s), 0 Error(s) |
| Smoke inchangé, 14 PNG | #279 : UI 14/14, assertion exacte des noms, artefact produit |
| Application et installeur | Deux artefacts produits, liens ci-dessus |
| Accents et identifiants | 77 cas InterfaceAccentTests verts, références techniques préservées |
| Workflows et périmètre L6+-b | Diff vide sur les chemins exclus |
| État de la PR | Brouillon conservé : l’écart d’isolation de la preuve rouge empêche de déclarer tous les critères stricts satisfaits |


Les tests Safety nommés A1–A5, les quatre cas de restauration, le refus d’un mauvais numéro, la réutilisation par une ancienne action après refus ou absence d’effet, les trois cas CBS/WU/aucune source, ainsi que pnputil FR et EN sont verts. `OperationCancellationSafetyTests.CancelAfterRestorePointPreventsWindowsUpdateSearchFromStarting` reste vert ; le cas Prepare est couvert par `Cancel_after_restore_starts_no_microsoft_step`.

Les tests complémentaires vérifient les points futurs/expirés avec une horloge fixe, l’exclusion des fonctionnalités des passes qualité, l’installation limitée aux éléments téléchargés et le repli UsoClient réservé à l’échec du searcher. Le test d’annulation bloque Install sur un `TaskCompletionSource`, annule, puis relâche l’appel : aucun Driver, Store, fonctionnalité ou passe suivante, et redémarrage WUA conservé.

Contrôle d’architecture : `rg 'Microsoft.Win32|Registry' src/Virgil.Core/Updates/WindowsUpdateStatusService.cs` ne renvoie rien. Les nouvelles lectures registre résident uniquement dans `RegistryPendingRebootProbe` (Infrastructure). Les instanciations des tests injectent une sonde vide ou CBS/WU factice. Le helper ne référence que Contracts parmi les projets Virgil.

Le garde `InterfaceAccentTests` est vert. Aucun identifiant C#, binding, x:Name/AutomationId, clé de ressource, clé JSON, nom sérialisé, valeur InterventionId ou jeton d’option n’a reçu d’accent. Les fragments techniques pnputil FR/EN sont conservés tels quels ; seule leur comparaison est normalisée. Aucune exemption ni entrée à `ValideSansAccent.txt` n’a été ajoutée.

Diff vide sur `.github/workflows`, `AGENTS.md`, les services de séquence, les ViewModels/vues et `tests/Virgil.UiTests`. Aucun travail L6+-b : ni branchement des actions, ni winget force/passe finale, ni retrait de code pilotes, ni politique de fermeture des lanceurs, ni nouveau smoke/PNG.

## Limites connues

- La séquence n’utilise pas encore les nouvelles actions avant L6+-b ; les bandeaux, le verdict combiné final et le rapport de redémarrage restent dans L6+-b.
- La détection de la limite de 24 h dépend du comportement de `Checkpoint-Computer` ; le repli repose sur l’absence de nouveau point relu, pas seulement sur le code de sortie. Un échec de lecture bloque l’installation.
- La première preuve rouge a conservé des accès OS hérités (registre et scan WUA), comme détaillé ci-dessus ; elle ne constitue pas une preuve entièrement factice. La tête finale corrige cette isolation.
- Les appels COM sont synchrones et non interruptibles. Une mise à niveau de fonctionnalité peut être longue et imposer un redémarrage ; Virgil ne redémarre jamais automatiquement.
- Une stratégie d’entreprise peut interdire MU : repli Windows Update seul annoncé. Le Store est best effort et un échec est émis comme `UNAVAILABLE`, jamais comme `OK`.
- Les tests factices prouvent l’orchestration, pas une installation effective sur un PC utilisateur.
