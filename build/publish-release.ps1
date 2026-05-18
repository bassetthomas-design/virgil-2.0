<#
.SYNOPSIS
    Publie Virgil 2.0 en version Windows x64 prête à packager.
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputPath = "artifacts\Virgil2"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "[Virgil 2.0] Restore" -ForegroundColor Yellow
dotnet restore Virgil.sln

Write-Host "[Virgil 2.0] Build $Configuration" -ForegroundColor Yellow
dotnet build Virgil.sln --configuration $Configuration --no-restore

Write-Host "[Virgil 2.0] Publish self-contained $Runtime" -ForegroundColor Yellow
dotnet publish src\Virgil.App\Virgil.App.csproj `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $OutputPath

Write-Host "[Virgil 2.0] Publication terminée : $OutputPath" -ForegroundColor Green
