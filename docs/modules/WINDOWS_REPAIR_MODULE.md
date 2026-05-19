# Module Réparation Windows - Virgil 2.0

## Décision validée

Le module Réparation Windows permet de diagnostiquer et corriger certains dysfonctionnements Windows de manière graduée, prudente et validée.

Virgil ne doit jamais lancer une réparation système sans validation explicite de l'utilisateur.

## Objectif

Répondre aux questions :

```text
Windows bug ?
Explorer plante ?
Icônes cassées ?
Windows Update bloque ?
Fichiers système à vérifier ?
```

## Principe général

Virgil doit proposer des réparations graduées :

```text
Diagnostic Windows
   ↓
Réparation légère
   ↓
Vérification système
   ↓
Réparation avancée avec popup critique
   ↓
Rapport final
```

## Actions visibles du module

L'interface doit rester simple.

Actions principales visibles :

```text
[Diagnostic Windows]
[Réparation légère]
[Vérification système]
[Réparation avancée]
```

Les actions détaillées apparaissent après diagnostic.

## 1. Diagnostic Windows

### Type

Lecture seule.

### Éléments analysés

- État Explorer Windows
- Uptime système
- Redémarrage requis
- Services Windows Update visibles
- Erreurs récentes accessibles
- Espace disque système
- État de base des fichiers système si possible
- Droits administrateur disponibles ou non

### Message type

```text
[VIRGIL]
Diagnostic Windows terminé.

Explorer : actif
Redémarrage requis : non
Windows Update : à vérifier
Droits administrateur : non

Actions disponibles :
- Réparation légère
- Vérification système
```

## 2. Réparation légère

### Niveau de risque

Faible.

### Actions possibles

- Relancer Explorer Windows
- Réinitialiser cache icônes
- Réinitialiser cache miniatures
- Ouvrir paramètres Windows Update
- Ouvrir paramètres système
- Conseiller redémarrage

### Popup simple - cache icônes

```text
[VIRGIL]
Réparation légère demandée.

Action : réinitialiser le cache des icônes
Risque : faible
Effet : certaines icônes peuvent se recharger progressivement.
Redémarrage : parfois utile, non obligatoire.

[EXÉCUTER] [PASSER] [ANNULER TOUT]
```

### Popup simple - Explorer

```text
[VIRGIL]
Relance d'Explorer Windows.

Le bureau et la barre des tâches peuvent disparaître quelques secondes.
Les applications ouvertes ne seront pas fermées.

[EXÉCUTER] [PASSER] [ANNULER TOUT]
```

## 3. Vérification système

### Objectif

Utiliser les outils officiels Windows pour vérifier l'état du système.

### Actions possibles

- Vérifier les fichiers système
- Vérifier l'image Windows
- Afficher le résultat
- Proposer une réparation si problème détecté

### Outils concernés

- SFC
- DISM

### Règle de langage

Virgil doit expliquer ces outils en langage utilisateur.

Libellé à éviter seul :

```text
sfc /scannow lancé
```

Libellé attendu :

```text
[VIRGIL]
Vérification des fichiers système lancée.
Cette opération peut prendre plusieurs minutes.
```

### Popup renforcée

```text
[VIRGIL]
Vérification système demandée.

Action : vérifier les fichiers système Windows
Risque : faible à moyen
Durée : plusieurs minutes
Droits administrateur : requis
Redémarrage : possible selon résultat

[CONFIRMER] [PASSER] [ANNULER TOUT]
```

## 4. Réparation avancée

### Niveau de risque

Élevé.

### Conditions

À utiliser uniquement si les actions légères ou la vérification système indiquent un problème réel.

### Actions possibles

- Réparer l'image Windows
- Réinitialiser composants Windows Update
- Réparer cache Windows Update
- Relancer services Windows Update, plus tard et encadré
- Réinitialiser paramètres réseau Windows, traité dans le module Réseau

### Exigences

- Droits administrateur requis
- Durée potentiellement longue
- Redémarrage possible
- Popup critique obligatoire

### Popup critique

```text
[VIRGIL]
Réparation Windows avancée.

Cette opération peut modifier des composants système.
Elle peut prendre plusieurs minutes.
Un redémarrage peut être nécessaire.

Virgil recommande cette action uniquement si le problème est confirmé.

[JE COMPRENDS ET JE CONFIRME] [PASSER] [ANNULER TOUT]
```

## 5. Windows Update bloqué

### Actions possibles

- Diagnostiquer Windows Update
- Ouvrir paramètres Windows Update
- Signaler redémarrage requis
- Nettoyer cache Windows Update via le module Nettoyage avancé
- Relancer services liés, plus tard
- Réinitialiser composants Windows Update, avancé

### Règle V1

En V1, Virgil reste prudent :

- afficher le statut ;
- ouvrir les paramètres ;
- signaler redémarrage requis ;
- nettoyer le cache uniquement via le module Nettoyage avec popup renforcée.

Les resets lourds sont réservés à une version ultérieure.

## 6. Ce que Virgil ne doit pas faire

Virgil ne doit jamais :

- réparer Windows automatiquement sans validation ;
- lancer SFC ou DISM sans popup ;
- modifier des services Windows sans explication ;
- supprimer des composants système ;
- forcer un redémarrage ;
- masquer les erreurs ;
- promettre de réparer Windows à coup sûr ;
- désactiver des services système ;
- modifier le registre automatiquement.

### Message d'échec attendu

```text
[VIRGIL]
Réparation impossible à garantir.
Windows a retourné une erreur.
Rapport disponible.
```

## 7. Rapport final

Après diagnostic ou réparation, Virgil doit produire un rapport.

### Rapport type

```text
[VIRGIL]
Réparation Windows terminée.

Actions exécutées :
- Relance Explorer : terminé
- Cache icônes : terminé
- Vérification système : problème détecté

Actions passées : 1
Erreurs : 0
Redémarrage recommandé : oui
```

### Le rapport doit contenir

- Date
- Actions exécutées
- Actions passées
- Droits administrateur utilisés ou non
- Durée
- Résultat
- Erreurs
- Redémarrage recommandé

## 8. Version 1

À inclure en V1 :

- diagnostic Windows ;
- relancer Explorer ;
- réinitialiser cache icônes ;
- réinitialiser cache miniatures ;
- ouvrir Windows Update ;
- conseiller redémarrage ;
- rapport ;
- popups garde-fou.

## 9. Version 2

À prévoir en V2 :

- vérification fichiers système ;
- DISM analyse ;
- détection Windows Update bloqué ;
- exécution admin encadrée ;
- rapport détaillé.

## 10. Plus tard

À prévoir plus tard :

- réparation composants Windows Update ;
- réparation image Windows avancée ;
- relance services Windows encadrée ;
- historique réparations ;
- mode récupération guidée ;
- intégration plus fine des erreurs Windows.

## 11. Règle produit

Le module Réparation Windows doit aider sans promettre l'impossible.

Virgil doit toujours expliquer, demander validation, exécuter seulement si confirmé, puis produire un rapport.
