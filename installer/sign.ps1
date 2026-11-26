<#
.SYNOPSIS
  Sign WinTools PE files with the local self-signed development certificate.

.DESCRIPTION
  Accepts a file or a directory. For a directory, only WinTools*.exe / WinTools*.dll
  are signed; third-party runtime DLLs keep their original Microsoft signatures.

  The certificate password is read from certs/.password (git-ignored) and never
  printed. Certificate: certs/wintools-selfsigned.pfx (also git-ignored).

  Self-signed certificates do NOT remove SmartScreen warnings. They are for
  rehearsing the signing pipeline and keeping a stable publisher name only.

.PARAMETER Timestamp
  Add an RFC3161 timestamp so the signature stays valid after the cert expires.
  Requires network access to the timestamp server; omit for offline signing.

.EXAMPLE
  powershell -File installer\sign.ps1 -Path ..\publish
  powershell -File installer\sign.ps1 -Path ..\installer-output\WinTools-0.17.1-setup.exe -Timestamp
#>
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$CertPath,
    [string]$PasswordFile,
    [switch]$Timestamp,
    [switch]$VerifyOnly,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $CertPath) { $CertPath = Join-Path $root 'certs\wintools-selfsigned.pfx' }
if (-not $PasswordFile) { $PasswordFile = Join-Path $root 'certs\.password' }

foreach ($required in @($CertPath, $PasswordFile)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Missing required file: $required"
    }
}

# Locate signtool.exe from the newest installed Windows SDK.
$signTool = $null
$sdkTools = @(Get-ChildItem -Path 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
    Sort-Object -Property FullName -Descending)
if ($sdkTools.Count -gt 0) { $signTool = $sdkTools[0].FullName }
if (-not $signTool) {
    $fallback = 'C:\Program Files (x86)\Windows Kits\10\App Certification Kit\signtool.exe'
    if (Test-Path -LiteralPath $fallback) { $signTool = $fallback }
}
if (-not $signTool) { throw 'signtool.exe not found. Install Windows SDK 10.' }

# Collect targets.
$targets = @()
if (Test-Path -LiteralPath $Path -PathType Container) {
    $targets = @(Get-ChildItem -LiteralPath $Path -File -Recurse |
        Where-Object { $_.Name -like 'WinTools*.exe' -or $_.Name -like 'WinTools*.dll' } |
        ForEach-Object { $_.FullName })
}
elseif (Test-Path -LiteralPath $Path -PathType Leaf) {
    $targets = @((Resolve-Path -LiteralPath $Path).Path)
}
else { throw "Path not found: $Path" }
if ($targets.Count -eq 0) { throw "No WinTools PE files found under: $Path" }

$password = [System.IO.File]::ReadAllText($PasswordFile).Trim()
if (-not $password) { throw "Password file is empty: $PasswordFile" }
$expectedThumb = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
    $CertPath, $password).Thumbprint

"certificate : {0}" -f $CertPath
"signtool    : {0}" -f $signTool
"timestamp   : {0}" -f $(if ($Timestamp) { $TimestampUrl } else { 'off' })
"targets     : {0}" -f $targets.Count
if (-not $VerifyOnly) {
    '--- signing ---'
    foreach ($target in $targets) {
        $args = @('sign', '/f', $CertPath, '/p', $password, '/fd', 'sha256')
        if ($Timestamp) { $args += @('/tr', $TimestampUrl, '/td', 'sha256') }
        $args += $target
        & $signTool @args | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Signing failed for $target (signtool exit $LASTEXITCODE)"
        }
        'signed     : {0}' -f (Split-Path -Leaf $target)
    }
}

'--- verification ---'
$allValid = $true
foreach ($target in $targets) {
    $sig = Get-AuthenticodeSignature -LiteralPath $target
    $signerOk = $sig.SignerCertificate -and $sig.SignerCertificate.Thumbprint -eq $expectedThumb
    # A self-signed leaf that is not in the local trust store reports UnknownError /
    # NotTrusted with a message about an untrusted root. That is a chain-trust issue,
    # not a broken signature. Real corruption reports BadDigest / HashMismatch etc.
    $trustOnly = $sig.Status -in @('NotTrusted', 'UnknownError')
    $ok = $signerOk -and ($sig.Status -eq 'Valid' -or $trustOnly)
    '{0,-30} {1,-12} signer-ok={2,-5} {3}' -f (Split-Path -Leaf $target), $sig.Status, $signerOk, $sig.StatusMessage
    if (-not $ok) { $allValid = $false }
}
if (-not $allValid) { throw 'Signature verification failed for at least one file.' }
'Note: an untrusted-root status is expected for a self-signed cert until it is added to the local trust store.'
