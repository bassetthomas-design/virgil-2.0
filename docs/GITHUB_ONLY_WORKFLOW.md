# Workflow GitHub-only

## Décision

Virgil 2.0 est développé uniquement sur GitHub.

Aucune étape de développement local n'est requise pour avancer sur le projet.

## Méthode de travail

Les changements se font par :

- commits directs sur GitHub ;
- branches de travail si nécessaire ;
- pull requests si le projet grossit ;
- GitHub Actions pour compiler ;
- artefacts GitHub Actions pour tester ;
- releases GitHub pour distribuer.

## Ce que GitHub doit produire

À terme, GitHub Actions doit produire :

1. un build Release ;
2. une publication self-contained Windows x64 ;
3. un dossier artefact téléchargeable ;
4. un installateur Windows ;
5. une release versionnée.

## Règle importante

Aucun fichier ne doit demander à l'utilisateur de :

- cloner le projet ;
- installer Visual Studio ;
- installer le SDK .NET ;
- lancer PowerShell localement ;
- modifier des fichiers à la main.

Le local peut servir pour tester plus tard, mais il ne doit jamais être obligatoire.

## Flux cible

```text
Commit GitHub
   ↓
GitHub Actions
   ↓
Build Release
   ↓
Publish self-contained win-x64
   ↓
Artefact téléchargeable
   ↓
Installateur final
   ↓
Release GitHub
```

## Implication produit

Virgil 2.0 doit être pensé comme un logiciel distribué, pas comme un projet de développeur.

L'utilisateur final télécharge un installateur. Il installe. Il lance. Fin de la tragédie humaine habituelle.
