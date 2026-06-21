# Virgil 2.0 - Architecture

## Objectif

Virgil 2.0 doit être un assistant PC Windows local, robuste, lisible et contrôlable. Il observe, recommande puis agit uniquement après validation.

## Structure cible

```text
Virgil2
│
├── src
│   ├── Virgil.App       # WPF, shell principal, HUD, avatar, navigation
│   ├── Virgil.Core      # Services métier : monitoring, nettoyage, mises à jour
│   ├── Virgil.Agent     # Agent arrière-plan, tray, notifications
│   └── Virgil.Domain    # Modèles, contrats, états système
│
├── tests
│   └── Virgil.Tests     # Tests unitaires
│
├── docs
│   ├── ARCHITECTURE.md
│   ├── ROADMAP.md
│   └── UI_DIRECTION.md
│
├── scripts
│   ├── Initialize-Virgil2FromVirgil.ps1
│   └── Apply-TacticalHudTheme.ps1
│
└── build
    └── package-release.ps1
```

## Principes

### 1. Lecture avant action

Chaque module doit pouvoir fonctionner en lecture seule : scanner, mesurer, afficher.

### 2. Prévisualisation avant modification

Toute action de nettoyage ou d'optimisation doit afficher :

- ce qui va être modifié
- le niveau de risque
- l'espace estimé récupérable
- la possibilité d'annuler

### 3. Actions sensibles confirmées

Aucune action destructive ne doit être exécutée automatiquement.

### 4. Logs lisibles

Les logs doivent permettre de comprendre :

- l'action lancée
- l'heure
- le résultat
- les erreurs éventuelles

Chemin cible : `%APPDATA%\Virgil\logs`.

## Modules cibles

### MonitoringService

- CPU
- RAM
- disque
- réseau
- uptime
- température si disponible

### Resources

- `ResourceMonitoringService` agrège plusieurs échantillons CPU, la RAM, l'uptime et une inspection des processus ;
- `ProcessInspectionService` compare deux instantanés pour estimer le CPU par processus et fusionne les principaux consommateurs CPU/RAM ;
- `ProcessProtectionPolicy` classe les processus Windows, sécurité, VPN, matériel, inaccessibles ou sans fenêtre avant d'exposer une action ;
- `ProcessActionService` revérifie l'identité du PID juste avant l'action, sépare fermeture propre et forcée, et réutilise `ExplorerRestarter` ;
- `ResourcesView` exige une validation explicite, et une confirmation renforcée pour la fermeture forcée ;
- le scan approfondi appelle uniquement l'analyse en lecture seule et n'exécute aucune action processus.

### CleanupService

- TEMP utilisateur
- TEMP Windows si droits suffisants
- estimation avant suppression
- mode simulation

### DriverService

- scan pilotes
- liste des mises à jour détectées
- bouton `Installer les pilotes` visible uniquement si des résultats existent

### UpdateService

- intégration winget
- scan logiciels
- mise à jour validée

### HudNotificationService

- messages courts
- niveau : info, success, warning, danger
- style agent tactique

## Identité visuelle

Virgil 2.0 doit être original. Il peut s'inspirer d'une ambiance tactique orange/noir, mais ne doit pas copier de nom, son, logo ou asset propriétaire.
