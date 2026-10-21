$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Get-ReleaseVersion.ps1"
foreach ($case in @(@('v2.0.0-alpha', '2.0.0-alpha'), @('v2.0.0', '2.0.0'), @('v12.34.56-rc.1', '12.34.56-rc.1'))) {
    if ((Get-ReleaseVersion $case[0]) -cne $case[1]) { throw "Version incorrecte : $($case[0])" }
}
foreach ($tag in @('', '2.0.0', 'v', 'v../../evil', 'v2.0', 'v2.0.0/evil', 'v2.0.0;evil', 'V2.0.0')) {
    $refused = $false
    try { Get-ReleaseVersion $tag | Out-Null } catch { $refused = $true }
    if (!$refused) { throw "Tag non refusé : $tag" }
}
Write-Host 'Version : 3 cas valides et 8 refus validés.'
