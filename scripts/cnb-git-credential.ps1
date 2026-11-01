param([ValidateSet('get','store','erase')][string]$Operation='get')
$ErrorActionPreference='Stop'
if ($Operation -ne 'get') { exit 0 }
$request=@{}
while ($null -ne ($line=[Console]::In.ReadLine())) {
    if (!$line.Length) { break }
    $separator=$line.IndexOf('=')
    if ($separator -gt 0) { $request[$line.Substring(0,$separator)]=$line.Substring($separator+1) }
}
if ($request.protocol -cne 'https' -or $request.host -cne 'cnb.cool') { exit 0 }
. (Join-Path $PSScriptRoot 'CnbPublishing.ps1')
$token=Get-CnbPublishingToken
try { [Console]::Out.WriteLine("username=cnb"); [Console]::Out.WriteLine('password='+$token); [Console]::Out.WriteLine() }
finally { $token=$null }
