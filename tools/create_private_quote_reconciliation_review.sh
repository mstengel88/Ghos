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
canonical_database="ghos_reconcile_review_local_$run_id"
legacy_database="ghos_reconcile_review_quote_$run_id"
review_root="$repo_root/migration/supabase/secrets/reconciliation-review/$run_id"
review_json="$review_root/private-quotes.json"
review_html="$review_root/private-quote-review.html"
decision_csv="$review_root/quote-decisions.csv"
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
            'record_key', record_key,
            'classification', classification,
            'canonical_payload', canonical_payload,
            'legacy_payload', legacy_payload
          )
          order by classification, record_key
        ),
        '[]'::jsonb
      )
      from migration_reconcile.unresolved_records
      where table_name = 'custom_delivery_quotes';
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

if len(records) != 4:
    raise SystemExit(f"Expected exactly four quote reviews, found {len(records)}")

def value(payload, key):
    if not payload:
        return ""
    raw = payload.get(key)
    if raw is None:
        return ""
    if isinstance(raw, (dict, list)):
        return json.dumps(raw, indent=2, sort_keys=True)
    return str(raw)

fields = [
    ("Customer", "customer_name"),
    ("Company", "company_name"),
    ("Email", "customer_email"),
    ("Phone", "customer_phone"),
    ("Address", "address1"),
    ("City", "city"),
    ("State", "province"),
    ("Postal code", "postal_code"),
    ("Created", "created_at"),
    ("Updated", "updated_at"),
    ("Quote total (cents)", "quote_total_cents"),
    ("Service", "service_name"),
    ("Summary", "summary"),
    ("Created by", "created_by_name"),
    ("Creator email", "created_by_email"),
]

with csv_path.open("w", encoding="utf-8", newline="") as handle:
    writer = csv.writer(handle)
    writer.writerow([
        "quote_id",
        "classification",
        "allowed_decisions",
        "decision",
        "review_notes",
    ])
    for record in records:
        allowed = (
            "import_legacy|archive_legacy"
            if record["classification"] == "legacy_only"
            else "keep_canonical|merge_reviewed"
        )
        writer.writerow([
            record["record_key"],
            record["classification"],
            allowed,
            "",
            "",
        ])

sections = []
for index, record in enumerate(records, start=1):
    canonical = record.get("canonical_payload")
    legacy = record.get("legacy_payload")
    allowed = (
        ("import_legacy", "archive_legacy")
        if record["classification"] == "legacy_only"
        else ("keep_canonical", "merge_reviewed")
    )
    rows = []
    for label, key in fields:
        canonical_value = value(canonical, key)
        legacy_value = value(legacy, key)
        differs = canonical_value != legacy_value
        css_class = "diff" if differs else ""
        rows.append(
            f"<tr class='{css_class}'><th>{html.escape(label)}</th>"
            f"<td>{html.escape(canonical_value)}</td>"
            f"<td>{html.escape(legacy_value)}</td></tr>"
        )
    detail_rows = []
    for label, key in (
        ("Line items", "line_items"),
        ("Source breakdown", "source_breakdown"),
        ("Shipping details", "shipping_details"),
        ("Description", "description"),
    ):
        detail_rows.append(
            "<details><summary>{}</summary><div class='payload-grid'>"
            "<pre>{}</pre><pre>{}</pre></div></details>".format(
                html.escape(label),
                html.escape(value(canonical, key)),
                html.escape(value(legacy, key)),
            )
        )
    sections.append(
        f"""
        <section>
          <div class="quote-heading">
            <div>
              <span>Review {index} of {len(records)}</span>
              <h2>{html.escape(record["record_key"])}</h2>
            </div>
            <strong>{html.escape(record["classification"])}</strong>
          </div>
          <p class="decision">
            Record the decision in <code>quote-decisions.csv</code>.
            Allowed choices for this record:
            <code>{html.escape(allowed[0])}</code> or
            <code>{html.escape(allowed[1])}</code>.
          </p>
          <table>
            <thead><tr><th>Field</th><th>Local-Delivery</th><th>Quote Live</th></tr></thead>
            <tbody>{''.join(rows)}</tbody>
          </table>
          {''.join(detail_rows)}
        </section>
        """
    )

document = f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Private GHOS quote reconciliation review</title>
<style>
:root {{ color-scheme: light dark; font-family: Inter, system-ui, sans-serif; }}
body {{ margin: 0; background: #101812; color: #edf3e8; }}
main {{ width: min(1400px, calc(100% - 32px)); margin: 32px auto 80px; }}
header, section {{ background: #17241a; border: 1px solid #344b38; border-radius: 16px; }}
header {{ padding: 24px; margin-bottom: 20px; }}
section {{ padding: 20px; margin: 18px 0; }}
h1, h2, p {{ margin-top: 0; }}
.warning {{ color: #ffcf70; }}
.quote-heading {{ display: flex; justify-content: space-between; gap: 16px; }}
.quote-heading span {{ color: #9bc623; font-weight: 700; }}
.quote-heading strong {{ color: #9bc623; }}
.decision {{ padding: 12px; background: #20311f; border-left: 4px solid #9bc623; }}
table {{ width: 100%; border-collapse: collapse; table-layout: fixed; }}
th, td {{ border-bottom: 1px solid #344b38; padding: 9px; text-align: left; vertical-align: top; overflow-wrap: anywhere; }}
th:first-child {{ width: 18%; }}
tr.diff {{ background: rgba(255, 185, 80, .1); }}
details {{ margin-top: 12px; border: 1px solid #344b38; border-radius: 10px; padding: 12px; }}
summary {{ cursor: pointer; font-weight: 700; }}
.payload-grid {{ display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }}
pre {{ white-space: pre-wrap; overflow-wrap: anywhere; background: #0b120d; padding: 12px; border-radius: 8px; }}
code {{ color: #bfe84d; }}
@media (max-width: 800px) {{
  .payload-grid {{ grid-template-columns: 1fr; }}
  table {{ table-layout: auto; display: block; overflow-x: auto; }}
}}
</style>
</head>
<body>
<main>
<header>
  <h1>Private GHOS quote reconciliation review</h1>
  <p class="warning">Contains customer information. Keep this local-only
  directory out of email, chat, and Git.</p>
  <p>The exact workflow left four business decisions unresolved. Highlighted
  rows differ between Local-Delivery and Quote Live.</p>
</header>
{''.join(sections)}
</main>
</body>
</html>
"""
html_path.write_text(document, encoding="utf-8")
PY

chmod 600 "$review_json" "$review_html" "$decision_csv" "$aggregate_log"

printf 'Private quote review created:\n%s\n' "$review_html"
printf 'Decision worksheet:\n%s\n' "$decision_csv"
printf 'Disposable reconciliation databases were removed.\n'
