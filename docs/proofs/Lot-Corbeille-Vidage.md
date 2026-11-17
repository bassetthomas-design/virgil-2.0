# Lot T3b — Corbeille : vidage vérifié

## Périmètre et comportement livré

Base vérifiée après `git fetch origin main` : `373118f0b77245ceba090e32d6136c0a8720685d`, fusion de T3a (#93). Le dépôt était propre. Branche : `codex/lot-corbeille-vidage` ; PR : [#95](https://github.com/bassetthomas-design/virgil-2.0/pull/95), conservée en brouillon. Neuf fichiers autorisés seulement : cinq de production, trois nouveaux tests, cette preuve. Aucun test existant modifié, aucune exception E, aucun workflow ni dictionnaire d’accents modifié. Historique linéaire, sans amend, force-push, rebase réécrit ni fusion.

Le service initial interprétait uniquement le retour de `SHEmptyRecycleBin`, perdait le HRESULT et ne relisait pas la Corbeille. Le bilan pouvait annoncer une fin normale malgré une étape Corbeille en échec.

Le P/Invoke passe de Core à `Virgil.Infrastructure.Windows.WindowsRecycleBinService`. La structure `ShQueryRbInfo` (Pack = 8), les drapeaux et la racine nulle sont conservés. L’adaptateur appelle `Query` avant et après le vidage, puis la politique pure Core `RecycleBinEmptyPolicy.Interpret` : relecture indisponible → échec explicite ; éléments restants → échec avec nombre restant, compteurs partiels et code ; zéro élément relu → réussite, quel que soit le HRESULT. L’estimation avant indisponible donne des compteurs récupérés à zéro. Le repli Core est `UnavailableRecycleBinService` ; la composition App injecte le vrai adaptateur Infrastructure.

Les contrats `IRecycleBinService`, `RecycleBinState`, `RecycleBinActionResult` et le parcours de `CleanupExecutionService` sont inchangés. La confirmation renforcée, les refus de plan avant tout effet, le compte `asInvoker` et l’absence de Corbeille dans le helper restent protégés. Le bilan affiche les erreurs propres à l’étape Corbeille ; la mention « Corbeille vidée et vérifiée » exige une étape Corbeille `Completed`.

## Rouge initial : tests seuls

Le premier commit `decfeead2bafc90e79ac4f4dca6332c6547683f1`, [run #460](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44112693674), échoue à la compilation des nouveaux tests (import Contracts et analyseur xUnit2029). Ce run ne compte pas comme preuve rouge. Un nouveau commit de tests seuls corrige uniquement ces erreurs.

Tests seuls compilables : `fe1277aa09b7a78b54bc3ba60e42caaed1b0dde3`, [run Windows #461](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44113192751), job `134144051285`, `build.yml`, `windows-latest`. Compilation : **0 Warning(s), 0 Error(s)**. Exactement **12 échecs par assertion prévus** : Core 9 échecs sur 1311 ; App 3 échecs sur 851. Aucun test existant en échec.

Artefact de diagnostic rouge `Virgil-ui-smoke` : [14342116704](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44113192751/artifacts/14342116704). B2 écrit `lot-t3b/corbeille-runner.txt` avant les assertions et dans un `finally`, UTF-8 sans BOM. Les runs rouges ne sont jamais une validation finale.

### Exigence → test et assertion réelle

| Exigence | Test | Message du run rouge : attendu puis obtenu |
| --- | --- | --- |
| R-B1, état relu plutôt que code seul | B1 `Empty_result_comes_from_the_state_read_back_after_the_call` : `videe`, `sfalse`, `dejavide`, `incomplet`, `refus`, `inverifiable`, `avantillisible` | Pour chacun des sept cas : `Assert.NotNull() Failure: Value is null` sur le type public ; attendu non nul, obtenu null. |
| R-B2, séparation métier/OS | G-B1 `Core_recycle_bin_file_has_no_native_call` | `Assert.Empty() Failure: Collection was not empty` ; attendu aucune ligne ; obtenu deux lignes `[DllImport("shell32.dll", CharSet = CharSet.Unicode)]`, avec quatre espaces en tête. xUnit tronque leur affichage après `CharSet.`. |
| Cas réel déjà vide, puis fichier de test | B2 `Real_recycle_bin_empties_only_the_deposited_test_file_and_reads_back_zero` | `Assert.Equal() Failure: Values differ` ; attendu `RecycleBinActionResult { Success = True, ItemCount = 0, FreedBytes = 0, ReadableError =  }` ; obtenu `RecycleBinActionResult { Success = False, ItemCount = 0, FreedBytes = 0, ReadableError = Windows n'a pas pu vider la corbeille. }`, à l’étape 2. |
| R-B4, annonce vérifiée | B3 `Verified_empty_is_announced_as_verified` | `Assert.Contains() Failure: Item not found in collection` ; attendu `Nettoyage terminé.\n\nCorbeille vidée et vérifiée : 1 Ko libérés.\nRapport disponible.` ; obtenu dans la collection `Nettoyage terminé.\n\nCorbeille : 1 Ko libérés.\nRapport disponible.` (textes tronqués par xUnit). |
| R-B4, échec incomplet | B4 `Failed_or_unverified_empty_is_never_announced_as_done`, premier cas | `Assert.Equal() Failure: Strings differ` ; attendu `Nettoyage terminé avec erreurs.` ; obtenu `Nettoyage terminé.`. |
| R-B4, résultat invérifiable | B4, second cas | Même assertion : attendu `Nettoyage terminé avec erreurs.` ; obtenu `Nettoyage terminé.`. |

D-T3b-3 est la dérogation nommée à M12 : B1 cherche le nouveau type pur par réflexion et vérifie sa présence par `Assert.NotNull` avant la méthode puis l’invocation. G-B1, B2 et B3/B4 portent les rouges de comportement sur les chemins réels.

## Tests verts dès le départ (gardes)

**G-B2 uniquement** : `Recycle_bin_runs_with_the_session_account_only`, réussi au run #461. Le manifeste contient exactement la ligne `        <requestedExecutionLevel level="asInvoker" uiAccess="false" />` ; les sources du helper ne contiennent aucun accès à la Corbeille. Les autres tests du lot sont tous rouges au run initial.

## Correctif initial et décision de relecture

Correctif initial `3988679c5d9413ada777fa89e9809b91e7594187`, [run #463](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44114113724), job `134147337896` : compilation **0 Warning(s), 0 Error(s)** ; Core **1311/1311**, App **850/851**. Tous les tests T3b passent, y compris B2. Le seul échec existant est `InterfaceAccentTests.Interface_labels_preserve_french_accents_in_all_source_projects` : l’ancien participe masculin introduit par le libellé d’échec alimente le dictionnaire automatique, qui rejette sept usages corrects de « vide ». Les tests UI sont sautés. Artefact de diagnostic : `14341962859`. Arrêt M4 respecté, sans modification du test ni de `Accents/*.txt`.

Décision de loops (relecture), transmise par virgil dans cette tâche : conserver le garde et ses dictionnaires ; remplacer le libellé de production et les attendus des nouveaux tests par exactement :

```text
Vidage incomplet de la corbeille : {after.ItemCount} élément(s) restant(s) (code 0x{code}).
```

Les cas `incomplet` et `refus` attendent respectivement 1 élément avec `0x00000000` et 3 éléments avec `0x80004005`. La forme féminine « vidée » reste utilisée pour les annonces vérifiées. L’ancien participe masculin n’est pas recopié dans cette preuve, conformément à la décision de relecture ; ses valeurs exactes restent consultables dans les logs historiques liés.

### M3 : nouveau rouge de tests seuls

Commit `aff11756bea4022985f2d336c0c001fb1fb2c19d`, [run Windows #465](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44121919494), job `134175283170` : **0 Warning(s), 0 Error(s)**. Les deux cas B1 `incomplet` et `refus` échouent par `Assert.Equal() Failure: Values differ`, sur `ReadableError` uniquement :

- `incomplet` : attendu `RecycleBinActionResult { Success = False, ItemCount = 2, FreedBytes = 924, ReadableError = Vidage incomplet de la corbeille : 1 élément(s) restant(s) (code 0x00000000). }` ; obtenu les mêmes compteurs et l’ancien libellé de production (logs à 16:08:28 UTC).
- `refus` : attendu `RecycleBinActionResult { Success = False, ItemCount = 0, FreedBytes = 0, ReadableError = Vidage incomplet de la corbeille : 3 élément(s) restant(s) (code 0x80004005). }` ; obtenu les mêmes compteurs et l’ancien libellé de production.

Core : 1317 réussis, 2 échecs ; App : 857 réussis, seul échec d’accents déjà identifié. Les autres tests T3b restent verts après le correctif initial ; en particulier B4 transmet le message factice et vérifie toujours sa restitution exacte. Ce run prouve la correction demandée en relecture, distinctement du rouge initial #461.

Le runner teste la référence de fusion de PR ; les totaux ci-dessus sont ceux publiés par chaque run, sans les extrapoler d’un run à l’autre. Aucun fichier de T2b n’est ajouté à la branche T3b.

## CI Windows : correctif vert et référence finale

Correctif de relecture dans un commit distinct : `521180478b3b502fd131dc95060fab3e12ec8ca4`, [run #466](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44122541932), job `134177504012`, workflow `build.yml` sur `windows-latest` : **succès**. Compilation : **0 Warning(s), 0 Error(s)** ; Core **1319/1319**, App **858/858**, UI **14/14**. Les deux assertions B1 de relecture, tous les autres tests T3b, B2 réel et `Interface_labels_preserve_french_accents_in_all_source_projects` passent. Publication, installateur et uploads réussis.

Artefact vert `Virgil-ui-smoke` : [ID 14346177564](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44122541932/artifacts/14346177564), incluant la preuve B2 `lot-t3b/corbeille-runner.txt` écrite avant les assertions. Ce run distinct valide le correctif après le rouge de tests seuls #465.

Cette preuve est ajoutée après le run vert du correctif. La section 5 de la [PR #95](https://github.com/bassetthomas-design/virgil-2.0/pull/95) est la référence pour le run **vert FINAL** sur le head contenant ce document : numéro, SHA exact et ID de l’artefact `Virgil-ui-smoke`. Les runs #460, #461, #463 et #465 restent des constats historiques et non la référence finale. Le feu vert du relecteur sur le head final reste distinct du résultat CI.

## Limites de mesure et vérifications Windows

**HRESULT brut de la Corbeille déjà vide : non mesuré.** L’ancien service perd cette valeur et le protocole B2 prescrit n’expose que `RecycleBinActionResult`. Le résultat rouge établit un code non nul, sans permettre de conclure à une valeur précise. Le code `0x8000FFFF` est une entrée déterministe de B1, pas une mesure du runner. Aucun diagnostic natif supplémentaire n’a été autorisé ou ajouté ; la demande P6 de cette mesure reste non satisfaite et signalée dans la PR.

B2 utilise le vrai `new CleanupDisk().RecycleBin` et `Task.Run` pour chaque `Empty`, sans forcer STA ; il trace l’ApartmentState du fil appelant. Il vérifie d’abord estimation disponible et zéro élément, puis le cas déjà vide. Il dépose ensuite son fichier unique de cinq octets `VIRGL` via `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(..., SendToRecycleBin)`, vérifie un seul élément, vide et relit zéro. Les attentes ajoutées sont bornées à dix secondes, sans attente conditionnelle ni `Thread.Sleep`.

**Sur un poste de développeur, B2 échoue à l’étape 1 si la Corbeille contient déjà des éléments ; il ne la vide pas.** Si elle est vide, le test ne dépose et ne supprime que son propre fichier. Aucun autre test du lot ne vide une vraie Corbeille.

Manipulations manuelles restantes, sur un profil de test avec fichiers sans valeur :

- Mettre deux fichiers à la Corbeille, cocher sa ligne, recopier la confirmation renforcée : vérifier « Corbeille vidée et vérifiée : … libérés. » et l’état vide dans l’Explorateur.
- Répéter via « Tout sélectionner », avec une zone de cache dans la même sélection.
- Annuler à la confirmation : vérifier que les fichiers restent dans la Corbeille.
- Si Windows le permet, garder un élément verrouillé, lancer le vidage : vérifier le bilan avec erreurs, relever le nombre restant et le code.
- Mesurer séparément le HRESULT déjà vide sur le poste et le runner si un protocole de diagnostic est validé en relecture.

Ces essais manuels ne sont pas attestés par les tests. Les faux App réutilisent les fixtures existantes sans modification ; la politique Core couvre les cas incomplet et invérifiable sans effet OS.

## Liste de contrôle obligatoire

1. Statut par étape : B3/B4 vérifient le résultat Corbeille propre ; aucun succès déduit du statut global de session.
2. Pas de fin normale sous un échec : B4 incomplet et invérifiable exigent « Nettoyage terminé avec erreurs. » et le motif exact.
3. Pas de ligne sans étape : la mention « Corbeille vidée et vérifiée » exige un résultat Corbeille `Completed` ; B3 vérifie l’annonce et la ligne du rapport.
4. Cas PARTIEL : B1 incomplet exige deux suppressions, 924 octets récupérés et un élément restant ; B2 couvre déjà vide puis un fichier déposé exprès.
5. Vrai service : B2 utilise Infrastructure et shell32, la vraie Corbeille du runner et son fichier ; G-B2 protège le compte de session.
6. Rouge puis vert dans des runs distincts : rouge initial #461, rouge de relecture #465 ; les références vertes et la référence FINAL figurent dans la section CI et la PR.
7. Feu vert de loops (relecture) sur le SHA final : non reçu. La décision de libellé ne vaut pas validation finale du lot.
8. Sortie du brouillon : non réalisée ; la PR reste en brouillon, sans fusion, conformément à la demande.
