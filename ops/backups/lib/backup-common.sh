#!/usr/bin/env bash
set -Eeuo pipefail

CONFIG_DIR="${GHOS_BACKUP_CONFIG_DIR:-/etc/ghos-backup}"
CONFIG_FILE="${GHOS_BACKUP_CONFIG_FILE:-$CONFIG_DIR/backup.env}"

die() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || die "Required command not found: $1"
}

require_root() {
  if [[ "${GHOS_BACKUP_ALLOW_NON_ROOT:-false}" != "true" && "$EUID" -ne 0 ]]; then
    die "Run this command as root."
  fi
}

load_backup_config() {
  [[ -f "$CONFIG_FILE" ]] || die "Missing configuration: $CONFIG_FILE"
  # The file is root-owned and intentionally contains shell-compatible KEY=VALUE entries.
  # shellcheck disable=SC1090
  source "$CONFIG_FILE"

  BACKUP_ROOT="${BACKUP_ROOT:-/var/lib/ghos-backup}"
  BACKUP_HOST_TAG="${BACKUP_HOST_TAG:-ghos}"
  BACKUP_CONFIG_DIR="${BACKUP_CONFIG_DIR:-$CONFIG_DIR}"
  REPOSITORIES_FILE="${REPOSITORIES_FILE:-$BACKUP_CONFIG_DIR/repositories.conf}"
  DATABASES_FILE="${DATABASES_FILE:-$BACKUP_CONFIG_DIR/databases.conf}"
  VOLUME_EXPORTS_FILE="${VOLUME_EXPORTS_FILE:-$BACKUP_CONFIG_DIR/volume-exports.conf}"
  SOURCE_PATHS_FILE="${SOURCE_PATHS_FILE:-$BACKUP_CONFIG_DIR/source-paths.conf}"
  EXCLUDES_FILE="${EXCLUDES_FILE:-$BACKUP_CONFIG_DIR/excludes.txt}"
  STATUS_DIR="$BACKUP_ROOT/status"
  # Used by the executable that sources this library.
  # shellcheck disable=SC2034
  LOCK_FILE="$BACKUP_ROOT/backup.lock"
}

ensure_private_directory() {
  install -d -m 0700 "$1"
}

each_config_line() {
  local file="$1"
  [[ -f "$file" ]] || die "Missing configuration: $file"
  sed -e 's/[[:space:]]*$//' -e '/^[[:space:]]*#/d' -e '/^[[:space:]]*$/d' "$file"
}

iso_timestamp() {
  date -u +'%Y-%m-%dT%H:%M:%SZ'
}

load_repository() {
  local wanted="$1"
  local name repository password_file environment_file

  while IFS='|' read -r name repository password_file environment_file; do
    [[ "$name" == "$wanted" ]] || continue
    [[ -n "$repository" && -f "$password_file" ]] ||
      die "Repository '$name' is missing its location or password file."
    if [[ -n "${environment_file:-}" ]]; then
      [[ -f "$environment_file" ]] ||
        die "Repository '$name' environment file does not exist: $environment_file"
      # Repository clients such as restic read credentials from the process
      # environment. The root-only file contains shell-compatible assignments;
      # allexport makes those assignments available to child processes without
      # requiring users to add `export` to secret files manually.
      local allexport_was_enabled=false
      [[ "$-" == *a* ]] && allexport_was_enabled=true
      set -a
      # shellcheck disable=SC1090
      source "$environment_file"
      if [[ "$allexport_was_enabled" != "true" ]]; then
        set +a
      fi
    fi
    export RESTIC_REPOSITORY="$repository"
    export RESTIC_PASSWORD_FILE="$password_file"
    return 0
  done < <(each_config_line "$REPOSITORIES_FILE")

  die "Repository '$wanted' is not configured."
}

for_each_repository() {
  local callback="$1"
  local name repository password_file environment_file
  local repository_count=0

  while IFS='|' read -r name repository password_file environment_file; do
    [[ -n "$name" ]] || continue
    repository_count=$((repository_count + 1))
    (
      load_repository "$name"
      "$callback" "$name"
    ) || return $?
  done < <(each_config_line "$REPOSITORIES_FILE")

  (( repository_count > 0 )) ||
    die "No backup repositories are configured in $REPOSITORIES_FILE."
}

write_status() {
  local state="$1"
  local phase="$2"
  local message="$3"
  local now
  now="$(iso_timestamp)"
  ensure_private_directory "$STATUS_DIR"
  printf '%s\t%s\t%s\t%s\n' "$now" "$state" "$phase" "$message" >"$STATUS_DIR/last-run"
  if [[ "$state" == "success" ]]; then
    cp "$STATUS_DIR/last-run" "$STATUS_DIR/last-success"
  elif [[ "$state" == "failure" ]]; then
    cp "$STATUS_DIR/last-run" "$STATUS_DIR/last-failure"
  fi
}

notify_status() {
  local state="$1"
  local phase="$2"
  local message="$3"
  logger -t ghos-backup -- "$state [$phase] $message"

  [[ -n "${BACKUP_WEBHOOK_URL:-}" ]] || return 0
  [[ -n "${BACKUP_WEBHOOK_TOKEN:-}" ]] ||
    die "BACKUP_WEBHOOK_TOKEN is required when BACKUP_WEBHOOK_URL is configured."
  require_command curl
  local payload
  payload="$(
    python3 - "$state" "$phase" "$message" "$BACKUP_HOST_TAG" <<'PY'
import json
import sys
print(json.dumps({
    "status": sys.argv[1],
    "phase": sys.argv[2],
    "message": sys.argv[3],
    "host": sys.argv[4],
}))
PY
  )"
  curl --fail --silent --show-error --max-time 20 \
    -H 'Content-Type: application/json' \
    -H "X-GHOS-Backup-Key: $BACKUP_WEBHOOK_TOKEN" \
    --data "$payload" \
    "$BACKUP_WEBHOOK_URL" >/dev/null
}

repository_exists() {
  restic snapshots --compact >/dev/null 2>&1
}
