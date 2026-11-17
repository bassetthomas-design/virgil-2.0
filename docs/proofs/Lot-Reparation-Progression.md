# Lot T2b — Réparation : progression par étape et bilan en français

Base initiale vérifiée : `0da6adb140dc5b862cda5d5fa6cb8c490de59071`, contenant T2a (#91). Avant le premier commit, remise autorisée sur `287649d24039694cff13c90f60ea8878c2c783d0`. Branche : `codex/lot-reparation-progression`. État de la PR et référence finale : [PR #94](https://github.com/bassetthomas-design/virgil-2.0/pull/94), conservée en brouillon, sans fusion. T2b seulement.

## Problème, cause et comportement livré

Le retour de test alpha.8 montre des pourcentages répétés, le passage de 100 % à 0 % entre phases CHKDSK, des noms d'exécutables dans le fil et le journal brut dans le statut final. Le runner prenait le premier pourcentage sans vérifier son ambiguïté ni sa progression ; le ViewModel diffusait les changements et utilisait directement ReadableError.

Le runner privilégie Total, accepte sinon un pourcentage unique, tronque et borne à 0..100, puis rapporte seulement une augmentation pendant la même commande. Le dispatcher nomme les étapes en français avec leur indice. Le statut affiche le pourcentage ou « en cours… » et le temps écoulé ; le fil reçoit une seule fois chaque étape, sans répétition liée au pourcentage ou à l'horloge.

RepairCheckSummary, pure et située dans Core, lit chaque ligne VIRGIL à quatre champs dans son ordre. Chaque contrôle conserve son propre résultat : OK/INFO sans anomalie, FINDING avec constat et action issue de RepairsFor et du catalogue, UNKNOWN en échec. La première ligne distingue bilan terminé, partiel, annulé ou en échec. Détails techniques est replié par défaut et contient le journal brut. SummaryOutput, ReadableError, IntegrityFinding, le rapport, ResultMessage et les messages de RunGuided restent inchangés.

## Runs Windows et historique linéaire

- Tests seuls initiaux : `96ed1f62571a2b2fd6881ce5e080eeb9f04a598e`, [run 455](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44098461086). Échec de compilation CS0535 du nouveau FakeHistory : GetAsync manquant. Ce run ne compte pas comme rouge de validation. Le membre a été ajouté dans un nouveau commit de tests seuls, sans amend.
- Rouge par assertion : `7d2c8f009dabdc6215dcb165e5b7e711099c7063`, [run 456](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44098861078), compilation **0 avertissement, 0 erreur**. Core : 1308 tests, 1300 verts et 8 rouges ; App : 847 tests, 840 verts et 7 rouges. Les 14 cas de régression ci-dessous sont rouges par assertion ; B3 échoue aussi sur l'espace décrit dans les gardes. Aucun test existant en échec.
- Attendu B3 ajusté, sur consigne explicite de loops (relecture), dans le commit de tests seuls `7f68af6331b056e16d4973c6409fac011224cbd7` : [run 458](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44111901518), compilation **0 avertissement, 0 erreur**, exactement **14 rouges par assertion** et **B3 vert**. Core : 1309 tests, 1302 verts et 7 rouges ; App : 855 tests, 848 verts et 7 rouges. Aucun test existant en échec.
- Correctif séparé : `3835e68cc6149d7b846ae272b8f8fbab0745cb16`, [run 459](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44112414242), entièrement vert : **0 avertissement, 0 erreur ; 1309/1309 Core, 855/855 App, 14/14 UI**. Publication et installateur réussis. Le checkout PR `c38f659` intègre ce head à `373118f0b77245ceba090e32d6136c0a8720685d` ; la branche du lot conserve son historique linéaire, sans rebase après son premier commit.
- Le **run vert FINAL du head final**, son numéro, son SHA exact et l'ID de son artefact sont consignés dans la section 5 de la description de la PR #94. Ce renvoi distingue le run du correctif du run après ajout de cette preuve, conformément à M10. Aucun run rouge n'est présenté comme preuve finale.

La différence des totaux entre 456 et 458 provient des tests de main intégrés par la référence PR ; aucun fichier des lots parallèles n'est modifié sur cette branche.

## Exigences et messages réels du rouge 456

Les noms ci-dessous appartiennent aux trois nouveaux fichiers LotReparationProgressionWindowsTests, LotReparationProgressionTests et LotReparationBilanTests. Tous les cas de cette table passent au run 459. Les `···` sont les troncatures du journal xUnit, conservées telles quelles ; les attendus complets sont écrits en dur dans les tests.

| Exigence / cas | Assertion réelle | Attendu, puis obtenu dans le journal 456 |
| --- | --- | --- |
| R-B1 / B1 Runner_reports_total_and_monotone_progress_only | `Assert.Equal() Failure: Collections differ`, position 1 | `[5, 10, 33, 34, 100]` ; `[5, 5, 10, 100, 0, ···]` |
| R-B2 / B2 Dispatcher_names_each_step_in_french, SfcScan | `Assert.Equal() Failure: Collections differ`, position 0 | `["Étape 1 sur 1 : Fichiers système (SFC, réparation)"]` ; `["sfc.exe"]` |
| R-B2 / B2, DismRestoreHealth | même message, position 0 | `["Étape 1 sur 1 : Image Windows (DISM, réparation)"]` ; `["dism.exe"]` |
| R-B2 / B2, RenewIp | même message, position 0 | `["Étape 1 sur 2 : Adresse IP (libération)", "Étape 2 sur 2 : Adresse IP (renouvellement)"]` ; `["ipconfig.exe"]` |
| R-B2 / B2, ResetPrintQueue | même message, position 0 | `["Étape 1 sur 1 : File d'impression"]` ; `["virgil-print"]` |
| R-B2 / B2, VerifyHealth | même message, position 0 | `["Étape 1 sur 4 : Fichiers système (SFC, vérificatio"···, "Étape 2 sur 4 : Image Windows (DISM, contrôle rapi"···, "Étape 3 sur 4 : Disque système (CHKDSK, analyse en"···, "Étape 4 sur 4 : Services Windows"]` ; `["sfc.exe", "dism.exe", "chkdsk.exe", "virgil-services"]` |
| R-B2 / B2, VerifyHealthDeep | même message, position 0 | `["Étape 1 sur 5 : Fichiers système (SFC, vérificatio"···, "Étape 2 sur 5 : Image Windows (DISM, contrôle rapi"···, "Étape 3 sur 5 : Image Windows (DISM, analyse)", "Étape 4 sur 5 : Disque système (CHKDSK, analyse en"···, "Étape 5 sur 5 : Services Windows"]` ; `["sfc.exe", "dism.exe", "chkdsk.exe", "virgil-services"]` |
| R-B4/R-B5 / B4 Verify_shows_french_summary_per_check, F1 PartialFailure | `Assert.Equal() Failure: Strings differ`, position 1 | `"Vérification partielle : 3 contrôle(s) en"···` ; `"VIRGIL\|SFC\|OK\|Aucune anomalie détectée\nÉt"···` |
| R-B4/R-B5 / B4, F2 Completed | même message, position 21 | `···"érification terminée : aucune anomalie.\nF"···` ; `···"érification terminée. Consultez le rappor"···` |
| R-B4/R-B5 / B4, F3 Cancelled | même message, position 13 | `"Vérification annulée : les contrôles suiv"···` ; `"Vérification terminée. Consultez le rappo"···` |
| R-B4/R-B5 / B4, F4 Failed | même message, position 0 | `"Vérification en échec : Droits administra"···` ; `"Droits administrateur requis."` |
| R-B3 / B5 Progress_line_updates_and_feed_gets_one_message_per_step | `Assert.Equal() Failure: Collections differ`, position 0 | deux étapes distinctes ; cinq messages avec pourcentage/« en cours… » et temps écoulé. Le journal affiche deux entrées attendues et cinq obtenues, tronquées après le libellé SFC/DISM. |
| R-B6 / B6 Technical_details_are_collapsed_in_view | `Assert.Single() Failure: The collection did not contain any matching items` | `Expected: (predicate expression)` ; `Collection: [<UserControl x:Class="Virgil.App.Views.InterventionsView" ...` : aucun élément nommé InterventionsTechnicalDetails. |
| R-B4/R-B6 / B7 Loaded_view_captures_collapsed_and_expanded_summary | `Assert.Equal() Failure: Strings differ`, position 1 | `"Vérification partielle : 3 contrôle(s) en"···` ; `"VIRGIL\|SFC\|OK\|Aucune anomalie détectée\r\nÉ"···` |

B1 lance le vrai ElevatedCommandRunner et le vrai powershell.exe avec neuf Write-Output sans effet système ; le processus termine avec code 0, sans erreur ni dépassement, avant l'assertion de progression. B2 utilise le vrai dispatcher et la vraie allowlist ; son faux runner rapporte 50, et les tests exigent `[null, 50]` pour chaque étape. B4 utilise le vrai ViewModel et compare le bilan exact, la première ligne envoyée au fil et TechnicalDetailsText lu par réflexion sur la propriété publique après Assert.NotNull. B5 fournit les Elapsed et installe un SynchronizationContext synchrone restauré dans finally, sans horloge réelle attendue. B6 lit la vraie vue ; B7 charge la vraie vue WPF STA avec le thème et écrit les deux captures avant son assertion de bilan, puis vérifie l'Expander et le TextBox. Aucun rouge de cette table n'est une exception de fixture.

## Gardes verts dès le départ et B3

B3 Summary_output_is_unchanged est le seul nouveau garde prévu vert sans correctif. Le run 456 expose une erreur de l'attendu fourni : `Assert.Equal() Failure: Strings differ`, position 394 ; attendu `virgil-services\nCode sortie : 0.`, obtenu `virgil-services \nCode sortie : 0.`. L'espace ASCII avant le saut de ligne appartient au journal de production lorsque la commande n'a pas d'arguments.

Après arrêt et clarification, loops (relecture) a demandé de conserver ce comportement. Seule la valeur attendue fixe B3 a été changée, avec `"Étape : virgil-services \n"`, dans `7f68af6`, commit de tests seuls. Aucun Trim ni normalisation des espaces n'affaiblit la comparaison. B3 passe au run 458 avant tout correctif, puis au run 459. Le journal brut est comparé intégralement, ligne vide et espace final compris.

Les tests existants restent inchangés et verts au run 459, notamment ExecutionResultKeepsMessagesAndSessionState, CancelEachRepairOperationAcknowledgesWithoutStartingNext, InterfaceAccentTests, LongOperationArchitectureTests et Virgil.UiTests. **Aucune exception sur des tests existants.**

## Artefacts et captures

[Virgil-ui-smoke du correctif, ID 14341322862](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44112414242/artifacts/14341322862). B7 produit avant ses assertions :

- `lot-t2b/bilan-replie.png` : bilan F1 et détails repliés.
- `lot-t2b/bilan-deplie.png` : même bilan, journal brut déplié.

Les captures sont générées par les tests Windows, pas ajoutées au dépôt. Les artefacts des runs rouges 456 (ID 14335636195) et 458 (ID 14340104143) ne constituent pas une preuve finale. L'artefact du head final est référencé dans la PR conformément à M10. Le succès automatisé ne remplace pas la relecture visuelle des captures ni les essais sur le PC concerné.

## Liste fermée des fichiers

1. `src/Virgil.ElevatedHelper/ElevatedCommandRunner.cs` : ReadAsync seulement.
2. `src/Virgil.ElevatedHelper/ElevatedActionDispatcher.cs` : deux affectations de Step et méthode privée de libellé.
3. `src/Virgil.Core/Interventions/RepairCheckSummary.cs` : nouvelle classe pure.
4. `src/Virgil.App/ViewModels/InterventionsViewModel.cs` : CreateProgress, propriété TechnicalDetailsText et son branchement/remise à vide dans VerifyHealthAsync ; suppression des anciens champs de diffusion.
5. `src/Virgil.App/Views/InterventionsView.xaml` : Expander et TextBox de détails techniques.
6. `tests/Virgil.Core.Tests/LotReparationProgressionWindowsTests.cs` : B1.
7. `tests/Virgil.Core.Tests/LotReparationProgressionTests.cs` : B2/B3.
8. `tests/Virgil.App.Tests/LotReparationBilanTests.cs` : B4 à B7.
9. `docs/proofs/Lot-Reparation-Progression.md` : cette preuve.

Aucun autre fichier, aucun workflow ni fichier généré versionné. Aucun amend, force-push ou rebase après le premier commit. Aucun accès système ajouté dans Core ou App. T2a et les lots parallèles restent hors périmètre.

## Liste de contrôle obligatoire

1. Statut par Id : B4 F1 tire chaque ligne du bilan de sa propre ligne VIRGIL, indépendamment du statut global PartialFailure.
2. Pas de « terminé » sous un échec : B4 F1 exige CHKDSK, PrintQueue et AudioEndpoint en « échec » ; F4 exige le bilan général en échec.
3. Pas de ligne sans contrôle : B4 F1 ignore les lignes Étape, Code sortie, pourcentage et sortie anglaise ; seules les lignes VIRGIL à quatre champs alimentent les contrôles.
4. Cas PARTIEL : F1 mêle réussite, échec et constat, avec action DISM issue du catalogue ; F2/F3/F4 couvrent terminé, annulé et échec sans contrôle.
5. Vrai service : B1 utilise le vrai runner et powershell.exe ; B2/B3 le vrai dispatcher et l'allowlist ; B4 à B7 le vrai ViewModel, B6/B7 la vraie vue.
6. Rouge puis vert dans des runs distincts : 456, puis 458 avec B3 vert, puis 459 vert ; la description de la PR donne le run vert final du head documentaire.
7. Feu vert de loops : non reçu ; la condition reste un fichier dans `/workspace/loops/feux-verts/` avec le SHA exact.
8. Sortie du brouillon après ce feu vert, avant soumission à Contre-Expert (virgil) : non effectuée ; la PR reste en brouillon.

## Vérifications Windows restantes et limites

Sur le PC concerné par le retour alpha.8, ouvrir Réparation et lancer « Vérifier ». Confirmer l'action, attendre le bilan final : un message par étape dans le fil, progression sans recul au sein de CHKDSK, statut final français avec un résultat par contrôle. Déplier « Détails techniques » et vérifier que le journal brut reste copiable ; ouvrir le rapport et retrouver ce même journal.

Recommencer avec « Scanner et réparer », confirmer séparément chaque action proposée et la nouvelle vérification après réparation ; contrôler son bilan final. Ne pas assimiler les messages des réparations seules au bilan de vérification : ils relèvent d'un éventuel T2c.

Relever le format réel de CHKDSK en français : `Total : N %` est privilégié. Une ligne avec plusieurs pourcentages sans Total est ignorée ; l'étape reste indéterminée tant qu'aucun signal sûr n'arrive. Aucun pourcentage global entre les outils n'est inventé. Les tests automatisés utilisent des données contrôlées et ne prouvent pas le format de toutes les versions Windows ni le résultat réel des réparations sur ce PC.
