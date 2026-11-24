# Lot R1 — Réinitialiser le cache du Microsoft Store

Après « Analyser », l’action est disponible si `wsreset.exe` existe dans le répertoire système. La validation annonce la commande fixe `wsreset.exe` sans argument, la réinitialisation du cache et l’ouverture possible du Store. Virgil laisse le Store ouvert. Aucun service n’est arrêté ou réactivé. Les applications installées, les fichiers personnels et les autres caches Windows sont préservés.

Le plan confirmé est vérifié avant tout appel au helper. L’élévation est demandée après confirmation et le point de restauration doit être créé et vérifié avant la réparation, sauf dérogation Critique explicite du parcours existant. L’annulation utilisateur ne tue pas la commande démarrée. Le délai interne de cinq minutes arrête `wsreset.exe` s’il n’a pas terminé et l’échec en français est conservé dans le journal. Aucun redémarrage n’est demandé.

- `InterventionDiagnosticService.cs` limite la recherche de `wsreset.exe` au répertoire système, comme le helper, avec des tests pour le dossier parent et le répertoire courant.
- `ElevatedCommandRunner.cs` applique à `wsreset.exe` le seul délai interne après démarrage. Les autres commandes conservent leur parcours d’attente.

## Tests

- `LotReparationWsResetTests` vérifie le catalogue, les valeurs sérialisées, l’allowlist, le diagnostic, le maintien de la recherche SFC, l’absence de suggestion sur AppXSvc et ClipSVC, la confirmation, la restauration, les droits et le journal d’échec. Une horloge manuelle et un processus factice vérifient l’arrêt à cinq minutes.
- `ElevatedAtomicCommandFinishesAfterCancellationWithoutReceivingKillToken` couvre l’annulation de la commande en cours et le journal « terminée après annulation ». Les cas existants, dont SFC, conservent leurs assertions.
- `Wsreset_validation_displays_command_and_refusal_runs_nothing` vérifie l’aperçu, l’avertissement d’élévation et le refus avec les doubles de vue modèle.

Aucun test ajouté ne lance de processus système réel. Les assertions CHKDSK `/scan`, les associations de constats existantes, les délais `TimeoutFor` existants et les seuils de la règle R4 sont conservés.

## Validations

- Moteur en local, `dotnet test --no-build --filter` avec les noms exacts des cas R1 et des gardes de catalogue, d’allowlist, de confirmation, d’annulation, de constats, de délais et d’attente asynchrone. 36 réussis, aucun échec. La copie de validation Linux utilise le runtime .NET seul pour ces tests sans interface.
- Application en local, `dotnet test --filter FullyQualifiedName=Virgil.Tests.InterventionsViewModelTests.Wsreset_validation_displays_command_and_refusal_runs_nothing`. Compilation réussie avec `EnableWindowsTargeting=true`. Le test n’a pas pu être exécuté localement faute de runtime Windows Desktop. Il a réussi sur la CI Windows.
- [Run #522](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45468674221), environ 2 min 19 s. Échec du garde d’attente asynchrone sur `Task.Result`, corrigé par `await`. Moteur 1 356 réussis et 1 échec, application 907 réussis, interface non exécutée.
- [Run #523](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45469301328), dernier commit de code `11cb85171017a099c55f211071fb57af19e5d9be`. Job Windows réussi en environ 4 min 47 s. Moteur 1 357, application 907, interface 14 réussis. Aucun échec ni relance pour test fragile.

La CI du commit final, qui modifie uniquement cette preuve, figure dans la description de la PR.

## Périmètre

Seuls les fichiers autorisés par la consigne initiale et les deux relances de virgil (relecture) sont touchés. CHKDSK planifié au redémarrage et la remise à zéro complète de Windows Update restent hors lot.

Sortie de `git diff --stat origin/main...HEAD` avec la base main vérifiée.

```text
 README.md                                          |   2 +-
 docs/ACTIONS.md                                    |   4 +-
 docs/ARCHITECTURE.md                               |   2 +-
 docs/PROJECT_STATUS.md                             |   2 +-
 docs/proofs/Lot-R1-WsReset.md                      |  50 ++++
 .../Interventions/InterventionModels.cs            |   3 +-
 .../Interventions/InterventionCatalog.cs           |  17 +-
 .../Interventions/InterventionDiagnosticService.cs |   6 +
 .../Interventions/InterventionSafety.cs            |   3 +-
 .../ElevatedActionAllowlist.cs                     |   3 +-
 .../ElevatedActionDispatcher.cs                    |   1 +
 src/Virgil.ElevatedHelper/ElevatedCommandRunner.cs |  32 +++
 .../InterventionsViewModelTests.cs                 |  28 ++
 .../Virgil.Core.Tests/LotReparationWsResetTests.cs | 282 +++++++++++++++++++++
 .../OperationCancellationSafetyTests.cs            |   1 +
 15 files changed, 428 insertions(+), 8 deletions(-)
```
