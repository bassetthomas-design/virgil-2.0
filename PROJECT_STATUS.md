# Virgil 2.0 - État du projet

Date de création : 18 mai 2026

## Décision produit

Virgil 2.0 garde le nom **Virgil** et devient une version orientée agent PC tactique :

- assistant local Windows
- monitoring système
- nettoyage encadré
- diagnostic express
- actions confirmées
- interface orange/noir
- notifications type agent système

## Base source

Le projet s'appuie sur le dépôt Virgil v1 existant :

- `Virgil.App`
- `Virgil.Core`
- `Virgil.Agent`
- `Virgil.Tests`

La version 2.0 doit reprendre la structure utile sans conserver les choix UI devenus trop génériques.

## Priorités immédiates

### P0 - Initialisation

- [x] Créer / préparer le dépôt `virgil-2.0`
- [x] Ajouter README projet
- [x] Ajouter architecture cible
- [x] Ajouter roadmap
- [x] Ajouter direction UI
- [ ] Importer la base Virgil v1
- [ ] Appliquer le thème Tactical HUD

### P1 - Corrections fonctionnelles

- [ ] Revoir le calcul du pourcentage RAM
- [ ] Ajouter une estimation avant nettoyage
- [ ] Afficher le bouton `Installer les pilotes` uniquement après scan si mises à jour trouvées
- [ ] Clarifier les actions disponibles / indisponibles
- [ ] Ajouter logs lisibles par action

### P2 - Expérience agent

- [ ] Notifications internes type `[VIRGIL]`
- [ ] États visuels de l'avatar
- [ ] Mode scan express
- [ ] Synthèse système globale
- [ ] Indice santé PC

## Contraintes

- Ne pas copier d'assets ou sons propriétaires
- Garder une identité originale
- Conserver un comportement prévisible
- Ne jamais exécuter d'action importante sans validation

## Décision UI

Palette cible : noir bleuté, orange ambre, textes clairs, cartes sombres, bordures fines.

## Prochaine action technique

Exécuter localement :

```powershell
.\scripts\Initialize-Virgil2FromVirgil.ps1 `
  -SourceRepo "https://github.com/bassetthomas-design/Virgil.git" `
  -DestinationRepo "https://github.com/bassetthomas-design/virgil-2.0.git" `
  -WorkDir "C:\Dev\Virgil2"
```
