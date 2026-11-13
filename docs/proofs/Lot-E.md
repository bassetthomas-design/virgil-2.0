# Lot E — Visage, progression et icône de Virgil

Base : `7ad4a7d62ec556440e6db406c96cb06f3c1fa8ee`. PR [#85](https://github.com/bassetthomas-design/virgil-2.0/pull/85), branche `codex/lot-e`, laissée en brouillon. Aucun workflow, réglage, secret, garde, service d’installation ou règle de statut des cartes modifié.

## Séquence de preuve Windows

- Tests seuls initiaux : run [399](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43420120538), ID `43420120538`, SHA `ac82db14dd78e2c52c09a4ec0e71483efc5d6625`. Compilation : 0 avertissement, 0 erreur. Core : 1247/1247. App : 727 réussis, 24 nouveaux cas échouent. Les assertions portent sur le rendu absent, ses libellés, l’état final absent et le câblage de l’icône ; une lecture de l’icône absente échoue aussi. Ce run rouge ne vaut pas validation finale.
- Complément tests seuls : run [400](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43420545422), ID `43420545422`, SHA `bebe6f1081ba8efba8bfb12876a3cd2d5a974ffd`. Compilation : 0 avertissement, 0 erreur. Core : 1247/1247. App : 727 réussis, 30 nouveaux cas échouent. Ajouts : séquence mixte dans une seule exécution, redémarrage, vrai contrôle chargé en STA, expression réservée et reproduction de l’icône.
- Correctif séparé : SHA `73eab828b61ddc16cb55508fc0eb651d78310f78`, arbre `e15d8b9c0bdbb86cc8e5b08f4f5e24eec12cf1ba`. Run [401](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43438464067), ID `43438464067` : compilation à 0 avertissement et 0 erreur ; Core 1247/1247, App 756/757. Les trois suites demandées et tous les tests préexistants passent. Un seul cas du nouveau `LotEBehaviorTests` avait une attente erronée : `mis à jour` au lieu de `redémarrage nécessaire` pour un résultat avec `RebootRequired = true`. Ce run rouge n’est pas une preuve finale.
- Correction de cette attente dans le nouveau test : SHA `629c1c347d2560c841f84c528fb5825a5a6c11cb`, arbre `b97477b051cf95aba59dcf495568b12ecbe52e4e`. Les règles de statut des cartes n’ont pas été modifiées. Validation verte du correctif : run [402](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43439114744), ID `43439114744`, SHA `629c1c347d2560c841f84c528fb5825a5a6c11cb`. **0 avertissement, 0 erreur ; Core 1247/1247, App 757/757, UI 14/14. Publication de l’application et compilation de l’installateur réussies.**
- Artefact vérifié de ce run : [Virgil-ui-smoke, ID 14044327338](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43439114744/artifacts/14044327338). Téléchargement et contenu vérifiés : **22 PNG dans `lot-e/`**. SHA-256 de l’archive : `c0ec7f84fee18af619506d8147eb39c87e419e89faeefd3de58f9b81e640d115`.

Ce document est ajouté après la validation ci-dessus, dans un commit documentaire distinct. Le nouveau run sur le head qui contient ce document sera vérifié à son tour ; son numéro, son SHA exact et son artefact seront consignés dans la description de la PR #85, sans réécriture de l’historique. La référence immuable ci-dessus identifie la validation de tout le code livré ; le commit suivant ne change que ce document.

Les actions GitHub intégrées créent leurs propres métadonnées de commit : `ac82db1` a exactement l’arbre du commit local de tests `e935f1c`, sans amend, rebase ni force-push. Chaque publication suivante conserve également l’arbre exact et ajoute un commit distant linéaire.

## Table du visage

Le dessin validé le 5/11 fournit les proportions du disque, les dégradés et les yeux. Le contrôle reste `VirgilCoreControl` ; les propriétés publiques, `SetState`, `NotifyCommunication` et tous les noms utilisés par les vues sont conservés.

| État | Forme des yeux | Couleur | Nom accessible exact |
| --- | --- | --- | --- |
| Idle | Rectangles arrondis | `#3fe0c5` | Virgil au repos |
| Communicating | Rectangles arrondis, léger clignement | `#3fe0c5` | Virgil au repos |
| Scanning | Yeux plissés | `#4aa8ff` | Virgil au travail |
| Executing | Yeux plissés | `#4aa8ff` | Virgil au travail |
| Warning | Ronds | `#ffc23d` | Virgil : attention |
| SensitiveAction | Ronds fixes | `#ffc23d` | Virgil : attention |
| Error | Barres inclinées | `#ff4d4d` | Virgil : erreur |
| Success | Arcs souriants | `#5ee07a` | Virgil : réussi |

La forme sarcastique est réservée dans `VirgilEyeShape` et sa géométrie, sans nouvelle valeur de `VirgilCoreState` ni écran qui l’utilise. Le rendu utilise des géométries WPF, aucun bitmap. Transition de 200 ms ; clignement du repos toutes les 5 secondes et léger clignement de communication. Désactivation Windows, contrôle masqué/déchargé et mode compact (y compris Auto sous 96 DIP) arrêtent les animations. Les préférences Windows sont relues à leur changement ; aucun anneau ou élément ne tourne. `VirgilMotionProfiles` et `VirgilCoreAnimationController` sont conservés à l’identique (décision E1 de loops (relecture)).

## Progression

`OperationFinalState` porte Neutral, Succeeded, NeedsReview, Failed et Cancelled. `Start` remet l’état à Neutral ; `Complete` ne déduit rien de `FinalSummary`, de son message ou de son libellé. Sans état fourni, la barre reste neutre. Après annulation elle reste neutre et conserve sa valeur ; les autres fins passent toujours à 100 %.

Le panneau lie sa couleur à cet enum : neutre `#9fb0c0`, réussite `#5ee07a`, reste/à vérifier/redémarrage `#ffc23d` (ambre), erreur/blocage `#ff4d4d`. Dans les séquences de Mises à jour et les mises à jour individuelles, l’agrégation utilise les cartes identifiées par `Component:Id`, jamais la ligne globale de source : erreur/blocage > reste ou contrôle incomplet > réussite confirmée. Les cartes d’information et les cartes non prises en charge ne participent pas. Le visage final reçoit le même résultat (Success, Warning, Error ; Idle après annulation). La séquence guidée préexistante utilise ses résultats typés et ses éléments restants.

Les scans en lecture seule gardent leur présentation et leurs libellés actuels ; ils ne prétendent pas avoir installé des mises à jour via une barre verte. Les appels génériques des autres écrans restent neutres : leur méthode commune de fin n’identifie pas un résultat typé de l’opération courante sans risque d’utiliser le rapport d’une opération précédente. Aucun nouveau champ ni nouvelle lecture n’y est ajouté (décision E4 de loops (relecture)).

## Icône reproductible

`assets/virgil-visage.svg` est l’extraction vectorielle du grand visage fourni. `build/Build-Icon.ps1` lit les cercles, rectangles arrondis, dégradés et contour de cette source, puis effectue un échantillonnage 4 × 4 et écrit les six images BGRA 32 bits et leurs masques dans un ICO. Aucun paquet, outil payant, service en ligne ou génération d’image par IA. Les pixels transparents et les bords conservent un alpha droit. Les tailles sont 16, 32, 48, 64, 128 et 256 px.

Reproduction depuis la racine, avec PowerShell 7 :

```powershell
./build/Build-Icon.ps1
# Ou sortie indépendante pour comparaison :
./build/Build-Icon.ps1 -OutputPath "$env:TEMP/virgil-verification.ico"
```

Un test Windows exécute réellement le script dans un processus PowerShell séparé et compare les octets à l’ICO commité. La CI compile l’ICO livré ; elle ne le régénère pas pour le build.

L’ICO est lié à `ApplicationIcon`, incorporé comme ressource WPF et référencé par `MainWindow.Icon`. L’installateur le référence dans `SetupIconFile`, le copie dans `{app}`, puis l’utilise pour `UninstallDisplayIcon` et les deux raccourcis. Les gardes d’installation existantes sont conservées.

## Captures et limites

Les nouveaux tests rendent les vrais contrôles WPF sur le thread STA avec le thème sombre de l’application. Sortie vérifiée dans `Virgil-ui-smoke/lot-e/` (22 PNG) : les huit états du visage, les panneaux finaux (dont vert, ambre et rouge), les bilans mixtes, et l’icône en 16, 32 et 256 px sur fonds clair et sombre. Aucun thème clair n’est créé (décision E2 de loops (relecture)).

Le rendu réel de l’icône dans la barre des tâches Windows, les raccourcis et l’installateur reste à vérifier sur une machine Windows à l’alpha.8. Une capture WPF et une compilation de l’installateur ne remplacent pas cette vérification manuelle. Aucun essai manuel sur cette machine n’est revendiqué.

## Périmètre et exception de test

Fichiers de production : les deux fichiers de `VirgilCoreControl`, `VirgilCoreAnimationModels.cs` (ajouts seulement), `OperationProgressPanel.xaml`, `LongOperationViewModel.cs`, `UpdatesViewModel.cs`, `UpdatesViewModel.Sequence.cs`, `MainWindow.xaml` (Icon seulement), `Virgil.App.csproj`, `installer/Virgil.iss`.

Nouveaux fichiers : `assets/virgil-visage.svg`, `assets/virgil.ico`, `build/Build-Icon.ps1`, `tests/Virgil.App.Tests/LotEFaceTests.cs`, `LotEIconTests.cs`, `LotEProgressColorTests.cs`, `LotEBehaviorTests.cs`, et ce document.

Exception explicitement autorisée dans ce fil : une seule assertion dans `tests/Virgil.App.Tests/UpdatesViewModelTests.cs`, méthode `ExecutionFailureAndErrorProduceWarningReport`, passe de `Warning` à `Error` pour un échec d’installation, conformément à R6. Les deux cas de la théorie et toutes ses autres assertions sont conservés. Aucun autre test existant n’est modifié ; en particulier les huit tests des profils de rotation restent intacts.

### Liste exacte des fichiers touchés

```text
assets/virgil-visage.svg
assets/virgil.ico
build/Build-Icon.ps1
docs/proofs/Lot-E.md
installer/Virgil.iss
src/Virgil.App/Animation/VirgilCoreAnimationModels.cs
src/Virgil.App/Controls/OperationProgressPanel.xaml
src/Virgil.App/Controls/VirgilCoreControl.xaml
src/Virgil.App/Controls/VirgilCoreControl.xaml.cs
src/Virgil.App/MainWindow.xaml
src/Virgil.App/ViewModels/LongOperationViewModel.cs
src/Virgil.App/ViewModels/UpdatesViewModel.cs
src/Virgil.App/ViewModels/UpdatesViewModel.Sequence.cs
src/Virgil.App/Virgil.App.csproj
tests/Virgil.App.Tests/LotEBehaviorTests.cs
tests/Virgil.App.Tests/LotEFaceTests.cs
tests/Virgil.App.Tests/LotEIconTests.cs
tests/Virgil.App.Tests/LotEProgressColorTests.cs
tests/Virgil.App.Tests/UpdatesViewModelTests.cs
```
