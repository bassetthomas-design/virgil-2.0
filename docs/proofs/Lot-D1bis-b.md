# Lot D1-bis-b — Lanceurs de jeux

Preuve du 10 novembre 2026. Cadrage et relecture : loops (relecture).
Après relecture de `63bbc1d`, les corrections sont vérifiées au head `7b5591d2efd228fb2fa8587757f5d4403207e87e` par le run Windows vert [42793391357](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42793391357) : Core 1206/1206, App 678/678, UI 14/14, 0 avertissement et 0 erreur. Les preuves antérieures ci-dessous restent historiques.
Dépôt : `bassetthomas-design/virgil-2.0`. PR unique : [#82](https://github.com/bassetthomas-design/virgil-2.0/pull/82).
Branche : `codex/lot-d1bis-b`. Base exacte après `git fetch origin main` : `427626194e4ad93611b1551a8338edae7284d581`, arbre propre. Base contrôlée de nouveau avant le dernier correctif ; aucun changement de main.

## Séquence de preuve Windows

Tous les runs ci-dessous utilisent `.github/workflows/build.yml`, inchangé. Les SHA sont ceux des heads de branche ; Actions teste également son commit de fusion synthétique, sans fusionner la PR.

| Étape | Head SHA | Run Windows | Résultat constaté |
| --- | --- | --- | --- |
| Tests seuls, contrats et implémentations vides nécessaires | `9dc037fc86be082bb133f253dc433651023293e4` | [42788214469](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42788214469) | Rouge : compilation 0 avertissement/0 erreur ; Core 1175 réussis, 10 échecs sur 1185 ; App 666 réussis, 8 échecs sur 674. Vrais échecs d’assertions sur cartes, statuts, relance et lectures. UI smoke non exécuté. |
| Premier correctif séparé | `a91b2f03a5e927e56a89e59d91045e8997b27077` | [42789508159](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42789508159) | Rouge : 0 avertissement ; Core 1188/1189, App 673/674. Une attente historique de carte groupée oubliée dans le recensement initial, plus le garde d’accents. Les nouveaux tests D1-bis-b passent. |
| Adaptation L6 dans un second commit de tests seuls | `ddf294112d5be6dddccba5923bc62cbbb8362c0b` | [42790013625](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42790013625) | Rouge : 0 avertissement ; Core 1189/1189, App 673/674. Le test L6 adapté passe ; seul le garde d’accents échoue réellement. Aucun test volontairement cassé. |
| Correctif des gardes de relance et du texte | `196196a8c84a3846cc561148991c06fc0b177824` | [42790453811](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42790453811) | Vert : 0 avertissement/0 erreur ; Core 1198/1198, App 674/674, UI 14/14. Publication et installateur réussis. |

La chronologie supplémentaire du test L6 est explicite : il n’était pas dans le premier commit rouge. Son adaptation, autorisée pour R1/R2/R6 par loops (relecture), figure seule dans le second commit de tests, lui aussi soumis à une CI rouge avant le dernier correctif. Aucun historique réécrit ni force-push.

## Comportement livré et vérifications

- R1/R6 : une étape et une carte `Launcher:<nom>` par lanceur détecté. Statut propre à chaque résultat ; ligne de fermeture sur sa carte. Les jeux des autres lanceurs ont une seule ligne non prise en charge, sans relance automatique. La compatibilité interne avec une ancienne étape groupée reste seulement pour les appelants historiques ; le scan et l’interface n’en créent plus.
- R2/R9 : Steam ouvert et valeur ActiveUser non nulle à l’aperçu ; seul le booléen est transmis, avec le chemin de l’exécutable, dans la sélection sérialisée liée au plan. Tests du lecteur de session avec processus/registre factices, de la session figée et du refus d’une sélection altérée. Une confirmation, par le moteur existant inchangé ; une case par lanceur avec libellé explicite. Préparation protégée et absence de redémarrage automatique conservées.
- R3 : fichier existant nommé steam.exe, chemin absolu et signature Valve validée par l’API Windows officielle WinVerifyTrust. Vérification sans récupération réseau de certificats. Lancement centralisé par IProcessRunner, sans shell, sans URL, sans argument brut, avec exactement `-silent`, après la passe finale réussie. Fermeture réalisée par la séquence ou état déjà fermé constaté ; une fermeture refusée ne permet aucune relance ultérieure. Relecture des processus juste avant l’effet ; jeu ou doute bloque. Tests des gardes, de l’absence de plan, d’un jeu actif et d’une fermeture refusée.
- R4 : lecture des bibliothèques et des jeux ; ajouts purs au parseur existant pour nom, StateFlags et buildid. Surveillance toutes les 15 secondes, attente de 10 minutes par défaut, TimeProvider et limite injectables. Un jeu n’est déclaré mis à jour que si une demande a été observée, puis l’état complet et une construction différente. Tests du cas partiel A mis à jour/B restant/C déjà à jour avec Epic, du changement de construction et des erreurs.
- R5/R7 : annulation de l’attente par jeton, sans fermeture de Steam après son ouverture. Les jeux non terminés restent à installer en arrière-plan. Le bilan Lanceurs omet les compteurs nuls et accorde les pluriels ; il est calculé à partir des mêmes résultats que les cartes et ne devient pas à jour en présence de restes, erreur, blocage ou annulation. Tests avant ouverture et pendant l’attente.
- R8 : les cartes de jeux ont une étape et une clé `SteamGame:<appid>` ; titres conservés. Aucun identifiant de manifeste, état numérique ou terme technique interdit dans les nouvelles cartes. Aucun statut de source recopié sur toutes les cartes. Le garde d’accents existant reste inchangé ; les nouveaux textes sont adaptés pour éviter son ambiguïté sur le nom « relance ».
- P5 : le test utilise SteamInventoryReader réel avec registre, bibliothèques et manifestes factices. Le port ISteamReadAccess expose exclusivement des lectures. L’implémentation Windows ouvre les clés avec écriture désactivée et utilise uniquement les lectures de fichiers ; aucun fichier Steam ni registre écrit. Aucun compte connecté, identifiant de compte conservé, jeu lancé ou outil tiers employé.

Les tests de séquence passent par UpdateSequenceService, UpdatesViewModel et LauncherShutdownPolicy réels, avec de faux backends de processus, registre, fichiers et horloge. Des tests supplémentaires vérifient l’adaptateur de lancement réel avec un faux IProcessRunner.

## Tests existants modifiés — déclaration exhaustive

Statistiques contre la base exacte ; toutes les adaptations sont limitées aux anciennes attentes contredisant R1/R2/R6.

| Fichier existant | Lignes +/− | Raison | Commit de tests seuls |
| --- | --- | --- | --- |
| `tests/Virgil.App.Tests/D1ReviewVerdictTests.cs` | +18/−6 | Cartes Steam et Battle.net distinctes, statut non pris en charge avec raisons propres, bilan correspondant. | `9dc037f` |
| `tests/Virgil.App.Tests/UpdatesSequenceViewModelTests.cs` | +6/−3 | Deux étapes de lanceurs, raison non prise en charge ; après relecture, assertion de fermeture rétablie dans le rapport et vérifiée sur chaque carte. | `9dc037f` ; renfort `7b5591d` |
| `tests/Virgil.Core.Tests/L6BehaviorTests.cs` | +5/−2 | Deux lanceurs avec leurs raisons ; après relecture, vérifier aussi la ligne de fermeture sur chaque résultat, en conservant les autres assertions. | `ddf2941` ; renfort `7b5591d` |

Aucune assertion de sécurité, d’échec, de nombre d’élévations, de fermeture pendant un jeu ou d’absence de faux succès retirée ou affaiblie. Les assertions de refus, d’absence d’exécution des lanceurs par le backend, d’actions sautées, du nombre de réussites et d’absence d’échecs du test L6 sont conservées. Aucun autre test existant modifié. Après la relecture de `63bbc1d`, seuls les deux tests expressément autorisés ont été renforcés (+3/−0 dans UpdatesSequenceViewModelTests, +1/−0 dans L6BehaviorTests). L’assertion de texte de fermeture, remplacée au premier passage, est maintenant conservée en plus de celle sur les jeux non pris en charge.

## PNG et artefacts du run vert initial (avant relecture)

[Virgil-ui-smoke — artefact 13750229135](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42790453811/artifacts/13750229135), archive SHA-256 `33f13de82ca30a90339b6f671413a1163dfbb7f286b8aafbd26c445d13af774c`.
Les quatre PNG ont été téléchargés, ouverts pour contrôle visuel et vérifiés dans le dossier `lot-d1bis-b/` de cet artefact vert :

| PNG | SHA-256 |
| --- | --- |
| `confirmation.png` | `6b035f396d06dcab82a95786a77fdec7eb41449d10647664712bf663409348ca` |
| `partial.png` | `6033a71fe9ebb23cb108d6cad5535a2829a64c3abec2cf8f3e27660b8f91bedd` |
| `no-session.png` | `9dd25080d10362355c26bba28c93ea983c3f5d5f1ad28a8c81d74252d4772a74` |
| `cancelled.png` | `c9204df0045b377ac581c70062315dd487541fb8f3d472c6ca8c000e51db9c68` |

Vrai panneau WPF et vrai ViewModel rendus sur STA avec données factices : **preuve STA factice**, sans exécution réelle de Steam. Les PNG de ce lot sont produits par les tests App ; les 14 tests UI smoke FlaUI du même run portent sur l’application publiée. La couleur de barre existante n’est pas modifiée (lot E).
Autres artefacts verts : `Virgil-2.0-win-x64` (13750384077), `Virgil-win-x64-setup` (13750669520). Aucun binaire ni PNG généré n’est committé.

## Limites et suite

Aucune exécution Windows réelle de Steam ou WeGame sur un PC personnel, aucune installation réelle de jeu et aucune validation manuelle de signature Steam effectuées. La preuve réelle reste prévue à l’alpha.8 sur le PC équipé de Steam et WeGame. Le mode silencieux ne force pas Steam à ignorer ses propres règles de planification : un reste reste affiché comme tel.
La compilation croisée locale a réussi sans avertissement ; l’exécution locale des tests Windows est non effectuée, le runtime WindowsDesktop étant absent. Le garde de sources sur les accents a aussi été exécuté dans un harness portable ; cela ne remplace pas la CI Windows verte ci-dessus.
Le fichier externe `/workspace/overmind/revues/liste-controle-virgil.md` n’est pas présent dans cet espace ; la liste intégrale fournie au cadrage a été appliquée. Aucun fichier de feu vert de loops (relecture) portant le SHA final n’est fourni ni fabriqué. Aucune soumission au Contre-Expert et aucune fusion : la relecture de loops reste à faire.

## Fichiers du diff

```text
docs/proofs/Lot-D1bis-b.md
src/Virgil.App/ViewModels/UpdatesViewModel.Sequence.cs
src/Virgil.App/ViewModels/UpdatesViewModel.cs
src/Virgil.Core/Updates/LauncherCardSummary.cs
src/Virgil.Core/Updates/LauncherShutdownPolicy.cs
src/Virgil.Core/Updates/SteamContracts.cs
src/Virgil.Core/Updates/SteamExecutableGuard.cs
src/Virgil.Core/Updates/SteamUpdateService.cs
src/Virgil.Core/Updates/UpdateSequenceService.cs
src/Virgil.Infrastructure/Windows/LauncherManifestParser.cs
src/Virgil.Infrastructure/Windows/SteamAuthenticode.cs
src/Virgil.Infrastructure/Windows/SteamInventoryReader.cs
src/Virgil.Infrastructure/Windows/SteamProcessLauncher.cs
src/Virgil.Infrastructure/Windows/SteamWindowsClient.cs
src/Virgil.Infrastructure/Windows/WindowsLauncherShutdownBackend.cs
src/Virgil.Infrastructure/Windows/WindowsUpdateSequenceBackend.cs
tests/Virgil.App.Tests/D1ReviewVerdictTests.cs
tests/Virgil.App.Tests/D1bisBPanelTests.cs
tests/Virgil.App.Tests/D1bisBSteamCardReviewTests.cs
tests/Virgil.App.Tests/UpdatesSequenceViewModelTests.cs
tests/Virgil.Core.Tests/D1bisBSteamTests.cs
tests/Virgil.Core.Tests/D1bisBSummaryReviewTests.cs
tests/Virgil.Core.Tests/L6BehaviorTests.cs
```

Diff de production et de tests : 22 fichiers, +1116/−37 ; ce document de preuve s’ajoute à ces fichiers. Aucun XAML, workflow, réglage, secret, fichier généré ou moteur de confirmation modifié. `git diff --check origin/main` passe.

## Corrections après relecture de loops sur 63bbc1d

La relecture n’accorde pas de feu vert. Trois corrections sont livrées dans trois commits séparés, suivies d’un quatrième commit de preuve et de mise à jour du corps de PR :

1. `4925aafab187db2a242d7ef225e3bcc8e7dd5d9a` : après ouverture réussie, le statut Steam découle exclusivement des résultats des jeux, par priorité : en erreur, reste à installer, mis à jour, à jour selon Steam. Nouveau `tests/Virgil.App.Tests/D1bisBSteamCardReviewTests.cs` : quatre cas avec le vrai service, ViewModel et politique (partiel, tout bon avec mise à jour, tout déjà à jour, erreur prioritaire sur un reste).
2. `52e67128e83152fbe3b2e1df99f74f17d45018b0` : résumé sans compteurs à zéro ; « 1 reste à installer / 2 restent à installer », « 1 non lancé / 2 non lancés » et pluriel des lanceurs. Nouveau `tests/Virgil.Core.Tests/D1bisBSummaryReviewTests.cs` : 0, 1 et 2 pour toutes les catégories, puis chaque catégorie seule.
3. `7b5591d2efd228fb2fa8587757f5d4403207e87e` : renforcement des deux tests existants expressément autorisés au point 3, sans retirer aucune assertion. La ligne de fermeture reste vérifiée en plus de la limite de prise en charge, sur le rapport et les cartes ou résultats du bon lanceur. Aucun correctif de production nécessaire à ce renforcement.
4. Preuve mise à jour avec les runs constatés et les PNG régénérés ; corps de PR réécrit avec les résultats courants et sans mention d’attente périmée. L’état brouillon de la PR est inchangé : `draft=false` au début de cette relecture, sans transition effectuée.

### Validation Windows après les corrections

[Run 42793391357](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42793391357), head `7b5591d2efd228fb2fa8587757f5d4403207e87e`, entièrement vert : compilation 0 avertissement/0 erreur ; Core 1206/1206, App 678/678, UI 14/14. Les douze nouveaux cas passent, ainsi que les deux tests existants renforcés. Publication et installateur réussis.

Les quatre PNG ont été régénérés puis téléchargés et ouverts pour contrôle visuel : Steam affiche « reste à installer » dans le cas partiel et le bilan ne contient plus les catégories à zéro. Le cas tout bon et les quatre priorités de statut sont vérifiés par les nouveaux tests du vrai ViewModel.
[Virgil-ui-smoke — artefact 13753372104](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42793391357/artifacts/13753372104), archive SHA-256 `0a8e8252c62060b0ec167209786d8d15a56bb3408fda20af85239673fa380c6c` :

| PNG sous `lot-d1bis-b/` | SHA-256 après relecture |
| --- | --- |
| `confirmation.png` | `3a1a24d260987b7a49f23df5b9a763ed9c7e14b2548408249e049750d6a9b7ae` |
| `partial.png` | `5665d3f92f397d0e22257bcc2cc0721c3b813f12cdee9038c2251555d5496eee` |
| `no-session.png` | `9dd25080d10362355c26bba28c93ea983c3f5d5f1ad28a8c81d74252d4772a74` |
| `cancelled.png` | `f2846ea1c424f83c3820caac14666440e1a3fd378624a387398537bfc31e428b` |

Preuve WPF sur STA avec données factices ; aucune exécution réelle de Steam. Les limites alpha.8 et l’absence de feu vert de loops (relecture) restent inchangées. Aucun changement d’état brouillon, aucune soumission au Contre-Expert, aucune fusion.
