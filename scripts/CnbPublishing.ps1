# Maintainer credentials are used only by publishing tools, never by update clients.
function Get-CnbPublishingToken {
    if ($env:CNB_TOKEN) { return $env:CNB_TOKEN }
    $tokenFile=$env:CNB_TOKEN_FILE
    if (!$tokenFile) { $tokenFile=(& git config --get cnb.tokenFile 2>$null) }
    if (!$tokenFile -or !(Test-Path -LiteralPath $tokenFile)) { throw 'Set CNB_TOKEN or CNB_TOKEN_FILE (a current-user encrypted SecureString XML file).' }
    $secret=Import-Clixml -LiteralPath $tokenFile
    if ($secret -isnot [Security.SecureString]) { throw 'CNB_TOKEN_FILE must contain an encrypted SecureString.' }
    $pointer=[IntPtr]::Zero
    try {
        $pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
        $value=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ($value -match '[\r\n\x00]') { throw 'Invalid CNB credential.' }
        return $value
    } finally {
        if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
        $secret.Dispose()
    }
}
