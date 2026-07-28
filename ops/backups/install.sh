#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

if [[ "$EUID" -ne 0 ]]; then
  printf 'Run this installer with sudo.\n' >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG_DIR=/etc/ghos-backup
BIN_DIR=/usr/local/lib/ghos-backup

command -v docker >/dev/null 2>&1 || {
  printf 'Docker is required.\n' >&2
  exit 1
}
command -v restic >/dev/null 2>&1 || {
  printf 'Install restic first: sudo apt install -y restic\n' >&2
  exit 1
}

install -d -m 0700 "$CONFIG_DIR" /var/lib/ghos-backup
install -d -m 0755 "$BIN_DIR/bin" "$BIN_DIR/lib"
install -m 0755 "$SCRIPT_DIR"/bin/* "$BIN_DIR/bin/"
install -m 0644 "$SCRIPT_DIR"/lib/backup-common.sh "$BIN_DIR/lib/"

for file in backup.env repositories.conf databases.conf volume-exports.conf source-paths.conf; do
  if [[ ! -e "$CONFIG_DIR/$file" ]]; then
    install -m 0600 "$SCRIPT_DIR/config/$file.example" "$CONFIG_DIR/$file"
  fi
done
if [[ ! -e "$CONFIG_DIR/excludes.txt" ]]; then
  install -m 0600 "$SCRIPT_DIR/config/excludes.txt" "$CONFIG_DIR/excludes.txt"
fi

# When installed from the GHOS application repository, connect the backup
# agent to the authenticated dashboard endpoint without printing the secret.
APP_ENV_FILE="${GHOS_APP_ENV_FILE:-$SCRIPT_DIR/../../.env}"
if [[ -r "$APP_ENV_FILE" ]]; then
  dashboard_secret="$(
    sed -n 's/^GHOS_BACKUP_STATUS_INTEGRATION_SECRET=//p' "$APP_ENV_FILE" |
      tail -n 1
  )"
  if [[ ${#dashboard_secret} -ge 32 ]]; then
    sed -i \
      -e '/^BACKUP_WEBHOOK_URL=/d' \
      -e '/^BACKUP_WEBHOOK_TOKEN=/d' \
      "$CONFIG_DIR/backup.env"
    printf '%s\n' \
      "BACKUP_WEBHOOK_URL=${GHOS_BACKUP_STATUS_URL:-http://127.0.0.1:8080/api/integrations/backup-status/ghos}" \
      "BACKUP_WEBHOOK_TOKEN=$dashboard_secret" \
      >>"$CONFIG_DIR/backup.env"
    chmod 0600 "$CONFIG_DIR/backup.env"
    dashboard_secret=
  fi
fi

for command_name in ghos-backup ghos-backup-init ghos-backup-maintenance ghos-backup-restore-drill ghos-backup-watchdog ghos-backup-configure-b2; do
  ln -sfn "$BIN_DIR/bin/$command_name" "/usr/local/sbin/$command_name"
done

install -m 0644 "$SCRIPT_DIR"/systemd/* /etc/systemd/system/
systemctl daemon-reload

printf '%s\n' \
  'Backup tooling installed but timers were not enabled.' \
  'Configure /etc/ghos-backup, initialize every repository, run a backup,' \
  'and pass the restore drill before enabling the timers.'
