# Module Chatbox - Virgil 2.0

## Décision validée

La chatbox de Virgil est un canal de communication textuel guidé.

En V1, l'utilisateur ne saisit pas de texte. Virgil parle uniquement dans la chatbox, et l'utilisateur répond avec des boutons contextuels.

Aucune IA locale n'est intégrée en V1.

## Objectif

Permettre à Virgil de guider l'utilisateur avec des messages courts, tactiques et clairs.

La chatbox doit servir à :

- annoncer les scans ;
- afficher les états ;
- présenter les priorités ;
- proposer les actions disponibles ;
- demander validation ;
- signaler les erreurs ;
- afficher les rapports courts ;
- guider les séquences étape par étape.

## Principe général

```text
Virgil parle
   ↓
Virgil propose des boutons
   ↓
L'utilisateur choisit
   ↓
Virgil poursuit la séquence
```

L'utilisateur ne saisit pas de texte en V1.

## Pourquoi pas d'IA locale en V1

Décision validée : pas d'IA locale intégrée dans la première version.

Raisons :

- installateur plus léger ;
- moins de consommation RAM / CPU ;
- comportement plus fiable ;
- moins de risques d'incompréhension ;
- développement plus rapide ;
- garde-fous plus simples à contrôler ;
- meilleure cohérence produit.

Une IA locale pourra être étudiée plus tard, uniquement comme couche d'explication ou de résumé, jamais comme moteur d'action directe.

## Style de communication

Virgil communique comme un agent système tactique :

- court ;
- froid ;
- précis ;
- clair ;
- opérationnel ;
- non bavard ;
- non émotionnel ;
- jamais infantilisant.

Virgil doit donner une impression proche d'un système tactique orange/noir, sans copier une licence existante.

## Vocabulaire autorisé

Virgil peut utiliser ces mots :

- analyse ;
- protocole ;
- module ;
- anomalie ;
- priorité ;
- intervention ;
- validation ;
- séquence ;
- état système ;
- rapport disponible ;
- action sensible ;
- confirmation requise.

## Format des messages

Format recommandé :

```text
[VIRGIL]
Message court.
Information prioritaire.
Action disponible.
```

Exemple :

```text
[VIRGIL]
Analyse terminée.
État système : à surveiller.
3 priorités détectées.
```

## Messages types

### Lancement d'analyse

```text
[VIRGIL]
Protocole d'analyse initialisé.
Scan système en cours.
```

### Analyse en cours

```text
[VIRGIL]
Scan en cours.
Modules actifs : système, stockage, réseau.
```

### Fin de scan

```text
[VIRGIL]
Scan terminé.
État général : à surveiller.
Priorités détectées : 3.
```

### Alerte stockage

```text
[VIRGIL]
Alerte stockage.
Disque système utilisé à 91 %.
Nettoyage recommandé.
```

### Action sensible

```text
[VIRGIL]
Action sensible détectée.
Validation requise.
Aucune modification ne sera effectuée sans confirmation.
```

### Succès

```text
[VIRGIL]
Action terminée.
Espace libéré : 1,6 Go.
Rapport disponible.
```

### Erreur

```text
[VIRGIL]
Action interrompue.
Cause probable : accès refusé.
Droits administrateur requis.
```

### Étape passée

```text
[VIRGIL]
Étape passée.
Séquence poursuivie.
```

### État stable

```text
[VIRGIL]
État système stable.
Aucune intervention prioritaire.
```

## Boutons contextuels

Les réponses utilisateur passent par des boutons.

Boutons standards :

```text
[SCAN RAPIDE]
[ANALYSE APPROFONDIE]
[EXÉCUTER]
[CONFIRMER]
[PASSER]
[ANNULER TOUT]
[VOIR RAPPORT]
[VOIR DÉTAILS]
[RETOUR]
```

## Exemple après scan complet

```text
[VIRGIL]
Analyse approfondie terminée.

État système : à surveiller.
Priorités détectées :
1. Stockage élevé.
2. Mises à jour disponibles.
3. Démarrage chargé.

Actions ciblées disponibles.
```

Boutons :

```text
[NETTOYAGE]
[MISES À JOUR]
[DÉMARRAGE]
[VOIR RAPPORT]
```

## Exemple nettoyage étape par étape

```text
[VIRGIL]
Séquence nettoyage préparée.

Zones détectées : 8.
Espace récupérable estimé : 4,8 Go.
Validation requise à chaque étape.
```

Puis :

```text
[VIRGIL]
Étape 1/8.
Zone : fichiers temporaires utilisateur.
Risque : faible.
Action disponible : nettoyage.
```

Boutons :

```text
[EXÉCUTER]
[PASSER]
[ANNULER TOUT]
```

## Exemple mise à jour sensible

```text
[VIRGIL]
Mise à jour sensible détectée.

Type : pilote réseau.
Effet possible : coupure temporaire.
Redémarrage : possible.

Confirmation renforcée requise.
```

Boutons :

```text
[JE CONFIRME]
[PASSER]
[ANNULER TOUT]
```

## Lien avec le noyau lumineux

Quand Virgil parle dans la chatbox, le noyau lumineux réagit selon l'état.

### Message normal

- lumière orange faible ;
- pulse lent.

### Alerte

- orange plus intense ;
- pulse plus rapide ;
- bordure de message renforcée.

### Action sensible

- orange / rouge-orange ;
- animation courte de verrouillage ;
- popup garde-fou.

### Succès

- flash orange doux ;
- retour progressif au calme.

### Erreur

- clignotement bref ;
- message sec ;
- détails techniques masqués par défaut.

## Ce que Virgil ne doit pas faire

Virgil ne doit pas :

- être bavard ;
- utiliser un ton trop humain ;
- demander une saisie texte en V1 ;
- intégrer une IA locale en V1 ;
- interpréter librement des demandes utilisateur ;
- exécuter une action depuis un message sans bouton de validation ;
- afficher des logs bruts par défaut ;
- copier directement le style, les phrases ou l'identité d'une licence existante.

## Version 1

À inclure en V1 :

- chatbox en lecture uniquement ;
- messages Virgil courts ;
- boutons contextuels ;
- état du scan ;
- priorités détectées ;
- validations étape par étape ;
- erreurs lisibles ;
- rapport court ;
- interaction avec le noyau lumineux.

## Version 2

À prévoir en V2 :

- commandes rapides prédéfinies ;
- filtres contextuels ;
- réponses plus variées ;
- meilleure personnalisation du ton ;
- messages plus contextuels selon les modules.

## Version 3 éventuelle

À étudier plus tard :

- IA locale optionnelle ;
- uniquement pour expliquer, résumer ou reformuler ;
- jamais pour exécuter directement ;
- toujours raccordée au moteur d'actions et aux popups garde-fou.

## Règle produit

La chatbox doit donner à Virgil une présence tactique et textuelle, sans devenir un chatbot libre.

Virgil parle quand il a quelque chose d'utile à dire. L'utilisateur décide avec des boutons. Les actions restent contrôlées par le système de garde-fous.
