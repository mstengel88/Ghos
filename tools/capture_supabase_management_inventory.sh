#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_root="${repo_root}/migration/supabase/generated/managed"
mkdir -p "${output_root}"

project_refs=(
  dlayrpnmfnbjlxgnkczv
  caegybyfdkmgjrygnavg
  kryirjstfeksxotyabis
  dbyxbgbkokcddgeybjmf
  mtntrlbuhcbdrngiubdu
  bnethnlrhwcjgjgjvoxz
)

command -v supabase >/dev/null
command -v jq >/dev/null

supabase projects list --output json |
  jq 'map({
    ref,
    name,
    region,
    status,
    created_at,
    postgres_engine: .database.postgres_engine,
    postgres_version: .database.version
  })' > "${output_root}/projects.json"

for ref in "${project_refs[@]}"; do
  project_root="${output_root}/${ref}"
  mkdir -p "${project_root}"

  supabase functions list --project-ref "${ref}" --output json |
    jq 'map({
      name,
      slug,
      status,
      version,
      verify_jwt,
      created_at,
      updated_at
    })' > "${project_root}/functions.json"

  supabase secrets list --project-ref "${ref}" --output json |
    jq 'map(.name) | sort' > "${project_root}/edge-function-secret-names.json"
done

echo "Secret-safe managed inventory written to ${output_root}"
