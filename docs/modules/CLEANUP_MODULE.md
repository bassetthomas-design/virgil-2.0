# Module Nettoyage - Virgil 2.0

## Décision validée

Le module Nettoyage doit être complet, mais contrôlé étape par étape.

Virgil commence toujours par une analyse. Ensuite, chaque zone nettoyable est présentée dans une popup de validation. L'utilisateur peut exécuter l'action, la passer ou annuler toute la séquence.

## Principe général

```text
Analyse nettoyage
   ↓
Liste des zones nettoyables
   ↓
Popup étape par étape
   ↓
Exécuter / Passer / Annuler tout
   ↓
Suite de la séquence
   ↓
Rapport final
```

## Objectif

Permettre un nettoyage très complet sans risque d'action cachée ou automatique.

Virgil doit toujours expliquer :

- ce qu'il va analyser ;
- ce qu'il va supprimer ;
- où il va travailler ;
- le niveau de risque ;
- l'espace estimé ;
- si des droits administrateur sont nécessaires ;
- si un redémarrage peut être requis.

## Interface principale du module

Le module Nettoyage ne doit pas afficher une forêt de boutons.

Actions visibles :

```text
[Analyser le nettoyage]
[Nettoyage sûr]
[Nettoyage avancé]
[Analyse stockage]
[Options avancées]
```

## 1. Analyser le nettoyage

### Type

Lecture seule.

### Rôle

Scanner toutes les zones nettoyables et estimer l'espace récupérable.

### Zones analysées

- TEMP utilisateur
- TEMP Windows
- Corbeille
- Miniatures Windows
- Cache d'aperçu
- Caches navigateurs
- Cache Windows Update
- Logs anciens
- Rapports d'erreur
- Fichiers crash
- Dumps mémoire
- Restes d'installation
- Fichiers temporaires d'installateurs
- Gros fichiers
- Gros dossiers
- Téléchargements
- Bureau
- Archives lourdes
- Images ISO
- Installateurs anciens

### Message type

```text
[VIRGIL]
Analyse nettoyage terminée.
12 zones nettoyables détectées.
Espace récupérable estimé : 4,8 Go.
Aucune action effectuée.
```

## 2. Nettoyage sûr

### Niveau de risque

Faible.

### Actions incluses

- Fichiers temporaires utilisateur
- Miniatures Windows
- Cache d'aperçu
- Logs utilisateur simples
- Rapports d'erreur anciens simples

### Popup simple

```text
[VIRGIL]
Étape 1/5 - Fichiers temporaires utilisateur

Zone concernée : TEMP utilisateur
Impact estimé : 842 Mo récupérables
Risque : faible
Droits administrateur : non
Redémarrage : non

Action : supprimer les fichiers temporaires non verrouillés.

[EXÉCUTER] [PASSER] [ANNULER TOUT]
```

## 3. Corbeille

### Niveau de risque

Faible à moyen.

### Règle

La corbeille doit être présentée séparément, car elle peut contenir des fichiers que l'utilisateur souhaite récupérer.

### Popup

```text
[VIRGIL]
Étape X/Y - Corbeille Windows

Zone concernée : Corbeille
Impact estimé : 1,2 Go récupérables
Risque : moyen
Droits administrateur : non
Redémarrage : non

Attention : les éléments vidés ne seront plus récupérables facilement.

[EXÉCUTER] [PASSER] [ANNULER TOUT]
```

## 4. Nettoyage avancé

### Niveau de risque

Moyen.

### Actions incluses

- Fichiers temporaires Windows
- Cache Windows Update
- Logs système anciens
- Rapports d'erreur Windows
- Fichiers crash
- Dumps mémoire
- Restes d'installation
- Fichiers temporaires d'installateurs

### Popup renforcée

```text
[VIRGIL]
Étape X/Y - Cache Windows Update

Zone concernée : cache Windows Update
Impact estimé : 2,1 Go récupérables
Risque : moyen
Droits administrateur : possible
Redémarrage : non requis généralement

Certains fichiers peuvent être utiles au diagnostic ou à la restauration.
Virgil recommande cette action si l'espace disque est insuffisant.

[CONFIRMER] [PASSER] [ANNULER TOUT]
```

## 5. Navigateurs

### Niveau de risque

Moyen.

### Navigateurs ciblés

- Edge
- Chrome
- Firefox
- Brave
- Opera

### Règle V1

Nettoyer uniquement les caches.

Ne jamais supprimer automatiquement :

- mots de passe ;
- sessions ;
- favoris ;
- cookies ;
- historique.

Cookies et historique pourront être proposés plus tard avec choix séparé.

### Popup

```text
[VIRGIL]
Étape X/Y - Cache navigateur

Navigateur : Edge
Impact estimé : 650 Mo récupérables
Risque : moyen
Droits administrateur : non
Redémarrage : non

Les sites peuvent se charger plus lentement au prochain lancement.
Mots de passe, sessions, favoris et cookies ne sont pas ciblés.

[CONFIRMER] [PASSER] [ANNULER TOUT]
```

## 6. Analyse stockage

### Type

Lecture seule.

### Rôle

Identifier ce qui occupe beaucoup d'espace sans supprimer.

### Actions incluses

- Gros fichiers
- Gros dossiers
- Téléchargements
- Bureau
- Documents lourds
- Vidéos lourdes
- Archives .zip / .rar / .7z
- Images ISO
- Installateurs anciens

### Règle

Virgil ne supprime pas automatiquement les fichiers personnels.

Il peut afficher, ouvrir l'emplacement, ou proposer d'ignorer.

### Message type

```text
[VIRGIL]
Analyse stockage terminée.

Éléments volumineux détectés :
1. Téléchargements : 18 Go
2. Vidéos : 42 Go
3. Archives : 9 Go

Action recommandée : examiner les fichiers volumineux.
```

## 7. Caches applicatifs

### Statut

Prévu pour version avancée.

### Actions possibles

- Cache Discord
- Cache Steam
- Cache Epic Games
- Cache NVIDIA
- Cache AMD
- Cache launchers
- Cache Teams
- Cache Spotify

### Niveau de risque

Moyen, avec popup renforcée.

## 8. Caches spécialisés

### Statut

Prévu pour version avancée.

### Actions possibles

- Cache shaders GPU
- Anciens packages pilotes
- Fichiers upgrade Windows
- Dossiers vides résiduels
- Raccourcis cassés

### Niveau de risque

Moyen à élevé selon action.

## 9. Actions à éviter

Virgil ne doit pas proposer en automatique :

- nettoyage registre automatique ;
- suppression automatique de doublons ;
- suppression automatique dans Documents ;
- suppression automatique dans Téléchargements ;
- suppression mots de passe navigateur ;
- suppression sessions navigateur ;
- suppression cookies sans choix détaillé.

### Doublons

Virgil peut analyser les doublons plus tard, mais pas les supprimer automatiquement.

## 10. Boutons standards des popups

### Action simple

```text
[EXÉCUTER] [PASSER] [ANNULER TOUT]
```

### Action sensible

```text
[CONFIRMER] [PASSER] [ANNULER TOUT]
```

### Action critique

```text
[JE COMPRENDS ET JE CONFIRME] [PASSER] [ANNULER TOUT]
```

## 11. Rapport final

À la fin d'une séquence de nettoyage, Virgil affiche un rapport.

### Rapport type

```text
[VIRGIL]
Nettoyage terminé.

Actions exécutées :
- TEMP utilisateur : 842 Mo libérés
- Miniatures : 120 Mo libérés
- Corbeille : passée
- Cache Edge : 650 Mo libérés
- Cache Windows Update : passé

Total libéré : 1,6 Go
Actions exécutées : 3
Actions passées : 2
Erreurs : 0
```

### Le rapport doit contenir

- Date
- Mode utilisé
- Actions exécutées
- Actions passées
- Espace libéré
- Fichiers ignorés
- Erreurs
- Besoin redémarrage

## 12. Règle produit

Le module Nettoyage doit être puissant, mais jamais agressif.

Virgil nettoie uniquement après validation explicite, étape par étape.
