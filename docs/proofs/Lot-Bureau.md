# Lot Bureau — retours alpha.8

Consigne et complément de loops (relecture), appliqués sur `codex/lot-bureau`, depuis la base exacte `8ef3986d8f1889b72211527183b9b464ba58a75f`. Espace initial propre, `git fetch origin main` effectué avant création de la branche. Une seule [PR, #90](https://github.com/bassetthomas-design/virgil-2.0/pull/90), maintenue en brouillon, sans fusion ni force-push.

## Constats vérifiés C1–C5

Les lignes non modifiées ci-dessous correspondent aussi à la base alpha.8. Pour le classifieur, distinguer explicitement les lignes avant/après.

| Constat | Preuve dans le code | Conclusion et périmètre |
| --- | --- | --- |
| C1 — relance explicite | `src/Virgil.Core/Interventions/InterventionExecutionService.cs:37` et `:43` : décision RestartExplorer, puis consommation du plan avant l'adaptateur (`:48`). `src/Virgil.Core/Resources/ProcessActionService.cs:56`, `:100`, `:188` : même protection côté Ressources. `src/Virgil.App/ViewModels/ResourcesViewModel.cs:164` et `src/Virgil.App/Views/ResourcesView.xaml:64` : bouton confirmé. `src/Virgil.App/ViewModels/InterventionsViewModel.cs:137`, `:193` et `src/Virgil.Core/Interventions/InterventionSafety.cs:13` : Scanner et réparer utilise les constats, sans proposition RestartExplorer. `src/Virgil.ElevatedHelper/ElevatedRequestValidator.cs:145` : le helper la refuse. | Deux consommateurs métier de IExplorerRestarter, aucun appelant implicite trouvé. Composition DI dans `src/Virgil.App/Hosting/ServiceCollectionExtensions.cs:75`. Le test existant `tests/Virgil.Core.Tests/ConfirmedPlanModuleArchitectureTests.cs:37` borne ces consommateurs. Aucun changement de production pour R3. |
| C2 — cache d'icônes éligible avant correctif | `src/Virgil.Core/Cleanup/CleanupZoneCatalog.cs:18` : racine Explorer, `.db`, âge 168 h. `src/Virgil.Core/Cleanup/CleanupSelectionPolicy.cs:16` : zone cochée par défaut. `src/Virgil.Core/Cleanup/CleanupPreviewService.cs:197` : parcours récursif. À la base, `src/Virgil.Core/Cleanup/CleanupSafetyClassifier.cs:63` ne filtre ni iconcache ni thumbcache. `src/Virgil.Core/Cleanup/CleanupExecutionService.cs:278` et `:296` : garde puis quarantaine. | Cause technique établie : un iconcache ancien pouvait être mis en quarantaine, également reproduit sur le vrai disque Windows par B3 au rouge. C'est une cause plausible de changement d'icônes ; l'effet visuel et la cause du détachement des raccourcis ne sont pas établis. R1/R2 corrigent cette éligibilité. |
| C3 — Bureau et épinglages | Recherche initiale dans `src/` : aucune occurrence de Taskband, User Pinned, Quick Launch, IconStreams, ie4uinit, SHChangeNotify. `src/Virgil.Core/Applications/ApplicationRemnantScanner.cs:67` lit le Bureau ; `src/Virgil.Core/Applications/ApplicationRemnantClassifier.cs:91` le protège comme racine personnelle, via la classification protégée `:38`. `src/Virgil.Core/Cleanup/CleanupStorageAnalyzer.cs:176` cite le Bureau, mais `src/Virgil.App/Hosting/ServiceCollectionExtensions.cs:175` injecte des racines vides. | Aucune écriture directe identifiée vers le Bureau, TaskBar ou Taskband. Après correctif, seule la garde R2 ajoute le marqueur Quick Launch. T6 borne cette exception à sa ligne de chemin. Cette recherche ne prouve pas l'absence d'effets d'un programme tiers. |
| C4 — mises à jour | `src/Virgil.Core/Updates/WingetUpdateExecutionService.cs:82` lance WinGet ; les arguments d'installation sont construits à `:172`. | Un installeur tiers peut recréer ou supprimer des raccourcis. Parcours manuel restant, sans changement dans ce lot. |
| C5 — comportement réel d'Explorer | `src/Virgil.Core/Interventions/ExplorerRestarter.cs:31`, `:38`, `:93` : CloseMainWindow, puis lancement d'explorer.exe. | Sans fenêtre principale, la fermeture peut être sans effet et le lancement ouvrir une fenêtre. Vérification Windows restante ; fichier strictement inchangé. |

## Changements livrés et limites

`CleanupSafetyClassifier.cs:38`, `:87`, `:96`, `:115` :

- R1 : la zone Miniatures Windows accepte uniquement les `thumbcache_*.db` directement à sa racine, sous réserve de toutes les protections et de l'âge existants. Les autres `.db` à la racine restent visibles comme inéligibles, avec le motif exact : « Cache d'icônes Windows protégé : le supprimer modifie l'affichage du bureau et de la barre des tâches. » Les fichiers de sous-dossiers portent « Seules les bases de miniatures thumbcache_*.db directement à la racine sont autorisées. »
- R2 : refus de `iconcache*.db`, insensible à la casse, dans toutes les zones exécutables. Refus des chemins ayant les segments `Desktop`, `Microsoft\Internet Explorer\Quick Launch` ou `Microsoft\Windows\Shell`, avec le motif « Bureau, raccourcis épinglés et données du shell Windows protégés. » Les limites de segments préservent notamment Desktop-copy et Shell-copy.
- Les nouveaux contrôles utilisent uniquement le chemin déjà normalisé, sans nouvel appel OS dans Core. La comparaison par segments ne résout pas un Bureau redirigé vers un dossier de nom arbitraire ; aucune nouvelle résolution Known Folders n'est revendiquée.
- R3/C5 : aucune modification de relance, de confirmation, du helper ou de politique de sélection. R4 : seul autre changement de production, la description de Miniatures précise « bases de miniatures (thumbcache) ». Son libellé, sa racine, son âge et sa sélection par défaut sont conservés.
- L'exécution réutilise la même garde avant toute lecture du faux disque ou quarantaine ; la quarantaine réelle la vérifie aussi (`src/Virgil.Infrastructure/Windows/QuarantineStore.cs:49`). L'exécuteur compte les fichiers ignorés sans exposer leur motif dans `CleanupStepResult` (`CleanupExecutionService.cs:397`). B1 vérifie donc le refus sans effet à l'exécution, puis le motif exact de la même garde par `Assert.Equal`. Aucun changement hors périmètre pour exposer un nouveau message d'exécution.

## Historique de preuve Windows

| Étape | SHA de branche | Run Windows | Résultat |
| --- | --- | --- | --- |
| Premier commit, tests seuls | `2749a8246b28e24400539588ec9a213281f255a7` | [439](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44046977439) | Build : 0 avertissement, 0 erreur. T1 aperçu/T2 et B3 échouent par assertion ; trois scénarios d'exécution échouent prématurément à cause du faux RecycleBin. T5 révèle une fixture sans sélection des réparations sensibles. Ce run n'est pas retenu comme preuve complète. |
| Fixtures corrigées, toujours tests seuls | `0e2c09ce6d79da61e418b2f5cb66d9da1bc9929b` | [440 — rouge de référence](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44047704350) | Build : 0 avertissement, 0 erreur, analyseurs xUnit compris. Core : 1 264 réussites, **16 échecs par assertion** sur 1 280 ; App : 840/840. T3–T6 passent avant tout correctif. Aucun test de la base ne casse. |
| Correctif séparé, tests identiques au rouge de référence | `c771e9eef10a65cdb65f9d2a9feab7c2fccc2a4c` | [441 — vert du correctif](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44048179853) | Build : **0 avertissement, 0 erreur** ; Core **1 280/1 280**, App **840/840**, smoke UI **14/14**. Publication applicative et construction de l'installateur réussies. |

La CI utilise le merge ref de la PR, sans fusionner la branche : `19da38b` au rouge de référence et `1de1e3d` au vert du correctif, chacun sur main `8ef3986`. Les SHA de branche ci-dessus identifient les changements testés. Après ajout de ce document, le **run vert FINAL du dernier commit et son SHA complet sont consignés dans la description de la [PR #90](https://github.com/bassetthomas-design/virgil-2.0/pull/90)** ; aucune validation d'un commit ultérieur n'est déduite du run 441.

SDK .NET absent du poste Linux de cette tâche : aucun build local revendiqué. Les résultats ci-dessus proviennent exclusivement du workflow Windows existant `.github/workflows/build.yml`, inchangé.

### T1–T6 et complément B1–B3

**31 cas ajoutés**, répartis dans quatre nouveaux fichiers de tests, tous réussis au run 441 :

| Exigence | Tests, fichier et preuve |
| --- | --- |
| T1/B2 | `LotBureauCleanupTests.T1_Preview_keeps_only_root_thumbnails_eligible_and_explains_exclusions` : vrai dossier temporaire, six fichiers anciens ; `Assert.Equal` en dur sur 6 vus, 2 éligibles, **10 octets**, 4 exclus, noms et motifs exacts. Deux thumbcache de racine restent éligibles. `T1_Confirmed_execution_never_touches_exclusions_even_if_preview_is_forged` : deux cas, aperçu normal puis éligibilité forgée ; seules deux lectures et deux quarantaines, aucun appel pour les exclus. Au rouge, 6 quarantaines au lieu de 2. |
| T2 | `LotBureauCleanupTests.T2_Shell_files_are_refused_in_every_executable_zone` : 11 chemins, vérifiés dans chaque définition exécutable avec racine temporaire et extension `.db` autorisée ; motifs exacts, casse mixte, Desktop/public, Quick Launch dont User Pinned/TaskBar, Shell et sous-dossiers. `T2_Segment_boundaries_preserve_unrelated_technical_files` : 5 contre-exemples qui restent nettoyables. |
| T3 | `LotBureauExplorerTests.T3_All_known_findings_never_propose_explorer_restart` : toutes les clés FINDING demandées et les formulations SFC/DISM ; ensemble exact de cinq réparations, sans RestartExplorer. |
| T4 | `LotBureauExplorerTests.T4_Intervention_restart_refuses_invalid_plan_before_every_adapter` et `T4_Resource_restart_refuses_invalid_plan_before_every_adapter` : trois cas chacun, plan absent, expiré, autre action ; zéro appel à Explorer/helper/runtime. Complètent les tests existants `ResourceUpdateConfirmedPlanTests.ResourceRefusalHasNoRuntimeEffect`, `ResourcesConsumeOnceImmediatelyBeforeEffect` et `InterventionsModuleTests.Execution_service_restarts_explorer_locally_without_helper`. |
| T5 | `LotBureauViewModelTests.T5_Verify_then_scan_repair_never_requests_or_executes_explorer_restart` : vrai ViewModel et vrai service d'exécution, faux helper/diagnostics/historique/Explorer ; Vérifier, puis Scanner et réparer, avec tous les constats. La fixture coche explicitement les réparations rendues, sensibles dans le vrai catalogue, puis confirme chaque décision. Réparations, points de restauration et vérification finale effectivement parcourus ; zéro appel Explorer, aucun titre ni plan RestartExplorer demandé. |
| T6 | `LotBureauCleanupTests.T6_Source_has_no_shell_mutation_markers_outside_the_path_guard` et `T6_Default_catalog_does_not_target_desktop_or_pinned_shortcuts`. Le garde source ne tolère Quick Launch que sur la ligne littérale de R2, pas sur tout le fichier. Le test d'architecture existant `ConfirmedPlanModuleArchitectureTests.ExplorerAdapterHasOnlyTwoGuardedConsumers` continue de passer. |
| B1 | `LotBureauCleanupTests.B1_Icon_cache_injected_after_preview_is_refused_at_execution_with_exact_reason` : fichier iconcache créé après l'aperçu, injecté comme éligible dans un nouvel aperçu confirmé ; fichier intact, zéro lecture disque métier, zéro quarantaine, 1 ignoré, motif exact de la garde. Au rouge : 1 quarantaine au lieu de 0. |
| B3 | `LotBureauWindowsTests.B3_Real_thumbnail_scan_and_cleanup_preserve_desktops_pins_taskband_and_icons`, `[Trait("Category", "Windows")]` : vrai dossier `%LOCALAPPDATA%\Microsoft\Windows\Explorer`, vrais services d'aperçu/exécution/disque/quarantaine, plan confirmé authentique. Deux fichiers techniques uniques, thumbcache et iconcache, sont vieillis pour exercer le nettoyage. Le thumbcache est réellement déplacé ; l'iconcache reste en place. Relevés récursifs des deux Bureaux et des épinglages (noms, tailles, dates, SHA-256 des fichiers), valeurs/types/sous-clés Taskband en lecture seule et relevés des iconcache comparés par `Assert.Equal`. Une absence de racine/clé est enregistrée explicitement. |

B3 dépose `avant.txt`, `apres.txt`, `comparaison.txt`, `apercu.json`, `execution.json` dans `artifacts/ui-smoke/lot-bureau/`, repris par l'upload existant. Comparaison vide et instantanés identiques exigés au vert. Au rouge, l'iconcache de fixture disparaît. Les deux fichiers uniques sont retirés en fin de test ; la quarantaine de test est isolée sous TEMP et conservée jusqu'à la fin du runner. Aucune écriture dans les Bureaux, les épinglages ou le registre par le test.

- [Artefact du rouge de référence](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44047704350/artifacts/14311808133), sous-dossier `lot-bureau/`.
- [Artefact du vert du correctif](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44048179853/artifacts/14313175628), même sous-dossier. SHA-256 de l'archive annoncé par GitHub : `37ce96f02beaf2bb0c8a124fe0b90b0048dcf28d42d317091e14ee869426f5db`.

Cette preuve CI ne remplace pas un essai visuel sur un profil représentatif, ni un essai d'installeur tiers. Les profils de runner peuvent avoir une clé Taskband ou un dossier absent ; cela figure dans les relevés.

## Protocole Windows manuel restant — avant/après chaque parcours

Sur une machine et un **profil de test**, installer l'artefact du SHA testé indiqué dans la PR. Préparer quelques raccourcis sur les deux Bureaux et des applications épinglées. Ne pas réorganiser le Bureau, épingler/détacher une application ou lancer d'autre installation pendant une mesure. Exécuter PowerShell dans la session courante, sous le même compte que Virgil ; conserver toutes les erreurs, y compris « clé absente » ou « accès refusé ».

Pour chacun des huit parcours ci-dessous, prendre un nouveau relevé **avant**, effectuer uniquement les clics indiqués et attendre la fin réelle du parcours, puis prendre le relevé **apres**. Ne pas réutiliser l'état avant d'un autre parcours.

| Nom de dossier | Clics et point de mesure |
| --- | --- |
| `scan` | Scan → lancer le scan, relever le mode exact (rapide/approfondi), attendre le rapport sans exécuter de proposition. Faire deux essais séparés pour les deux modes. |
| `nettoyage-defaut` | Nettoyage → analyser, conserver les sélections par défaut, ouvrir le récapitulatif, confirmer le nettoyage. Vérifier les exclusions de Miniatures et attendre le résultat. |
| `nettoyage-tout` | Nettoyage → analyser → Tout sélectionner, noter les lignes réellement retenues et le niveau de confirmation, confirmer, attendre le résultat. Ne pas ajouter de ligne personnelle séparément. |
| `verifier` | Réparation → Vérifier → EXÉCUTER, accepter l'élévation annoncée, attendre le rapport ; aucune réparation supplémentaire. |
| `scanner-reparer` | Réparation → Scanner et réparer → confirmer chaque décision voulue ; noter aussi les actions passées. Si aucune réparation n'est proposée ou sélectionnée, le consigner sans inventer une exécution. Attendre la vérification finale lorsqu'elle est proposée. |
| `winget` | Mises à jour → vérifier les mises à jour → sélectionner une application WinGet de test → confirmer son installation ; noter identifiant, versions et installeur. Attendre également la fin de l'installeur tiers, puis relever. Refaire avec chaque installeur suspect, séparément. |
| `explorer-ressources` | Ressources → Relancer Explorer → lire puis confirmer la décision. Mesurer les PID/heures de départ et l'effet visible après la fin de l'action. |
| `explorer-reparation` | Réparation → analyser les interventions → sélectionner uniquement Relancer l'Explorateur Windows → parcours guidé → EXÉCUTER. Mesurer comme pour Ressources. |

Dans PowerShell, définir la fonction suivante une fois. Elle reprend les commandes de relevé demandées, en conservant des noms avant/apres distincts :

```powershell
$preuve = Join-Path $env:USERPROFILE 'Virgil-Lot-Bureau-preuves'
New-Item -ItemType Directory -Force $preuve | Out-Null

function Save-BureauState([string]$parcours, [ValidateSet('avant','apres')][string]$phase) {
    $dossier = Join-Path (Join-Path $preuve $parcours) $phase
    New-Item -ItemType Directory -Force $dossier | Out-Null
    Get-Date -Format o | Out-File (Join-Path $dossier 'heure.txt')
    Start-Transcript -Path (Join-Path $dossier 'releve.txt') -Force
    Push-Location $dossier
    try {
        Get-ChildItem "$env:APPDATA\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar" |
            Select Name,Length,LastWriteTime |
            Export-Csv 'epingles.csv' -NoTypeInformation -Encoding UTF8
        reg export "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband" "taskband-$phase.reg" /y
        Get-ChildItem "$env:LOCALAPPDATA\Microsoft\Windows\Explorer" -Filter *.db |
            Select Name,Length,LastWriteTime |
            Export-Csv 'caches.csv' -NoTypeInformation -Encoding UTF8
        Get-ChildItem ([Environment]::GetFolderPath('Desktop')), "$env:PUBLIC\Desktop" |
            Select FullName,LastWriteTime |
            Export-Csv 'bureaux.csv' -NoTypeInformation -Encoding UTF8
        reg export "HKCU\Software\Microsoft\Windows\Shell\Bags\1\Desktop" "bureau-$phase.reg" /y
        Get-ChildItem "$env:LOCALAPPDATA\Virgil\Quarantine" -Recurse -Force |
            Select FullName,Length,LastWriteTime |
            Export-Csv 'quarantaine.csv' -NoTypeInformation -Encoding UTF8
        $dernierJournal = Get-ChildItem "$env:LOCALAPPDATA\Virgil\logs\virgil-*.log" |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($dernierJournal) { Copy-Item $dernierJournal.FullName 'dernier-virgil.log' }
        Get-Process explorer | Select Id,StartTime |
            Export-Csv 'explorer.csv' -NoTypeInformation -Encoding UTF8
    }
    finally {
        Pop-Location
        Stop-Transcript
    }
}

# Exemple : prendre le relevé, effectuer le parcours, puis reprendre le relevé.
Save-BureauState 'nettoyage-defaut' 'avant'
# Effectuer ici uniquement les clics de ce parcours, jusqu'à son résultat final.
Save-BureauState 'nettoyage-defaut' 'apres'
```

Les exports exécutés sont notamment `reg export "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband" taskband-avant.reg /y` et `reg export "HKCU\Software\Microsoft\Windows\Shell\Bags\1\Desktop" bureau-avant.reg /y`, puis leurs variantes `-apres.reg` dans l'autre dossier. Une clé absente n'est pas un export réussi : conserver la sortie d'erreur et noter l'absence dans le compte rendu.

**Avant et après chaque parcours**, prendre aussi une capture d'écran du Bureau et de la barre des tâches, enregistrée dans le dossier de phase sous `bureau-et-barre-avant.png` / `bureau-et-barre-apres.png`. Garder les mêmes écran, résolution et échelle ; relever la disposition et les icônes vides/modifiées, pas seulement les noms de raccourcis. Exporter le rapport Virgil du parcours et noter version, SHA, heure avec fuseau et chaque clic, y compris confirmation/élévation/passage/annulation.

Comparer les relevés (exemple, à répéter pour chaque nom de dossier) :

```powershell
$essai = Join-Path $preuve 'nettoyage-defaut'
$avant = Join-Path $essai 'avant'
$apres = Join-Path $essai 'apres'
Compare-Object (Import-Csv "$avant\epingles.csv") (Import-Csv "$apres\epingles.csv") -Property Name,Length,LastWriteTime
Compare-Object (Import-Csv "$avant\caches.csv") (Import-Csv "$apres\caches.csv") -Property Name,Length,LastWriteTime
Compare-Object (Import-Csv "$avant\bureaux.csv") (Import-Csv "$apres\bureaux.csv") -Property FullName,LastWriteTime
Compare-Object (Import-Csv "$avant\quarantaine.csv") (Import-Csv "$apres\quarantaine.csv") -Property FullName,Length,LastWriteTime
Compare-Object (Import-Csv "$avant\explorer.csv") (Import-Csv "$apres\explorer.csv") -Property Id,StartTime
fc.exe "$avant\taskband-avant.reg" "$apres\taskband-apres.reg"
fc.exe "$avant\bureau-avant.reg" "$apres\bureau-apres.reg"
```

Comparer également les captures et les journaux. Noter le **clic exact** qui précède chaque écart et le résultat de chaque commande. Attendu pour le nettoyage : seuls les thumbcache admissibles partent en quarantaine ; iconcache, Bureaux, épinglages et Taskband restent intacts. Les autres fichiers techniques des autres zones peuvent suivre leur politique existante. Pour une relance explicite, distinguer un vrai changement du processus shell, une simple nouvelle fenêtre et une absence de fermeture. Pour WinGet, attribuer l'écart au parcours observé, sans conclure automatiquement à un effet direct de Virgil.

## Traces à recueillir lors d'un prochain incident

Conserver les mêmes relevés avant/après, les captures, les exports Taskband et disposition du Bureau, le contenu et les manifestes de `%LOCALAPPDATA%\Virgil\Quarantine`, le dernier `%LOCALAPPDATA%\Virgil\logs\virgil-*.log`, les PID/heures de départ d'Explorer, le **rapport Virgil exporté** et l'heure précise avec fuseau. Ajouter la version/SHA, le parcours, les clics exacts, le nom des raccourcis concernés et, pour une mise à jour, l'identifiant WinGet, les versions et l'installeur. Si aucun relevé avant n'existe, le signaler ; ne pas reconstruire une preuve avant supposée.

Restent non effectués dans cette tâche : les huit parcours manuels sur profil représentatif, l'effet visuel réel sur les icônes, la disposition du Bureau, C4 (installeurs WinGet), C5 (relance réelle du shell), et le cas d'un Bureau redirigé vers un chemin sans segment Desktop. L'absence d'écriture directe trouvée et les succès automatisés ne tranchent pas seuls l'origine du détachement des raccourcis.

## Liste fermée et décompte final

**7 fichiers modifiés/ajoutés**, soit les six chemins autorisés initialement plus l'unique nouveau test Windows permis par B4 :

1. `src/Virgil.Core/Cleanup/CleanupZoneCatalog.cs`
2. `src/Virgil.Core/Cleanup/CleanupSafetyClassifier.cs`
3. `tests/Virgil.Core.Tests/LotBureauCleanupTests.cs`
4. `tests/Virgil.Core.Tests/LotBureauExplorerTests.cs`
5. `tests/Virgil.App.Tests/LotBureauViewModelTests.cs`
6. `docs/proofs/Lot-Bureau.md`
7. `tests/Virgil.Core.Tests/LotBureauWindowsTests.cs`

Aucun test de la base, workflow, réglage, secret ni fichier généré ajouté au diff. Aucun reformatage global ni réécriture des fins de ligne. Les changements de fixtures précèdent le correctif dans des commits de tests seuls ; les tests du rouge de référence sont identiques à ceux du vert du correctif.
