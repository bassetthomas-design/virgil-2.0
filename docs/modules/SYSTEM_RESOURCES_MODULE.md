# Module Ressources système - Virgil 2.0

## Décision validée

Le module Ressources système gère la RAM, le CPU, les processus actifs, Explorer Windows, la mémoire inactive et les sessions Windows longues.

Le module ne doit contenir aucun mode jeu, aucun boost FPS et aucune optimisation gaming.

## Objectif

Aider l'utilisateur à comprendre pourquoi le PC ralentit et proposer des actions ciblées, contrôlées et validées.

Flux :

```text
Analyser
   ↓
Identifier les causes probables
   ↓
Proposer une action ciblée
   ↓
Demander confirmation si l'action modifie le système
   ↓
Exécuter si validé
   ↓
Rapport
```

## Actions visibles du module

L'interface doit rester simple.

Actions principales visibles :

```text
[Analyser ressources]
[Processus lourds]
[Libérer mémoire inactive]
[Relancer Explorer]
```

Les actions plus précises apparaissent après analyse ou dans le détail d'un processus.

## 1. Analyser ressources

### Type

Lecture seule.

### Éléments analysés

- RAM totale
- RAM utilisée
- RAM disponible
- Pourcentage RAM utilisé
- CPU instantané
- CPU moyen sur une courte période
- Processus les plus gourmands en RAM
- Processus les plus gourmands en CPU
- Session Windows / uptime
- État général des ressources

### Message type

```text
[VIRGIL]
Analyse ressources terminée.
RAM : 86 %
CPU : 34 %
3 processus lourds détectés.
```

## 2. Analyse RAM

### Données affichées

- RAM totale
- RAM utilisée
- RAM disponible
- Pourcentage utilisé
- Processus principaux consommateurs

### Seuils indicatifs

| Utilisation RAM | État |
| --- | --- |
| 0 à 69 % | Stable |
| 70 à 84 % | À surveiller |
| 85 à 94 % | Intervention conseillée |
| 95 % et plus | Critique |

### Message type

```text
[VIRGIL]
RAM élevée détectée.

Utilisation : 86 %
Processus principaux :
1. Chrome - 2,8 Go
2. Discord - 740 Mo
3. Steam - 620 Mo

Action recommandée : examiner les processus actifs.
```

## 3. Analyse CPU

### Données affichées

- CPU instantané
- CPU moyen sur quelques secondes
- Processus les plus consommateurs
- Activité anormale prolongée si détectable

### Règle

Virgil ne doit pas alerter pour un pic CPU très court.

Une alerte CPU doit s'appuyer sur une charge élevée maintenue pendant une courte observation.

### Message type

```text
[VIRGIL]
CPU élevé détecté sur la durée.

Processus principal : exemple.exe - 72 %
Action recommandée : surveiller le processus ou examiner les détails.
```

## 4. Processus lourds

### Type

Lecture seule par défaut.

### Données affichées

- Nom du processus
- Application associée si possible
- RAM utilisée
- CPU utilisé
- Éditeur si disponible
- Chemin si utile
- Statut : normal / lourd / à vérifier

### Actions disponibles

- Voir détails
- Ouvrir emplacement
- Fermer proprement après validation
- Forcer fermeture avec confirmation renforcée
- Ignorer

### Règle

Virgil ne doit pas qualifier un processus de virus.

Le libellé autorisé est :

```text
à vérifier
```

Virgil n'est pas un antivirus.

## 5. Fermer une application sélectionnée

### Niveau de risque

Faible à moyen selon l'application.

### Règle

Virgil ne ferme jamais une application sans choix explicite de l'utilisateur.

### Popup simple

```text
[VIRGIL]
Fermeture d'application demandée.

Application : Discord
Risque : faible
Effet : l'application sera fermée.
Données non sauvegardées : possible selon l'application.

[EXÉCUTER] [PASSER] [ANNULER TOUT]
```

## 6. Forcer la fermeture

### Niveau de risque

Moyen.

### Règle

À utiliser uniquement si l'application ne répond pas ou si l'utilisateur le demande explicitement.

### Popup renforcée

```text
[VIRGIL]
Fermeture forcée demandée.

Cette action peut entraîner une perte de données non sauvegardées.
À utiliser seulement si l'application ne répond plus.

[JE CONFIRME] [PASSER] [ANNULER TOUT]
```

## 7. Libérer mémoire inactive

### Nom validé

```text
Libérer mémoire inactive
```

### Noms interdits

```text
Boost RAM
Optimisation RAM magique
Accélération mémoire
```

### Objectif

Tenter de récupérer temporairement une partie de la mémoire inactive.

### Message obligatoire

```text
[VIRGIL]
Libération mémoire inactive disponible.

Cette action peut récupérer temporairement une partie de la mémoire inactive.
Elle ne remplace pas la fermeture des applications lourdes.

[EXÉCUTER] [PASSER] [ANNULER TOUT]
```

### Niveau de risque

Faible à moyen.

### Popup

Popup simple.

## 8. Relancer Explorer Windows

### Objectif

Relancer Explorer Windows si le bureau, la barre des tâches ou l'explorateur deviennent instables.

### Cas d'utilisation

- Barre des tâches bloquée
- Bureau lent
- Explorateur Windows instable
- Menus Windows figés
- Icônes qui ne se chargent pas

### Popup simple

```text
[VIRGIL]
Relance d'Explorer Windows disponible.

Le bureau et la barre des tâches peuvent disparaître quelques secondes.
Les applications ouvertes ne seront pas fermées.

[EXÉCUTER] [PASSER] [ANNULER TOUT]
```

### Niveau de risque

Faible.

## 9. Session Windows trop longue

### Type

Recommandation uniquement en V1.

### Règle

Virgil peut conseiller un redémarrage si la session Windows est active depuis longtemps.

Virgil ne doit pas redémarrer automatiquement le PC en V1.

### Message type

```text
[VIRGIL]
Session Windows active depuis 9 jours.

Certains ralentissements peuvent venir d'une session trop longue.
Action recommandée : redémarrage manuel.
```

## 10. Applications bloquées

### Statut

Prévu pour version avancée.

### Actions possibles

- Détecter une application non réactive
- Proposer d'attendre
- Fermer proprement
- Forcer fermeture avec confirmation renforcée

### Message type

```text
[VIRGIL]
Application non réactive détectée.

Action recommandée : attendre quelques secondes ou fermer proprement.
La fermeture forcée peut entraîner une perte de données.
```

## 11. Actions exclues

Virgil ne doit pas proposer :

- mode jeu ;
- boost FPS ;
- overclock ;
- modification agressive du plan d'alimentation ;
- fermeture automatique d'applications sans choix ;
- désactivation automatique de services Windows ;
- optimisation magique RAM ;
- arrêt de processus système critiques.

## 12. Version 1

À inclure en V1 :

- analyser RAM ;
- analyser CPU ;
- lister processus lourds ;
- fermer une application sélectionnée ;
- relancer Explorer ;
- libérer mémoire inactive ;
- conseiller redémarrage si session longue ;
- générer un rapport d'action.

## 13. Plus tard

À prévoir plus tard :

- détection automatique d'application bloquée ;
- surveillance courte sur 30 secondes ;
- historique consommation RAM / CPU ;
- comparaison avant / après action ;
- rappel redémarrage ;
- analyse plus détaillée par éditeur et chemin d'application.

## 14. Règle produit

Le module Ressources système doit aider l'utilisateur à comprendre et corriger les ralentissements sans jamais promettre d'accélération magique.

Virgil agit comme un agent tactique prudent, pas comme un booster PC marketing.

## 15. État de l'implémentation V1

Implémenté :

- bouton `RESSOURCES` raccordé à `ResourcesView` ;
- observation CPU multi-échantillons et moyenne courte, sans alerte critique sur un pic isolé ;
- lecture RAM totale, utilisée, disponible et seuils validés ;
- principaux consommateurs CPU/RAM avec statut normal, lourd, à vérifier ou protégé ;
- fermeture propre après validation, sans escalade automatique ;
- fermeture forcée séparée avec confirmation renforcée ;
- revérification du nom, du chemin et de l'heure de démarrage du PID avant fermeture ;
- protection conservative des processus Windows, sécurité, VPN, matériel, inaccessibles et du processus Virgil ;
- relance d'Explorer via le service d'intervention existant ;
- rapport de session avec analyses, propositions, actions, actions passées et erreurs ;
- prévisualisation Ressources dans l'analyse approfondie, strictement en lecture seule.

Limitation V1 assumée :

- `Libérer mémoire inactive` affiche une information seulement. Aucune API suffisamment fiable et cohérente avec les garde-fous n'est exécutée ;
- aucun redémarrage automatique, aucun mode jeu, aucun service Windows désactivé et aucun « boost RAM ».
