# Lot T6a — Mises à jour

Le scan de l’écran Mises à jour interroge en lecture seule WinGet, Windows Update et Microsoft Store. Il conserve les résultats valides et indique les vérifications partielles, avec l’accès au client d’origine.

Base de départ `aedc5fc1cd689f92e17655587fb4f8d4c4735f2a`. Après la publication du premier commit, `main` a reçu uniquement T5b (#99, `39cf280e22bd38268c5d6b673bcb4ad50b8546e2`). Les deux lots touchent des fichiers distincts.

## CI Windows

- [Run #483](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45140905986), commit de tests seuls `ee2798f6258d17a4c7e16e81fbaf225b327c1d48`. Compilation avec 0 avertissement et 0 erreur. A1 et A2 ont échoué par assertion. A3, A4 et A5 se sont arrêtés à la résolution DI avant leur assertion. Les deux gardes ont réussi. La fixture a été corrigée dans le commit de tests seuls suivant.
- [Run rouge #485](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45141290958), tests seuls sur `a78ebd85187ed8c7d60918ebfc881a1aad044cdb`. Compilation avec 0 avertissement et 0 erreur. Seuls A1 à A5 échouent, chacun par assertion. G-1 et G-2 réussissent. GitHub Actions teste le résultat de fusion `1644a499de30711b8e32187e0f22cb79d92db192` sur la base T5b.
- [Run vert du correctif #486](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45141659734), head `7ad089b3d40499f22fa801cb6708a5c7ce249269`, résultat de fusion testé `71b6dbf725b202f8673f2c5c3b96e9ae7166c125`. Compilation avec 0 avertissement et 0 erreur. Les 1 324 tests Core, 886 tests App et 14 tests UI smoke réussissent. [Artefact Virgil-ui-smoke, ID 14802866482](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45141659734/artifacts/14802866482).
- La [description de la PR #100](https://github.com/bassetthomas-design/virgil-2.0/pull/100) porte le lien du run vert FINAL, le SHA testé et l’ID de son artefact `Virgil-ui-smoke`. La trace est dans `lot-t6a/mises-a-jour-sources.txt`.

## Régressions et assertions

| Exigences | Test | Message du run rouge |
| --- | --- | --- |
| R-5, R-6 | A1 `Store_unmatched_and_unreadable_manifest_keep_matched_updates_and_mark_partial` | `Assert.DoesNotContain() Failure: Sub-string found`. Attendu, absence de `throw 'Version installée introuvable'`. Obtenu, `Found: "throw 'Version installée introuvable'"`. |
| R-1 à R-4, R-12 | A2 `Windows_update_read_only_scan_distinguishes_detected_current_and_unavailable_states` | `Assert.Equal() Failure: Strings differ`. Attendu, « 2 mises à jour Windows Update détectées (ouverture manuelle ou « Tout mettre à jour »). » (journal tronqué après la parenthèse ouvrante). Obtenu, « Windows Update non confirmé localement. ». |
| R-5 à R-10, R-12 | A3 `Screen_scan_covers_all_sources_and_keeps_valid_sources_when_another_fails` | `Assert.Equal() Failure: Strings differ`. Attendu, « 2 mises à jour détectées via WinGet. ». Obtenu, « WinGet détecté (1.8.1911). ». |
| R-8 à R-11 | A4 `Rendered_updates_screen_exposes_partial_store_card_and_opens_store_without_installing` | `Assert.Contains() Failure: Item not found in collection`. Attendu, « 1 mise à jour détectée via WinGet. » dans les textes visibles. Obtenu, `Not found: "1 mise à jour détectée via WinGet."`. |
| R-6 à R-10 | A5 `Store_up_to_date_without_unmatched_items_shows_current_status_at_scan` | `Assert.Equal() Failure: Strings differ`. Attendu, « À jour selon WinGet. ». Obtenu, « WinGet détecté (1.8.1911). ». |

## Tests verts dès le départ (gardes)

- G-1 `Winget_upgrade_keeps_force_and_include_unknown_flags` vérifie les arguments WinGet de scan et de mise à jour.
- G-2 `Pending_reboot_and_store_dedup_in_sequence_remain_intact` vérifie la détection du redémarrage en attente et la déduplication par la séquence.

## Tests fragiles connus

Aucun échec fragile connu relevé dans les runs #483, #485 et #486. Le résultat du run sur le head final figure dans la description de la PR.

## Limites et manipulations Windows

1. **W1** Les réponses WUA et Store sont simulées en CI. Sur un PC Windows, ouvrir « Mises à jour », cliquer sur « Scanner les mises à jour », puis vérifier les résultats WinGet, Windows Update et Microsoft Store ainsi que l’absence d’installation automatique.
2. **W1** Avec des offres Store en attente ou une session qui limite la recherche, vérifier que les résultats des autres sources restent visibles. Contrôler la carte « vérification partielle » et sa raison, puis cliquer sur « Ouvrir le Microsoft Store » et vérifier l’ouverture de la page Téléchargements et mises à jour.
3. **W1** Refaire le scan hors ligne ou avec le service Windows Update bloqué. Vérifier que la raison est affichée et que « Ouvrir Windows Update » ouvre les Paramètres, tout en laissant visibles les résultats disponibles.
4. **W2** Une offre Store dont la version proposée est inconnue reste en lecture seule et permet d’ouvrir le client.
5. **W3** La couverture du scan Steam relève de T6b.

## Liste de contrôle

1. Les chemins et zones modifiées ont été relus sur la base `aedc5fc`.
2. Les statuts et compteurs attendus sont écrits en dur dans les assertions.
3. Les cinq régressions échouent par assertion au run rouge de référence. Les deux gardes sont recensées séparément.
4. Le diff se limite aux quatorze fichiers autorisés. Aucun fichier ne chevauche T5b. Périmètre des tests existants, **aucune exception E**.
5. Les tests simulent les processus et écrivent leur preuve uniquement sous `artifacts/ui-smoke/lot-t6a/`. Les arguments `--force` et `--include-unknown` sont conservés.
6. Le contrôle des accents du corpus passe. Les textes des exigences sont repris avec leurs accents.
7. La trace WPF et ce document de preuve accompagnent la PR.
8. Les exigences proviennent du retour de test alpha.8. La décision de fusion relève de virgil (relecture).
