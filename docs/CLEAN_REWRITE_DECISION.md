# Décision - Virgil 2.0 repart de zéro

Date : 18 mai 2026

## Décision

Virgil 2.0 est recréé sur une base neuve.

Virgil v1 reste une référence fonctionnelle, mais le code n'est pas importé automatiquement dans la base principale.

## Pourquoi

Repartir de zéro permet de :

- garder une architecture propre
- éviter les fichiers obsolètes
- stabiliser le build plus rapidement
- intégrer le thème Tactical HUD dès le départ
- préparer un installateur final sans dette technique inutile
- garder des actions système strictement contrôlées

## Objectif final

Créer un logiciel Windows installable, prêt à l'emploi, sans dépendance manuelle pour l'utilisateur final.

L'utilisateur doit pouvoir :

1. télécharger l'installateur
2. installer Virgil
3. lancer l'application
4. analyser son PC
5. valider les actions recommandées

## Base fonctionnelle minimale

La première version propre doit contenir :

- tableau de bord WPF
- thème sombre/orange
- monitoring CPU / RAM / disque
- diagnostic express
- prévisualisation nettoyage TEMP
- rapport simple
- structure installable
- workflow GitHub Actions

## Règle produit

Aucune action importante ne doit s'exécuter sans validation explicite de l'utilisateur.

Virgil doit observer, expliquer, puis agir seulement après autorisation.
