#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
lab_root="${repo_root}/migration/supabase/runtime"
upstream_root="${lab_root}/upstream"
stack_root="${lab_root}/stack"
supabase_commit="c9ed51c99ea088b9cafbddcf4d0881445ffc7985"

if [[ -e "${upstream_root}" || -e "${stack_root}" ]]; then
  echo "The local lab already exists at ${lab_root}."
  echo "Nothing was overwritten. Move it aside intentionally before preparing a new lab."
  exit 1
fi

mkdir -p "${lab_root}"

git clone \
  --filter=blob:none \
  --no-checkout \
  https://github.com/supabase/supabase.git \
  "${upstream_root}"

git -C "${upstream_root}" sparse-checkout init --cone
git -C "${upstream_root}" sparse-checkout set docker
git -C "${upstream_root}" checkout "${supabase_commit}"

mkdir -p "${stack_root}"
cp -R "${upstream_root}/docker/." "${stack_root}/"
cp "${stack_root}/.env.example" "${stack_root}/.env"

{
  echo "repository=https://github.com/supabase/supabase.git"
  echo "commit=${supabase_commit}"
  echo "prepared_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
} > "${lab_root}/UPSTREAM_COMMIT"

chmod 600 "${stack_root}/.env"

echo
echo "Supabase compatibility lab prepared."
echo "Pinned upstream commit: ${supabase_commit}"
echo "Stack directory: ${stack_root}"
echo
echo "The stack has NOT been started."
echo "Before starting it:"
echo "  1. Start Docker Desktop."
echo "  2. Generate and review secrets using the official utilities in ${stack_root}/utils."
echo "  3. Set local-only URLs and unique passwords in ${stack_root}/.env."
echo "  4. Validate with: docker compose --env-file .env config"
