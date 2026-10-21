# Fonction pure commune au workflow et à ses tests.
function Get-ReleaseVersion([string] $Tag) {
    if ($Tag -cnotmatch '^v(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)$') {
        throw 'Tag de version invalide.'
    }
    return $Matches[1]
}
