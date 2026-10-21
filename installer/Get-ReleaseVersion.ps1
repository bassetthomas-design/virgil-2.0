# Fonction pure commune au workflow et au parseur SemVer Core (tags sans metadata).
function Get-ReleaseVersion([string] $Tag) {
    $number = '(?:0|[1-9][0-9]*)'
    $identifier = '(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
    $pattern = '^v(' + $number + '\.' + $number + '\.' + $number + '(?:-' + $identifier + '(?:\.' + $identifier + ')*)?)\z'
    if ($Tag -cnotmatch $pattern) { throw 'Tag de version invalide.' }
    return $Matches[1]
}

function Get-ReleasePublication([string] $Tag) {
    $version = Get-ReleaseVersion $Tag
    $prerelease = $version.Contains('-')
    if (!$prerelease) { throw 'Publication stable refusée : aucune note stable n’est disponible.' }
    return [pscustomobject]@{
        Version = $version
        IsPrerelease = $prerelease
        NotesFile = 'docs/RELEASE_NOTES_2.0.0-alpha.md'
    }
}
