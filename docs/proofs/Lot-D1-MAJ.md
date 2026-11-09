# Lot D1 — Tout mettre à jour : contrôle final et bilan honnête

Base : `548e9828ce1a95d0b9253bd5297ed98940619bd0` (main après #79).
Branche : `codex/alpha8-lot-d1`. PR unique : [#80](https://github.com/bassetthomas-design/virgil-2.0/pull/80), brouillon, sans fusion.

## Séquence de preuve Windows

1. Tests seuls : `acf4342fc20a04fff90cb1cc13a1bdb4c27eafa6`, parent égal à la base exacte.
   [Run rouge 42609868685](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42609868685) : build réussi, **0 avertissement, 0 erreur** ; six échecs d'assertion D1, sans échec de compilation ou d'environnement.
   Deux cas constatent un seul scan au lieu du contrôle final ; trois refusent les statuts Store trompeurs ; le cas des applications homonymes constate la collision de cartes.
2. Correctif séparé initial : `b136b6dbb6db641d4f0549b8183780e9672279c1`.
   [Run intermédiaire 42610792528](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42610792528) : compilation sans avertissement, quatre assertions encore en échec (course de progression, compatibilité des preuves C et accents), ensuite corrigées sans modifier les fichiers de tests C.
3. Correctif final : `7cb42cd1388a2ee3b7f0877368857ebdf7612258`.
   [Run vert 42611515703](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42611515703) : **succès complet, 0 avertissement, 0 erreur** ; 1 155 tests Core, 631 tests App et 14 smoke tests UI réussis ; publication et construction de l'installateur réussies.

La CI Windows `build.yml` existante est la référence. Aucun SDK .NET disponible dans la session Linux ; aucun résultat local de compilation ou de tests n'est annoncé.

## Sources et preuve

| Source | Action dans la séquence confirmée | Contrôle final | Statuts possibles | Test |
| --- | --- | --- | --- | --- |
| WinGet / applications, y compris msstore | Paquets sélectionnés puis passe finale existante ; arguments et confirmation inchangés | Nouveau scan applicatif via `IUpdateScanService`, inventaire pilotes désactivé ; disponibilité et erreurs prises en compte | à jour, mis à jour, bloquée si reste, en erreur, indisponible, non lancée | `D1_final_scan_proves_remaining_or_empty_applications`, `D1_real_service_keeps_same_title_applications_distinct_by_identity` |
| Windows Update logiciels | Installation existante, jusqu'à trois passes | Nouvelle recherche WUA des logiciels non installés et non masqués ; code de recherche exact 2 requis | à jour, bloquée si reste, en erreur, indisponible, non lancée, redémarrage nécessaire | `Real_service_verifies_each_selected_source_after_execution_even_after_cancellation`, `Backend_final_checks_only_read_and_require_an_exact_success_marker` |
| Pilotes | Installation existante cochée par défaut | Nouvelle recherche WUA des pilotes, uniquement si sélectionnés | mêmes statuts | mêmes tests Core, `Update_plan_lists_script_steps_in_order_with_drivers_checked` |
| Defender | Préparation/signatures existantes | `Get-MpComputerStatus` : antivirus actif, âge des signatures égal à zéro et absence de signatures obsolètes | mêmes statuts ; échec de lecture visible | tests Core de contrôle final |
| Microsoft Update | Enregistrement existant | Lecture seule de la présence du service Microsoft Update | à jour, bloquée, en erreur, indisponible, non lancée | tests Core de contrôle final |
| Microsoft Store | Scan CIM existant ; mises à jour accessibles à WinGet dans ses passes confirmées | Aucun scan Store seul ne prouve l'installation des UWP hors WinGet | bloquée, indisponible, en erreur, non lancée ; jamais à jour sur la seule base du scan | `D1_store_scan_unavailable_or_reboot_cannot_prove_everything_current`, test Core Store sans processus |
| Nouvelle version Windows | Étape existante | Recherche finale des logiciels WUA ; tout reste logiciel empêche une conclusion positive | à jour, bloquée, en erreur, indisponible, non lancée, redémarrage nécessaire | tests Core de contrôle final |
| Redémarrage | Étape informative existante | `IPendingRebootProbe` et indicateurs des installations ; sonde absente/illisible interdit une conclusion positive | oui/non, ou contrôle indisponible explicite | `Pending_reboot_probe_drives_banner_and_report`, cas D1 reboot |
| Lanceurs | Fermeture existante strictement gardée | Fermeture seule ne prouve pas les mises à jour des jeux ; vérification dans les lanceurs nécessaire | bloquée, non lancée | `Blocked_launcher_is_reported_as_not_executed` |
| Firmware | Information seulement | Hors installation D1 | information | aucun nouveau chemin d'installation |

Les lectures finales du backend utilisent uniquement `IProcessRunner` dans Infrastructure, sans élévation. Les scripts sont fixes, sans entrée libre, installation, téléchargement, acceptation d'EULA ni enregistrement de service. La préparation de l'aperçu et un plan refusé ne les déclenchent pas : `Opening_update_plan_never_searches_Windows_Update` vérifie désormais le comportement du port de contrôle final, plutôt que l'absence textuelle de COM dans tout le backend.

Chaque source sélectionnée est relue après l'exécution, même après annulation entre étapes, avec un délai propre de 45 secondes par lecture. Ces jetons indépendants ne permettent aucune installation supplémentaire. Les sources décochées ne sont pas sondées par le backend. La lecture WinGet conserve le périmètre du scanner applicatif existant et peut donc détecter des restes hors des paquets individuellement sélectionnés : le bilan reste prudent.

## R2 : Store

`UpdateScanMethod` lance une recherche ; son succès ne signifie ni installation des UWP, ni inventaire Store vide. Le bilan indique « Recherche Store lancée — reste possible hors WinGet ; installation UWP non prouvée ». Aucun nouveau jeton helper ni API d'installation n'est ajouté. La passe finale WinGet existante reste dans le plan confirmé et couvre ce que WinGet trouve, dont msstore. Un Store indisponible reste information-only dans les lignes internes du rapport, avec un bilan et une sévérité Warning visibles.

## R4 et panneau C

La conclusion positive est produite uniquement si tous les contrôles des sources cochées sont positifs, toutes les étapes attendues ont un résultat réussi sans saut ni indisponibilité, sans annulation/refus/redémarrage. Un scan partiel, une erreur, un reste, une source non lancée ou non prouvée bloque cette conclusion. Le résumé persisté et les textes de fin utilisent ce verdict ; les détails d'exécution ne peuvent pas réintroduire les expressions positives interdites.

Le test positif sélectionne la passe WinGet seule, toutes les sources de ce plan étant alors prouvées vides. Le plan complet par défaut conserve le Store et les lanceurs : leur état non prouvé empêche une affirmation globale. Un redémarrage détecté pendant le nouveau scan ou la sonde effectuée après les recherches finales met aussi à jour le bandeau et le rapport (`D1_reboot_detected_during_final_scan_updates_banner_and_persisted_verdict`).

Le panneau C reste utilisé sans refonte : cartes indexées par `Component` + `Id`, résultats rapprochés par la même paire. Les phases techniques ne créent aucune carte hors des étapes confirmées ; la restauration et le redémarrage restent informatifs. Les répétitions d'avancement mettent à jour une carte existante. La conclusion apparaît une fois dans `FinalSummary`, le commentaire final est distinct ; barre à 100 % hors annulation. L'annulation garde le comportement C : elle ne force pas la barre à 100 %. Les textes d'exécution vides deviennent « non renseigné ».

Le test R10 utilise le **vrai `UpdateSequenceService`**, deux applications de même titre et un backend factice qui réussit l'une et refuse l'autre. Les tests existants du vrai service avec préparation bloquée préservent R11. Les lectures et installations des tests sont factices.

## Preuve UI et limites

`D1UpdatesPanelProofTests.Real_panel_renders_final_verdict_with_fake_sources` rend le vrai `OperationProgressPanel` en WPF/STA avec services factices, sans action SafeUi destructive. Les PNG ont été régénérés dans le run vert 42611515703 et extraits de [son artefact Virgil-ui-smoke, ID 13670407593](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42611515703/artifacts/13670407593), puis ouverts pour vérification visuelle :

- `artifacts/ui-smoke/lot-d1/tout-mettre-a-jour-bilan.png`
- `artifacts/ui-smoke/lot-d1/tout-mettre-a-jour-bloque-ou-reboot.png`

Tailles dans le ZIP : respectivement 35 814 et 36 485 octets. Empreinte SHA-256 du ZIP : `560ccf600be1b0c499dd6b3d20f1281afbc4af7ef3ca287c844e168c86f85bd7`. Les captures montrent le Store bloqué faute de preuve d'installation, WinGet indisponible dans ce scénario factice, puis l'état de redémarrage explicite. Le commentaire final est distinct de la conclusion et la barre atteint 100 %.

Il s'agit d'une **preuve STA/WPF factice**, pas d'une installation réelle. Les fichiers générés restent dans les artefacts CI, jamais dans le commit. Le PC cible alpha.8 doit encore vérifier manuellement la disparition des neuf mises à jour Store restantes et les résultats Windows Update réels.

Périmètre suivant loops (relecture) : D1 uniquement ; aucun changement du moteur de confirmation, des gardes, de la quarantaine, des workflows, des secrets ou réglages ; aucun lot D2/E/F. Aucun redémarrage automatique.

## Complément après relecture du run 42610792528

Les quatre échecs de ce run sont corrigés sur la même branche. Le marqueur technique `VIRGIL_SEARCH_INCOMPLETE` est reconnu dans stdout/stderr et traduit en recherche indisponible, même si une sortie contradictoire prétend réussir. Les détails bloqués ou en échec conservent le résultat de leur propre étape ; le contrôle final reste dans le bilan par source. Les tests C de détail et de conclusion unique restent inchangés.

Chaque carte WinGet nomme son identité ; en l'absence d'un résultat, « Aucun résultat confirmé. Identité : … » reste associé à `Component` + `Id`, avec le statut non lancée. `D1_missing_result_keeps_application_identity_without_claiming_success` passe par le vrai service puis simule la perte d'un résultat ; il vérifie les deux identités et interdit la conclusion positive. La CI Windows et les PNG du commit complémentaire sont référencés dans la PR #80.
