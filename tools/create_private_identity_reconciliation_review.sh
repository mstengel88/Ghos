#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_admin="${SUPABASE_DATABASE_ADMIN:-supabase_admin}"
run_id="$(date -u +%Y%m%d_%H%M%S)"
canonical_database="ghos_identity_review_local_$run_id"
legacy_database="ghos_identity_review_quote_$run_id"
review_root="$repo_root/migration/supabase/secrets/identity-review/$run_id"
review_json="$review_root/private-identities.json"
review_html="$review_root/private-identity-review.html"
decision_csv="$review_root/identity-decisions.csv"
aggregate_log="$review_root/aggregate-reconciliation.log"

database_exists() {
  docker exec -i "$db_container" \
    psql -X -v ON_ERROR_STOP=1 -U "$database_admin" -d template1 -Atqc \
      "select 1 from pg_database where datname = '$1'" |
    grep -qx 1
}

drop_database() {
  local database_name="$1"
  if database_exists "$database_name"; then
    docker exec "$db_container" \
      dropdb -U "$database_admin" --if-exists --force "$database_name" \
      >/dev/null
  fi
}

cleanup() {
  set +e
  drop_database "$legacy_database"
  drop_database "$canonical_database"
}
trap cleanup EXIT

for command_name in docker python3; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'Required command not found: %s\n' "$command_name" >&2
    exit 1
  fi
done
if [[ "$db_container" != "supabase-db" ]]; then
  printf 'Refusing unexpected database container: %s\n' "$db_container" >&2
  exit 1
fi

mkdir -p "$review_root"
chmod 700 "$review_root"

GHOS_RECONCILE_CANONICAL_DATABASE="$canonical_database" \
GHOS_RECONCILE_LEGACY_DATABASE="$legacy_database" \
GHOS_RECONCILE_RETAIN_DATABASES=1 \
  "$repo_root/tools/reconcile_local_delivery_quote_live_snapshots.sh" \
  > "$aggregate_log" 2>&1

docker exec "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
    -d "$canonical_database" -Atqc "
      select coalesce(
        jsonb_agg(
          jsonb_build_object(
            'legacy_user_id', candidate.legacy_user_id,
            'canonical_user_id', candidate.canonical_user_id,
            'normalized_email', candidate.normalized_email,
            'uuid_rewrite_required', candidate.uuid_rewrite_required,
            'quote_creator_references', (
              select count(*)
              from migration_reconcile.source_rows quote
              where quote.source_project = 'quote_live'
                and quote.table_name = 'custom_delivery_quotes'
                and quote.payload ->> 'created_by_user_id' =
                  candidate.legacy_user_id::text
            ),
            'audit_actor_references', (
              select count(*)
              from migration_reconcile.source_rows audit
              where audit.source_project = 'quote_live'
                and audit.table_name = 'app_audit_log'
                and audit.payload ->> 'actor_user_id' =
                  candidate.legacy_user_id::text
            ),
            'canonical_profile', canonical.payload,
            'legacy_profile', legacy.payload
          )
          order by candidate.normalized_email
        ),
        '[]'::jsonb
      )
      from migration_reconcile.identity_map_candidates candidate
      join migration_reconcile.source_rows canonical
        on canonical.source_project = 'local_delivery'
        and canonical.table_name = 'app_user_profiles'
        and canonical.record_key = candidate.normalized_email
      join migration_reconcile.source_rows legacy
        on legacy.source_project = 'quote_live'
        and legacy.table_name = 'app_user_profiles'
        and legacy.record_key = candidate.normalized_email;
    " > "$review_json"

python3 - "$review_json" "$review_html" "$decision_csv" <<'PY'
import csv
import html
import json
import pathlib
import sys

source_path = pathlib.Path(sys.argv[1])
html_path = pathlib.Path(sys.argv[2])
csv_path = pathlib.Path(sys.argv[3])
records = json.loads(source_path.read_text(encoding="utf-8"))

rewrite_count = sum(bool(record["uuid_rewrite_required"]) for record in records)
quote_reference_count = sum(int(record["quote_creator_references"]) for record in records)
if len(records) != 11:
    raise SystemExit(f"Expected 11 identity candidates, found {len(records)}")
if rewrite_count != 2:
    raise SystemExit(f"Expected two UUID rewrites, found {rewrite_count}")
if quote_reference_count != 39:
    raise SystemExit(
        f"Expected 39 quote creator references, found {quote_reference_count}"
    )

with csv_path.open("w", encoding="utf-8", newline="") as handle:
    writer = csv.writer(handle)
    writer.writerow([
        "normalized_email",
        "legacy_user_id",
        "canonical_user_id",
        "uuid_rewrite_required",
        "quote_creator_references",
        "approved",
        "review_notes",
    ])
    for record in records:
        writer.writerow([
            record["normalized_email"],
            record["legacy_user_id"],
            record["canonical_user_id"],
            str(bool(record["uuid_rewrite_required"])).lower(),
            record["quote_creator_references"],
            "",
            "",
        ])

def display(payload, key):
    raw = (payload or {}).get(key)
    if raw is None:
        return ""
    if isinstance(raw, (dict, list)):
        return json.dumps(raw, indent=2, sort_keys=True)
    return str(raw)

sections = []
for index, record in enumerate(records, start=1):
    canonical = record["canonical_profile"]
    legacy = record["legacy_profile"]
    profile_rows = []
    for label, key in (
        ("Name", "name"),
        ("Email", "email"),
        ("Permissions", "permissions"),
        ("Created", "created_at"),
        ("Updated", "updated_at"),
    ):
        canonical_value = display(canonical, key)
        legacy_value = display(legacy, key)
        css_class = "diff" if canonical_value != legacy_value else ""
        profile_rows.append(
            f"<tr class='{css_class}'><th>{html.escape(label)}</th>"
            f"<td>{html.escape(canonical_value)}</td>"
            f"<td>{html.escape(legacy_value)}</td></tr>"
        )

    rewrite_label = (
        "UUID rewrite required"
        if record["uuid_rewrite_required"]
        else "UUID already matches"
    )
    sections.append(
        f"""
        <section>
          <div class="identity-heading">
            <div>
              <span>Review {index} of {len(records)}</span>
              <h2>{html.escape(record["normalized_email"])}</h2>
            </div>
            <strong>{html.escape(rewrite_label)}</strong>
          </div>
          <div class="ids">
            <p><b>Canonical:</b> <code>{html.escape(record["canonical_user_id"])}</code></p>
            <p><b>Legacy:</b> <code>{html.escape(record["legacy_user_id"])}</code></p>
            <p><b>Quote creator references:</b>
              {int(record["quote_creator_references"])}</p>
            <p><b>Audit actor references:</b>
              {int(record["audit_actor_references"])}</p>
          </div>
          <p class="decision">Set <code>approved</code> to <code>yes</code> or
          <code>no</code> in <code>identity-decisions.csv</code>. Approval means
          both profiles represent the same person and Quote Live references may
          be rewritten to the canonical Local-Delivery UUID.</p>
          <table>
            <thead><tr><th>Field</th><th>Local-Delivery</th><th>Quote Live</th></tr></thead>
            <tbody>{''.join(profile_rows)}</tbody>
          </table>
        </section>
        """
    )

document = f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Private GHOS identity reconciliation review</title>
<style>
:root {{ color-scheme: light dark; font-family: Inter, system-ui, sans-serif; }}
body {{ margin: 0; background: #101812; color: #edf3e8; }}
main {{ width: min(1400px, calc(100% - 32px)); margin: 32px auto 80px; }}
header, section {{ background: #17241a; border: 1px solid #344b38; border-radius: 16px; }}
header {{ padding: 24px; margin-bottom: 20px; }}
section {{ padding: 20px; margin: 18px 0; }}
h1, h2, p {{ margin-top: 0; }}
.warning {{ color: #ffcf70; }}
.identity-heading {{ display: flex; justify-content: space-between; gap: 16px; }}
.identity-heading span, .identity-heading strong {{ color: #9bc623; }}
.ids {{ display: grid; grid-template-columns: 1fr 1fr; gap: 4px 20px; }}
.decision {{ padding: 12px; background: #20311f; border-left: 4px solid #9bc623; }}
table {{ width: 100%; border-collapse: collapse; table-layout: fixed; }}
th, td {{ border-bottom: 1px solid #344b38; padding: 9px; text-align: left; vertical-align: top; overflow-wrap: anywhere; }}
th:first-child {{ width: 18%; }}
tr.diff {{ background: rgba(255, 185, 80, .1); }}
code {{ color: #bfe84d; overflow-wrap: anywhere; }}
@media (max-width: 800px) {{
  .ids {{ grid-template-columns: 1fr; }}
  table {{ table-layout: auto; display: block; overflow-x: auto; }}
}}
</style>
</head>
<body>
<main>
<header>
  <h1>Private GHOS identity reconciliation review</h1>
  <p class="warning">Contains employee identity information. Keep this
  local-only directory out of email, chat, and Git.</p>
  <p>Review all 11 normalized-email matches. Two require UUID rewrites and the
  complete approved map resolves 39 Quote Live creator references.</p>
</header>
{''.join(sections)}
</main>
</body>
</html>
"""
html_path.write_text(document, encoding="utf-8")
PY

chmod 600 "$review_json" "$review_html" "$decision_csv" "$aggregate_log"

printf 'Private identity review created:\n%s\n' "$review_html"
printf 'Identity decision worksheet:\n%s\n' "$decision_csv"
printf 'Disposable reconciliation databases were removed.\n'
