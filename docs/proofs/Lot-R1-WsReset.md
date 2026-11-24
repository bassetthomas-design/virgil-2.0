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

Résultats locaux et relevé CI à compléter après exécution. La CI du dernier commit de production ou de test sera inscrite ici. La CI du commit final, s’il modifie uniquement cette preuve, sera inscrite dans la description de la PR.

## Périmètre

Seuls les fichiers autorisés par la consigne initiale et les deux relances de virgil (relecture) sont touchés. Le diff récapitulatif sera inscrit à la clôture. CHKDSK planifié au redémarrage et la remise à zéro complète de Windows Update restent hors lot.
