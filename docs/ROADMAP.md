# Virgil 2.0 — Roadmap

Cette roadmap remplace les anciens jalons. L’[architecture](ARCHITECTURE.md) définit les responsabilités et la sécurité ; le [statut](PROJECT_STATUS.md) distingue l’existant des travaux validés. Les spécifications produit figurent dans [ACTIONS.md](ACTIONS.md). Chaque phase se termine par une validation GitHub Actions Windows et une revue de ses critères de sortie.

## Phase 0 — Fondations

Refactor sans nouvelle fonctionnalité. Sous-lots obligatoirement ordonnés, une PR par sous-lot :

| Sous-lot | Périmètre |
| --- | --- |
| 0a | Documentation initiale et suppression des workflows/scripts obsolètes (PR #27 fusionnée). |
| 0a.1 | Corrections documentaires, sans changement C#, csproj ou workflow. |
| 0b | Introduire Contracts et déplacer les types partagés de Domain vers Contracts ; les modèles d’animation vont dans App. |
| 0c | Extraire Infrastructure et centraliser les lancements de l’application derrière un seul `IProcessRunner`, en deux PR : 0c-1 (Infrastructure, IProcessRunner, lanceurs Updates/applications/helper) puis 0c-2 (ouverture d’emplacements, Explorer, vues, test de garde). |
| 0d | 0d-1 : composition Hosting/DI ; 0d-2 : Serilog, settings JSON et gestionnaire global d’exceptions ; 0d-3 : instance unique, manifeste `asInvoker` et `TreatWarningsAsErrors` ; 0d-4+ : vues minces et ViewModels CommunityToolkit.Mvvm injectés. |
| 0e-1 | Livrer les contrats `RiskLevel`, `ActionPreview`, `ActionResult`, le `ConfirmedPlan` dans Core et la porte unique `ConfirmationService` avec tests de sécurité ; aucun branchement ni changement de comportement des modules. |
| 0e-2 | Livré (PR #43) : correspondances pures des quatre enums vers RiskLevel, registre de confirmation borné, pilote de désinstallation Applications sur la porte ; dialogues et politiques conservés. |
| 0e-3 | Livré (PR #44) : Ressources et Mises à jour exigent ConfirmedPlan après les overlays existants ; gardes avant la porte, usage unique avant effet. |
| 0e-4 | Livré (PR #45) : Interventions et Nettoyage sur ConfirmedPlan, gardes conservés et RequiresReinforcedConfirmation indépendant de l’enum ; lot 0e terminé. 0f livré (PR #47) ; 0g livré (PR #48). |
| 0f | Livré (PR #47) : helper limité à Contracts, gardes de chemins et racine du protocole partagés ; allowlist et validations préservées. |
| 0f-bis | Livré (PR #49) : gardes de chemins purs dans Contracts ; disque, dossiers, ACL et fichiers du protocole client dans Infrastructure derrière les ports Core, composition Hosting ; helper inchangé limité à Contracts. |
| 0g | Livré (PR #48) : tests séparés en `Virgil.Core.Tests` (sans App) et `Virgil.App.Tests` (dont animation), aides non dupliquées et deux tests d’architecture ; 746 cas existants préservés. |

Chaque lot préserve les protections existantes ; le code legacy sans rapport reste hors périmètre. Les critères ci-dessous portent sur la fin de la phase complète.

Critères de sortie :

- Documentation cohérente et fichiers obsolètes supprimés pour 0a, sans modification C#.
- Dépendances conformes à la cible, helper ne référençant que Contracts parmi les projets Virgil ; aucun appel OS direct dans Core.
- Vues minces, services et ViewModels injectés, aucun `new XxxService()` dans les vues/ViewModels ; logs Serilog, settings JSON et stockage des rapports dans Infrastructure.
- Contrats et validation de ConfirmedPlan disponibles et testés : expiration, hash incorrect, provenance invalide et absence d’effet après refus. Les protections existantes restent actives pendant la transition.
- Aucune nouvelle fonctionnalité produit ; comportements existants et tests de sécurité préservés, CI Windows verte.

## Phase 1 — Diagnostic + Nettoyage

Actions concernées : [Action 1 — Diagnostic](ACTIONS.md#action-1--diagnostic-validee-2026-10-18), [Action 2 — Nettoyage](ACTIONS.md#action-2--nettoyage-validee-2026-10-18), [Action 6 — Ressources fusionnée dans le Diagnostic](ACTIONS.md#action-6--ressources---fusionnee-dans-laction-1-2026-10-18) ; lien avec les restes après désinstallation de l’[Action 4 — Applications](ACTIONS.md#action-4--applications-validee-2026-10-18).

Migrer Diagnostic et Nettoyage vers IMaintenanceModule/IMaintenanceAction : scan en lecture seule, constats, aperçu, confirmation et résultat.

| Sous-lot | Périmètre |
| --- | --- |
| 1a | Contrat commun `Findings` / `IMaintenanceModule` / `IMaintenanceAction` et Nettoyage branché dessus, à comportement identique ; Diagnostic à venir. |
| 1b | Livré : quarantaine de 7 jours réglable (1–30), restauration taille/SHA-256 vérifiée, purge distincte confirmée ; expiration = proposition de purge confirmée, jamais effacement silencieux. |
| 1c | Scan en trois groupes : Sûr (coché), À vérifier (décoché), Restes de logiciels désinstallés (taille, fichiers et clés de registre) ; garde-fous sur les dossiers personnels. |
| 1d | Module Diagnostic : vue détaillée CPU, RAM, disques, réseau, GPU et processus, en lecture seule. |
| 1e | Analyse « ce qui ne va pas » : note, top 3 et actions proposées après validation, dont fermeture de processus protégée. |

1a conserve le parcours existant de suppression/corbeille et les protections ; quarantaine, nouveaux groupes, restes de logiciels, Diagnostic et nouvelle UI restent hors de ce sous-lot.


Critères de sortie :

- Les deux modules utilisent le contrat commun et l’unique RiskLevel ; aucune exécution destructive via un simple booléen de confirmation.
- Plan absent, expiré ou différent de l’aperçu refusé ; tests prouvant qu’aucune écriture destructive ne suit le refus.
- Nettoyage vers quarantaine/corbeille avec restauration vérifiée ; protections de chemins et classification conservées. Aucun point de restauration requis pour le nettoyage ni pour les actions sur les processus.
- Créer et vérifier un point de restauration uniquement avant les changements système : pilotes, réparations Windows via le helper, modifications du démarrage, du registre ou des services. Il n’est pas requis pour le nettoyage, protégé par quarantaine/corbeille, ni pour les actions sur les processus. Si la restauration système est désactivée ou limitée, expliquer la situation et proposer de l’activer ; ne permettre de continuer sans cette protection que via une confirmation de niveau Critique indiquant explicitement « aucun point de restauration ». Ne jamais ignorer cette situation en silence. Hors de cette dérogation explicite, un échec de création ou de vérification bloque le changement système.
- Résultats, erreurs, annulation et rapports compréhensibles en français ; CI Windows verte.

## Phase 2 — Installateur Inno Setup + release v2.0.0-alpha

Distribuer une première version installable centrée sur Diagnostic et Nettoyage.

Critères de sortie :

- `installer/` contient la configuration Inno Setup ; la CI Windows produit l’installateur depuis une publication self-contained win-x64 avec le helper.
- Installation par machine dans `Program Files` ; le helper ne doit jamais résider dans un dossier modifiable par l’utilisateur standard.
- Version issue du tag git et workflow de release déclenché sur les tags `v*` ; aucune modification de workflow dans le lot documentaire 0a.1.
- Signature de code requise, ou exception pour une alpha non signée explicitement acceptée et documentée dans les notes de version.
- Vérification des mises à jour de Virgil via GitHub Releases, avec état explicite en cas d’indisponibilité.
- Toute action legacy non migrée vers ConfirmedPlan est désactivée ou bloquée à l’exécution, y compris ses entrées UI, chat et tray ; tests prouvant l’absence de contournement avant publication de l’alpha.
- Installation, lancement, demande d’élévation, redémarrage de l’application et désinstallation vérifiés sur Windows sans SDK .NET installé.
- Droits utilisateur standard testés ; données utilisateur, rapports et logs traités explicitement lors de la désinstallation.
- Tag et release `v2.0.0-alpha`, installateur téléchargeable, notes de version et limites connues publiés ; CI Windows verte.

## Phase 3 — Mises à jour, Applications, Démarrage, Réparations

Actions concernées : [Action 0 — Tout mettre à jour](ACTIONS.md#action-0--tout-mettre-a-jour-exigence), [Action 3 — Mises à jour](ACTIONS.md#action-3--mises-a-jour-pack-all-inclusive-validee-2026-10-18), [Action 4 — Applications](ACTIONS.md#action-4--applications-validee-2026-10-18), [Action 5 — Réparations](ACTIONS.md#action-5--reparations-validee-2026-10-18), [Action 7 — Démarrage](ACTIONS.md#action-7--demarrage-validee-2026-10-18).

Migrer les capacités existantes et livrer les parcours manquants avec le même contrat et les mêmes garanties.

Critères de sortie :

- Mises à jour, Applications, Démarrage et Réparations exposent scan, aperçu, confirmation et résultat via le contrat commun.
- Dépendances absentes (dont winget), échecs et droits insuffisants donnent des états explicites ; aucune commande libre transmise au helper.
- Sauvegarde vérifiée avant toute modification du démarrage et restauration testée ; échec de sauvegarde bloquant.
- Créer et vérifier un point de restauration uniquement avant les changements système : pilotes, réparations Windows via le helper, modifications du démarrage, du registre ou des services. Il n’est pas requis pour le nettoyage, protégé par quarantaine/corbeille, ni pour les actions sur les processus. Si la restauration système est désactivée ou limitée, expliquer la situation et proposer de l’activer ; ne permettre de continuer sans cette protection que via une confirmation de niveau Critique indiquant explicitement « aucun point de restauration ». Ne jamais ignorer cette situation en silence. Hors de cette dérogation explicite, un échec de création ou de vérification bloque le changement système.
- Tests des refus, désinstallations, protections des processus et allowlist conservés ou renforcés ; CI Windows verte.

## Phase 4 — Monitoring continu

Actions concernées : [Action 1 — Diagnostic : vue en direct continue et historique](ACTIONS.md#action-1--diagnostic-validee-2026-10-18), [Action 8 — Historique (priorité basse)](ACTIONS.md#action-8--historique-validee-priorite-basse).

Stabiliser le suivi CPU, RAM, disque, réseau et processus existant pour une observation continue maîtrisée.

Critères de sortie :

- Échantillonnage, annulation, erreurs de lecture, arrêt et reprise testés ; mesures cohérentes et informations indisponibles signalées.
- Fréquence et rétention bornées, consommation mesurée selon un budget documenté et validé ; absence de fuite lors d’une session prolongée.
- Le monitoring observe et recommande ; toute action passe par aperçu et ConfirmationService ; CI Windows verte.

## Phase 5 — Agent barre des tâches

Actions concernées : [Action 9 — Surveillance / Tray](ACTIONS.md#action-9--surveillance-en-arriere-plan--tray-validee-2026-10-18).

Ajouter Virgil.Tray : présence discrète, notifications, ouverture du tableau de bord et démarrage optionnel avec Windows. L’agent n’agit jamais seul.

Critères de sortie :

- Cycle de vie, instance unique, fermeture et démarrage optionnel vérifiés ; arrêt effectif de l’observation à la demande.
- Notifications contrôlables et limitées ; accès clair aux constats et à l’application.
- Aucune maintenance déclenchée automatiquement : toute intention d’action ouvre le parcours de ConfirmationService dans App, avec tests de ce passage obligatoire ; CI Windows verte.

## Phase 6 — Personnalité

Actions concernées : [Action 10 — Personnalité](ACTIONS.md#action-10--personnalite-validee-2026-10-18-bonus).

Ajouter avatar, messages `[VIRGIL]` et chat, sans élargir les pouvoirs de l’assistant.

Critères de sortie :

- États d’avatar et messages français cohérents avec les opérations réelles, sans annoncer une réussite avant le résultat.
- Le chat transforme les intentions reconnues en propositions du catalogue : aperçu puis ConfirmationService avant toute action.
- Intentions inconnues, ambiguës ou interdites refusées ou clarifiées ; aucun texte libre exécuté comme commande OS et aucun contournement du ConfirmedPlan.
- Tests des intentions et des refus, accessibilité de l’interface et CI Windows validées.

1b livré : section Quarantaine minimale dans Nettoyage ; les trois groupes, restes de logiciels et Diagnostic restent à livrer dans les lots suivants. La CI Windows de la PR est la référence de validation.
