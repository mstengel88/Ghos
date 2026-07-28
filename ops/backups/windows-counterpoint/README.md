# CounterPoint and SQL off-site backup

This package copies the completed backup sets in `D:\Acronis Backups` and
`D:\SQLBackups` to a dedicated encrypted restic repository in Backblaze B2.
It does not modify, delete, reformat, or mount the external drive.

## Safety design

- Restic 0.19.1 is pinned and its Windows archive is verified against the
  publisher's SHA-256 digest before installation.
- B2 and repository credentials are encrypted with Windows DPAPI in
  `LocalMachine` scope and the installation directory is restricted to SYSTEM
  and local Administrators.
- The scheduled task runs as SYSTEM at 1:30 AM.
- Files modified during the preceding 60 minutes are safely deferred while
  every completed file is uploaded. A continuously updated SQL log cannot
  block the rest of the backup set.
- A named mutex prevents overlapping backup and maintenance operations.
- Retention keeps 14 daily, 8 weekly, and 12 monthly snapshots.
- Weekly maintenance prunes expired data and reads/checks a repository subset.
- A weekly disposable restore test recovers a known file and verifies its
  SHA-256 hash.
- A daily watchdog records an Application Event Log error if no successful
  upload exists or the latest upload is more than 36 hours old.

This is an off-site copy of backup artifacts. The existing Acronis and SQL
backup jobs remain responsible for creating application-consistent backups.

## GHOS dashboard status

After GHOS has a `GHOS_BACKUP_STATUS_INTEGRATION_SECRET`, copy the current
runner and `Set-CounterPointBackupStatusWebhook.ps1` to the Windows server.
Run the setup script in an elevated Windows PowerShell window and enter the
same integration secret when prompted. The secret is protected with
machine-scoped DPAPI and is never stored as plain text.

## Install

Create a separate private B2 bucket and a Read/Write application key restricted
to that bucket. Leave bucket Object Lock off because restic retention must
delete expired repository objects.

Download both PowerShell scripts into the same temporary directory on
GHSSERVER. Then run PowerShell as Administrator:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Install-CounterPointCloudBackup.ps1
```

The installer securely prompts for the B2 endpoint, bucket, application key ID,
application key, and a separate 20+ character restic encryption password.
Store the restic password in the company password manager and sealed recovery
record. Losing it makes the backup unrecoverable.

Do not display or stage either secret in Notepad, a command line, screenshots,
chat, or source control. Enter secrets only at the installer's hidden prompts.

## First upload and status

Start the initial upload:

```powershell
Start-ScheduledTask -TaskName "GHOS CounterPoint Cloud Backup"
```

Inspect task state:

```powershell
Get-ScheduledTask -TaskName "GHOS CounterPoint Cloud Backup" |
    Get-ScheduledTaskInfo
```

Inspect backup status and snapshots:

```powershell
& "C:\ProgramData\GreenHills\CounterPointCloudBackup\Invoke-CounterPointCloudBackup.ps1" `
    -Mode Status
```

Logs and the most recent status are stored under:

```text
C:\ProgramData\GreenHills\CounterPointCloudBackup
```

## Recovery

Never restore over the active external-drive backup folders. Restore into a new
empty directory, validate the archive or SQL backup, and only then begin an
application recovery. The restic password, B2 credentials, and access to a
Windows machine are all required.
