# Lot B — Applications / Steam

Base imposée : `6b83871766d125d56d98c3305b704af957c4493a`.
Branche : `codex/alpha8-lot-b-apps-steam`. Une seule [PR brouillon #78](https://github.com/bassetthomas-design/virgil-2.0/pull/78), sans fusion.

## Séquence rouge puis correctif

- Tests seuls : `f019b4386e8972e5e2df620748d391ef215dae2e`, deux fichiers, +201 lignes.
- [Run rouge Windows 42584596471](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42584596471), build.yml n°316 : restore et build réussis, 0 avertissement et 0 erreur. Core : 1140 tests, 3 échecs ; App : 595 tests, 7 échecs. Les dix échecs sont des assertions des nouveaux tests.
- P1 : `OfficialSteamUriBuildsLaunchablePlan` échoue à `Assert.True(plan.CanLaunch)` avec « Exécutable relatif ou introuvable. » pour `steam://uninstall/2221490`. Les deux autres échecs Core portent sur le lancement confirmé et le preview Steam/MSI. Les sept échecs App exposent les causes fusionnées.
- Le checkout CI rouge est le merge synthétique `be154c10f4e7d84d7ee873231c835c20794d29e1`, qui associe le head tests seuls à la base imposée.
- Correctif dans un commit ultérieur au rouge, sur la même branche. Résultat vert Windows et head exact : à renseigner après exécution, sans revendiquer de validation Linux.

## Comportement livré et sécurité

`ApplicationRegistryReader` reconnaît l’URI Steam avant le repli MSI/registre. Le validateur exige exactement `steam://uninstall/` suivi de chiffres ASCII représentant un entier positif sur 32 bits. Il refuse zéro, négatif, lettres, dépassement, encodage, arguments, suffixes, requêtes, fragments et commandes chaînées. Les gardes de shell et de chemins personnels restent appliqués avant l’acceptation. Le type Steam ne peut pas déguiser une commande MSI ou un exécutable.

Le plan conserve l’URI complète comme cible, avec zéro argument. `ShellApplicationProcessLauncher` transmet `UseShellExecute=true` au port `IProcessRunner` : Windows remet l’URI au gestionnaire Steam enregistré. `WaitForExit=false` pour cette URI évite d’attendre la fermeture du client Steam. Les autres désinstalleurs conservent leur attente existante. La réussite signifie ouverture demandée, pas désinstallation achevée. Un protocole absent ou en échec affiche un message Steam lisible ; aucune exception brute n’est affichée dans ce statut.

Aucun dossier, manifeste, bibliothèque ou reste n’est supprimé. Le scanner de restes reste inchangé et en lecture seule. Le service conserve `ApplicationUninstallConfirmationPolicy`, `ConfirmationService.Issue` et `IConfirmedPlanGate` ; les tests refusent l’absence de plan, l’absence de confirmation renforcée et le rejeu, sans appel au lanceur. Dans un lot, un état externe inconnu interrompt toujours les éléments suivants : construire les plans Steam/MSI ne promet pas de les lancer simultanément.

La méthode et les détails affichent « Steam officiel » et l’URI. Le preview inclut la cible officielle. Le succès indique « Steam a été ouvert pour désinstaller ce jeu. Suivez l'assistant Steam. » ; l’échec indique que Steam n’a pas pu être ouvert et demande de vérifier son installation et son association aux liens Steam.

Le seul ajout dans `ApplicationRiskClassifier` reconnaît Steam parmi les méthodes identifiables. Le blocage initial de périmètre a été signalé, puis la reprise du correctif a été demandée par l’utilisateur après ce signalement. Aucun faux Protected n’est revendiqué : les termes de protection et les gardes système restent inchangés. Les jeux dont nom/éditeur contient Steam restent Caution avec confirmation renforcée.

## P2 — Causes et priorité

Les messages sont identiques dans le détail unitaire et la sélection. Priorité globale : **vide → protégé → commande invalide**, indépendamment du tri alphabétique. `!CanUninstall` sans preuve de protection relève de la commande indisponible ; un composant système ou un risque Protected relève toujours de la protection.

| Cause | Message exact | Test App `ApplicationsSteamRegressionTests` |
| --- | --- | --- |
| Vide | Aucune application sélectionnée. Cochez au moins une application ou un jeu, ou ouvrez ses détails. | `EmptySelectionAndMissingDetailsHaveSameExactMessage` ×2 |
| Protégé | Application protégée. Virgil ne désinstalle pas les composants système, pilotes ni outils de sécurité. | `ProtectedAndInvalidCausesAreDistinctInDetailsAndBatch` ×2 ; `ProtectedCauseHasPriorityOverInvalidCommandInBatch` |
| Commande invalide | Commande de désinstallation officielle invalide ou manquante. Virgil ne lance que les désinstalleurs officiels (Steam, MSI, WinGet, exécutable d'uninstall). | `ProtectedAndInvalidCausesAreDistinctInDetailsAndBatch` ×2 |

Ces tests exigent zéro appel au lanceur et zéro dialogue. `MergedFailureLabelIsAbsentFromViewModel` vérifie la disparition du libellé fusionné. `SteamDetailsShowOfficialMethodAndRequireConsent` vérifie méthode, URI, annulation et ouverture après consentement.

## P3 — URI, confirmation et lot

Dans Core `ApplicationsSteamRegressionTests` :

- `OfficialSteamUriBuildsLaunchablePlan`, `InventoryClassificationKeepsOrdinarySteamGameUninstallable`, `ConfirmedSteamLaunchUsesOnlyOfficialUriAndShellExecute` : type Steam, Allowed, jeu désinstallable, un lancement URI sans arguments avec ShellExecute.
- `InvalidSteamUriIsBlocked` : zéro, négatif, lettres, chaîne shell, chemin Windows déguisé, suffixe, requête, fragment, encodage, dépassement et chiffres non ASCII.
- `SteamNeverLaunchesWithoutConfirmedPlan` ×2 et `CautionSteamKeepsReinforcedConfirmationAndCannotReplayPlan` : refus sans effet, confirmation renforcée conservée, usage unique.
- `SteamKindCannotDisguiseAnotherCommandOrBypassProtection` : mauvaise cible et protections bloquées.
- `SteamAndMsiBatchPreviewContainsBothOfficialTargets` : deux plans lançables, preview avec URI Steam et msiexec.
- `ProtocolFailureIsReadableAndSteamRequestDoesNotWaitForClientExit` ×2 : erreur retournée ou exception du port factice, message français, requête ShellExecute sans attente.

Les suites existantes MSI, WinGet, Store, rundll32 et protections restent exécutées par build.yml, sans modification des mécanismes de confirmation.

## P4 — Preuve UI Windows

Le smoke général régénère `06-applications.png`, mais son inventaire ne garantit pas de jeu Steam. La preuve spécifique utilise la vraie vue WPF sur thread STA, les dictionnaires de thème de l’application et un inventaire registre factice : `ApplicationsSteamWpfTests.SteamDetailsRenderOfficialMethodAndUriWithoutLaunching`.

Elle vérifie les TextBlock visibles « Méthode : Steam officiel » et `steam://uninstall/2221490`, le bouton disponible, zéro dialogue et zéro processus. Elle produit `artifacts/ui-smoke/lot-b/applications-steam-details.png`, collecté par l’artefact existant `Virgil-ui-smoke`. Ce PNG est une preuve équivalente STA avec données factices, pas la capture d’un vrai jeu installé. Résultat et inspection visuelle à renseigner après CI.

## P5 — Limites

Le SDK n’est pas disponible dans l’environnement Linux ; aucune compilation locale n’est revendiquée. La CI Windows existante fait foi. La preuve finale d’une désinstallation d’un vrai jeu Steam sur le PC de thoms reste à réaliser manuellement pendant l’alpha.8, y compris Steam déjà ouvert, association protocole et suivi de l’assistant. Aucun achèvement de désinstallation réelle n’est déduit d’un lancement mocké ou d’un PNG.
