# Lot D1 — Tout mettre à jour : contrôle final et bilan honnête

Base : `548e9828ce1a95d0b9253bd5297ed98940619bd0` (main après #79).
Branche : `codex/alpha8-lot-d1`. PR unique : [#80](https://github.com/bassetthomas-design/virgil-2.0/pull/80), brouillon, sans fusion.

## Séquence de preuve Windows

1. Tests seuls : `acf4342fc20a04fff90cb1cc13a1bdb4c27eafa6`, parent égal à la base exacte.
   [Run rouge 42609868685](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42609868685) : build réussi, **0 avertissement, 0 erreur** ; six échecs d'assertion D1, sans échec de compilation ou d'environnement.
   Deux cas constatent un seul scan au lieu du contrôle final ; trois refusent les statuts Store trompeurs ; le cas des applications homonymes constate la collision de cartes.
2. Correctif séparé initial : `b136b6dbb6db641d4f0549b8183780e9672279c1`.
   [Run intermédiaire 42610792528](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42610792528) : compilation sans avertissement, quatre assertions encore en échec (course de progression, compatibilité des preuves C et accents), ensuite corrigées sans modifier les fichiers de tests C.
3. Premier correctif validé, avant la relecture des statuts : `7cb42cd1388a2ee3b7f0877368857ebdf7612258`.
   [Run vert 42611515703](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42611515703) : **succès complet, 0 avertissement, 0 erreur** ; 1 155 tests Core, 631 tests App et 14 smoke tests UI réussis ; publication et construction de l'installateur réussies.

La CI Windows `build.yml` existante est la référence. Aucun SDK .NET disponible dans la session Linux ; aucun résultat local de compilation ou de tests n'est annoncé.

## Sources et preuve

| Source | Action dans la séquence confirmée | Contrôle final | Statuts possibles | Test |
| --- | --- | --- | --- | --- |
| WinGet / applications, y compris msstore | Paquets sélectionnés puis passe finale existante ; arguments et confirmation inchangés | Nouveau scan applicatif via `IUpdateScanService`, inventaire pilotes désactivé ; disponibilité et erreurs prises en compte | à jour, mis à jour, reste à installer, reste à vérifier, bloquée seulement par une garde, en erreur, indisponible, non lancée | `D1_final_scan_proves_remaining_or_empty_applications`, `D1InformationOnlyTests`, `D1WingetIdentityTests` |
| Windows Update logiciels | Installation existante, jusqu'à trois passes | Nouvelle recherche WUA des logiciels non installés et non masqués ; code de recherche exact 2 requis | à jour, reste à installer, bloquée seulement par une garde, en erreur, indisponible, non lancée, redémarrage nécessaire | `Real_service_verifies_each_selected_source_after_execution_even_after_cancellation`, `Backend_final_checks_only_read_and_require_an_exact_success_marker` |
| Pilotes | Installation existante cochée par défaut | Nouvelle recherche WUA des pilotes, uniquement si sélectionnés | mêmes statuts | mêmes tests Core, `Update_plan_lists_script_steps_in_order_with_drivers_checked` |
| Defender | Préparation/signatures existantes | `Get-MpComputerStatus` : antivirus actif, âge des signatures égal à zéro et absence de signatures obsolètes | mêmes statuts ; échec de lecture visible | tests Core de contrôle final |
| Microsoft Update | Enregistrement existant | Lecture seule de la présence du service Microsoft Update | à jour, reste à installer, bloquée si garde, en erreur, indisponible, non lancée | tests Core de contrôle final |
| Microsoft Store | Scan CIM existant ; mises à jour accessibles à WinGet dans ses passes confirmées | Aucun scan Store seul ne prouve l'installation des UWP hors WinGet | à vérifier après scan, indisponible, en erreur, non lancée, bloquée si garde ; jamais à jour sur la seule base du scan | `D1_store_scan_unavailable_or_reboot_cannot_prove_everything_current`, test Core Store sans processus |
| Nouvelle version Windows | Étape existante | Recherche finale des logiciels WUA ; tout reste logiciel empêche une conclusion positive | à jour, reste à installer, bloquée si garde, en erreur, indisponible, non lancée, redémarrage nécessaire | tests Core de contrôle final |
| Redémarrage | Étape informative existante | `IPendingRebootProbe` et indicateurs des installations ; sonde absente/illisible interdit une conclusion positive | oui/non, ou contrôle indisponible explicite | `Pending_reboot_probe_drives_banner_and_report`, cas D1 reboot |
| Lanceurs | Fermeture existante strictement gardée | Fermeture seule ne prouve pas les mises à jour des jeux ; vérification dans les lanceurs nécessaire | à vérifier, non lancée, bloquée si refus de sécurité | `Blocked_launcher_is_reported_as_not_executed` |
| Firmware | Information seulement | Hors installation D1 | information | aucun nouveau chemin d'installation |

Les lectures finales du backend utilisent uniquement `IProcessRunner` dans Infrastructure, sans élévation. Les scripts sont fixes, sans entrée libre, installation, téléchargement, acceptation d'EULA ni enregistrement de service. La préparation de l'aperçu et un plan refusé ne les déclenchent pas : `Opening_update_plan_never_searches_Windows_Update` vérifie désormais le comportement du port de contrôle final, plutôt que l'absence textuelle de COM dans tout le backend.

Chaque source sélectionnée est relue après l'exécution, même après annulation entre étapes, avec un délai propre de 45 secondes par lecture. Ces jetons indépendants ne permettent aucune installation supplémentaire. Les sources décochées ne sont pas sondées par le backend. La lecture WinGet conserve le périmètre du scanner applicatif existant et peut donc détecter des restes hors des paquets individuellement sélectionnés : le bilan reste prudent.

## R2 : Store

`UpdateScanMethod` lance une recherche ; son succès ne signifie ni installation des UWP, ni inventaire Store vide. Le bilan indique le statut « à vérifier » et le détail « à terminer dans le Microsoft Store ». Ce statut ne constitue pas une preuve de mise à jour. Aucun nouveau jeton helper ni API d'installation n'est ajouté. La passe finale WinGet existante reste dans le plan confirmé et couvre ce que WinGet trouve, dont msstore. Un Store indisponible reste information-only dans les lignes internes du rapport, avec un bilan et une sévérité Warning visibles.

## R4 et panneau C

La conclusion positive est produite uniquement si tous les contrôles des sources cochées sont positifs, toutes les étapes attendues ont un résultat réussi sans saut ni indisponibilité, sans annulation/refus/redémarrage. Un scan partiel, une erreur, un reste, une source non lancée ou non prouvée bloque cette conclusion. Le résumé persisté et les textes de fin utilisent ce verdict ; les détails d'exécution ne peuvent pas réintroduire les expressions positives interdites.

Le test positif sélectionne la passe WinGet seule, toutes les sources de ce plan étant alors prouvées vides. Le plan complet par défaut conserve le Store et les lanceurs : leur état non prouvé empêche une affirmation globale. Un redémarrage détecté pendant le nouveau scan ou la sonde effectuée après les recherches finales met aussi à jour le bandeau et le rapport (`D1_reboot_detected_during_final_scan_updates_banner_and_persisted_verdict`).

Le panneau C reste utilisé sans refonte : cartes indexées par `Component` + `Id`, résultats rapprochés par la même paire. Les phases techniques ne créent aucune carte hors des étapes confirmées ; la restauration et le redémarrage restent informatifs. Les répétitions d'avancement mettent à jour une carte existante. La conclusion apparaît une fois dans `FinalSummary`, le commentaire final est distinct ; barre à 100 % hors annulation. L'annulation garde le comportement C : elle ne force pas la barre à 100 %. Les textes d'exécution vides deviennent « non renseigné ».

Le test R10 utilise le **vrai `UpdateSequenceService`**, deux applications de même titre et un backend factice qui réussit l'une et refuse l'autre. Les tests existants du vrai service avec préparation bloquée préservent R11. Les lectures et installations des tests sont factices.

Ce premier test R10 utilisait toutefois un scan final vide : il ne suffisait pas à prouver l'indépendance des statuts avec un reste réel. La nouvelle preuve de contre-expertise ci-dessous couvre ce cas.

## Preuve UI et limites

`D1UpdatesPanelProofTests.Real_panel_renders_final_verdict_with_fake_sources` rend le vrai `OperationProgressPanel` en WPF/STA avec services factices, sans action SafeUi destructive. Les PNG ont été régénérés dans le run vert 42611515703 et extraits de [son artefact Virgil-ui-smoke, ID 13670407593](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42611515703/artifacts/13670407593), puis ouverts pour vérification visuelle :

- `artifacts/ui-smoke/lot-d1/tout-mettre-a-jour-bilan.png`
- `artifacts/ui-smoke/lot-d1/tout-mettre-a-jour-bloque-ou-reboot.png`

Tailles dans le ZIP : respectivement 35 814 et 36 485 octets. Empreinte SHA-256 du ZIP : `560ccf600be1b0c499dd6b3d20f1281afbc4af7ef3ca287c844e168c86f85bd7`. Ces captures historiques montrent le Store bloqué faute de preuve d'installation, WinGet indisponible dans ce scénario factice, puis l'état de redémarrage explicite. Elles précèdent les libellés corrigés après loops (relecture) ; les nouvelles captures sont référencées ci-dessous. Le commentaire final est distinct de la conclusion et la barre atteint 100 %.

Il s'agit d'une **preuve STA/WPF factice**, pas d'une installation réelle. Les fichiers générés restent dans les artefacts CI, jamais dans le commit. Le PC cible alpha.8 doit encore vérifier manuellement la disparition des neuf mises à jour Store restantes et les résultats Windows Update réels.

Périmètre suivant loops (relecture) : D1 uniquement ; aucun changement du moteur de confirmation, des gardes, de la quarantaine, des workflows, des secrets ou réglages ; aucun lot D2/E/F. Aucun redémarrage automatique.

## Complément après relecture du run 42610792528

Les quatre échecs de ce run sont corrigés sur la même branche. Le marqueur technique `VIRGIL_WUA_001` est reconnu dans stdout/stderr et traduit en recherche indisponible, même si une sortie contradictoire prétend réussir. Les détails bloqués ou en échec conservent le résultat de leur propre étape ; le contrôle final reste dans le bilan par source. Les tests C de détail et de conclusion unique restent inchangés.

Chaque carte WinGet nomme son identité ; en l'absence d'un résultat, « Aucun résultat confirmé. Identité : … » reste associé à `Component` + `Id`, avec le statut non lancée. `D1_missing_result_keeps_application_identity_without_claiming_success` passe par le vrai service puis simule la perte d'un résultat ; il vérifie les deux identités et interdit la conclusion positive. La CI Windows et les PNG du commit complémentaire sont référencés dans la PR #80.

## Nouvelle preuve après loops (relecture) — statuts honnêtes

Le marqueur `VIRGIL_WUA_001`, identique dans backend et tests, remplace celui contenant le mot anglais rejeté par le garde d'accents. Le run rouge précédent `42612973473`, head `c969c98680aa5e632a30a20edd1f15ee382c62ba`, constitue la preuve de ce rejet ; aucune modification du garde d'accents.

Après cette correction préalable (`a8f43827297e922c67a55f1df3a7abbb94234189`), le commit **tests seuls** `93983a11666e472cc7a6670bdfde04e290e4634a` produit le [nouveau rouge Windows 42613762954](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42613762954) : compilation avec **0 avertissement et 0 erreur**, puis 4 échecs d'assertion sur 1 161 tests Core et 12 sur 644 tests App. Ces 16 échecs reproduisent les problèmes de statut, détail, destination et redémarrage ; aucun échec d'environnement, de compilation ou d'accents.

Le correctif séparé applique les règles suivantes :

- `VIRGIL_REMAINING` et les applications encore disponibles donnent « reste à installer ».
- Un scan Store seul donne « à vérifier », détail « à terminer dans le Microsoft Store » ; les lanceurs donnent « à vérifier », détail « à terminer dans le lanceur ».
- « bloquée » est réservé aux points de restauration non vérifiés, refus et gardes de sécurité ; les politiques et le ConfirmedPlan restent inchangés.
- Les deux nouveaux statuts restent exclus de `IsCurrent`. R4 interdit donc toujours la conclusion positive avec eux.
- Chaque carte non réussie retire les détails « Terminé » au profit d'une formulation d'exécution et de contrôle final ; les identités restent attachées à leur propre carte.
- Si seuls le Store et/ou les lanceurs restent « à vérifier », la conclusion nomme directement leurs destinations, aussi dans le résumé persisté.
- Le redémarrage utilise « oui, après une installation », « après Windows Update » ou « signalé par … », sans parenthèse technique.

`D1ReviewVerdictTests` exerce le vrai `UpdateSequenceService` avec backend et scans factices. Il vérifie les trois destinations possibles Store/lanceur/les deux, les cartes avec reste/indisponibilité/erreur/scan Store, le vrai blocage de restauration et les textes de redémarrage. Les attentes des anciens tests D1/L6+ sont adaptées aux nouveaux libellés ; les fichiers de tests WPF du lot C restent inchangés.

Correctif séparé : `8fbdba0999edb989327747a67f0fd57d81216108`. Le [nouveau vert Windows 42614652452](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42614652452) réussit intégralement : **0 avertissement, 0 erreur**, **1 161 tests Core, 644 tests App et 14 smoke tests UI** ; publication et installateur réussis. Les 16 cas initialement rouges sont tous réussis.

Les deux PNG ont été régénérés dans `artifacts/ui-smoke/lot-d1/`, extraits de [l’artefact 13672317160](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42614652452/artifacts/13672317160) et ouverts pour vérification visuelle. Tailles : `tout-mettre-a-jour-bilan.png` = 30 642 octets ; `tout-mettre-a-jour-bloque-ou-reboot.png` = 35 477 octets. SHA-256 du ZIP : `3674fefdeb5f87e768498e1f3e9bbfd680d4785fc255e012b3d7fe5e2789c647`.

Les captures D1 utilisent un WinGet factice prouvé à jour et un Store « à vérifier » : le bilan sans reboot indique « à terminer dans le Microsoft Store ». Avec reboot, la carte WinGet dit « Étape exécutée ; contrôle final : redémarrage nécessaire » et le texte dit « Redémarrage nécessaire : oui, après une installation. ». Aucun détail de carte non réussie ne contient « Terminé ». La preuve reste **WPF/STA factice** et ne valide pas les installations réelles alpha.8.

## R10 — contre-expertise : résultat propre à chaque application

Le commit **tests seuls** `168a94bed4152ec60c0d551a4f36dd617b275f93` reproduit le défaut sur le head précédent `b40b71dd916a37a574161c37ef4b8a1a790b83f4`. [Run rouge Windows 42617335566](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42617335566) : compilation **0 avertissement, 0 erreur** ; 1 161 tests Core réussis, 644 tests App réussis sur 647 et **3 vrais échecs d'assertion R10**. Pour app-a réussie et absente du scan final, l'ancien code affiche selon le cas « en erreur », « bloquée » ou « non lancée » au lieu de « mis à jour ».

`D1WingetIdentityTests` utilise le vrai `UpdateSequenceService`, un backend factice et un scan final qui conserve app-b. Il distingue l'échec ordinaire du refus explicite d'une garde de sécurité. Le troisième cas annule après app-b : app-c n'est jamais exécutée mais reste présente au scan final. Les assertions vérifient statut, détail propre et absence de texte contradictoire ou de résultat d'une autre identité sur chaque carte.

Le correctif conserve séparément le rapport brut du scan final et les verdicts agrégés. Les cartes `UpdateComponent.Winget` rapprochent leur résultat par **Id + Component**, puis recherchent uniquement leur propre Id applicatif au scan final, sans lire le statut global de la source :

| Résultat propre | Présence de cet Id au contrôle final complet | Carte |
| --- | --- | --- |
| Réussie, sans redémarrage propre | absente | mis à jour |
| Réussie | encore disponible | reste à installer ; exécution mentionnée sans promettre une mise à jour achevée |
| Échec ou refus d'installation ordinaire | présente ou absente | en erreur ; détail propre de l'échec |
| Refus explicite d'une garde de sécurité | présente ou absente | bloquée ; détail propre de la garde |
| Non lancée, sautée ou résultat absent | encore disponible | reste à installer ; mention explicite de l'absence d'exécution |
| Non lancée ou résultat absent | absente ou contrôle non prouvé | Non lancée ; jamais de mise à jour inventée |
| Réussie mais contrôle incomplet/indisponible | absence non prouvée | en erreur ou indisponible ; contrôle non prouvé |

Le bilan global et la carte de passe finale restent agrégés ; ils ne fournissent plus le statut des cartes d'application. Un refus d'installation ordinaire ne devient plus un blocage de sécurité par le seul mot « refus ». Cette distinction concerne le bilan, sans modification de ConfirmedPlan ni des gardes d'exécution.

Correctif séparé : `8b4b5f62085b8fa8d06b50032af9e1c2bdae681e`. Le [nouveau vert Windows 42618155247](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42618155247) réussit intégralement : **0 avertissement, 0 erreur ; 1 161 tests Core, 648 App et 14 smoke UI réussis**, publication et installateur réussis. Les trois assertions R10 initialement rouges et la nouvelle preuve WPF sont réussies.

Les trois PNG `lot-d1/` ont été régénérés dans ce run : bilan (30 642 octets), reboot (35 477 octets), et `tout-mettre-a-jour-identites-winget.png` (40 265 octets). [Artefact 13673513099](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42618155247/artifacts/13673513099), SHA-256 du ZIP : `f9d5101a0545dd2ed33daefd6c97d0040c6ec35cdd60f10462cf121ead5ed708`. Le nouveau PNG a été extrait et ouvert : app-a « mis à jour » avec son succès propre, app-b « en erreur » avec son échec propre, app-c « reste à installer » et « Non lancée : annulation demandée ». La barre reste sous 100 % après annulation. Preuve **WPF/STA factice** utilisant le vrai service ; les scans et installations restent factices.

## P2 — éléments InformationOnly encore détectés

Le [commentaire 4512650970](https://github.com/bassetthomas-design/virgil-2.0/pull/80#discussion_r4512650970) relève qu'un identifiant tronqué non résolu ou une mise à jour firmware/BIOS peut rester `InformationOnly` sans être installé. Ce statut ne prouve jamais une source vide.

Le commit **tests seuls** `a63c2c364041abe6f1f679f49b62afc528186158` donne le [rouge Windows 42620451462](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42620451462) : **0 avertissement, 0 erreur de compilation**, 1 161 Core réussis ; 648 App réussis sur 654 et **6 échecs d'assertion P2**. Trois montrent la conclusion positive indue, trois l'indicateur `HasRemaining` faux. `D1InformationOnlyTests` passe par le vrai service, met à jour app-a seule avec succès et décoche la passe groupée. Le scan final contient uniquement un élément informatif : identifiant tronqué, firmware, ou même Id qu'app-a. Le verdict privé est aussi vérifié sans ajouter d'API de test en production.

Le correctif distingue les éléments `Available` (« reste à installer ») et `InformationOnly` (« reste à vérifier »). `HasRemaining` compte les deux : une détection positive reste vraie même si d'autres lectures sont incomplètes ; une absence non prouvée ne devient pas fausse. `IsCurrent` reste limité à « à jour » / « mis à jour », donc les conclusions du panneau et du rapport persisté restent prudentes. Une carte portant elle-même un Id encore informatif indique aussi « reste à vérifier » ; les autres cartes gardent leur résultat propre R10. Aucun élément informatif supplémentaire n'entre dans le plan ni dans une installation automatique.

Correctif séparé : `7530d7c79eab8ca6a2dc9d7b47a4670127f26c5a`. Le [vert Windows 42621119857](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42621119857) est intégralement réussi : **0 avertissement, 0 erreur ; 1 161 Core, 655 App et 14 smoke UI réussis**, publication et installateur réussis. Les six cas P2 et la nouvelle preuve WPF sont réussis.

Les quatre PNG `lot-d1/` sont régénérés dans [l’artefact 13676073231](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42621119857/artifacts/13676073231), ZIP SHA-256 `993a8972efbb012ae82bfeba94dfaa75e73306af191fe40f08b47cf628894f8f`. `tout-mettre-a-jour-information-only.png` (28 036 octets) a été extrait et ouvert : la carte et la source affichent « reste à vérifier », sans conclusion positive, avec passe groupée décochée. Les trois captures antérieures sont aussi présentes. Preuve **WPF/STA factice**, sans installation réelle alpha.8. Référence du fil de réponse : commentaire 4512650970.

## Source partiellement exécutée — loops (relecture)

Le commit **tests seuls** `7f748d780e9aee42ed8af7e5dd62465975436006` donne le [rouge Windows 42624134199](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42624134199) : **0 avertissement, 0 erreur de compilation** ; 1 161 Core réussis, 655 App réussis sur 659 et **4 échecs d'assertion de source**. Le vrai service et un backend factice annulent après app-a réussie ou après l'échec d'app-b, avec restes ou scan final vide. Chaque cas vérifie la ligne WinGet dans `FinalSummary` et dans le rapport persisté, son statut et son détail court sans message brut.

Une source avec une étape exécutée n'est plus « non lancée » à cause des étapes suivantes annulées. WinGet conserve « reste à installer » si le scan final détecte un Id `Available` ; sinon un échec exécuté donne « en erreur », et un scan vide sans échec exécuté conserve « à jour ». Les vrais blocages et les états indisponibles restent explicites ; les éléments `InformationOnly` conservent P2. Les messages détaillés restent sur leurs cartes propres R10 : le bilan de source ne copie plus les résultats bruts ni « Étape terminée après annulation ».

La section P2 ci-dessus contient déjà le vrai run vert `42621119857` et son artefact. Correctif séparé `4aca3a96bd7d30c67083f476403d3a18d505b573` : [vert Windows 42625231928](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42625231928), **0 avertissement, 0 erreur ; 1 161 Core, 659 App et 14 smoke UI réussis**, publication et installateur réussis. Les quatre PNG sont régénérés dans [l’artefact 13677798631](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42625231928/artifacts/13677798631), ZIP SHA-256 `ed0765d128d0ebf0774ace40f8dfbac5120ccbe3572c84057869b772662bb362`. Le PNG `tout-mettre-a-jour-identites-winget.png` (37 695 octets), extrait et ouvert, montre la ligne courte « WinGet / applications (y compris msstore) : reste à installer — Contrôle final : reste applicatif détecté. » sans résultats bruts. Les trois cartes R10 et la preuve P2 restent conformes ; preuve **WPF/STA factice**, sans installation réelle alpha.8.
