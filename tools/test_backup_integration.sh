#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
test_root="$(mktemp -d "${TMPDIR:-/tmp}/ghos-backup-test.XXXXXXXX")"
compose_file="$test_root/compose.yml"
config_dir="$test_root/config"
state_dir="$test_root/state"
container_name="ghos-backup-test-postgres"

cleanup() {
  docker compose -f "$compose_file" down --volumes >/dev/null 2>&1 || true
  if [[ -d "$test_root" && "$test_root" == *"/ghos-backup-test."* ]]; then
    find "$test_root" -depth -delete
  fi
}
trap cleanup EXIT

mkdir -p "$config_dir" "$test_root/source" "$test_root/bin"
printf 'configuration fixture\n' >"$test_root/source/application.conf"
printf 'volume fixture\n' >"$test_root/source/volume-file"

cat >"$compose_file" <<YAML
services:
  postgres:
    image: postgres:16
    container_name: $container_name
    environment:
      POSTGRES_USER: testadmin
      POSTGRES_PASSWORD: integration-test-only
      POSTGRES_DB: testdb
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U testadmin -d testdb"]
      interval: 1s
      timeout: 1s
      retries: 30
    volumes:
      - $test_root/source:/fixture:ro
YAML

cat >"$config_dir/backup.env" <<EOF
BACKUP_ROOT=$state_dir
BACKUP_HOST_TAG=ghos-integration-test
BACKUP_CONFIG_DIR=$config_dir
RESTORE_DRILL_REPOSITORY=offsite
RESTORE_DRILL_DATABASE=ghos
EOF
printf 'integration-restic-password\n' >"$config_dir/restic.password"
cat >"$config_dir/repositories.conf" <<EOF
offsite|$test_root/offsite-repository|$config_dir/restic.password|
EOF
cat >"$config_dir/databases.conf" <<EOF
ghos|$compose_file|postgres|testadmin|testdb|postgres:16
EOF
cat >"$config_dir/volume-exports.conf" <<EOF
test-volume|$compose_file|postgres|/fixture
EOF
printf '%s\n' "$test_root/source/application.conf" >"$config_dir/source-paths.conf"
printf '# no integration-test exclusions\n' >"$config_dir/excludes.txt"
chmod 600 "$config_dir"/*

# macOS does not ship util-linux flock. Production Ubuntu uses the real command;
# this single-process test shim only supplies the already-serialized test run.
if ! command -v flock >/dev/null 2>&1; then
  cat >"$test_root/bin/flock" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
  chmod +x "$test_root/bin/flock"
fi

docker compose -f "$compose_file" up -d --wait
docker compose -f "$compose_file" exec -T postgres \
  psql -v ON_ERROR_STOP=1 -U testadmin -d testdb \
  -c 'create table backup_probe (id integer primary key, value text not null);' \
  -c "insert into backup_probe values (1, 'restorable');" >/dev/null

export GHOS_BACKUP_ALLOW_NON_ROOT=true
export GHOS_BACKUP_CONFIG_DIR="$config_dir"
export GHOS_BACKUP_CONFIG_FILE="$config_dir/backup.env"
export PATH="$test_root/bin:$PATH"

"$repo_root/ops/backups/bin/ghos-backup-init"
"$repo_root/ops/backups/bin/ghos-backup"
"$repo_root/ops/backups/bin/ghos-backup-restore-drill"

test -s "$state_dir/status/last-success"
RESTIC_REPOSITORY="$test_root/offsite-repository" \
RESTIC_PASSWORD_FILE="$config_dir/restic.password" \
  restic snapshots --tag ghos-automatic --compact | grep -q ghos-integration-test

printf 'Backup integration test passed with one off-site repository and a database restore drill.\n'
