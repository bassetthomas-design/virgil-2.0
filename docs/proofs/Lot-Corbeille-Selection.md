# Lot T3a — Corbeille : sélection (retour de test alpha.8)

## Périmètre et cause établie

Base : `0da6adb140dc5b862cda5d5fa6cb8c490de59071` (T2a, PR #91), directement après `5ab0db9275e08e1cb9ead929be896048008ebc6f`. Cette avance est autorisée par la demande T3a. Le dépôt était propre après `git fetch origin main` ; la branche `codex/lot-corbeille-selection` est issue de cette base.

La sélection globale passait par les groupes, donc par `CanGroupSelect`, qui exclut les lignes personnelles. Le filtre `EligibleBytes > 0` masquait les aperçus Corbeille vide ou illisible ; les lignes inéligibles n'exposaient pas de motif dédié.

Le correctif ajoute `IsIncludedInSelectAll` et `IneligibleReason`, sélectionne les lignes autorisées dans `SelectAll`, conserve l'aperçu réel de la Corbeille à 0 octet et affiche son motif. Le texte d'aide annonce le vidage définitif et la confirmation renforcée. `CanSelect`, `CanGroupSelect`, les décisions par défaut, les cases de catégorie/groupe et le parcours Critical sont inchangés. Aucun nouvel accès OS.

Exactement dix fichiers autorisés : quatre de production, deux nouveaux tests, les trois fichiers de tests existants couverts par E1–E3 et cette preuve. E1–E3 figurent uniquement dans le premier commit du correctif. L’exception supplémentaire E4, autorisée après le constat du run #451, reste dans le fichier de tests déjà autorisé et dans un commit correctif séparé. T2a, T3b, T3c, les workflows, les réglages, les secrets et les autres documents sont hors périmètre. Aucun amend, rebase réécrit, force-push ou fusion.

## CI Windows : rouge par assertion

Tests seuls : `9cfe1e3409a2b5ba00ce37189c042c7f7f377337`, [run #449](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44062889690), job `133969040988`, workflow `build.yml`, `windows-latest`.

Compilation réussie : `0 Warning(s)`, `0 Error(s)`. App : 849 cas, 840 réussis, **9 échecs**. Core : **1300 cas réussis**. Les seuls échecs sont les neuf cas A1–A5 ci-dessous ; aucun test existant n'a échoué. Le rouge précède le correctif et provient exclusivement des assertions prévues.

Artefact rouge de diagnostic : `Virgil-ui-smoke`, ID `14319486214`, dont `lot-t3a/lignes.txt` écrit avant l'assertion A4. Ce run rouge n'est pas une preuve finale de validation.

### Exigence → test et message réel du run rouge

Les noms ci-dessous sont préfixés par `Virgil.Tests.LotCorbeilleSelectionTests.`. La troncature `···` du message A3 est celle de xUnit ; l'attendu intégral est écrit en dur dans le test.

| Exigence | Test | Message d'assertion réel : attendu, puis obtenu |
| --- | --- | --- |
| R-A1/R-A2, défaut prudent, sélection sans effet, désélection | A1 `Select_all_checks_available_recycle_bin_only_in_preview` | `Assert.Equal() Failure: Collections differ` ; `Expected: ["RecycleBin", "UserTemporaryFiles"]` ; `Actual: ["UserTemporaryFiles"]` |
| R-A2, catégories/groupes exclus, seul SelectAll inclut la Corbeille | A2 `Category_and_group_toggles_never_check_recycle_bin` | `Assert.True() Failure` ; `Expected: True` ; `Actual: False` |
| Consentement Critical, annulation | A3 `Select_all_then_refusal_or_cancel_never_executes_recycle_bin(decision: "annuler")` | `Assert.Equal() Failure: Strings differ` ; `Expected: "Corbeille : suppression irréversible via "···` ; `Actual: "Fichiers : quarantaine uniquement."` |
| Consentement Critical, passer | A3 `Select_all_then_refusal_or_cancel_never_executes_recycle_bin(decision: "passer")` | `Assert.Equal() Failure: Strings differ` ; `Expected: "Corbeille : suppression irréversible via "···` ; `Actual: "Fichiers : quarantaine uniquement."` |
| Consentement Critical, texte vide | A3 `Select_all_then_refusal_or_cancel_never_executes_recycle_bin(decision: "vide")` | `Assert.Equal() Failure: Strings differ` ; `Expected: "Corbeille : suppression irréversible via "···` ; `Actual: "Fichiers : quarantaine uniquement."` |
| Consentement Critical, texte faux | A3 `Select_all_then_refusal_or_cancel_never_executes_recycle_bin(decision: "faux")` | `Assert.Equal() Failure: Strings differ` ; `Expected: "Corbeille : suppression irréversible via "···` ; `Actual: "Fichiers : quarantaine uniquement."` |
| Consentement Critical, texte exact | A3 `Select_all_then_refusal_or_cancel_never_executes_recycle_bin(decision: "exact")` | `Assert.Equal() Failure: Strings differ` ; `Expected: "Corbeille : suppression irréversible via "···` ; `Actual: "Fichiers : quarantaine uniquement."` |
| R-A1/R-A3, motifs par aperçu et Corbeille à 0 octet visible | A4 `Ineligible_rows_show_their_reason_and_empty_recycle_bin_stays_visible` | `Assert.Equal() Failure: Collections differ` ; `Expected: ["InternetCache", "RecycleBin", "TechnicalLogs", "UserTemporaryFiles", "WindowsOld"]` ; `Actual: ["InternetCache", "TechnicalLogs", "UserTemporaryFiles", "WindowsOld"]` |
| R-A2/R-A4, case WPF, motif et aide visibles | A5 `Rendered_recycle_row_is_checked_by_select_all_and_shows_reason_when_empty` | `Assert.True() Failure` ; `Expected: True` ; `Actual: False` |

Attendu intégral A3 :

```text
Corbeille : suppression irréversible via l'API Windows, confirmation renforcée obligatoire.
Dossier personnel : confirmation Critical obligatoire. Recopiez le texte demandé.
```

Les neuf assertions rouges précèdent toute lecture d'un membre nouveau. A4 compare d'abord les Id existants, puis vérifie `Assert.NotNull(property)` avant de lire `IneligibleReason`. L'écriture de l'artefact de diagnostic utilise une lecture optionnelle sûre avant les assertions.

## Tests verts dès le départ (gardes)

G-A1 uniquement : `Virgil.Tests.LotCorbeilleSelectionCoreTests.Recycle_selection_requires_typed_critical_consent_and_never_empties_otherwise`, réussi dès le run #449.

Vrais `PersonalFolderPolicy`, définition Corbeille du catalogue, `ConfirmationService` et `CleanupExecutionService`. L'aperçu personnel est Critical et partiellement réversible. Consentement Reinforced ou texte « oui » refusés ; plan absent ou lié au seul temporaire refusé avant tout appel disque, quarantaine, Query ou Empty. Texte Critical exact : un seul Empty factice ; rejeu refusé sans second Empty. Aucune vraie Corbeille n'est vidée.

A1–A4 utilisent le vrai ViewModel et les fixtures existantes inchangées ; A5 instancie la vraie vue WPF via `ThemeContrastTests.OnSta`. Les attendus sont écrits en dur selon le brief de loops (relecture). Les attentes ajoutées sont bornées à 10 secondes, sans `Thread.Sleep`.

## Correctif initial et constat avant E4

Le correctif initial `344b13a9f6c29b58b4854058645526f04a877bad`, [run #451](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44063574762), compile avec `0 Warning(s)`, `0 Error(s)` et fait passer les neuf cas A1–A5 ainsi que G-A1. Core : 1300 réussis ; App : 846 réussis, 2 échecs existants. Les tests UI sont sautés après l'échec de l'étape Test. Artefact de diagnostic `Virgil-ui-smoke` : ID `14318114657`.

Les deux seuls `[FAIL]` concernent `CleanupLotAReproductionTests.Every_personal_zone_stays_out_of_bulk_selection_and_rescan(id: RecycleBin, personalDefinition: True)` et le même cas avec `False`, ligne 103 : `Assert.False() Failure`, `Expected: False`, `Actual: True`. Ce test générique imposait encore l'exclusion après SelectAll, contrairement à T3a. L'arrêt M4 a été respecté ; une instruction de poursuite a ensuite autorisé sa mise en cohérence dans le périmètre existant.

Le cas `MaintenanceContractTests.RefusedPlansReturnFrenchReasonBeforeAnyDiskOrRecycleCall(reason: "apercuExpire", recycle: False)` réussit : la sortie parallèle mélange les marqueurs `Passed` et `Failed` sur la même ligne, mais la trace d'échec renvoie au test App ci-dessus, la liste `[FAIL]` n'en contient que deux et le bilan Core est 1300/1300. Aucun changement de `MaintenanceContractTests.cs`.

## CI Windows : correctif vert et référence finale

Correctif E4 : `8fde2063c0de3be9106540a3f94848282153b9fa`, [run #452](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44097318157), job `134087774222`, workflow `build.yml` sur `windows-latest` : **succès**. Compilation : `0 Warning(s)`, `0 Error(s)` ; Core : **1300/1300**, App : **848/848**, UI : **14/14**. Publication, installateur et uploads réussis.

Les deux variantes Corbeille du test générique passent, avec toutes les autres zones personnelles. Le cas `MaintenanceContractTests.RefusedPlansReturnFrenchReasonBeforeAnyDiskOrRecycleCall(reason: "apercuExpire", recycle: False)` est explicitement marqué `Passed` dans ce run, sans modification du fichier.

Artefact vert `Virgil-ui-smoke` : [ID `14333509821`](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44097318157/artifacts/14333509821), dont la preuve `lot-t3a/lignes.txt` écrite par A4 (UTF-8 sans BOM, avant assertions, après SelectAll). Les autres artefacts sont `Virgil-2.0-win-x64` ID `14334112961` et `Virgil-win-x64-setup` ID `14334203544`.

Cette preuve est ajoutée après ce run vert. La section 5 de la [PR #93](https://github.com/bassetthomas-design/virgil-2.0/pull/93) est la référence pour le run **vert FINAL** sur le head contenant cette preuve : numéro, SHA exact et ID de l'artefact `Virgil-ui-smoke`. Le run #451 reste un constat d'échec et non une validation finale.

## Exceptions E1–E3 : remplacements mot pour mot

Décision de loops (relecture), retour de test alpha.8 : seule la Corbeille disponible entre dans « Tout sélectionner ». Les autres exclusions et les contrôles pendant analyse sont conservés.

### E1 — `tests/Virgil.App.Tests/CleanupSelectAllTests.cs`

Ligne 29 de la base : remplacer

```csharp
    public async Task Select_all_excludes_personal_recycle_bin_windows_old_and_non_executable_rows()
```

par

```csharp
    public async Task Select_all_excludes_personal_windows_old_and_non_executable_rows_but_checks_available_recycle_bin()
```

Lignes 43 et 46 de la base : remplacer chacune

```csharp
        Assert.Single(rows, r => r.IsSelected);
```

par

```csharp
        Assert.Equal(new[] { "RecycleBin", "UserTemporaryFiles" }, rows.Where(r => r.IsSelected).Select(r => r.Preview.Definition.Id.ToString()).Order(StringComparer.Ordinal).ToArray());
```

Ligne 44 de la base : remplacer

```csharp
        Assert.All(rows.Where(r => !r.CanGroupSelect), r => Assert.False(r.IsSelected));
```

par

```csharp
        Assert.All(rows.Where(r => !r.CanGroupSelect && r.Preview.Definition.Id != CleanupZoneId.RecycleBin), r => Assert.False(r.IsSelected));
```

Motif : liste exacte des deux Id sélectionnés ; toutes les autres exclusions restent contrôlées.

### E2 — `tests/Virgil.App.Tests/CleanupLotAReproductionTests.cs`

Ligne 120 de la base seulement : supprimer

```csharp
    [InlineData(CleanupZoneId.RecycleBin)]
```

Motif : ce cas imposait l'exclusion après SelectAll. A1 et A2 reprennent le défaut décoché et l'absence d'effet des catégories à true et false. Le cas OldDownloads reste inchangé.

### E3 — `tests/Virgil.App.Tests/CleanupScanTransitionTests.cs`

Ligne 98 de la base seulement : remplacer

```csharp
        AssertPersonalUnchecked(vm);
```

par ces cinq lignes :

```csharp
        var recycleRows = vm.Groups.SelectMany(g => g.Rows).Where(r => r.Preview.Definition.Id == CleanupZoneId.RecycleBin).ToArray();
        Assert.NotEmpty(recycleRows);
        Assert.All(recycleRows, r => Assert.True(r.IsSelected));
        Assert.All(vm.Groups.SelectMany(g => g.Rows).Where(r => !r.CanGroupSelect && r.Preview.Definition.Id != CleanupZoneId.RecycleBin), r => Assert.False(r.IsSelected));
        vm.DeselectAllCommand.Execute(null);
```

Motif : seul le SelectAll après la fin de l'analyse inclut les Corbeilles ; DeselectAll rétablit l'état attendu par la suite du test. Les gestes pendant analyse et leurs contrôles sont inchangés.

## E4 — cohérence du test générique personnel

`tests/Virgil.App.Tests/CleanupLotAReproductionTests.cs`, méthode `Every_personal_zone_stays_out_of_bulk_selection_and_rescan`, commit `8fde2063c0de3be9106540a3f94848282153b9fa`.

Après l'assertion du défaut décoché, les contrôles de groupe suivants sont ajoutés pour tous les Id et les deux formes de marqueur personnel :

```csharp
            foreach (var group in fixture.Vm.Groups)
            {
                group.IsSelected = true;
                Assert.False(row.IsSelected);
                group.IsSelected = false;
                Assert.False(row.IsSelected);
            }
```

Immédiatement après `fixture.Vm.SelectAllCommand.Execute(null);`, ces deux lignes précèdent l'assertion existante `Assert.False(row.IsSelected);` :

```csharp
            Assert.Equal(id == CleanupZoneId.RecycleBin, row.IsSelected);
            fixture.Vm.DeselectAllCommand.Execute(null);
```

Seul l'Id Corbeille est attendu coché après SelectAll dans cette fixture éligible. Toutes les autres zones personnelles restent exclues. Les assertions existantes pour la sélection par défaut, les catégories à true/false, le retour et les deux analyses sont conservées. A1/A4 couvrent la disponibilité ; A2 vérifie qu'une catégorie ne décoche pas une Corbeille explicitement sélectionnée. Aucun test supprimé, désactivé ou renommé par E4, aucune nouvelle modification de production.

## Limites (W) et vérifications manuelles Windows

La CI avec ports factices prouve la sélection et le consentement, pas le résultat d'un vidage réel. Le vidage vérifié, les retours HRESULT, la relecture de la Corbeille et les informations complémentaires appartiennent à T3b/T3c. Aucun de ces comportements n'est livré par T3a.

Sur un profil de test, avec des fichiers sans valeur :

- Mettre deux fichiers à la Corbeille, lancer l'analyse : la ligne apparaît décochée. Cliquer « Tout sélectionner » : elle est cochée. Cliquer « Lancer » : recopier le texte est obligatoire. Annuler : vérifier que les deux fichiers sont toujours dans la Corbeille.
- Corbeille vide : lancer une analyse, déplier la catégorie Corbeille et vérifier la ligne désactivée « Corbeille vide : rien à supprimer. ».

Ces manipulations manuelles ne sont pas attestées par les tests factices.

## Liste de contrôle obligatoire

1. Statut tiré de sa propre étape : A4 vérifie les motifs de chaque aperçu, sans statut global.
2. Pas de « Terminé » sous un échec : aucun résultat d'exécution modifié ; A4 distingue estimation indisponible et Corbeille vide.
3. Pas de ligne sans aperçu : seul l'aperçu réel Corbeille est conservé à 0 octet.
4. Cas PARTIEL : A1 combine cache sélectionné par défaut, Corbeille ajoutée explicitement et Téléchargements refusés ; A4 compare une ligne sélectionnable et quatre motifs distincts.
5. Vrai service : A1–A4 vrais ViewModel/politique/catalogue/PersonalFolderPolicy, A5 vraie vue WPF ; G-A1 vrais ConfirmationService et CleanupExecutionService.
6. Rouge puis vert dans des runs distincts : références dans les sections CI de cette preuve et dans la description de la PR.
7. Feu vert de loops : non reçu ; le fichier attendu dans `/workspace/loops/feux-verts/` doit citer le SHA exact relu.
8. Sortie du brouillon après feu vert de loops, avant soumission à Contre-Expert (virgil) : non réalisée ; la PR reste en brouillon conformément à la demande T3a.
