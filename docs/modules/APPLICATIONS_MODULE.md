# Module Applications - Virgil 2.0

## Décision validée

Le module Applications permet d'analyser les applications installées, de les classer, de lancer une désinstallation propre lorsque c'est possible, puis de scanner les restes après désinstallation.

Aucune application et aucun reste ne doivent être supprimés sans validation explicite de l'utilisateur.

Virgil peut désinstaller une application uniquement si une méthode officielle est détectée.

## Objectif

Répondre aux questions :

```text
Quelles applications sont installées ?
Lesquelles prennent de la place ?
Lesquelles peuvent être désinstallées proprement ?
Reste-t-il des fichiers après désinstallation ?
```

Virgil ne doit pas décider seul qu'une application est inutile. Il peut signaler, classer et proposer. L'utilisateur choisit.

## Actions visibles du module

L'interface doit rester simple.

Actions principales visibles :

```text
[Analyser applications]
[Désinstaller proprement]
[Scanner les restes]
[Applications volumineuses]
```

Les actions détaillées apparaissent après analyse ou après sélection d'une application.

## 1. Analyser applications

### Type

Lecture seule.

### Données à récupérer

- Nom de l'application
- Éditeur
- Version
- Taille si disponible
- Date d'installation si disponible
- Source : classique / Store / système / inconnue
- Désinstalleur disponible ou non
- Applications volumineuses
- Applications avec éditeur inconnu ou à vérifier

### Message type

```text
[VIRGIL]
Analyse applications terminée.

Applications détectées : 86
Applications volumineuses : 7
Applications sans éditeur clair : 3
Applications Store : 12

Actions disponibles :
- Voir applications volumineuses
- Désinstaller proprement
- Scanner les restes
```

## 2. Désinstaller proprement

### Principe

Virgil doit utiliser une méthode officielle de désinstallation quand elle existe.

Virgil ne doit jamais désinstaller une application en supprimant simplement son dossier.

### Méthodes acceptées

| Type d'application | Méthode |
| --- | --- |
| Application classique EXE / MSI | Désinstalleur officiel Windows ou commande UninstallString fiable |
| Application installée via winget | Commande ou source compatible si disponible |
| Application Microsoft Store | Méthode Store / Windows si fiable |
| Application système Windows | Lecture seule ou avertissement, désinstallation non recommandée |
| Application portable | Pas de désinstallation automatique, analyse uniquement |

### Flux

```text
Choisir une application
   ↓
Afficher les détails
   ↓
Popup de validation
   ↓
Lancer le désinstalleur officiel
   ↓
Scanner les restes après désinstallation
   ↓
Afficher les restes trouvés
   ↓
Validation utilisateur
   ↓
Suppression des restes ou passage
   ↓
Rapport final
```

### Popup avant désinstallation

```text
[VIRGIL]
Désinstallation demandée.

Application : ExempleApp
Éditeur : Exemple Software
Version : 1.2.3
Source : application classique
Désinstalleur officiel : détecté

Virgil va lancer la procédure officielle de désinstallation.
Aucun reste ne sera supprimé sans validation séparée.

[CONFIRMER] [PASSER] [ANNULER TOUT]
```

## 3. Cas où Virgil ne doit pas désinstaller automatiquement

Virgil doit bloquer ou encadrer fortement :

- composants Windows critiques ;
- pilotes ;
- antivirus ;
- VPN ;
- logiciels de sécurité ;
- services système ;
- applications sans désinstalleur fiable ;
- applications portables ;
- outils constructeur sensibles.

### Message type

```text
[VIRGIL]
Désinstallation automatique non fiable.

Cette application ne fournit pas de méthode officielle claire.
Action recommandée : utiliser les paramètres Windows ou le désinstalleur officiel de l'éditeur.
```

## 4. Scanner les restes

### Quand scanner

- Après une désinstallation lancée par Virgil
- Manuellement pour une application déjà supprimée

### Zones à scanner

- Program Files
- Program Files (x86)
- ProgramData
- AppData Local
- AppData Roaming
- Bureau
- Menu Démarrer
- Raccourcis
- Tâches planifiées si accessible, lecture seule en V1
- Services liés si accessible, lecture seule en V1
- Registre plus tard, lecture seule et très encadré

### Message type

```text
[VIRGIL]
Scan des restes terminé.

Restes détectés :
- Dossier AppData : 320 Mo
- Raccourci Menu Démarrer : 1 élément
- Dossier ProgramData : 42 Mo

Risque : moyen
Validation requise avant suppression.
```

## 5. Supprimer les restes

### Principe

La suppression des restes est toujours étape par étape.

Aucun reste ne doit être supprimé automatiquement.

### Popup étape par étape

```text
[VIRGIL]
Restes détectés - étape 1/3

Zone : AppData Local
Chemin : C:\Users\...\AppData\Local\ExempleApp
Taille : 320 Mo
Risque : moyen

Cette suppression peut effacer des paramètres locaux restants.

[SUPPRIMER] [PASSER] [ANNULER TOUT]
```

## 6. Applications volumineuses

### Type

Lecture seule.

### Données affichées

- Nom
- Éditeur
- Taille
- Version
- Source
- Date d'installation si disponible

### Message type

```text
[VIRGIL]
Applications volumineuses détectées :

1. Microsoft Flight Simulator - 140 Go
2. Adobe Premiere Pro - 8,4 Go
3. Steam - 3,2 Go

Action disponible : examiner ou désinstaller proprement.
```

### Règle

Virgil ne dit jamais qu'une application est inutile.

Libellé autorisé :

```text
Application volumineuse détectée.
Désinstallation possible si elle n'est plus utilisée.
```

## 7. Applications rarement utilisées

### Statut

Prévu pour plus tard.

### Règle

Windows ne donne pas toujours une information fiable sur la dernière utilisation. Virgil ne doit donc pas affirmer qu'une application est inutilisée sans source fiable.

Libellé autorisé :

```text
Usage rarement détecté
```

Libellé interdit :

```text
Application inutile
```

## 8. Applications Store

### Actions possibles

- Lister les applications Store
- Afficher la source Store
- Ouvrir les paramètres Windows si nécessaire
- Désinstaller uniquement si une méthode fiable est disponible

## 9. Applications système

### Règle

Les applications système ou composants Windows doivent être marqués comme sensibles.

Message type :

```text
[VIRGIL]
Application système ou composant Windows.
Désinstallation non recommandée.
```

## 10. Registre

### Décision

Le registre est exclu en V1.

### Position

```text
V1 : pas de suppression registre
V2 : scan registre en lecture seule éventuellement
Plus tard : suppression uniquement avec popup critique
```

### Message type si intégré plus tard

```text
[VIRGIL]
Entrées registre potentiellement liées détectées.

Action critique.
Suppression non recommandée sans certitude.
```

## 11. Raccourcis cassés

### Actions possibles

- Scanner Bureau
- Scanner Menu Démarrer
- Détecter les raccourcis dont la cible n'existe plus
- Afficher la liste
- Supprimer après validation

### Popup simple

```text
[VIRGIL]
Raccourci cassé détecté.

Nom : ExempleApp
Cible : introuvable
Risque : faible

[SUPPRIMER] [PASSER] [ANNULER TOUT]
```

## 12. Rapport final

Après désinstallation ou nettoyage des restes, Virgil doit produire un rapport.

### Rapport type

```text
[VIRGIL]
Intervention applications terminée.

Application traitée : ExempleApp
Désinstalleur officiel : terminé
Restes détectés : 3
Restes supprimés : 2
Restes passés : 1
Espace libéré : 1,2 Go
Erreurs : 0
```

### Le rapport doit contenir

- Date
- Application concernée
- Désinstalleur utilisé ou non
- Restes détectés
- Restes supprimés
- Restes passés
- Espace libéré
- Erreurs
- Besoin redémarrage éventuel

## 13. Actions interdites

Virgil ne doit jamais :

- désinstaller sans validation ;
- supprimer des restes sans validation ;
- supprimer une application en supprimant simplement son dossier ;
- supprimer automatiquement des entrées registre ;
- désinstaller des composants Windows critiques ;
- qualifier une application de virus ;
- supprimer des dossiers AppData sans explication ;
- supprimer des données personnelles liées à une application sans avertissement.

## 14. Version 1

À inclure en V1 :

- analyser applications ;
- lister applications installées ;
- afficher applications volumineuses ;
- désinstaller via désinstalleur officiel ;
- scanner restes simples après désinstallation ;
- supprimer restes fichiers / dossiers après validation ;
- scanner raccourcis cassés ;
- générer rapport.

## 15. Plus tard

À prévoir plus tard :

- meilleure gestion des applications Store ;
- détection d'usage rarement utilisé ;
- tâches planifiées liées ;
- services liés en lecture seule ;
- scan des restes plus intelligent ;
- registre en lecture seule ;
- suppression registre avec popup critique ;
- analyse des profils applicatifs détaillés.

## 16. Règle produit

Le module Applications doit permettre une désinstallation propre, mais jamais agressive.

Virgil utilise les méthodes officielles, explique les risques, puis demande validation à chaque étape.
