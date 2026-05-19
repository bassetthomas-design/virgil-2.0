# Virgil 2.0 - Catalogue des actions ciblées

## Principe général

Virgil ne doit pas afficher 40 boutons sur l'écran principal.

L'interface principale doit reposer sur :

1. un gros bouton de scan complet ;
2. une synthèse claire ;
3. une section Actions ciblées ;
4. une chat box ;
5. un journal / rapport.

## Flux utilisateur

```text
Scan complet
   ↓
Analyse PC / composants / réseau / logiciels / stockage
   ↓
Synthèse des problèmes ou points à surveiller
   ↓
Actions ciblées proposées
   ↓
Validation utilisateur avant modification
   ↓
Rapport
```

## 1. Bouton principal

### Nom proposé

```text
SCAN COMPLET
```

### Rôle

Le scan complet analyse le PC dans son ensemble et prépare la liste des actions pertinentes.

Il ne modifie rien.

## 2. Section Actions ciblées

Les actions ciblées sont des modules spécialisés, visibles dans une section dédiée.

Elles peuvent être :

- proposées automatiquement après scan ;
- lancées manuellement par l'utilisateur ;
- déclenchées via la chat box.

## 3. Catégories d'actions à anticiper

### A. Système

- Diagnostic système complet
- Afficher état global
- Afficher informations PC
- Afficher uptime
- Vérifier droits administrateur
- Vérifier version Windows
- Vérifier espace disque système
- Vérifier intégrité basique des chemins importants

### B. Composants

- Analyser CPU
- Analyser RAM
- Analyser GPU si détectable
- Analyser disques
- Analyser températures si disponible
- Analyser batterie si PC portable
- Afficher composants principaux

### C. Processus

- Lister processus actifs
- Trier par RAM
- Trier par CPU
- Identifier processus lourds
- Afficher éditeur si disponible
- Fermer un processus sélectionné après validation
- Relancer Explorer après validation

### D. Stockage

- Analyser occupation disque
- Trouver gros fichiers
- Trouver gros dossiers
- Analyser dossier Téléchargements
- Analyser Bureau
- Analyser Corbeille
- Analyser fichiers temporaires
- Préparer nettoyage ciblé

### E. Nettoyage

- Nettoyage sûr
- Nettoyage avancé
- Nettoyage expert
- Nettoyer fichiers temporaires utilisateur
- Nettoyer miniatures
- Nettoyer corbeille
- Nettoyer logs anciens
- Nettoyer cache Windows Update après validation
- Nettoyer caches navigateurs après validation
- Rapport espace récupéré

### F. Applications

- Lister applications installées
- Trier par taille
- Trier par date d'installation
- Identifier applications rarement utilisées si possible
- Désinstaller via désinstalleur officiel
- Scanner restes après désinstallation
- Supprimer restes uniquement après validation
- Détecter raccourcis cassés

### G. Mises à jour

- Vérifier Windows Update
- Vérifier applications via winget si disponible
- Vérifier Microsoft Store si possible
- Vérifier pilotes disponibles si possible
- Vérifier pilote GPU
- Proposer mise à jour sélectionnée
- Proposer mise à jour globale
- Rapport mises à jour

### H. Démarrage Windows

- Lister applications au démarrage
- Afficher statut activé / désactivé
- Estimer impact
- Désactiver une entrée sélectionnée après validation
- Réactiver une entrée
- Rapport démarrage

### I. Réseau

- Vérifier connexion Internet
- Tester passerelle
- Tester DNS
- Tester latence
- Tester stabilité ping
- Afficher IP locale
- Afficher DNS utilisés
- Proposer reset réseau léger après validation
- Générer rapport réseau

### J. Gaming / performance

- Mode diagnostic jeu
- Identifier processus non essentiels lourds
- Vérifier RAM disponible avant jeu
- Vérifier espace disque jeu
- Proposer fermeture d'applications sélectionnées
- Restaurer état normal si actions appliquées

### K. Réparation légère Windows

- Relancer Explorer
- Réinitialiser cache icônes après validation
- Vérifier fichiers système via outil Windows si disponible
- Proposer réparation réseau légère
- Proposer redémarrage si nécessaire

### L. Rapports et historique

- Générer rapport de scan
- Générer rapport d'intervention
- Afficher historique des actions
- Exporter rapport texte
- Afficher erreurs rencontrées

## 4. Niveaux de risque

Chaque action doit avoir un niveau de risque :

| Niveau | Description |
| --- | --- |
| Lecture seule | Observe uniquement |
| Faible | Action simple, peu risquée |
| Moyen | Peut fermer, nettoyer ou modifier un réglage utilisateur |
| Élevé | Peut toucher système, pilotes, réseau ou Windows |

## 5. Règles de validation

### Actions sans confirmation

Seulement :

- scans ;
- lectures ;
- prévisualisations ;
- rapports.

### Actions avec confirmation simple

- vider corbeille ;
- nettoyer TEMP ;
- fermer processus choisi ;
- désactiver application de démarrage choisie.

### Actions avec confirmation renforcée

- pilotes ;
- reset réseau ;
- réparation Windows ;
- nettoyage avancé ;
- suppression de restes logiciels.

## 6. Affichage après scan

Après scan complet, Virgil affiche :

```text
État général : À surveiller

Priorités :
1. Disque C: presque plein
2. RAM élevée
3. 4 mises à jour disponibles

Actions ciblées recommandées :
- Nettoyage stockage
- Analyse processus RAM
- Mises à jour applications
```

## 7. Interface recommandée

### Écran principal

- Noyau Virgil
- Bouton SCAN COMPLET
- État global
- Chat box

### Section actions ciblées

Les actions sont regroupées par cartes :

- Système
- Nettoyage
- Applications
- Mises à jour
- Démarrage
- Réseau
- Performance
- Rapports

Les cartes ne doivent pas toutes exploser en boutons. Elles s'ouvrent uniquement si l'utilisateur clique ou si Virgil recommande une action.

## 8. Règle produit

Virgil doit rester simple en façade et complet en profondeur.

Le scan complet guide l'utilisateur. Les actions ciblées restent disponibles, mais rangées par modules.
