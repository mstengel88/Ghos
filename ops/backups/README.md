# GHOS backup and disaster-recovery foundation

This directory defines the backup standard for GHOS and every application that
will move to GHSSERVER. It deliberately separates database-consistent exports
from file snapshots.

## Recovery layers

1. **Logical application backups** — PostgreSQL custom-format dumps, database
   globals, Docker volume exports, application configuration, assets, and
   self-hosted Supabase Storage objects.
2. **Two encrypted restic repositories** — one on storage physically separate
   from the GHOS VM and one off site.
3. **Host/VM recovery** — Acronis protects the Windows host, Hyper-V
   configuration, and VM disks. This is not a replacement for logical database
   backups.

The backup is unsuccessful if any configured database dump, volume export, or
repository write fails.

## Required production layout

Do not put the only backup repository under `/opt/ghos`, inside the GHOS VHDX,
or on the same RAID volume as the live server.

Recommended targets:

- `local`: a dedicated NAS/backup disk mounted read-write only for the backup
  service, for example `/mnt/ghos-backup/restic`.
- `offsite`: a private restic-compatible object-storage repository with a
  separate account, MFA, and billing/capacity alerts.

The repository password must be unique, stored in a password manager, printed
into the sealed recovery envelope, and copied to neither Git nor the repository
it protects. Losing it makes the encrypted backup unrecoverable.

## Install on the Ubuntu GHOS VM

Install prerequisites:

```bash
sudo apt update
sudo apt install -y restic
```

Copy the tooling and example configuration:

```bash
cd /opt/ghos
sudo ./ops/backups/install.sh
```

Edit the root-only files under `/etc/ghos-backup/`:

- `backup.env`
- `repositories.conf`
- `databases.conf`
- `volume-exports.conf`
- `source-paths.conf`

Create each repository password file with mode `0600`. If an object-storage
backend needs credentials, put them in its repository environment file, also
with mode `0600`.

Initialize and validate both repositories:

```bash
sudo /usr/local/sbin/ghos-backup-init
```

Run the first backup and restore drill manually:

```bash
sudo /usr/local/sbin/ghos-backup
sudo /usr/local/sbin/ghos-backup-restore-drill
```

Only after both succeed, enable the schedules:

```bash
sudo systemctl enable --now ghos-backup.timer
sudo systemctl enable --now ghos-backup-maintenance.timer
sudo systemctl enable --now ghos-backup-restore-drill.timer
sudo systemctl enable --now ghos-backup-watchdog.timer
```

Inspect schedules and results:

```bash
systemctl list-timers 'ghos-backup*'
systemctl status ghos-backup.service
sudo cat /var/lib/ghos-backup/status/last-success
```

## Default schedule and retention

- Backup: every 6 hours, including a missed run shortly after boot.
- Repository integrity/retention: weekly.
- Disposable PostgreSQL restore drill: monthly.
- Freshness watchdog: hourly; alerts if no successful backup exists or the
  latest one is more than eight hours old.
- Retention: 24 hourly, 14 daily, 8 weekly, and 12 monthly snapshots.

Adjust these only after considering the maximum acceptable data loss and
available capacity.

## Adding an application

Add one line to `databases.conf` for every PostgreSQL database:

```text
name|compose-file|compose-service|database-user|database-name|postgres-image
```

Add bind-mounted application files or Supabase Storage roots to
`source-paths.conf`. Add named-volume or container-only paths to
`volume-exports.conf`:

```text
name|compose-file|compose-service|path-inside-container
```

For self-hosted Supabase, back up all of the following:

- the PostgreSQL database;
- Storage object bytes (not only the `storage.objects` metadata table);
- `.env`, Compose configuration, functions, migrations, and custom services;
- SMTP/OAuth/JWT configuration and secrets inside the encrypted backup;
- any S3-compatible Storage bucket as its own protected source.

Managed Supabase projects remain under Supabase's platform backup boundary until
cutover. Before cutover, retain a `supabase db dump` migration export and a
separate Storage export; a database dump does not contain Storage object bytes.

## Restore procedure

Never restore over the live database first.

1. Declare an incident and stop application writes.
2. Restore the selected restic snapshot into a new empty directory.
3. Verify `SHA256SUMS`.
4. Restore the database into a clean, isolated PostgreSQL instance.
5. Validate migrations, record counts, authentication, RLS, critical workflows,
   and Storage object hashes.
6. Restore configuration and Storage to a new application instance.
7. Switch traffic only after acceptance tests pass.

The automated drill proves that the most recent GHOS dump can create and query a
new PostgreSQL database. Supabase restores require the project-specific clean
room tests already kept under `migration/supabase/`.

## Windows host and Hyper-V checklist

Acronis must protect:

- the Windows Server system state and production volumes;
- Hyper-V VM configuration and the entire GHOS VHDX;
- any future data-services VM;
- application files that remain on the Windows host.

At least one Acronis copy must be off the R640. Confirm application-aware/VSS
handling, encryption, retention, failed-job alerts, and quarterly bare-metal or
alternate-host restore testing. Do not count an attached USB disk as the only
off-host copy.

## Quarterly recovery test

Quarterly, perform a documented recovery into isolated infrastructure:

- recover the VM or build a clean replacement;
- restore GHOS and one Supabase project;
- verify login, Shopify sync, quote creation, delivery sync, files, and audit
  history;
- record elapsed recovery time and every manual correction;
- update this runbook and the sealed recovery information.
