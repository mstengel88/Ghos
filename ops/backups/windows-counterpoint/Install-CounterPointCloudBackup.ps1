[CmdletBinding()]
param(
    [string]$RunnerSource = (
        Join-Path $PSScriptRoot "Invoke-CounterPointCloudBackup.ps1"
    )
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$Root = "C:\ProgramData\GreenHills\CounterPointCloudBackup"
$BinDirectory = Join-Path $Root "bin"
$RunnerPath = Join-Path $Root "Invoke-CounterPointCloudBackup.ps1"
$ConfigPath = Join-Path $Root "config.json"
$SecretsPath = Join-Path $Root "secrets.json"
$ResticVersion = "0.19.1"
$ResticArchiveName = "restic_0.19.1_windows_amd64.zip"
$ResticArchiveSha256 = (
    "da948ad707ed690426473aaba2046cd61f8f90f6f0e7dab6be0d5796531de67d"
)
$ResticDownload = (
    "https://github.com/restic/restic/releases/download/" +
    "v$ResticVersion/$ResticArchiveName"
)

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

function Set-PrivateAcl {
    param([Parameter(Mandatory = $true)][string]$Path)

    & icacls.exe $Path /inheritance:r /grant:r `
        "*S-1-5-18:(OI)(CI)F" `
        "*S-1-5-32-544:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to restrict permissions on $Path."
    }
}

if (-not (Test-IsAdministrator)) {
    throw "Run this installer from PowerShell as Administrator."
}
if (-not (Test-Path $RunnerSource -PathType Leaf)) {
    throw "Runner script not found: $RunnerSource"
}

$SourcePaths = @(
    "D:\Acronis Backups",
    "D:\SQLBackups"
)
foreach ($SourcePath in $SourcePaths) {
    if (-not (Test-Path $SourcePath -PathType Container)) {
        throw "Required source folder not found: $SourcePath"
    }
}

Write-Host ""
Write-Host "Green Hills CounterPoint off-site backup installer" -ForegroundColor Green
Write-Host "Secrets are hidden and protected for this Windows server."
Write-Host ""

$Endpoint = (Read-Host "B2 S3 endpoint").Trim()
$Endpoint = $Endpoint -replace "^https?://", ""
$Endpoint = $Endpoint.TrimEnd("/")
$Bucket = (Read-Host "Private CounterPoint B2 bucket name").Trim().Trim("/")
$AccessKeyId = (Read-Host "B2 application key ID").Trim()
$AccessKeySecure = Read-Host "B2 application key" -AsSecureString
$ResticPasswordSecure = Read-Host `
    "New restic encryption password (20+ characters)" -AsSecureString
$ResticPasswordRepeatSecure = Read-Host `
    "Repeat restic encryption password" -AsSecureString

if ($Endpoint -notmatch "^s3\.([^.]+)\.backblazeb2\.com$") {
    throw "The endpoint is not a valid Backblaze B2 S3 endpoint."
}
$Region = $Matches[1]
if ($Bucket -notmatch "^[A-Za-z0-9][A-Za-z0-9.-]{4,48}[A-Za-z0-9]$") {
    throw "The B2 bucket name is invalid."
}
if ([string]::IsNullOrWhiteSpace($AccessKeyId)) {
    throw "The B2 application key ID is required."
}

$AccessKey = Convert-SecureStringToPlainText $AccessKeySecure
$ResticPassword = Convert-SecureStringToPlainText $ResticPasswordSecure
$ResticPasswordRepeat = Convert-SecureStringToPlainText `
    $ResticPasswordRepeatSecure

try {
    if ($ResticPassword.Length -lt 20) {
        throw "Use a restic encryption password of at least 20 characters."
    }
    if ($ResticPassword -cne $ResticPasswordRepeat) {
        throw "The restic encryption passwords did not match."
    }

    New-Item -ItemType Directory -Path $Root, $BinDirectory -Force |
        Out-Null
    Set-PrivateAcl $Root

    $DownloadDirectory = Join-Path $env:TEMP (
        "ghos-restic-" + [Guid]::NewGuid().ToString("N")
    )
    New-Item -ItemType Directory -Path $DownloadDirectory | Out-Null
    try {
        $ArchivePath = Join-Path $DownloadDirectory $ResticArchiveName
        [Net.ServicePointManager]::SecurityProtocol = `
            [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri $ResticDownload `
            -OutFile $ArchivePath

        $ActualHash = (Get-FileHash $ArchivePath -Algorithm SHA256).Hash
        if ($ActualHash -ne $ResticArchiveSha256) {
            throw "The downloaded restic archive failed SHA-256 verification."
        }

        Expand-Archive -Path $ArchivePath -DestinationPath $DownloadDirectory `
            -Force
        $ExtractedBinary = Get-ChildItem $DownloadDirectory `
            -Filter "restic*.exe" | Select-Object -First 1
        if ($null -eq $ExtractedBinary) {
            throw "The restic executable was not found in the verified archive."
        }
        Copy-Item $ExtractedBinary.FullName `
            (Join-Path $BinDirectory "restic.exe") -Force
    }
    finally {
        Remove-Item $DownloadDirectory -Recurse -Force `
            -ErrorAction SilentlyContinue
    }

    Copy-Item $RunnerSource $RunnerPath -Force

    $ProbePath = Join-Path $Root "recovery-probe.txt"
    @(
        "Green Hills CounterPoint cloud recovery probe"
        "Created: $([DateTime]::UtcNow.ToString("o"))"
        "ID: $([Guid]::NewGuid().ToString("N"))"
    ) | Set-Content -Path $ProbePath -Encoding UTF8

    $Config = [ordered]@{
        Repository            = (
            "s3:https://$Endpoint/$Bucket/counterpoint"
        )
        Region                = $Region
        HostTag               = $env:COMPUTERNAME
        SourcePaths           = $SourcePaths
        ProbePath             = $ProbePath
        ProbeSha256           = (
            Get-FileHash $ProbePath -Algorithm SHA256
        ).Hash
        MinimumFileAgeMinutes = 60
        KeepDaily             = 14
        KeepWeekly            = 8
        KeepMonthly           = 12
        InstalledRestic       = $ResticVersion
    }
    $Config | ConvertTo-Json -Depth 4 |
        Set-Content -Path $ConfigPath -Encoding UTF8

    Add-Type -AssemblyName System.Security
    $Secrets = [ordered]@{
        AwsAccessKeyId     = Protect-MachineValue $AccessKeyId
        AwsSecretAccessKey = Protect-MachineValue $AccessKey
        ResticPassword     = Protect-MachineValue $ResticPassword
    }
    $Secrets | ConvertTo-Json |
        Set-Content -Path $SecretsPath -Encoding UTF8
    Set-PrivateAcl $Root

    if (-not [Diagnostics.EventLog]::SourceExists("GHOS CounterPoint Backup")) {
        New-EventLog -LogName Application -Source "GHOS CounterPoint Backup"
    }

    $PowerShell = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
    & $PowerShell -NoProfile -ExecutionPolicy Bypass `
        -File $RunnerPath -Mode Initialize
    if ($LASTEXITCODE -ne 0) {
        throw "Repository initialization failed. Review the latest log in $Root\logs."
    }

    $Principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" `
        -LogonType ServiceAccount -RunLevel Highest
    $Settings = New-ScheduledTaskSettingsSet `
        -StartWhenAvailable `
        -MultipleInstances IgnoreNew `
        -ExecutionTimeLimit (New-TimeSpan -Hours 12) `
        -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Hours 1)

    $BackupAction = New-ScheduledTaskAction -Execute $PowerShell -Argument (
        "-NoProfile -ExecutionPolicy Bypass -File `"$RunnerPath`" -Mode Backup"
    )
    $BackupTrigger = New-ScheduledTaskTrigger -Daily -At "1:30 AM"
    Register-ScheduledTask -TaskName "GHOS CounterPoint Cloud Backup" `
        -Action $BackupAction -Trigger $BackupTrigger `
        -Principal $Principal -Settings $Settings -Force | Out-Null

    $MaintenanceAction = New-ScheduledTaskAction -Execute $PowerShell -Argument (
        "-NoProfile -ExecutionPolicy Bypass -File `"$RunnerPath`" " +
        "-Mode Maintenance"
    )
    $MaintenanceTrigger = New-ScheduledTaskTrigger -Weekly `
        -DaysOfWeek Sunday -At "4:00 AM"
    Register-ScheduledTask `
        -TaskName "GHOS CounterPoint Cloud Backup Maintenance" `
        -Action $MaintenanceAction -Trigger $MaintenanceTrigger `
        -Principal $Principal -Settings $Settings -Force | Out-Null

    $RestoreAction = New-ScheduledTaskAction -Execute $PowerShell -Argument (
        "-NoProfile -ExecutionPolicy Bypass -File `"$RunnerPath`" " +
        "-Mode RestoreTest"
    )
    $RestoreTrigger = New-ScheduledTaskTrigger -Weekly `
        -DaysOfWeek Sunday -At "6:00 AM"
    Register-ScheduledTask `
        -TaskName "GHOS CounterPoint Cloud Backup Restore Test" `
        -Action $RestoreAction -Trigger $RestoreTrigger `
        -Principal $Principal -Settings $Settings -Force | Out-Null

    $WatchdogAction = New-ScheduledTaskAction -Execute $PowerShell -Argument (
        "-NoProfile -ExecutionPolicy Bypass -File `"$RunnerPath`" " +
        "-Mode Watchdog"
    )
    $WatchdogTrigger = New-ScheduledTaskTrigger -Daily -At "9:00 AM"
    Register-ScheduledTask `
        -TaskName "GHOS CounterPoint Cloud Backup Watchdog" `
        -Action $WatchdogAction -Trigger $WatchdogTrigger `
        -Principal $Principal -Settings $Settings -Force | Out-Null

    Write-Host ""
    Write-Host "CounterPoint cloud backup installed successfully." `
        -ForegroundColor Green
    Write-Host "Daily backup: 1:30 AM (with hourly retries if files are active)"
    Write-Host "Weekly maintenance: Sunday at 4:00 AM"
    Write-Host "Weekly disposable restore test: Sunday at 6:00 AM"
    Write-Host "Freshness watchdog: daily at 9:00 AM"
    Write-Host "Start the initial upload with:"
    Write-Host (
        "  Start-ScheduledTask -TaskName " +
        "'GHOS CounterPoint Cloud Backup'"
    ) -ForegroundColor Cyan
}
finally {
    $AccessKey = $null
    $ResticPassword = $null
    $ResticPasswordRepeat = $null
    [GC]::Collect()
}
