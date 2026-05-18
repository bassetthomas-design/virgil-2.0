# Virgil 2.0

**Virgil 2.0** est la nouvelle base du projet Virgil : un assistant PC Windows local orienté surveillance, nettoyage, diagnostic et assistance système, avec une interface tactique sombre/orange.

> Objectif : garder le nom **Virgil**, repartir sur une base propre, reprendre les fonctions utiles de Virgil v1 et donner une vraie identité visuelle d'agent système.

## Positionnement

Virgil 2.0 vise une ambiance originale :

- HUD sombre
- accent orange / ambre
- notifications système brèves
- effet scan
- avatar / agent animé
- diagnostic local
- actions validées par l'utilisateur

Aucun asset, son, nom ou élément propriétaire issu d'un jeu tiers ne doit être intégré.

## Base technique visée

- **.NET 8**
- **WPF**
- **Windows x64**
- Architecture séparée : App / Core / Agent / Tests
- Actions importantes uniquement après confirmation
- Logs dans `%APPDATA%\\Virgil\\logs`

## Modules cibles

| Module | But |
| --- | --- |
| Monitoring | CPU, RAM, disque, réseau, température si disponible |
| Nettoyage | TEMP, cache léger, prévisualisation avant action |
| Démarrage | Analyse des applications lancées avec Windows |
| Pilotes | Scan des pilotes disponibles, bouton d'installation après résultats |
| Applications | Mise à jour via winget |
| Assistant | Notifications internes et recommandations lisibles |
| HUD | Interface orange/noir avec état système et effets de scan |

## Principe de sécurité

Virgil doit fonctionner en trois niveaux :

1. **Observation** : lire l'état du PC sans rien modifier.
2. **Recommandation** : proposer une action claire.
3. **Action validée** : exécuter uniquement après validation.

## Démarrage depuis Virgil v1

Le script prévu :

```powershell
.\\scripts\\Initialize-Virgil2FromVirgil.ps1 `
  -SourceRepo "https://github.com/bassetthomas-design/Virgil.git" `
  -DestinationRepo "https://github.com/bassetthomas-design/virgil-2.0.git" `
  -WorkDir "C:\\Dev\\Virgil2"
```

Ce script clone Virgil v1, prépare la copie Virgil 2.0, applique les premiers fichiers de thème Tactical HUD et configure le dépôt de destination.

## Documentation

- `PROJECT_STATUS.md`
- `docs/ARCHITECTURE.md`
- `docs/ROADMAP.md`
- `docs/UI_DIRECTION.md`

## Statut

Projet initialisé comme base **Virgil 2.0 Tactical HUD**.
