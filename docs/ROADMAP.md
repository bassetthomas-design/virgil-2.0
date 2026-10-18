# Virgil 2.0 — Roadmap

Cette roadmap remplace les anciens jalons. L’[architecture](ARCHITECTURE.md) définit les responsabilités et la sécurité ; le [statut](PROJECT_STATUS.md) distingue l’existant des travaux validés. Chaque phase se termine par une validation GitHub Actions Windows et une revue de ses critères de sortie.

## Phase 0 — Fondations

Refactor sans nouvelle fonctionnalité. Sous-lots obligatoirement ordonnés, une PR par sous-lot :

| Sous-lot | Périmètre |
| --- | --- |
| 0a | Documentation initiale et suppression des workflows/scripts obsolètes (PR #27 fusionnée). |
| 0a.1 | Corrections documentaires, sans changement C#, csproj ou workflow. |
| 0b | Introduire Contracts et déplacer les types partagés de Domain vers Contracts ; les modèles d’animation vont dans App. |
| 0c | Extraire Infrastructure et centraliser les lancements de l’application derrière un seul `IProcessRunner`, en deux PR : 0c-1 (Infrastructure, IProcessRunner, lanceurs Updates/applications/helper) puis 0c-2 (ouverture d’emplacements, Explorer, vues, test de garde). |
| 0d | 0d-1 : composition Hosting/DI ; 0d-2 : Serilog, settings JSON et gestionnaire global d’exceptions ; 0d-3 : instance unique, manifeste `asInvoker` et `TreatWarningsAsErrors` ; 0d-4+ : vues minces et ViewModels CommunityToolkit.Mvvm injectés. |
| 0e | Unifier `RiskLevel`, `ActionResult`, `ConfirmedPlan` et `ConfirmationService`, avec tests de sécurité. |
| 0f | Faire en sorte que le helper ne référence que Contracts parmi les projets Virgil ; préserver son allowlist et ses validations. |
| 0g | Séparer les tests en `Virgil.Core.Tests` / `Virgil.App.Tests`, dont les tests d’animation dans App.Tests. |

Chaque lot préserve les protections existantes ; le code legacy sans rapport reste hors périmètre. Les critères ci-dessous portent sur la fin de la phase complète.

Critères de sortie :

- Documentation cohérente et fichiers obsolètes supprimés pour 0a, sans modification C#.
- Dépendances conformes à la cible, helper ne référençant que Contracts parmi les projets Virgil ; aucun appel OS direct dans Core.
- Vues minces, services et ViewModels injectés, aucun `new XxxService()` dans les vues/ViewModels ; logs Serilog, settings JSON et stockage des rapports dans Infrastructure.
- Contrats et validation de ConfirmedPlan disponibles et testés : expiration, hash incorrect, provenance invalide et absence d’effet après refus. Les protections existantes restent actives pendant la transition.
- Aucune nouvelle fonctionnalité produit ; comportements existants et tests de sécurité préservés, CI Windows verte.

## Phase 1 — Diagnostic + Nettoyage

Migrer Diagnostic et Nettoyage vers IMaintenanceModule/IMaintenanceAction : scan en lecture seule, constats, aperçu, confirmation et résultat.

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

Migrer les capacités existantes et livrer les parcours manquants avec le même contrat et les mêmes garanties.

Critères de sortie :

- Mises à jour, Applications, Démarrage et Réparations exposent scan, aperçu, confirmation et résultat via le contrat commun.
- Dépendances absentes (dont winget), échecs et droits insuffisants donnent des états explicites ; aucune commande libre transmise au helper.
- Sauvegarde vérifiée avant toute modification du démarrage et restauration testée ; échec de sauvegarde bloquant.
- Créer et vérifier un point de restauration uniquement avant les changements système : pilotes, réparations Windows via le helper, modifications du démarrage, du registre ou des services. Il n’est pas requis pour le nettoyage, protégé par quarantaine/corbeille, ni pour les actions sur les processus. Si la restauration système est désactivée ou limitée, expliquer la situation et proposer de l’activer ; ne permettre de continuer sans cette protection que via une confirmation de niveau Critique indiquant explicitement « aucun point de restauration ». Ne jamais ignorer cette situation en silence. Hors de cette dérogation explicite, un échec de création ou de vérification bloque le changement système.
- Tests des refus, désinstallations, protections des processus et allowlist conservés ou renforcés ; CI Windows verte.

## Phase 4 — Monitoring continu

Stabiliser le suivi CPU, RAM, disque, réseau et processus existant pour une observation continue maîtrisée.

Critères de sortie :

- Échantillonnage, annulation, erreurs de lecture, arrêt et reprise testés ; mesures cohérentes et informations indisponibles signalées.
- Fréquence et rétention bornées, consommation mesurée selon un budget documenté et validé ; absence de fuite lors d’une session prolongée.
- Le monitoring observe et recommande ; toute action passe par aperçu et ConfirmationService ; CI Windows verte.

## Phase 5 — Agent barre des tâches

Ajouter Virgil.Tray : présence discrète, notifications, ouverture du tableau de bord et démarrage optionnel avec Windows. L’agent n’agit jamais seul.

Critères de sortie :

- Cycle de vie, instance unique, fermeture et démarrage optionnel vérifiés ; arrêt effectif de l’observation à la demande.
- Notifications contrôlables et limitées ; accès clair aux constats et à l’application.
- Aucune maintenance déclenchée automatiquement : toute intention d’action ouvre le parcours de ConfirmationService dans App, avec tests de ce passage obligatoire ; CI Windows verte.

## Phase 6 — Personnalité

Ajouter avatar, messages `[VIRGIL]` et chat, sans élargir les pouvoirs de l’assistant.

Critères de sortie :

- États d’avatar et messages français cohérents avec les opérations réelles, sans annoncer une réussite avant le résultat.
- Le chat transforme les intentions reconnues en propositions du catalogue : aperçu puis ConfirmationService avant toute action.
- Intentions inconnues, ambiguës ou interdites refusées ou clarifiées ; aucun texte libre exécuté comme commande OS et aucun contournement du ConfirmedPlan.
- Tests des intentions et des refus, accessibilité de l’interface et CI Windows validées.
