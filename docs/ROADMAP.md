# Virgil 2.0 — Roadmap

Cette roadmap remplace les anciens jalons. L’[architecture](ARCHITECTURE.md) définit les responsabilités et la sécurité ; le [statut](PROJECT_STATUS.md) distingue l’existant des travaux validés. Chaque phase se termine par une validation GitHub Actions Windows et une revue de ses critères de sortie.

## Phase 0 — Fondations

Refactor sans nouvelle fonctionnalité. Phase 0a : supprimer les workflows/scripts obsolètes, documenter l’existant et la cible, ajouter les règles agents. Les lots suivants introduisent Contracts, séparent Core/Infrastructure, centralisent IProcessRunner, installent DI/Hosting, MVVM et ConfirmationService, unifient RiskLevel et préparent le protocole élevé.

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
- Nettoyage vers quarantaine/corbeille avec restauration vérifiée ; protections de chemins et classification conservées. Point de restauration vérifié avant action sensible, échec bloquant.
- Résultats, erreurs, annulation et rapports compréhensibles en français ; CI Windows verte.

## Phase 2 — Installateur Inno Setup + release v2.0.0-alpha

Distribuer une première version installable centrée sur Diagnostic et Nettoyage.

Critères de sortie :

- `installer/` contient la configuration Inno Setup ; la CI Windows produit l’installateur depuis une publication self-contained win-x64 avec le helper.
- Installation, lancement, demande d’élévation, redémarrage de l’application et désinstallation vérifiés sur Windows sans SDK .NET installé.
- Droits utilisateur standard testés ; données utilisateur, rapports et logs traités explicitement lors de la désinstallation.
- Tag et release `v2.0.0-alpha`, installateur téléchargeable, notes de version et limites connues publiés ; CI Windows verte.

## Phase 3 — Mises à jour, Applications, Démarrage, Réparations

Migrer les capacités existantes et livrer les parcours manquants avec le même contrat et les mêmes garanties.

Critères de sortie :

- Mises à jour, Applications, Démarrage et Réparations exposent scan, aperçu, confirmation et résultat via le contrat commun.
- Dépendances absentes (dont winget), échecs et droits insuffisants donnent des états explicites ; aucune commande libre transmise au helper.
- Sauvegarde vérifiée avant toute modification du démarrage et restauration testée ; point de restauration avant actions sensibles.
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
