# Lot R2 — Vérification planifiée du disque système

## Commande et parcours

Après « Analyser », la personne sélectionne l’action « Planifier la vérification du disque système au prochain redémarrage ». La confirmation affiche `fsutil.exe dirty set C:` si le lecteur système est `C:`. Le catalogue contient les trois arguments fixes `dirty`, `set` et `<system-drive>`. Le diagnostic fournit le lecteur affiché et le helper détermine et valide son propre lecteur système. `fsutil.exe` est recherché uniquement dans le répertoire système et exécuté sans shell ni entrée standard.

virgil (relecture) a retenu [la commande Microsoft `fsutil dirty`](https://learn.microsoft.com/windows-server/administration/windows-commands/fsutil-dirty). « When a volume's dirty bit is set, autochk automatically checks the volume for errors the next time the computer is restarted ». Microsoft précise aussi que CHKDSK vérifie l’intégrité du système de fichiers et tente de corriger les problèmes. Le contrôle est l’équivalent de CHKDSK /F. Sur le lecteur système, `chkdsk /f` demanderait une réponse interactive localisée. `chkntfs` ne permet pas d’obtenir ce résultat avec une seule commande fixe sur un volume propre.

La confirmation indique « Un redémarrage sera nécessaire » et « Virgil ne redémarre pas le PC ». Le parcours demande l’élévation après confirmation, crée et vérifie un point de restauration, puis lance la commande. Le résultat conserve la sortie et le code de la commande dans les détails techniques et le rapport. Seule une planification réussie indique qu’un redémarrage est requis. Si le parcours est annulé avant la commande, elle n’est pas lancée. Une fois lancée, elle ne reçoit pas le jeton d’annulation du parcours.

Une fois planifiée, Virgil ne peut pas annuler la vérification. Elle se lance au prochain redémarrage décidé par la personne et peut durer de quelques minutes à plus d’une heure selon le disque. Le PC doit rester allumé pendant la vérification.

## Correction minimale au titre de la règle générale

- `InterventionExecutionService.cs` rend le point vérifié obligatoire pour cette action, bloque le parcours sans proposer la dérogation de confirmation critique et traite l’annulation pendant la protection avant de produire un échec de restauration. Les tests d’exécution couvrent ces cas.

## Tests

- `Catalog_and_allowlist_define_one_protected_command` et `Allowlist_rejects_invalid_drives` vérifient l’identifiant 26, les propriétés du catalogue, les arguments exacts, la validation du lecteur, CHKDSK en ligne, le délai de cinq minutes et l’exclusion de `RepairsFor`.
- `Diagnostic_searches_for_fsutil_only_in_system_directory` et `Diagnostic_rejects_invalid_system_drive` vérifient le chemin, le lecteur, la recherche SFC et l’avertissement de redémarrage.
- `Dispatcher_reports_actual_scheduling_outcome`, `Dispatcher_refuses_scheduling_without_administrator` et `Existing_reboot_actions_keep_status_indicator_and_log` vérifient les messages, le journal, le statut et l’indicateur de redémarrage en succès et en échec.
- `Execution_requires_confirmation_and_refuses_unverified_restore_even_with_override`, `Execution_runs_fixed_command_after_verified_restore` et `Cancellation_during_or_after_restore_prevents_scheduling` vérifient l’ordre confirmation, restauration et commande ainsi que le blocage et l’annulation.
- `Elevated_allowlist_uses_fixed_safe_commands_only` vérifie aussi les arguments et exécutables interdits pour toutes les actions de son périmètre. `ElevatedAtomicCommandFinishesAfterCancellationWithoutReceivingKillToken` ajoute le cas fsutil à la couverture de la non-interruptibilité.
- `Scheduled_disk_check_preview_and_cancellation_precede_execution` vérifie l’aperçu résolu, le texte de redémarrage et l’absence d’exécution après annulation ou refus depuis le modèle de vue.

Les tests utilisent les doubles du diagnostic, de la confirmation, du client du helper et du lanceur de commandes. Aucun processus système réel n’est lancé. Les assertions existantes et les seuils RG4 sont conservés. `IsForbiddenArgument`, `IsSafe`, `ChkdskOnlineScan` et son `/scan`, `RepairsFor` et `TimeoutFor` restent identiques.

## Vérification locale

- Moteur, `dotnet test --filter` par noms complets avec `Category!=Windows` sur le fichier R2 et le garde d’allowlist modifié. **16 réussis, 0 échec** sous Linux après retrait de la référence au runtime Windows Desktop dans le seul fichier de configuration généré par la copie de travail temporaire.
- Application, `dotnet test --filter FullyQualifiedName=Virgil.Tests.InterventionsViewModelTests.Scheduled_disk_check_preview_and_cancellation_precede_execution`. Compilation Release réussie. Exécution locale interrompue au démarrage de testhost, faute de runtime Windows Desktop.
- Les tests nécessitant l’interprétation Windows du chemin du lecteur système, dont le nouveau cas du test d’annulation atomique, sont réservés à la CI Windows.

## CI Windows

La CI du dernier commit de production ou de test sera consignée ici. Si le commit final ne modifie que cette preuve, son run est indiqué dans la description de la PR.

## Périmètre

La branche est basée sur `754e71412a565896757e1cb0f0704bc2e2c3fd58`. Les fichiers modifiés sont autorisés par la liste du lot ou par sa règle générale. La remise à zéro complète de Windows Update reste au lot R3. `virgil-update` redémarre uniquement `wuauserv`.

Sortie de `git diff --stat origin/main...HEAD` (la référence locale de main est `origin/main`).

```text
Diff en attente de publication.
```
