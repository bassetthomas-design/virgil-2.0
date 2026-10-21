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
foreach ($tag in @('v02.0.0', 'v2.00.0', 'v2.0.00', 'v2.0.0-alpha.01', 'v2.0.0-', 'v2.0.0-alpha..2', 'v2.0.0+build', "v2.0.0`n")) {
    $refused = $false
    try { Get-ReleaseVersion $tag | Out-Null } catch { $refused = $true }
    if (!$refused) { throw "Tag SemVer non refusé : $tag" }
}
$alpha = Get-ReleasePublication 'v2.0.0-alpha.2'
if (!$alpha.IsPrerelease -or $alpha.NotesFile -cne 'docs/RELEASE_NOTES_2.0.0-alpha.md') { throw 'Notes alpha incorrectes.' }
$refused = $false
try { Get-ReleasePublication 'v2.0.0' | Out-Null } catch {
    if ($_.Exception.Message -notlike '*aucune note stable*') { throw }
    $refused = $true
}
if (!$refused) { throw 'Publication stable sans notes autorisée.' }
Write-Host 'SemVer strict et refus de publication stable sans notes validés.'
