# Audit fonctionnel Virgil 2.0

Date: 2026-06-16  
Base: code present dans `src` et `tests`, sans extrapolation produit.

## Synthese

- Modules operationnels: 9
- Modules partiels: 5
- Placeholders ou modules absents: 17
- Aucune elevation administrateur n'est demandee par l'application actuelle.
- Les scans systeme sont en lecture seule.
- Le nettoyage guide existe deja, mais uniquement par zones autorisees et apres validation explicite.

## Tableau d'audit

| Module | Bouton ou point d'entree UI | Service ou classe principale | Etat | Fonctions reellement disponibles | Actions systeme effectuees | Droits admin | Garde-fous | Tests existants | Limitations | Prochaine etape recommandee |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Accueil | `ACCUEIL`, grand noyau, `SCAN COMPLET` | `MainWindow` | Operationnel | Tableau d'accueil, lancement scan, dernier rapport, metriques apres scan | Aucune | Non | Navigation bloquee pendant scan, chat sans saisie utilisateur | Couvert indirectement par build WPF | Metriques non temps reel avant scan | Raccorder l'accueil a l'historique local |
| Scan rapide | Overlay protocole, `SCAN RAPIDE` | `SystemScanService.RunAsync(ScanMode.Quick)` | Operationnel | Windows, CPU instantane, RAM, disque systeme, reseau primaire | Lecture systeme uniquement | Non | `_scanInProgress`, boutons desactives, erreurs partielles lisibles | `SystemScanServiceTests.QuickScan_does_not_preview_cleanup` | Pas de processus, pas de preview nettoyage | Ajouter export et trace d'historique |
| Analyse approfondie | Overlay protocole, `ANALYSE APPROFONDIE` | `SystemScanService.RunAsync(ScanMode.Deep)` | Operationnel | Scan complet lecture seule, disques fixes, processus memoire, preview nettoyage | Enumeration fichiers et processus, aucune suppression | Non | Annulation, erreurs distinctes, preview nettoyage sans execution | `SystemScanServiceTests.DeepScan_previews_cleanup_without_execution` | Pas de diagnostic pilote ni mises a jour | Ajouter categories detaillees au rapport |
| Rapport de scan | `VOIR LE DERNIER RAPPORT` | `MainWindow.ShowLastReport` | Operationnel | Etat global, RAM, disque, nettoyage potentiel, recommandations, erreurs | Aucune | Non | Bouton desactive sans rapport, overlay focusable | Couvert indirectement par tests scan | Rapport conserve uniquement en memoire | Export JSON ou texte date |
| Monitoring CPU | Rapport scan, metrique issue du scan | `ProcessorReader`, `MonitoringService` | Partiel | Mesure instantanee CPU via `GetSystemTimes`, nom processeur | Lecture compteur systeme et registre CPU | Non | Catch des echecs, statut N/A | `ScanRulesTests`, tests scan indirects | Pas de graphe temps reel, pas de top CPU | Module Ressources temps reel |
| Monitoring RAM | Rapport scan, carte memoire | `MemoryReader`, `MonitoringService` | Partiel | Snapshot RAM physique via `GlobalMemoryStatusEx`, seuils de severite | Lecture memoire uniquement | Non | Total 0 gere comme N/A | `ScanRulesTests.CalculateMemory*` | Pas de courbe temps reel ni detail processus | Module Ressources temps reel |
| Disques | Rapport scan, carte disque | `DiskReader`, `ScanRules` | Operationnel | Disques fixes accessibles, taux utilisation, disque systeme | Lecture `DriveInfo` uniquement | Non | Disques inaccessibles ignores avec erreur lisible | `ScanRulesTests.CalculateDisk*` | Pas de SMART, pas de verification disque | Ajouter diagnostic disque avance lecture seule |
| Reseau | Rapport scan | `NetworkReader` | Partiel | Interface active primaire, IPv4, passerelle, DNS, vitesse | Lecture interfaces reseau | Non | Loopback/tunnel exclus, fallback N/A | Tests indirects via scan | Pas de ping, DNS repair, Winsock, debit reel | Module reseau avance |
| Processus | Rapport approfondi | `ProcessReader` | Partiel | Top 10 processus par memoire, chemin si accessible | Lecture liste processus | Non | Processus proteges ignores, chemin inaccessible marque | Tests scan indirects | Pas de fin de processus, pas de CPU par processus | Vue processus dediee avec actions protegees |
| Nettoyage securise | Navigation `NETTOYAGE`, `ANALYSER LES ZONES`, `LANCER LE NETTOYAGE GUIDE` | `CleanupView`, `CleanupPreviewService`, `CleanupExecutionService` | Operationnel | Preview zones autorisees, validation par zone, execution guidee | Suppression de fichiers eligibles uniquement apres confirmation | Non | Zone root, refus hors zone, refus reparse point, preview expiree, annulation | `CleanupServicesTests` | Zones limitees: TEMP, CrashDumps, D3DSCache | Ajouter historique et restauration lorsque possible |
| Rapport de nettoyage | `VOIR LE DERNIER RAPPORT` nettoyage | `CleanupView.FormatReport`, `CleanupSessionReport` | Operationnel | Rapport session, zones, fichiers supprimes/ignores, erreurs | Aucune pendant affichage | Non | Rapport partiel en cas d'erreur, overlay fermable | `CleanupServicesTests.Execution_report_aggregates_results_and_errors` | Rapport non persiste | Export local date |
| Chatbox Virgil | Panneau `COMMUNICATION VIRGIL` | `MainWindow.AppendVirgilMessage` | Operationnel | Messages courts systeme, prefixe unique, scroll automatique | Aucune | Non | Normalisation `[VIRGIL]`, aucune saisie utilisateur | Couvert par revue code et build | Pas d'historique persistant | Journal local optionnel |
| Animation Virgil | `VirgilCoreControl` | `VirgilCoreAnimationController`, `VirgilMotionProfiles` | Operationnel | Noyau vectoriel, etats Idle/Scanning/Warning/Sensitive/Executing/Success/Error, communication | Aucune | Non | Respect animations Windows, stop storyboards, fallback statique | `VirgilCoreAnimationControllerTests` | Validation visuelle utilisateur encore requise | Test visuel manuel de la PR draft |
| Navigation | Colonne navigation | `MainWindow`, `ModulePlaceholder_Click` | Partiel | Accueil et nettoyage actifs, autres boutons annoncent preparation | Aucune | Non | Boutons clavier WPF, navigation bloquee pendant scan | Build WPF | La plupart des modules sont placeholders | Remplacer chaque placeholder par un module reel |
| Mises a jour Windows | `MISES A JOUR` | Aucun service | Placeholder | Bouton seulement | Aucune | Non | Message placeholder | Aucun | Aucune detection ni lancement Windows Update | Module mises a jour lecture et lancement controle |
| Mises a jour Winget | Aucun bouton dedie | Aucun service | Absent | Aucune | Aucune | Non | Aucun | Aucun | Pas d'appel winget | Inventaire winget lecture seule puis validation |
| Mises a jour des applications | `APPLICATIONS` placeholder | Aucun service | Placeholder | Bouton seulement | Aucune | Non | Message placeholder | Aucun | Pas d'inventaire apps, Store non gere | Inventaire applications installees |
| Pilotes | Aucun bouton dedie | Aucun service | Absent | Aucune | Aucune | Non | Aucun | Aucun | Pas de detection pilotes | Diagnostic lecture seule via sources fiables |
| Ressources | `RESSOURCES` placeholder | `MonitoringService` existe mais non raccorde a cette vue | Placeholder | Bouton seulement dans l'UI | Aucune | Non | Message placeholder | Aucun test UI du module | Service snapshot non expose en module | Creer vue ressources temps reel |
| Interventions ciblees | Aucun module actif | Aucun service | Absent | Aucune | Aucune | Non | Aucun | Aucun | Pas de reparations guidees | Construire catalogue d'interventions avec preview |
| Reseau avance | `RESEAU` placeholder | Aucun service avance | Placeholder | Bouton seulement | Aucune | Non | Message placeholder | Aucun | Pas de DNS, Winsock, renouvellement IP | Ajouter diagnostics et reparations validees |
| Reparations Windows | `REPARATION` placeholder | Aucun service | Placeholder | Bouton seulement | Aucune | Non | Message placeholder | Aucun | Pas de SFC, DISM, CHKDSK | Concevoir workflow avec confirmations et rapports |
| Desinstallation | `APPLICATIONS` placeholder | Aucun service | Absent | Aucune | Aucune | Non | Aucun | Aucun | Pas d'inventaire uninstall | Ajouter module applications avec garde-fous |
| Gestion des programmes au demarrage | `DEMARRAGE` placeholder | Aucun service | Placeholder | Bouton seulement | Aucune | Non | Message placeholder | Aucun | Pas de lecture ni modification startup | Ajouter lecture seule avant toute action |
| Historique des actions | Aucun | Aucun service | Absent | Aucune | Aucune | Non | Aucun | Aucun | Aucun journal persistant | Journal local sans telemetrie |
| Export de rapports | Aucun | Aucun service | Absent | Aucune | Aucune | Non | Aucun | Aucun | Rapports non exportables | Export JSON ou texte |
| Parametres | Aucun | Aucun service | Absent | Aucune | Aucune | Non | Aucun | Aucun | Pas de preferences utilisateur | Page parametres locale |
| Installateur | Aucun dans solution | Aucun projet d'installateur | Absent | Aucune | Aucune | Non | Aucun | Build/publish seulement | Pas de MSIX/MSI/installer | Choisir strategie d'installation |
| Mise a jour de Virgil | Aucun | Aucun service | Absent | Aucune | Aucune | Non | Aucun | Aucun | Pas d'auto-update | Versioning et canal de mise a jour |
| Restauration ou rollback | Aucun | Aucun service | Absent | Aucune | Aucune | Non | Aucun | Aucun | Pas de rollback cleanup ni configuration | Definir points de restauration par action |
| Take Ownership | Aucun | Aucun service | Absent | Aucune | Aucune | Non | Aucun | Aucun | Non implemente volontairement | Concevoir seulement en interventions ciblees avancees |

