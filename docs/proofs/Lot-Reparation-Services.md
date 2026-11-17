# Lot T2a — Réparation : parseur et scripts

Base vérifiée : `5ab0db9275e08e1cb9ead929be896048008ebc6f`.
Branche : `codex/lot-reparation-services`. PR : [#91](https://github.com/bassetthomas-design/virgil-2.0/pull/91), laissée en brouillon, sans fusion. T2b est hors périmètre.

## Problème, cause et changement visible

Le retour de test alpha.8 montre un ParserError avant tout contrôle des services. PowerShell interprète U+2019 comme un délimiteur simple : la chaîne du catch PrintQueue se ferme après « File d ». Les deux lignes R1 utilisent désormais une apostrophe ASCII dans des guillemets doubles. Aucun autre comportement de production ne change.

Avant : code 1, aucun contrôle PowerShell, seule la ligne DefaultAudioEndpoint ajoutée en C#. Après : les contrôles émettent leurs propres lignes ; les branches impression et audio restent UNKNOWN si leurs lectures échouent.

## Runs Windows réels

- Tests seuls : [run 444](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44054791000), SHA de branche `0cc2bab0157d03e7380753a4f2016a4968bb7e46`, checkout de la référence PR `c43e653b223426abd0b8d78d919405e137123526`. Build réussi, **0 Warning(s), 0 Error(s)**. Core : 1299 tests, 1293 verts et exactement 6 rouges ci-dessous ; App : 840 verts. Aucun test existant en échec. Ce run rouge n'est pas une preuve finale.
- Correctif R1 + E1 : [run 445](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44055137608), SHA `b0b40f196d25f09d5162f5225f46c51e3d1c9622`. Build réussi, **0 Warning(s), 0 Error(s)** ; Core 1299/1299, App 840/840, UI 14/14. Publication et installateur réussis.
- Le **run vert FINAL du head final**, son numéro, son SHA exact et l'ID de son artefact sont consignés dans la section 5 de la description de la PR #91. Ce renvoi distingue le run du correctif du run après ajout de cette preuve, conformément à M10.

Artefact du correctif : [Virgil-ui-smoke, ID 14316076200](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44055137608/artifacts/14316076200). Les tests écrivent avant les assertions, en UTF-8 sans BOM : `lot-t2a/services-echec.txt`, `lot-t2a/services-succes.txt`, `lot-t2a/services-runner.txt` (sortie standard puis sortie d'erreur).

## Exigence → tests rouges puis verts

Tous les noms ci-dessous sont ceux des deux nouvelles classes LotReparationServicesTests et LotReparationServicesWindowsTests. Chaque rouge est une assertion après démarrage et terminaison du processus lorsque le test en lance un, jamais une exception de préparation. Tous passent au run 445.

| Exigence | Test / cas exact | Message réel du run 444 : attendu puis obtenu |
| --- | --- | --- |
| G1, D-T2a-2 | `Helper_powershell_scripts_contain_no_typographic_quotes` | `Assert.Empty() Failure: Collection was not empty` ; attendu vide ; `Collection: ["Services:25:U+2019", "Services:26:U+2019"]` |
| P1-b, parseur 5.1 | `Every_helper_script_parses_with_windows_powershell_and_pwsh`, Services / powershell.exe | `Assert.Equal() Failure: Collections differ` ; `Expected: ["FIN"]` ; `Actual: ["UnexpectedToken\|26", "TerminatorExpectedAtEndOfString\|30", "MissingEndCurlyBrace\|26", "FIN"]` |
| P1-b, parseur 7 | même test, Services / pwsh.exe | `Assert.Equal() Failure: Collections differ` ; `Expected: ["FIN"]` ; `Actual: ["UnexpectedToken\|26", "TerminatorExpectedAtEndOfString\|30", "MissingEndCurlyBrace\|26", "FIN"]` |
| P1-c, échec | `Services_print_and_audio_branches_report_their_own_lines`, echec | `Assert.Equal() Failure: Values differ` ; `Expected: 0` ; `Actual: 1` |
| P1-c, succès | même test, succes | `Assert.Equal() Failure: Values differ` ; `Expected: 0` ; `Actual: 1` |
| P1-d, vrai service | `Real_runner_executes_every_service_check` | `Assert.Equal() Failure: Values differ` ; `Expected: 0` ; `Actual: 1` |

Les erreurs réelles du parseur Windows PowerShell 5.1 sont donc `UnexpectedToken|26`, `TerminatorExpectedAtEndOfString|30`, `MissingEndCurlyBrace|26`, dans cet ordre. Le parseur termine avec code 0 ; c'est bien la comparaison des lignes qui échoue.

## Tests verts dès le départ (gardes)

- G2 : `Runner_script_switch_is_a_closed_inventory` fixe les cinq bras, leur ordre et les deux littéraux du switch. Les cinq constantes sont lues directement, sans réflexion.
- Les 12 cas de P1-b hors Services : PrintQueue, RestorePoint, StartupRestorePoint, UpdateRestorePointRead, virgil-update et virgil-audio, chacun avec powershell.exe et pwsh.exe. Ils passent au run 444 et au run 445.

Aucun autre nouveau cas n'est vert au run de tests seuls. Aucun script de réparation modifiant le système n'est exécuté : les sept scripts sont analysés ; seuls Services avec fonctions factices, puis virgil-services réel en lecture seule sont exécutés. Les processus sont bornés à 60 secondes ; aucun Thread.Sleep.

## Fichiers touchés et exception E1

Liste fermée :

1. `src/Virgil.ElevatedHelper/RepairScripts.cs` : lignes 31 et 32 seulement.
2. `tests/Virgil.Core.Tests/LotReparationServicesTests.cs` : nouveau, G1/G2.
3. `tests/Virgil.Core.Tests/LotReparationServicesWindowsTests.cs` : nouveau, P1-b/c/d.
4. `tests/Virgil.App.Tests/InterfaceAccentTests.cs` : E1 seulement, lignes 49 et 50.
5. `docs/proofs/Lot-Reparation-Services.md` : cette preuve.

E1 (consigne de loops, relecture), mot pour mot :

> **E1** — tests/Virgil.App.Tests/InterfaceAccentTests.cs, l.49 et l.50 seulement, dans le commit du correctif (pas dans celui des tests seuls). Remplacer
> `          Check 'PrintQueue' ($stuck.Count -eq 0) "$($stuck.Count) tâche(s) d’impression bloquée(s)"`
> `        } catch { 'VIRGIL|PrintQueue|UNKNOWN|File d’impression indisponible' }`
> par
> `          Check 'PrintQueue' ($stuck.Count -eq 0) "$($stuck.Count) tâche(s) d'impression bloquée(s)"`
> `        } catch { "VIRGIL|PrintQueue|UNKNOWN|File d'impression indisponible" }`
> Motif : l'exemption doit rester le texte EXACT du script (sa raison, l.55, le dit) ; R1 change ces deux lignes. Rien d'autre dans ce fichier : ni la raison, ni les autres exemptions, ni les dictionnaires.

R1 et E1 figurent ensemble dans `b0b40f1`. Le test existant `Interface_labels_preserve_french_accents_in_all_source_projects` passe au run 445. Aucun autre test existant n'est modifié.

## Liste de contrôle obligatoire

1. Statut tiré de sa propre étape : P1-c compare les onze lignes exactes, P1-d vérifie les identifiants et statuts de chaque contrôle.
2. Pas de « Terminé » sous un échec : P1-c echec exige Spooler, PrintQueue et AudioEndpoint en UNKNOWN.
3. Pas de carte sans étape : sans objet, aucune carte modifiée.
4. Cas PARTIEL : P1-c echec mêle OK, INFO et UNKNOWN ; succes mêle OK, INFO et FINDING.
5. Vrai service : P1-d utilise le vrai ElevatedCommandRunner, le vrai script et les commandes de lecture ; P1-b utilise les deux vrais moteurs.
6. Rouge puis vert dans des runs distincts : 444 puis 445 ; le run final est référencé dans la PR.
7. Feu vert de loops : non reçu ; la condition est un fichier dans `/workspace/loops/feux-verts/` avec le SHA exact.
8. Sortie du brouillon après le feu vert de loops, avant soumission à Contre-Expert (virgil) : non effectuée ; la PR reste en brouillon.

## Limites et manipulations Windows restantes

Sur le PC concerné par le retour alpha.8 : ouvrir Réparation, cliquer « Vérifier », attendre la fin, ouvrir le rapport détaillé ; vérifier l'absence de ParserError et la présence des lignes PrintQueue et AudioEndpoint. Vérifier le rendu console OEM de « d'impression ». La CI ne remplace pas cette validation sur le PC concerné. Les statuts réels et la présence de Gateway dépendent du runner ; P1-d ne leur impose pas une santé fictive. La progression et le bilan relèvent de T2b et restent inchangés.
