[CmdletBinding()]
param(
    [string]$StatusWebhookUrl = (
        "http://192.168.36.207:8080/" +
        "api/integrations/backup-status/counterpoint"
    )
)

$ErrorActionPreference = "Stop"
$Root = "C:\ProgramData\GreenHills\CounterPointCloudBackup"
$ConfigPath = Join-Path $Root "config.json"
$SecretsPath = Join-Path $Root "secrets.json"

function Test-IsAdministrator {
    $Identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $Principal = New-Object Security.Principal.WindowsPrincipal($Identity)
    return $Principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )
}

function Convert-SecureStringToPlainText {
    param([Parameter(Mandatory = $true)][Security.SecureString]$Value)

    $Pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($Pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($Pointer)
    }
}

function Protect-MachineValue {
    param([Parameter(Mandatory = $true)][string]$Value)

    $Bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    try {
        $ProtectedBytes = [Security.Cryptography.ProtectedData]::Protect(
            $Bytes,
            $null,
            [Security.Cryptography.DataProtectionScope]::LocalMachine
        )
        return [Convert]::ToBase64String($ProtectedBytes)
    }
    finally {
        [Array]::Clear($Bytes, 0, $Bytes.Length)
    }
}

if (-not (Test-IsAdministrator)) {
    throw "Run this script from PowerShell as Administrator."
}
if (-not (Test-Path $ConfigPath) -or -not (Test-Path $SecretsPath)) {
    throw "The CounterPoint cloud backup must be installed first."
}
if (
    $StatusWebhookUrl -notmatch "^https://" -and
    $StatusWebhookUrl -notmatch "^http://(192\.168\.|100\.)"
) {
    throw "Use HTTPS or a private LAN/Tailscale HTTP address."
}

Add-Type -AssemblyName System.Security
$TokenSecure = Read-Host `
    "GHOS backup status integration secret" -AsSecureString
$Token = Convert-SecureStringToPlainText $TokenSecure

try {
    if ($Token.Length -lt 32) {
        throw "The integration secret must contain at least 32 characters."
    }

    $Config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
    $Secrets = Get-Content $SecretsPath -Raw | ConvertFrom-Json
    $Config | Add-Member -NotePropertyName StatusWebhookUrl `
        -NotePropertyValue $StatusWebhookUrl -Force
    $Secrets | Add-Member -NotePropertyName StatusWebhookToken `
        -NotePropertyValue (Protect-MachineValue $Token) -Force
    $Config | ConvertTo-Json -Depth 6 |
        Set-Content $ConfigPath -Encoding UTF8
    $Secrets | ConvertTo-Json -Depth 6 |
        Set-Content $SecretsPath -Encoding UTF8

    & icacls.exe $Root /inheritance:r /grant:r `
        "*S-1-5-18:(OI)(CI)F" `
        "*S-1-5-32-544:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to restrict permissions on $Root."
    }

    Write-Host "CounterPoint backup status reporting is configured." `
        -ForegroundColor Green
}
finally {
    $Token = $null
}
