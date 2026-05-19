# Virgil 2.0 - Décisions produit validées

## Statut

Ce document regroupe les décisions validées pour Virgil 2.0 avant implémentation.

## 1. Méthode de travail

Virgil 2.0 est développé uniquement sur GitHub.

Règle de travail :

```text
Discussion
   ↓
Proposition
   ↓
Validation utilisateur
   ↓
Inscription GitHub
   ↓
Implémentation
```

Aucun changement de direction produit ne doit être inscrit sans validation explicite.

## 2. Rôle officiel de Virgil

Virgil est défini comme :

```text
70 % agent tactique PC
20 % assistant système prudent
10 % compagnon intelligent
```

### Agent tactique PC

Virgil doit être direct, visuel, précis, orienté diagnostic et action contrôlée.

### Assistant système prudent

Virgil explique avant d'agir et demande confirmation pour toute action modifiant le PC.

### Compagnon intelligent

Virgil peut dialoguer par texte via une chat box, mais ne doit pas être bavard, intrusif ou trop humain.

## 3. Interface générale validée

L'interface principale doit rester simple.

Éléments principaux :

```text
Noyau Virgil lumineux
Bouton SCAN COMPLET
État global
Actions recommandées
Actions ciblées
Chat box
Journal / rapport
```

Virgil doit être simple en façade et complet en profondeur.

## 4. Apparence de Virgil

Virgil est représenté par un noyau lumineux abstrait, géométrique, orange / ambre, sur fond sombre.

Décisions validées :

- pas d'avatar humain ;
- pas de robot 3D ;
- pas de voix ;
- communication uniquement en texte ;
- chat box intégrée ;
- noyau qui s'éclaire ou pulse quand Virgil communique, analyse ou alerte ;
- style inspiré d'un HUD tactique orange/noir, sans copie de licence existante.

## 5. Scan complet validé

Virgil possède un bouton principal :

```text
SCAN COMPLET
```

Au lancement, Virgil propose deux niveaux :

```text
Scan rapide
Analyse approfondie
```

### Scan rapide

Objectif : obtenir rapidement l'état général du PC.

À analyser :

- RAM ;
- CPU ;
- disque principal ;
- espace libre ;
- réseau basique ;
- démarrage Windows rapide ;
- estimation nettoyage ;
- état simplifié des mises à jour.

### Analyse approfondie

Objectif : analyse complète du PC.

À analyser :

- système Windows ;
- CPU ;
- RAM ;
- GPU ;
- disques ;
- stockage détaillé ;
- températures si accessibles ;
- batterie si portable ;
- réseau complet ;
- DNS ;
- ping ;
- latence ;
- applications installées ;
- applications lourdes ;
- applications au démarrage ;
- Windows Update ;
- mises à jour applications ;
- winget ;
- Microsoft Store si possible ;
- pilotes ;
- GPU ;
- runtimes importants ;
- fichiers temporaires ;
- caches ;
- corbeille ;
- gros fichiers ;
- gros dossiers ;
- Explorer Windows ;
- redémarrage requis.

### Règle

Le scan est toujours en lecture seule.

Virgil n'installe rien, ne supprime rien, ne ferme rien et ne modifie rien pendant le scan.

## 6. Actions ciblées validées

Après le scan, Virgil affiche les priorités et propose les actions ciblées pertinentes.

Familles validées :

```text
1. Nettoyage
2. Ressources système
3. Applications
4. Mises à jour
5. Démarrage Windows
6. Réseau
7. Réparation Windows
8. Rapports / historique
```

Les actions recommandées sont mises en avant après le scan. Toutes les familles restent accessibles dans une section dédiée.

## 7. Ressources système validé

Anciennement Performance / RAM. Le mode gaming est exclu.

À inclure :

- analyser RAM ;
- analyser CPU ;
- voir processus gourmands RAM ;
- voir processus gourmands CPU ;
- fermer une application sélectionnée après validation ;
- relancer Explorer Windows après validation ;
- libérer mémoire inactive avec message honnête ;
- conseiller un redémarrage si session Windows très longue.

À exclure :

- mode jeu ;
- boost FPS ;
- optimisation gaming ;
- fermeture automatique d'apps pour jouer.

## 8. Mises à jour validé

Le module Mises à jour doit être complet.

À couvrir :

- Windows Update ;
- applications via winget ;
- Microsoft Store si possible ;
- pilotes ;
- pilote GPU ;
- runtimes importants ;
- navigateurs ;
- applications avec updateur interne ;
- firmware / BIOS en information uniquement ou très encadré.

### Classement

Les mises à jour sont classées en :

```text
Sûres
À valider
Sensibles
Critiques / information uniquement
```

### Règles

- les applications sûres peuvent être mises à jour après validation simple ;
- Windows Update et runtimes demandent validation ;
- pilotes, GPU et réseau demandent validation renforcée ;
- BIOS / firmware ne doit pas être installé automatiquement.

## 9. Garde-fous popup validés

Virgil peut prévoir toutes les actions possibles, mais doit utiliser un système de validation.

Flux :

```text
Virgil détecte
   ↓
Virgil propose
   ↓
Virgil explique le risque
   ↓
Popup de validation
   ↓
Utilisateur accepte ou refuse
   ↓
Virgil agit ou annule
   ↓
Rapport
```

### Actions sans popup

Uniquement les lectures, scans, prévisualisations et rapports.

### Popup simple

Pour les actions à risque faible.

Boutons :

```text
[EXÉCUTER] [ANNULER]
```

### Popup renforcée

Pour les actions sensibles.

Boutons :

```text
[CONFIRMER] [ANNULER]
```

### Popup critique

Pour les actions très sensibles.

Boutons :

```text
[JE COMPRENDS ET JE CONFIRME] [ANNULER]
```

### Informations obligatoires en popup

Chaque popup doit afficher :

- nom de l'action ;
- pourquoi Virgil la propose ;
- ce qui sera modifié ;
- zone concernée ;
- niveau de risque ;
- besoin admin ou non ;
- redémarrage requis ou non ;
- choix de confirmation ou annulation.

## 10. Module Nettoyage validé

Le module Nettoyage est validé dans le document dédié :

```text
docs/modules/CLEANUP_MODULE.md
```

Décisions principales :

- analyse avant nettoyage ;
- étapes successives ;
- popup à chaque étape ;
- choix Exécuter / Passer / Annuler tout ;
- fichiers personnels jamais supprimés automatiquement ;
- navigateurs : pas de mots de passe, sessions, favoris, cookies ou historique sans choix séparé ;
- registre exclu du nettoyage automatique ;
- doublons automatiques exclus ;
- rapport final obligatoire.

## 11. Ton de Virgil

Virgil parle en texte court, tactique et clair.

Exemples :

```text
[VIRGIL]
Scan complet terminé.
3 priorités détectées.
```

```text
[VIRGIL]
Action sensible détectée.
Validation requise.
```

```text
[VIRGIL]
Étape passée.
Passage à l'action suivante.
```

## 12. Prochaine étape de conception

Après validation de ce socle, les prochains modules à détailler sont :

```text
1. Ressources système
2. Applications
3. Mises à jour
4. Démarrage Windows
5. Réseau
6. Réparation Windows
7. Rapports / historique
8. Chat box
9. Apparence finale / états lumineux
```
