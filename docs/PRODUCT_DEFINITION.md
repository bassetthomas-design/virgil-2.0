# Virgil 2.0 - Définition produit

## 1. Vision

Virgil 2.0 est un assistant PC Windows local.

Il observe l'état du système, explique clairement les problèmes détectés, propose des actions et n'agit qu'après validation de l'utilisateur.

Virgil n'est pas un nettoyeur agressif, ni un antivirus, ni un outil magique. C'est un assistant de maintenance lisible, prudent et prêt à l'emploi.

## 1.1 Rôle officiel

Virgil est défini comme :

```text
70 % agent tactique PC
20 % assistant système prudent
10 % compagnon intelligent
```

### Agent tactique PC - 70 %

Virgil doit donner l'impression d'un système opérationnel : précis, direct, visuel, orienté diagnostic et action contrôlée.

Il parle peu, affiche clairement l'état du PC et guide l'utilisateur avec des messages courts.

### Assistant système prudent - 20 %

Virgil doit rester sûr : il explique, demande confirmation et évite toute action risquée sans validation explicite.

Il ne doit jamais se comporter comme un outil de nettoyage agressif.

### Compagnon intelligent - 10 %

Virgil peut accompagner l'utilisateur avec une chat box textuelle, des explications simples et une présence visuelle vivante.

Il ne doit pas devenir bavard, intrusif ou trop humain.

## 2. Promesse utilisateur

L'utilisateur doit pouvoir :

1. ouvrir Virgil ;
2. voir l'état général de son PC ;
3. lancer un diagnostic express ;
4. comprendre ce qui ralentit ou encombre le PC ;
5. prévisualiser les actions possibles ;
6. valider ou refuser chaque action ;
7. obtenir un rapport clair.

## 3. Public cible

Virgil s'adresse à des utilisateurs Windows classiques :

- personnes non techniques ;
- joueurs PC ;
- utilisateurs qui veulent nettoyer sans risque ;
- utilisateurs qui veulent comprendre leur PC sans ouvrir dix menus Windows ;
- personnes qui veulent un outil prêt à l'emploi.

## 4. Ce que Virgil doit faire

Les actions ci-dessous sont des catégories à définir et valider ensemble avant implémentation définitive.

### Diagnostic

- Lire l'état RAM
- Lire l'état disque
- Lire l'état CPU
- Lire l'état réseau de base
- Vérifier l'espace libre
- Vérifier les applications au démarrage
- Vérifier si des recommandations sont nécessaires

### Nettoyage

- Scanner les fichiers temporaires utilisateur
- Estimer l'espace récupérable
- Afficher la liste des emplacements concernés
- Demander confirmation avant toute action
- Produire un rapport après action

### Pilotes

- Scanner les pilotes disponibles
- Afficher les mises à jour trouvées
- Afficher le bouton d'installation uniquement si des résultats existent
- Demander confirmation avant installation
- Produire un rapport final

### Applications

- Vérifier les mises à jour disponibles
- Lister les applications concernées
- Proposer une mise à jour globale ou sélectionnée
- Demander confirmation

### Démarrage Windows

- Lister les applications lancées au démarrage
- Identifier les éléments lourds ou inutiles
- Proposer une désactivation encadrée
- Ne jamais désactiver silencieusement

### Agent arrière-plan

- Rester disponible dans la zone de notification
- Lancer le tableau de bord
- Afficher des alertes simples
- Surveiller de manière légère

## 5. Ce que Virgil ne doit pas faire

Virgil ne doit pas :

- supprimer sans confirmation ;
- modifier le registre sans explication ;
- promettre une accélération irréaliste ;
- se présenter comme antivirus ;
- copier des assets ou sons d'une licence existante ;
- forcer l'utilisateur à installer des dépendances ;
- lancer des actions administrateur sans justification ;
- masquer les erreurs.

## 6. Ton de Virgil

Virgil parle comme un agent tactique PC calme, précis et prudent.

Il doit être :

- bref ;
- clair ;
- froid mais pas hostile ;
- rassurant ;
- transparent ;
- jamais infantilisant.

Exemples :

```text
[VIRGIL]
Diagnostic terminé.
3 recommandations disponibles.
```

```text
[VIRGIL]
RAM élevée détectée.
Analyse des processus recommandée.
```

```text
[VIRGIL]
Nettoyage prêt.
Aucune action ne sera effectuée sans validation.
```

## 7. Identité visuelle

Virgil doit avoir une interface :

- sombre ;
- orange / ambre ;
- technique ;
- lisible ;
- calme ;
- avec effets légers.

Effets autorisés :

- scanline légère ;
- glow orange contrôlé ;
- pulse discret ;
- animation d'état ;
- transitions courtes.

Effets interdits :

- clignotements agressifs ;
- sons répétitifs ;
- interface illisible ;
- effets trop lourds ;
- imitation directe d'une licence existante.

## 8. États de Virgil

Virgil doit avoir des états clairs :

| État | Signification |
| --- | --- |
| Idle | En attente |
| Scan | Analyse en cours |
| Warning | Anomalie légère |
| Alert | Problème important |
| Success | Action terminée |
| Error | Action impossible ou erreur |

## 9. Niveau d'autonomie

Virgil suit toujours ce flux :

```text
Observer
↓
Expliquer
↓
Recommander
↓
Demander validation
↓
Agir
↓
Rapporter
```

## 10. Version 1.0 minimale

La première version exploitable doit contenir :

- tableau de bord ;
- diagnostic express ;
- RAM ;
- disque ;
- nettoyage TEMP en prévisualisation ;
- action nettoyage validée ;
- journal d'action ;
- build self-contained ;
- artefact GitHub Actions.

## 11. Version 2.0 complète cible

La version complète doit contenir :

- diagnostic express complet ;
- monitoring RAM / CPU / disque / réseau ;
- scan pilotes ;
- installation pilotes après validation ;
- mises à jour applications ;
- analyse démarrage ;
- agent arrière-plan ;
- installateur Windows ;
- release GitHub ;
- interface Tactical HUD finalisée.
