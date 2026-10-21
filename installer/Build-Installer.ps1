param([string] $AppVersion = '0.0.0-dev')
$ErrorActionPreference = 'Stop'
if ($AppVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Version invalide.' }
if (!(Test-Path 'artifacts/Virgil2/Virgil.ElevatedHelper.exe')) { throw 'Helper absent de la publication.' }
$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
if (!(Test-Path $iscc)) {
    choco install innosetup --yes --no-progress
    if ($LASTEXITCODE -ne 0) { throw 'Installation Inno Setup impossible.' }
}
& $iscc "/DAppVersion=$AppVersion" 'installer/Virgil.iss'
if ($LASTEXITCODE -ne 0) { throw 'Compilation ISCC en échec.' }
$setup = "artifacts/installer/Virgil-$AppVersion-win-x64-setup.exe"
if (!(Test-Path $setup)) { throw 'Installateur absent.' }
Write-Host "Installateur vérifié : $setup"
