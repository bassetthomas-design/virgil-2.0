# Module Rapports / historique - Virgil 2.0

## Décision validée

Le module Rapports / historique permet à Virgil de conserver une trace locale claire des scans, recommandations, actions exécutées, actions passées, erreurs et résultats.

Virgil doit toujours pouvoir expliquer ce qu'il a analysé, proposé et réalisé.

Aucun rapport ne doit être envoyé en ligne.

## Objectif

Répondre aux questions :

```text
Qu'est-ce que Virgil a trouvé ?
Qu'est-ce que Virgil a fait ?
Qu'est-ce que l'utilisateur a refusé ou passé ?
Qu'est-ce qui a échoué ?
Peut-on relire ou exporter le rapport ?
```

## Actions visibles du module

L'interface doit rester simple.

Actions principales visibles :

```text
[Dernier rapport]
[Historique]
[Exporter rapport]
[Comparer deux scans]
```

## 1. Dernier rapport

### Rôle

Afficher le dernier rapport généré par un scan ou une action.

### Message type

```text
[VIRGIL]
Dernier rapport disponible.

Type : Scan complet approfondi
État général : À surveiller
Priorités détectées : 3
Actions proposées : 5
Actions exécutées : 0
```

### Règle

Le dernier rapport doit être accessible depuis l'écran principal après un scan ou une intervention.

## 2. Types de rapports

Virgil doit générer un rapport pour :

- Scan rapide
- Analyse approfondie
- Nettoyage
- Ressources système
- Applications
- Mises à jour
- Démarrage Windows
- Réseau
- Réparation Windows

Chaque module produit son propre rapport, mais tous doivent suivre une structure commune.

## 3. Structure standard d'un rapport

Chaque rapport doit contenir :

- Date et heure
- Type d'action
- État avant action si disponible
- Actions proposées
- Actions exécutées
- Actions passées
- Erreurs
- Résultat final
- Redémarrage requis : oui / non
- Niveau de risque si applicable

### Exemple - Nettoyage

```text
[VIRGIL]
Rapport nettoyage.

Date : 19/05/2026 - 18:42
Mode : Nettoyage étape par étape

Actions exécutées :
- TEMP utilisateur : 842 Mo libérés
- Miniatures : 120 Mo libérés

Actions passées :
- Cache navigateur
- Corbeille

Erreurs : 0
Total libéré : 962 Mo
Redémarrage requis : non
```

## 4. Historique

### Éléments conservés

Virgil doit conserver localement :

- Derniers scans
- Derniers nettoyages
- Dernières mises à jour
- Dernières désinstallations
- Dernières réparations Windows
- Dernières actions réseau
- Erreurs rencontrées
- Actions annulées
- Actions passées

### Quantité par défaut

Conserver par défaut :

```text
30 derniers événements
```

### Options futures

- Nettoyer l'historique
- Exporter tout l'historique
- Filtrer par module
- Rechercher dans l'historique

## 5. Confidentialité

Les rapports peuvent contenir des noms d'applications, chemins, fichiers ou informations réseau.

Virgil ne doit pas stocker inutilement :

- mots de passe ;
- cookies ;
- contenu personnel ;
- détails réseau trop sensibles ;
- clés de licence ;
- tokens ;
- données inutiles au diagnostic.

### Règle d'affichage des chemins

Virgil doit afficher le minimum utile par défaut.

Exemple préféré :

```text
Téléchargements\Windows11.iso
```

Le chemin complet doit être disponible seulement dans les détails techniques si nécessaire.

## 6. Exporter rapport

### Version 1

Format disponible :

```text
.txt
```

### Version 2

Formats possibles :

```text
.md
.json
```

### Règle

L'export doit être déclenché par l'utilisateur.

Virgil ne doit pas exporter ou transmettre un rapport automatiquement.

## 7. Comparer deux scans

### Statut

Prévu en V2.

### Objectif

Comparer deux scans pour montrer l'évolution du PC.

### Exemple

```text
[VIRGIL]
Comparaison des scans.

Scan précédent :
RAM : 76 %
Disque C: 88 %

Scan actuel :
RAM : 61 %
Disque C: 72 %

Évolution :
- RAM améliorée
- Stockage libéré : 18 Go
```

## 8. Rapport après action échouée

### Règle

Virgil ne doit jamais afficher uniquement :

```text
Erreur.
```

Il doit expliquer l'action, la cause probable et l'action suivante possible.

### Message type

```text
[VIRGIL]
Action échouée.

Action : mise à jour VLC
Cause probable : accès refusé
Action recommandée : relancer avec droits administrateur ou passer cette étape.
```

### Champs à inclure

- Statut : échec
- Cause probable
- Code erreur en détails techniques si disponible
- Action suivante proposée

## 9. Journal en direct

Pendant un scan ou une intervention, Virgil doit afficher un journal court.

### Exemple

```text
[VIRGIL] Analyse démarrée.
[VIRGIL] Lecture RAM terminée.
[VIRGIL] Scan stockage en cours.
[VIRGIL] 3 actions recommandées.
```

### Règle

Le journal ne doit pas afficher des logs bruts par défaut.

Les erreurs techniques doivent être masquées derrière :

```text
[Voir détails techniques]
```

## 10. Niveaux de détail

Virgil doit proposer deux niveaux de lecture.

### Vue simple

- Action
- Résultat
- Espace libéré si applicable
- Erreur éventuelle
- Redémarrage requis ou non

### Détails techniques

- Chemins
- Codes erreur
- Source
- Durée
- Journal complet
- Commande utilisée si pertinent

### Règle

La vue simple est affichée par défaut.

Les détails techniques sont accessibles uniquement si l'utilisateur les ouvre.

## 11. Ce que Virgil ne doit pas faire

Virgil ne doit jamais :

- enregistrer des infos sensibles inutiles ;
- masquer les erreurs ;
- écraser l'historique sans prévenir ;
- afficher des logs incompréhensibles par défaut ;
- exporter des données personnelles sans validation ;
- envoyer des rapports en ligne ;
- synchroniser l'historique sans consentement explicite.

## 12. Stockage local

Les rapports et l'historique doivent rester locaux.

Chemin cible à confirmer à l'implémentation :

```text
%APPDATA%\Virgil\reports
%APPDATA%\Virgil\logs
```

## 13. Version 1

À inclure en V1 :

- dernier rapport ;
- rapport par module ;
- journal court ;
- historique des 30 derniers événements ;
- export .txt ;
- erreurs lisibles ;
- détails techniques masqués par défaut ;
- stockage local uniquement.

## 14. Version 2

À prévoir en V2 :

- export .md ;
- comparaison de deux scans ;
- filtres par module ;
- recherche dans l'historique ;
- nettoyage de l'historique ;
- meilleure présentation des erreurs récurrentes.

## 15. Plus tard

À prévoir plus tard :

- export .json ;
- tableau d'évolution ;
- graphiques simples ;
- résumé mensuel local ;
- comparaison avant / après par module.

## 16. Règle produit

Le module Rapports / historique est la mémoire locale de Virgil.

Virgil doit être capable d'expliquer ce qu'il a fait, ce qu'il n'a pas fait, ce qui a été passé, ce qui a échoué et pourquoi.
