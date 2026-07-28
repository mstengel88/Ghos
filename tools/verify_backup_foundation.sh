#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
backup_root="$repo_root/ops/backups"

scripts=(
  "$backup_root/install.sh"
  "$backup_root/lib/backup-common.sh"
  "$backup_root/bin/ghos-backup"
  "$backup_root/bin/ghos-backup-init"
  "$backup_root/bin/ghos-backup-maintenance"
  "$backup_root/bin/ghos-backup-restore-drill"
  "$backup_root/bin/ghos-backup-watchdog"
  "$repo_root/tools/test_backup_integration.sh"
)

for script in "${scripts[@]}"; do
  bash -n "$script"
done

for unit in "$backup_root"/systemd/*.service "$backup_root"/systemd/*.timer; do
  grep -q '^\[Unit\]' "$unit"
done

grep -q '^/opt/ghos/postgres/\*\*$' "$backup_root/config/excludes.txt"
grep -q '^ghos|' "$backup_root/config/databases.conf.example"
grep -q '^local|' "$backup_root/config/repositories.conf.example"
grep -q '^offsite|' "$backup_root/config/repositories.conf.example"

if grep -R -E \
  '(POSTGRES_PASSWORD|RESTIC_PASSWORD|AWS_SECRET_ACCESS_KEY)=[^$[:space:]#]+' \
  "$backup_root" --exclude='*.example' >/dev/null; then
  printf 'A committed backup file appears to contain a secret.\n' >&2
  exit 1
fi

printf 'Backup foundation static verification passed.\n'
