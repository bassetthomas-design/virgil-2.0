# Virgil 2.0 - Roadmap complète

Objectif final : produire un logiciel Windows installable, prêt à l'emploi, sans demander à l'utilisateur final d'installer .NET, winget, des scripts ou des dépendances externes à la main.

## 1. Décision produit

**Nom :** Virgil 2.0

**Positionnement :** assistant PC local pour Windows.

**Promesse utilisateur :**

- installer Virgil
- lancer l'application
- voir l'état du PC
- recevoir des recommandations compréhensibles
- nettoyer, diagnostiquer ou mettre à jour après validation
- ne pas devoir installer d'add-on ou bricoler Windows

## 2. Base technique

- .NET 8
- WPF
- Windows x64
- Publication self-contained
- Installateur Windows
- Logs locaux
- Pas de dépendance manuelle côté utilisateur final

## 3. Structure cible

```text
Virgil 2.0
│
├── src
│   ├── Virgil.App
│   ├── Virgil.Core
│   ├── Virgil.Agent
│   └── Virgil.Domain
│
├── tests
│   └── Virgil.Tests
│
├── build
│   ├── publish-release.ps1
│   └── installer.iss
│
├── scripts
│   ├── Initialize-Virgil2FromVirgil.ps1
│   └── Apply-TacticalHudTheme.ps1
│
└── docs
```

## 4. Plan de travail pas à pas

### Étape 1 - Importer Virgil v1

But : récupérer la base existante sans repartir de zéro.

- [ ] Cloner `bassetthomas-design/Virgil`
- [ ] Importer dans `bassetthomas-design/virgil-2.0`
- [ ] Créer une branche `virgil-2-tactical-hud`
- [ ] Vérifier `dotnet restore`
- [ ] Vérifier `dotnet build`
- [ ] Corriger les erreurs de chemin ou de namespace

### Étape 2 - Nettoyer la base

But : supprimer ou isoler ce qui gêne la version 2.0.

- [ ] Revoir les anciens fichiers obsolètes
- [ ] Garder App / Core / Agent / Tests
- [ ] Supprimer les essais inutiles
- [ ] Uniformiser les noms de projets
- [ ] Valider que le build reste stable

### Étape 3 - Stabiliser le monitoring

But : afficher des informations fiables.

- [ ] Corriger le calcul RAM
- [ ] Afficher RAM utilisée / totale / disponible
- [ ] Vérifier CPU
- [ ] Vérifier disque
- [ ] Vérifier réseau
- [ ] Préparer température matériel si disponible
- [ ] Ajouter tests unitaires

### Étape 4 - Refaire l'interface Virgil 2.0

But : donner l'identité Tactical HUD orange/noir.

- [ ] Intégrer `TacticalHudTheme.xaml`
- [ ] Revoir `App.xaml`
- [ ] Supprimer les couleurs codées en dur
- [ ] Créer cartes CPU / RAM / disque / réseau
- [ ] Créer panneau assistant
- [ ] Créer centre d'actions rapides
- [ ] Ajouter animations légères

### Étape 5 - Créer le système de notifications internes

But : donner l'impression d'un agent système cohérent.

- [ ] Créer `HudNotificationService`
- [ ] Niveaux : info, success, warning, danger
- [ ] Messages courts type `[VIRGIL]`
- [ ] Historique visible
- [ ] Notifications non bloquantes

### Étape 6 - Revoir le nettoyage

But : nettoyer sans comportement dangereux.

- [ ] Scan avant action
- [ ] Estimation espace récupérable
- [ ] Liste des emplacements concernés
- [ ] Mode simulation
- [ ] Validation utilisateur
- [ ] Rapport après action

### Étape 7 - Revoir les pilotes

But : scan puis bouton d'installation uniquement si résultat.

- [ ] Bouton `Scanner les pilotes`
- [ ] Liste des pilotes détectés
- [ ] Statut clair : aucun / disponibles / erreur
- [ ] Bouton `Installer les pilotes` affiché seulement si utile
- [ ] Confirmation avant installation
- [ ] Log détaillé

### Étape 8 - Revoir les mises à jour d'applications

But : mettre à jour les logiciels simplement.

- [ ] Vérifier disponibilité de winget
- [ ] Si winget absent, expliquer clairement
- [ ] Scan des applications
- [ ] Mise à jour sélectionnée ou globale
- [ ] Rapport final

### Étape 9 - Créer le mode diagnostic express

But : un bouton simple pour obtenir l'état général du PC.

- [ ] Santé globale
- [ ] CPU / RAM / disque / réseau
- [ ] Démarrage
- [ ] Mises à jour
- [ ] Pilotes
- [ ] Recommandations classées par priorité

### Étape 10 - Créer l'agent en arrière-plan

But : surveiller sans garder la fenêtre ouverte.

- [ ] Icône tray
- [ ] Démarrage optionnel avec Windows
- [ ] Notifications système
- [ ] Surveillance légère
- [ ] Ouverture rapide du tableau de bord

### Étape 11 - Créer l'installateur prêt à l'emploi

But : double-clic, installation, terminé.

- [ ] Publication self-contained win-x64
- [ ] Inclure runtime .NET dans l'application publiée
- [ ] Créer installateur Inno Setup ou MSIX
- [ ] Raccourci bureau
- [ ] Raccourci menu Démarrer
- [ ] Désinstallation propre
- [ ] Dossier logs propre

### Étape 12 - Préparer CI/CD GitHub

But : générer automatiquement un build installable.

- [ ] Workflow GitHub Actions Windows
- [ ] Restore
- [ ] Build
- [ ] Test
- [ ] Publish self-contained
- [ ] Génération installateur
- [ ] Upload artefact `.exe`

### Étape 13 - Tests utilisateur

But : vérifier que le logiciel est exploitable par tous.

- [ ] Tester sur PC principal
- [ ] Tester sur PC sans environnement dev
- [ ] Tester après redémarrage
- [ ] Tester sans droits admin
- [ ] Tester avec droits admin
- [ ] Tester désinstallation
- [ ] Tester logs

### Étape 14 - Version 1.0 publique

But : première version exploitable.

- [ ] Créer tag `v2.0.0-alpha`
- [ ] Publier release GitHub
- [ ] Ajouter notes de version
- [ ] Ajouter capture écran
- [ ] Ajouter avertissements clairs
- [ ] Préparer backlog beta

## 5. Critères de réussite

Virgil 2.0 est considéré prêt quand :

- l'utilisateur installe un seul fichier
- l'application démarre sans outils de dev
- les métriques sont cohérentes
- le nettoyage demande confirmation
- les pilotes suivent le flux scan puis installation
- les logs permettent de comprendre ce qui s'est passé
- l'interface est lisible et stable
- le build GitHub produit un artefact installable

## 6. Priorité immédiate

Ordre obligatoire :

1. Importer Virgil v1
2. Compiler
3. Corriger erreurs
4. Appliquer thème
5. Corriger monitoring RAM
6. Créer build self-contained
7. Créer installateur

Pas d'effet visuel avancé avant build stable. Faire l'inverse serait très humain, donc évidemment catastrophique.
