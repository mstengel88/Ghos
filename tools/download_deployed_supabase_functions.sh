#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export_root="${repo_root}/migration/supabase/exports"

project_refs=(
  dlayrpnmfnbjlxgnkczv
  caegybyfdkmgjrygnavg
  kryirjstfeksxotyabis
  dbyxbgbkokcddgeybjmf
  mtntrlbuhcbdrngiubdu
  bnethnlrhwcjgjgjvoxz
)

command -v supabase >/dev/null
mkdir -p "${export_root}"

for ref in "${project_refs[@]}"; do
  workdir="${export_root}/${ref}"
  mkdir -p "${workdir}/supabase"

  function_count="$(
    supabase functions list --project-ref "${ref}" --output json |
      jq 'length'
  )"

  if [[ "${function_count}" == "0" ]]; then
    echo "${ref}: no deployed Edge Functions"
    continue
  fi

  supabase functions download \
    --project-ref "${ref}" \
    --use-api \
    --workdir "${workdir}" \
    --yes

  echo "${ref}: downloaded ${function_count} deployed Edge Function(s)"
done

echo "Downloads are stored in the ignored directory ${export_root}"
