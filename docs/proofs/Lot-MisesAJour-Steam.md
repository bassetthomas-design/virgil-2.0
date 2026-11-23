# Lot T6b — Steam au scan des mises à jour

[PR #101](https://github.com/bassetthomas-design/virgil-2.0/pull/101) · Base `26e0c7aed3c1b60121be89076e76ec5d99a92bd4`

Le retour de test alpha.8 relève l’absence des jeux Steam dans le scan de l’écran Mises à jour. Le scan charge désormais les cartes Steam à côté des cartes Store et Pilotes. Il lit les bibliothèques locales et présente l’état et la date de chaque jeu pour information. La carte Steam distingue la fermeture du client de l’absence de session active et donne accès aux téléchargements du client.

La séquence Steam attend deux lectures avant de conclure « à jour selon Steam » pour un jeu resté à l’état installé lorsque la limite permet 30 secondes. Un échec réel a priorité sur une indisponibilité du même composant. Les refus des gardes exposent le champ nullable `SafetyBlocked`. Le port injectable du registre hors WinGet expose uniquement la lecture. `ReadSingleItemsAsync` reste une lecture explicite et isolée des commandes Scanner les mises à jour, Examiner les pilotes et Tout mettre à jour.

## CI Windows de tests seuls

Le [run #489](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45146128698), commit `6718639851d5bfa66ebb14f30468853124a09bc7`, s’est arrêté sur CS1503 dans le faux lanceur. Le commit de tests seuls suivant corrige le passage de `LaunchError`.

Le [run #490](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45146410245), commit `fa10a6b4209ad389ab50e5af1dfe8c404dc9022a`, a compilé avec 0 avertissement et 0 erreur. Les journaux CI donnent les assertions suivantes.

| Repère | Test | Assertion | Attendu | Obtenu |
| --- | --- | --- | --- | --- |
| A1 | `Second_disk_library_and_last_updated_metadata_are_read` | `Assert.Null() Failure: Value is not null` | `null` pour l’erreur du jeu en file | « Impossible de lire l’état de ce jeu dans Steam. » |
| A2 | `Steam_scan_distinguishes_statuses_freshness_and_local_metadata_limits_without_jargon` | `Assert.NotNull() Failure: Value is null` | Méthode `ScanSteamAsync` présente | `null` |
| A3 | `Steam_scan_distinguishes_closed_offline_connected_and_empty_libraries` | `Assert.NotNull() Failure: Value is null` | Propriété `IsProcessRunning` présente | `null` |
| A4 | `Steam_update_service_does_not_conclude_current_on_first_15s_poll_when_all_games_start_installed` | `Assert.Equal() Failure: Strings differ` | « reste à installer » | « à jour selon Steam » |
| A5 | `Typed_safety_blocked_field_overrides_message_heuristics_and_beats_unavailable` | `Assert.NotNull() Failure: Value is null` | Propriété `SafetyBlocked` présente | `null` |
| A6 | `Outside_winget_registry_access_reads_without_writing_and_rejects_writable_access` | `Assert.NotNull() Failure: Value is null` | Type `IOutsideWingetRegistryAccess` présent | `null` |
| B1 | `UpdatesView_scan_displays_steam_games_statuses_freshness_and_preserves_store_cards` | `Assert.Single() Failure: The collection did not contain any matching items` | Une carte `Launcher:Steam` | Zéro correspondance |
| B2 | `Closed_or_offline_steam_offers_open_steam_action_launching_steam_downloads` | `Assert.Contains() Failure: Item not found in collection` | « Steam est fermé : 1 jeu en téléchargement, 1 jeu en file d’attente, 1 jeu arrêté, 1 jeu à jour localement. Ouvrez Steam pour vérifier « Téléchargements ». » | Texte absent de la collection des textes visibles |

Le même run rapporte une `TimeoutException` après 10 secondes sur `LotDiagnosticChargeTests.Rendered_screen_shows_pending_hint_then_load_card_with_its_button` dans `tests/Virgil.App.Tests/LotDiagnosticChargeTests.cs:142`. Échec connu, sans rapport avec T6b, selon virgil (relecture). Ce test est inchangé.

### Gardes vertes dès le run de tests seuls

| Repère | Test |
| --- | --- |
| G1 | `Final_winget_pass_preserves_force_and_include_unknown` |
| G2 | `Steam_session_keeps_no_user_property_and_informational_games_keep_launcher_line_unchanged` |
| G3 | `Store_sequence_with_unsupported_and_failed_packages_remains_failed` |
| G4 | `H2_read_single_items_stays_isolated_and_windows_registry_rejects_writes` |

## Correctif et validation finale

Le correctif initial est `9772a100a253d1f94ebf3b3e0fdc179169e6f906`. Le [run Windows #492](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45189290156), SHA `45e390281823ece9589106c6474c05b2dd7cf1a3`, compile avec 0 avertissement et 0 erreur et rapporte trois échecs.

| Test | Résultat du run #492 |
| --- | --- |
| `D1bisBSteamTests.Cancellation_during_wait_keeps_Steam_running_and_remaining_games_coherent` | `Assert.Contains() Failure: Sub-string not found`. Le résumé attend « reste à installer » et contient « 2 restent à installer ». |
| `D1bisBPanelTests.Real_panel_uses_own_results_and_displays_confirmation(scenario: "cancelled")` | `Assert.Equal() Failure: Strings differ`. Le jeu attend « à jour selon Steam » et obtient « reste à installer ». |
| `LotMisesAJourSteamViewTests.Closed_or_offline_steam_offers_open_steam_action_launching_steam_downloads` | `InvalidOperationException` à la ligne 83 pendant l’écriture de l’artefact. `File.InternalWriteAllLinesAsync` énumère la projection différée qui lit `Button.Content` hors du thread WPF. Les assertions sur le bouton et le lancement restent sans résultat. |

Le commit `1a346bcc2858ea23d565109ffcbc24f05100b1d5` rétablit le résultat fondé sur la dernière lecture réussie quand l’attente est annulée. Le jeu resté installé reprend le statut « à jour selon Steam » et le résumé retrouve un seul jeu qui « reste à installer ». Une attente non annulée conserve la règle des deux lectures. Le test WPF est inchangé sur demande de virgil (relecture). Son écriture d’artefact reste bloquée par l’accès au bouton hors du thread WPF.

Les statuts Windows se consultent dans les [checks de la PR #101](https://github.com/bassetthomas-design/virgil-2.0/pull/101/checks). Aucun run vert final ni ID d’artefact final `Virgil-ui-smoke` n’est confirmé.

## Vérifications manuelles restantes sous Windows

1. Placer un jeu Steam dans une bibliothèque sur un second disque, par exemple `D:\SteamLibrary`, référencée dans `libraryfolders.vdf`. Lancer « Scanner les mises à jour » et vérifier la présence du jeu, sa date locale et la mention de données locales.
2. Comparer « Téléchargements » dans Steam aux cartes Virgil pour un jeu en file d’attente, un jeu en téléchargement actif et un jeu en pause. Vérifier les statuts « en file d’attente », « en téléchargement » et « téléchargement arrêté ».
3. Scanner avec Steam fermé, puis en mode hors ligne. Vérifier le message de chaque état et utiliser « Ouvrir Steam » pour vérifier l’ouverture de `steam://open/downloads` sans installation lancée par Virgil.
4. Scanner un PC qui expose un élément de microprogramme et vérifier le libellé « Microprogramme (BIOS) » sur l’écran Mises à jour.
5. Avec Steam ouvert et un jeu indiqué à jour localement, lancer « Tout mettre à jour ». Vérifier que la surveillance laisse 30 secondes et deux lectures à Steam avant d’afficher « à jour selon Steam » si Steam vient d’ouvrir la file.
