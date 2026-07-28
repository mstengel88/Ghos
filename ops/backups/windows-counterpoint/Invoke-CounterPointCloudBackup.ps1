[CmdletBinding()]
param(
    [ValidateSet(
        "Initialize",
        "Backup",
        "Maintenance",
        "RestoreTest",
        "Watchdog",
        "Status"
    )]
    [string]$Mode = "Backup"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$Root = "C:\ProgramData\GreenHills\CounterPointCloudBackup"
$ConfigPath = Join-Path $Root "config.json"
$SecretsPath = Join-Path $Root "secrets.json"
$ResticPath = Join-Path $Root "bin\restic.exe"
$LogDirectory = Join-Path $Root "logs"
$StatusPath = Join-Path $Root "status.json"
$LastBackupSuccessPath = Join-Path $Root "last-backup-success.json"
$EventSource = "GHOS CounterPoint Backup"
$Script:BackupFileListPath = $null

function Unprotect-MachineValue {
    param([Parameter(Mandatory = $true)][string]$CipherText)

    $ProtectedBytes = [Convert]::FromBase64String($CipherText)
    $PlainBytes = [Security.Cryptography.ProtectedData]::Unprotect(
        $ProtectedBytes,
        $null,
        [Security.Cryptography.DataProtectionScope]::LocalMachine
    )

    try {
        return [Text.Encoding]::UTF8.GetString($PlainBytes)
    }
    finally {
        [Array]::Clear($PlainBytes, 0, $PlainBytes.Length)
    }
}

function Write-BackupStatus {
    param(
        [Parameter(Mandatory = $true)][string]$State,
        [Parameter(Mandatory = $true)][string]$Operation,
        [Parameter(Mandatory = $true)][string]$Message
    )

    $Status = [ordered]@{
        UpdatedAtUtc = [DateTime]::UtcNow.ToString("o")
        State        = $State
        Operation    = $Operation
        Message      = $Message
    }

    $Status | ConvertTo-Json | Set-Content -Path $StatusPath -Encoding UTF8
    if ($State -eq "Success" -and $Operation -eq "Backup") {
        $Status | ConvertTo-Json |
            Set-Content -Path $LastBackupSuccessPath -Encoding UTF8
    }

    try {
        if ([Diagnostics.EventLog]::SourceExists($EventSource)) {
            $EntryType = if ($State -eq "Success") {
                "Information"
            }
            else {
                "Error"
            }
            Write-EventLog -LogName Application -Source $EventSource `
                -EntryType $EntryType -EventId 4200 `
                -Message "$Operation`: $Message"
        }
    }
    catch {
        # File status is authoritative; Event Log reporting is best effort.
    }
}

function Invoke-Restic {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & $ResticPath @Arguments 2>&1 | Tee-Object -FilePath $Script:LogPath -Append
    if ($LASTEXITCODE -ne 0) {
        throw "restic exited with code $LASTEXITCODE."
    }
}

if (-not (Test-Path $ConfigPath) -or -not (Test-Path $SecretsPath)) {
    throw "CounterPoint cloud backup is not configured. Run the installer first."
}
if (-not (Test-Path $ResticPath)) {
    throw "restic is not installed at $ResticPath."
}

Add-Type -AssemblyName System.Security
$Config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
$Secrets = Get-Content $SecretsPath -Raw | ConvertFrom-Json

$env:AWS_ACCESS_KEY_ID = Unprotect-MachineValue $Secrets.AwsAccessKeyId
$env:AWS_SECRET_ACCESS_KEY = Unprotect-MachineValue $Secrets.AwsSecretAccessKey
$env:RESTIC_PASSWORD = Unprotect-MachineValue $Secrets.ResticPassword
$env:AWS_DEFAULT_REGION = $Config.Region
$env:RESTIC_REPOSITORY = $Config.Repository
$env:RESTIC_CACHE_DIR = Join-Path $Root "cache"

New-Item -ItemType Directory -Path $LogDirectory, $env:RESTIC_CACHE_DIR -Force |
    Out-Null
$Script:LogPath = Join-Path $LogDirectory (
    "{0}-{1}.log" -f $Mode.ToLowerInvariant(), (Get-Date -Format "yyyyMMdd-HHmmss")
)

$Mutex = New-Object Threading.Mutex(
    $false,
    "Global\GreenHillsCounterPointCloudBackup"
)
$HasMutex = $false

try {
    $HasMutex = $Mutex.WaitOne(0)
    if (-not $HasMutex) {
        throw "Another CounterPoint cloud backup operation is already running."
    }

    switch ($Mode) {
        "Initialize" {
            # A missing repository is expected on the first run. Windows
            # PowerShell converts native stderr into an error record, so
            # temporarily avoid Stop semantics while probing for the config.
            $PreviousErrorActionPreference = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            & $ResticPath snapshots --compact 1> $null 2> $null
            $RepositoryProbeExitCode = $LASTEXITCODE
            $ErrorActionPreference = $PreviousErrorActionPreference

            if ($RepositoryProbeExitCode -ne 0) {
                Invoke-Restic @("init")
            }
            Invoke-Restic @("check")
            Write-BackupStatus "Success" $Mode "Repository initialized and checked."
        }

        "Backup" {
            $Cutoff = (Get-Date).AddMinutes(-[int]$Config.MinimumFileAgeMinutes)
            $StableFiles = New-Object Collections.Generic.List[string]
            $SkippedFiles = New-Object Collections.Generic.List[object]

            foreach ($SourcePath in $Config.SourcePaths) {
                if (-not (Test-Path $SourcePath -PathType Container)) {
                    throw "Required backup source is unavailable: $SourcePath"
                }

                foreach ($File in Get-ChildItem $SourcePath -File -Recurse `
                    -ErrorAction Stop) {
                    if ($File.LastWriteTime -le $Cutoff) {
                        $StableFiles.Add($File.FullName)
                    }
                    else {
                        $SkippedFiles.Add($File)
                    }
                }
            }

            if ($StableFiles.Count -eq 0) {
                throw (
                    "No completed backup files are old enough to upload. " +
                    "The scheduled task will retry."
                )
            }

            $StableFiles.Add([string]$Config.ProbePath)
            $Script:BackupFileListPath = Join-Path $Root (
                "backup-files-" + [Guid]::NewGuid().ToString("N") + ".txt"
            )
            $Utf8WithoutBom = New-Object Text.UTF8Encoding($false)
            [IO.File]::WriteAllLines(
                $Script:BackupFileListPath,
                $StableFiles,
                $Utf8WithoutBom
            )

            if ($SkippedFiles.Count -gt 0) {
                (
                    "Skipping {0} file(s) modified after {1:u}; they will be " +
                    "included after they are no longer changing." -f
                    $SkippedFiles.Count, $Cutoff
                ) | Tee-Object -FilePath $Script:LogPath -Append

                $SkippedFiles |
                    Select-Object FullName, LastWriteTime, Length |
                    Format-Table -AutoSize |
                    Out-String |
                    Tee-Object -FilePath $Script:LogPath -Append
            }

            $Arguments = @(
                "backup",
                "--host", $Config.HostTag,
                "--tag", "counterpoint-offsite",
                "--tag", (Get-Date -Format "yyyyMMdd"),
                "--files-from-verbatim", $Script:BackupFileListPath,
                "--exclude", "*.tmp",
                "--exclude", "*.temp",
                "--exclude", "*.lock",
                "--exclude", "~*"
            )
            Invoke-Restic $Arguments
            Invoke-Restic @(
                "snapshots",
                "--latest", "1",
                "--host", $Config.HostTag,
                "--tag", "counterpoint-offsite",
                "--compact"
            )
            Write-BackupStatus "Success" $Mode (
                "{0} completed CounterPoint and SQL backup files uploaded; " +
                "{1} active file(s) safely deferred." -f
                ($StableFiles.Count - 1), $SkippedFiles.Count
            )
        }

        "Maintenance" {
            Invoke-Restic @(
                "forget",
                "--host", $Config.HostTag,
                "--tag", "counterpoint-offsite",
                "--group-by", "host,tags",
                "--keep-daily", [string]$Config.KeepDaily,
                "--keep-weekly", [string]$Config.KeepWeekly,
                "--keep-monthly", [string]$Config.KeepMonthly,
                "--prune"
            )
            Invoke-Restic @("check", "--read-data-subset=5%")
            Write-BackupStatus "Success" $Mode "Retention and repository check completed."
        }

        "RestoreTest" {
            $RestoreDirectory = Join-Path $Root (
                "restore-test-" + [Guid]::NewGuid().ToString("N")
            )
            New-Item -ItemType Directory -Path $RestoreDirectory | Out-Null
            try {
                Invoke-Restic @(
                    "restore", "latest",
                    "--host", $Config.HostTag,
                    "--tag", "counterpoint-offsite",
                    "--target", $RestoreDirectory,
                    "--include", "**/recovery-probe.txt"
                )
                $RestoredProbe = Get-ChildItem $RestoreDirectory `
                    -Filter "recovery-probe.txt" -File -Recurse |
                    Select-Object -First 1
                if ($null -eq $RestoredProbe) {
                    throw "The recovery probe was not restored."
                }
                $RestoredHash = (
                    Get-FileHash $RestoredProbe.FullName -Algorithm SHA256
                ).Hash
                if ($RestoredHash -ne $Config.ProbeSha256) {
                    throw "The restored recovery probe failed SHA-256 validation."
                }
                Write-BackupStatus "Success" $Mode (
                    "A file was restored into a disposable directory and validated."
                )
            }
            finally {
                Remove-Item $RestoreDirectory -Recurse -Force `
                    -ErrorAction SilentlyContinue
            }
        }

        "Watchdog" {
            if (-not (Test-Path $LastBackupSuccessPath)) {
                throw "No successful CounterPoint cloud backup has been recorded."
            }
            $LastSuccess = Get-Content $LastBackupSuccessPath -Raw |
                ConvertFrom-Json
            $LastSuccessUtc = [DateTime]::Parse(
                $LastSuccess.UpdatedAtUtc,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind
            ).ToUniversalTime()
            $Age = [DateTime]::UtcNow - $LastSuccessUtc
            if ($Age.TotalHours -gt 36) {
                throw (
                    "The latest successful CounterPoint cloud backup is " +
                    "$([Math]::Round($Age.TotalHours, 1)) hours old."
                )
            }
            Write-BackupStatus "Success" $Mode (
                "Latest successful backup is " +
                "$([Math]::Round($Age.TotalHours, 1)) hours old."
            )
        }

        "Status" {
            if (Test-Path $StatusPath) {
                Get-Content $StatusPath -Raw
            }
            else {
                throw "No completed backup status exists yet."
            }
            Invoke-Restic @(
                "snapshots",
                "--latest", "3",
                "--host", $Config.HostTag,
                "--compact"
            )
        }
    }
}
catch {
    Write-BackupStatus "Failure" $Mode $_.Exception.Message
    $ErrorText = $_ | Out-String
    $ErrorText | Add-Content -Path $Script:LogPath -Encoding UTF8
    [Console]::Error.WriteLine($ErrorText)
    exit 1
}
finally {
    if ($null -ne $Script:BackupFileListPath) {
        Remove-Item $Script:BackupFileListPath -Force `
            -ErrorAction SilentlyContinue
    }
    if ($HasMutex) {
        $Mutex.ReleaseMutex()
    }
    $Mutex.Dispose()
    Remove-Item Env:\AWS_ACCESS_KEY_ID -ErrorAction SilentlyContinue
    Remove-Item Env:\AWS_SECRET_ACCESS_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:\RESTIC_PASSWORD -ErrorAction SilentlyContinue
}
