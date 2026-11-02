# Pure selection logic shared by the publisher and its offline regression tests.
function Get-ReleaseRetentionPlan {
    param(
        [AllowEmptyCollection()][object[]]$Releases,
        [ValidateRange(1,100)][int]$Keep=3,
        [string[]]$ProtectedTags=@()
    )
    $stable=@(foreach($release in $Releases){
        if($release.draft -eq $false -and $release.prerelease -eq $false -and
            $release.tag_name -match '^v?(\d+\.\d+\.\d+)$'){
            [pscustomobject]@{Version=[version]$Matches[1];Release=$release}
        }
    })
    $stable=@($stable|Sort-Object Version -Descending)
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($item in $stable){
        if(!$item.Release.id -or !$seen.Add($item.Version.ToString())){
            throw 'Stable releases must have unique versions and nonempty IDs.'
        }
    }
    $protected=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($tag in $ProtectedTags){[void]$protected.Add($tag)}
    foreach($item in ($stable|Select-Object -First $Keep)){[void]$protected.Add($item.Release.tag_name)}
    # A platform can ship less often than the others. Keep its last installer too.
    foreach($pattern in @('(?i)(-full\.nupkg|\.exe)$','(?i)\.apk$','(?i)\.dmg$')){
        $latest=$stable|Where-Object {@($_.Release.assets|Where-Object name -match $pattern).Count -gt 0}|Select-Object -First 1
        if($latest){[void]$protected.Add($latest.Release.tag_name)}
    }
    [pscustomobject]@{
        Keep=@($stable|Where-Object {$protected.Contains($_.Release.tag_name)}|ForEach-Object Release)
        Remove=@($stable|Where-Object {!$protected.Contains($_.Release.tag_name)}|Sort-Object Version|ForEach-Object Release)
        Untouched=@($Releases|Where-Object {$_.id -notin @($stable|ForEach-Object {$_.Release.id})})
    }
}
