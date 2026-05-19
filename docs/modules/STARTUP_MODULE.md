# Module Démarrage Windows - Virgil 2.0

## Décision validée

Le module Démarrage Windows permet d'analyser les applications et entrées qui se lancent avec Windows, d'expliquer leur impact potentiel, puis de proposer une désactivation ou une réactivation uniquement après validation explicite de l'utilisateur.

Virgil ne doit jamais désactiver automatiquement une entrée de démarrage.

## Objectif

Répondre aux questions :

```text
Pourquoi mon PC met du temps à démarrer ?
Quelles applications se lancent toutes seules ?
Qu'est-ce que je peux désactiver sans risque ?
Puis-je réactiver une entrée si besoin ?
```

## Actions visibles du module

L'interface doit rester simple.

Actions principales visibles :

```text
[Analyser démarrage]
[Voir applications au démarrage]
[Optimiser démarrage]
[Restaurer une entrée]
```

Le bouton Optimiser démarrage ne lance aucune action automatique. Il ouvre une séquence de validation étape par étape.

## 1. Analyser démarrage

### Type

Lecture seule.

### Éléments analysés

- Applications au démarrage
- Entrées activées
- Entrées désactivées
- Éditeur si disponible
- Chemin de lancement
- Impact estimé si disponible
- Type : utilisateur / système / Store / tâche planifiée
- Éléments inconnus à vérifier
- Éléments sensibles à protéger

### Message type

```text
[VIRGIL]
Analyse démarrage terminée.

Applications actives au démarrage : 12
Entrées à examiner : 4
Entrées sensibles : 2

Action recommandée : examiner les applications au démarrage.
```

## 2. Voir applications au démarrage

### Données affichées

- Nom
- Éditeur
- Statut : activé / désactivé
- Impact : faible / moyen / élevé / inconnu
- Type
- Source
- Recommandation prudente

### Libellés autorisés

```text
Désactivation possible
À conserver
À vérifier
Sensible
```

### Libellés interdits

```text
Application inutile
Dangereux
Virus
À supprimer
```

Virgil n'est pas un antivirus et ne doit pas qualifier une entrée comme dangereuse sans preuve.

## 3. Optimiser démarrage

### Principe

Virgil propose une séquence étape par étape.

Aucune désactivation ne doit être effectuée sans validation.

### Message de lancement

```text
[VIRGIL]
Optimisation démarrage prête.

4 entrées peuvent être examinées.
Aucune désactivation ne sera effectuée sans validation.
```

### Popup étape par étape

```text
[VIRGIL]
Étape 1/4 - Discord

Statut : activé au démarrage
Impact estimé : moyen
Risque : faible

Effet :
Discord ne se lancera plus automatiquement avec Windows.
L'application restera disponible manuellement.

[DÉSACTIVER] [PASSER] [ANNULER TOUT]
```

## 4. Réactiver une entrée

### Principe

Si Virgil peut désactiver une entrée, il doit aussi pouvoir proposer sa réactivation quand c'est techniquement possible.

### Action visible

```text
[Restaurer une entrée]
```

### Message type

```text
[VIRGIL]
Entrées désactivées détectées.

Sélectionne une application à réactiver au démarrage.
```

### Popup

```text
[VIRGIL]
Réactivation demandée.

Application : Discord
Effet : l'application se lancera à nouveau avec Windows.

[EXÉCUTER] [PASSER]
```

## 5. Éléments sensibles

Virgil doit protéger ou avertir fortement pour :

- Antivirus
- VPN
- Pilotes
- Services audio
- Outils GPU
- Outils constructeurs PC
- Synchronisation cloud importante
- Outils de sécurité
- Outils clavier / souris
- Applications système Windows

### Message type

```text
[VIRGIL]
Entrée sensible détectée.

Application : NordVPN
Rôle possible : sécurité / réseau
Désactivation non recommandée sans raison claire.

[IGNORER] [VOIR DÉTAILS]
```

### Message pour outils périphériques

```text
[VIRGIL]
Cette application peut gérer un périphérique ou un pilote.
Désactivation à confirmer uniquement si tu sais pourquoi.
```

## 6. Sources à analyser

### V1

- Applications au démarrage visibles côté utilisateur
- Clés Run principales utilisateur
- Clés Run principales machine si accessibles
- Dossier Démarrage utilisateur
- Dossier Démarrage commun

### Plus tard

- Tâches planifiées liées au démarrage
- Applications Store au démarrage si accessibles
- Services liés, lecture seule au départ
- Profils utilisateur multiples

## 7. Règles de sécurité

Virgil ne doit jamais :

- désactiver automatiquement ;
- désactiver un antivirus sans avertissement ;
- désactiver un VPN sans avertissement ;
- désactiver pilotes ou services système ;
- supprimer une entrée au lieu de la désactiver ;
- modifier sans possibilité de retour quand une restauration est possible ;
- qualifier une application de dangereuse sans preuve ;
- masquer une erreur de désactivation.

## 8. Rapport final

Après une intervention sur le démarrage, Virgil doit produire un rapport.

### Rapport type

```text
[VIRGIL]
Intervention démarrage terminée.

Entrées examinées : 4
Entrées désactivées : 2
Entrées passées : 2
Entrées sensibles ignorées : 1

Redémarrage recommandé : oui
```

### Le rapport doit contenir

- Date
- Entrées analysées
- Entrées désactivées
- Entrées passées
- Entrées sensibles
- Possibilité de restauration
- Erreurs
- Redémarrage recommandé ou non

## 9. Version 1

À inclure en V1 :

- analyser démarrage ;
- lister applications activées ;
- lister applications désactivées si possible ;
- désactiver une entrée choisie après popup ;
- réactiver une entrée ;
- identifier quelques éléments sensibles ;
- générer un rapport ;
- utiliser les popups garde-fou.

## 10. Version 2

À prévoir en V2 :

- tâches planifiées liées au démarrage ;
- impact plus précis ;
- détection éditeur plus fiable ;
- historique avant / après démarrage ;
- comparaison de scans ;
- meilleure gestion des applications Store.

## 11. Plus tard

À prévoir plus tard :

- analyse du temps de démarrage réel ;
- détection plus fine des services liés ;
- recommandations plus intelligentes ;
- profils utilisateur multiples ;
- restauration plus complète des modifications.

## 12. Règle produit

Le module Démarrage Windows doit aider l'utilisateur à réduire les lancements inutiles, sans jamais casser un démarrage utile ou sensible.

Virgil désactive uniquement ce que l'utilisateur valide explicitement et doit permettre de réactiver quand c'est possible.
