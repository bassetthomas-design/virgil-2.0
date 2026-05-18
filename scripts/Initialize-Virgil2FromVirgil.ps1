<#
.SYNOPSIS
    Initialise Virgil 2.0 à partir du dépôt Virgil v1.

.DESCRIPTION
    Clone le dépôt source Virgil, prépare une copie locale pour Virgil 2.0,
    configure le dépôt de destination, applique les fichiers de base Tactical HUD
    puis prépare un commit local.

    Le script ne pousse rien automatiquement sans le paramètre -Push.

.EXAMPLE
    .\scripts\Initialize-Virgil2FromVirgil.ps1 `
        -SourceRepo "https://github.com/bassetthomas-design/Virgil.git" `
        -DestinationRepo "https://github.com/bassetthomas-design/virgil-2.0.git" `
        -WorkDir "C:\Dev\Virgil2"

.EXAMPLE
    .\scripts\Initialize-Virgil2FromVirgil.ps1 `
        -SourceRepo "https://github.com/bassetthomas-design/Virgil.git" `
        -DestinationRepo "https://github.com/bassetthomas-design/virgil-2.0.git" `
        -WorkDir "C:\Dev\Virgil2" `
        -Push
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$SourceRepo = "https://github.com/bassetthomas-design/Virgil.git",

    [Parameter(Mandatory = $false)]
    [string]$DestinationRepo = "https://github.com/bassetthomas-design/virgil-2.0.git",

    [Parameter(Mandatory = $false)]
    [string]$WorkDir = "C:\Dev\Virgil2",

    [Parameter(Mandatory = $false)]
    [switch]$Push,

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[Virgil 2.0] $Message" -ForegroundColor Yellow
}

function Assert-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Commande introuvable : $Name. Installe-la avant de relancer le script."
    }
}

Assert-Command git

if (Test-Path $WorkDir) {
    if (-not $Force) {
        throw "Le dossier existe déjà : $WorkDir. Relance avec -Force pour le supprimer, ou choisis un autre -WorkDir."
    }

    Write-Step "Suppression du dossier existant : $WorkDir"
    Remove-Item -LiteralPath $WorkDir -Recurse -Force
}

$parent = Split-Path -Parent $WorkDir
if (-not (Test-Path $parent)) {
    New-Item -ItemType Directory -Path $parent | Out-Null
}

Write-Step "Clonage de Virgil v1 depuis $SourceRepo"
git clone $SourceRepo $WorkDir

Set-Location $WorkDir

Write-Step "Configuration du dépôt de destination"
git remote remove origin
git remote add origin $DestinationRepo

Write-Step "Création de la branche virgil-2-tactical-hud"
git checkout -b virgil-2-tactical-hud

Write-Step "Création des dossiers Virgil 2.0"
New-Item -ItemType Directory -Force -Path "docs", "scripts", "src\Virgil.App\Themes" | Out-Null

Write-Step "Application du thème Tactical HUD"
@'
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Virgil 2.0 - Tactical HUD Theme -->
    <Color x:Key="VirgilColorBackground">#05070A</Color>
    <Color x:Key="VirgilColorPanel">#0B1016</Color>
    <Color x:Key="VirgilColorCard">#111820</Color>
    <Color x:Key="VirgilColorBorder">#24303A</Color>
    <Color x:Key="VirgilColorAccent">#FF8A00</Color>
    <Color x:Key="VirgilColorAccentSoft">#FFB14A</Color>
    <Color x:Key="VirgilColorTextPrimary">#F4F7FA</Color>
    <Color x:Key="VirgilColorTextSecondary">#9AA6B2</Color>
    <Color x:Key="VirgilColorWarning">#FFB020</Color>
    <Color x:Key="VirgilColorError">#FF4D2E</Color>
    <Color x:Key="VirgilColorSuccess">#40D47E</Color>

    <SolidColorBrush x:Key="App.BackgroundBrush" Color="{StaticResource VirgilColorBackground}" />
    <SolidColorBrush x:Key="App.PanelBrush" Color="{StaticResource VirgilColorPanel}" />
    <SolidColorBrush x:Key="App.BorderBrush" Color="{StaticResource VirgilColorBorder}" />
    <SolidColorBrush x:Key="App.AccentBrush" Color="{StaticResource VirgilColorAccent}" />
    <SolidColorBrush x:Key="App.AccentBrushSoft" Color="#33220B" />
    <SolidColorBrush x:Key="App.TextPrimaryBrush" Color="{StaticResource VirgilColorTextPrimary}" />
    <SolidColorBrush x:Key="App.TextSecondaryBrush" Color="{StaticResource VirgilColorTextSecondary}" />
    <SolidColorBrush x:Key="App.WarningBrush" Color="{StaticResource VirgilColorWarning}" />
    <SolidColorBrush x:Key="App.ErrorBrush" Color="{StaticResource VirgilColorError}" />
    <SolidColorBrush x:Key="App.SuccessBrush" Color="{StaticResource VirgilColorSuccess}" />

    <SolidColorBrush x:Key="AppBackgroundBrush" Color="{StaticResource VirgilColorBackground}" />
    <SolidColorBrush x:Key="PanelBackgroundBrush" Color="{StaticResource VirgilColorPanel}" />
    <SolidColorBrush x:Key="CardBackgroundBrush" Color="{StaticResource VirgilColorCard}" />
    <SolidColorBrush x:Key="TextPrimaryBrush" Color="{StaticResource VirgilColorTextPrimary}" />
    <SolidColorBrush x:Key="TextSecondaryBrush" Color="{StaticResource VirgilColorTextSecondary}" />

    <DropShadowEffect x:Key="VirgilAccentGlow"
                      BlurRadius="18"
                      ShadowDepth="0"
                      Opacity="0.42"
                      Color="#FF8A00" />

    <Style x:Key="VirgilHudCard" TargetType="Border">
        <Setter Property="Background" Value="{DynamicResource CardBackgroundBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource App.BorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="10" />
        <Setter Property="Padding" Value="14" />
        <Setter Property="Margin" Value="8" />
    </Style>

    <Style x:Key="VirgilPrimaryButton" TargetType="Button">
        <Setter Property="Foreground" Value="{DynamicResource App.TextPrimaryBrush}" />
        <Setter Property="Background" Value="#1A1208" />
        <Setter Property="BorderBrush" Value="{DynamicResource App.AccentBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Padding" Value="10,6" />
        <Setter Property="Cursor" Value="Hand" />
    </Style>
</ResourceDictionary>
'@ | Set-Content -Encoding UTF8 "src\Virgil.App\Themes\TacticalHudTheme.xaml"

Write-Step "Ajout de la note d'intégration UI"
@'
# Intégration du thème Virgil 2.0

Ajouter ce dictionnaire dans `src/Virgil.App/App.xaml` :

```xml
<ResourceDictionary Source="Themes/TacticalHudTheme.xaml" />
```

Le thème expose les anciennes clés `App.*` pour limiter les modifications dans l'UI existante.

Objectif : migration progressive, pas réécriture brutale façon chantier sans casque.
'@ | Set-Content -Encoding UTF8 "docs\TACTICAL_HUD_INTEGRATION.md"

Write-Step "Mise à jour du README local"
@'
# Virgil 2.0

Virgil 2.0 est un assistant PC Windows local basé sur Virgil v1, avec une direction Tactical HUD sombre/orange.

## Objectif

- conserver les fonctions utiles de Virgil
- renforcer la sécurité des actions
- corriger les métriques système, notamment RAM
- ajouter une expérience agent visuelle
- préparer le scan et l'installation des pilotes après validation

## Lancer localement

```powershell
dotnet restore
dotnet build
```

## Thème

Le thème principal est ici :

```text
src/Virgil.App/Themes/TacticalHudTheme.xaml
```
'@ | Set-Content -Encoding UTF8 "README.md"

Write-Step "Préparation du commit"
git add .
git commit -m "feat: initialize Virgil 2.0 tactical HUD base"

if ($Push) {
    Write-Step "Push vers GitHub"
    git push -u origin virgil-2-tactical-hud
} else {
    Write-Step "Commit local prêt. Pour pousser : git push -u origin virgil-2-tactical-hud"
}

Write-Step "Terminé. Virgil 2.0 est préparé dans $WorkDir"
