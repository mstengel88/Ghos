\set ON_ERROR_STOP on

create schema if not exists migration_reconcile;

revoke all on schema migration_reconcile from public;
revoke all on schema migration_reconcile from anon;
revoke all on schema migration_reconcile from authenticated;

create table if not exists migration_reconcile.import_batches (
  source_project text primary key
    check (source_project in ('local_delivery', 'quote_live')),
  extracted_at timestamptz not null,
  source_table_count integer not null check (source_table_count > 0),
  source_row_count bigint not null check (source_row_count > 0),
  manifest_sha256 text not null
    check (manifest_sha256 ~ '^[0-9a-f]{64}$'),
  loaded_at timestamptz not null default now()
);

create table if not exists migration_reconcile.source_rows (
  source_project text not null
    check (source_project in ('local_delivery', 'quote_live')),
  table_name text not null,
  record_key text not null,
  payload jsonb not null,
  payload_hash text generated always as (md5(payload::text)) stored,
  imported_at timestamptz not null default now(),
  primary key (source_project, table_name, record_key)
);

create table if not exists migration_reconcile.source_tables (
  source_project text not null
    check (source_project in ('local_delivery', 'quote_live')),
  table_name text not null,
  source_row_count bigint not null check (source_row_count >= 0),
  record_key_strategy text not null
    check (length(btrim(record_key_strategy)) > 0),
  primary key (source_project, table_name)
);

create index if not exists source_rows_table_key_idx
  on migration_reconcile.source_rows (table_name, record_key);

create table if not exists migration_reconcile.merge_decisions (
  table_name text not null,
  record_key text not null,
  decision text not null
    check (
      decision in (
        'keep_canonical',
        'import_legacy',
        'merge_reviewed',
        'archive_legacy',
        'exclude_environment_state'
      )
    ),
  decision_notes text not null,
  decided_by text not null,
  decided_at timestamptz not null default now(),
  primary key (table_name, record_key)
);

create table if not exists migration_reconcile.identity_map (
  legacy_user_id uuid primary key,
  canonical_user_id uuid not null unique,
  normalized_email text not null unique
    check (
      normalized_email = lower(btrim(normalized_email))
      and position('@' in normalized_email) > 1
    ),
  decision_notes text not null,
  decided_by text not null,
  decided_at timestamptz not null default now()
);

create or replace function migration_reconcile.shared_jsonb_projection(
  left_payload jsonb,
  right_payload jsonb
)
returns jsonb
language sql
immutable
strict
set search_path = ''
as $$
  select coalesce(jsonb_object_agg(item.key, item.value), '{}'::jsonb)
  from jsonb_each(left_payload) as item
  where right_payload ? item.key
$$;

create or replace function migration_reconcile.rewrite_quote_creator(
  quote_payload jsonb
)
returns jsonb
language sql
stable
strict
set search_path = ''
as $$
  select case
    when not quote_payload ? 'created_by_user_id'
      or nullif(quote_payload ->> 'created_by_user_id', '') is null
      or identity.canonical_user_id is null
      then quote_payload
    else jsonb_set(
      quote_payload,
      '{created_by_user_id}',
      to_jsonb(identity.canonical_user_id::text),
      false
    )
  end
  from (select 1) seed
  left join migration_reconcile.identity_map identity
    on identity.legacy_user_id =
      nullif(quote_payload ->> 'created_by_user_id', '')::uuid
$$;

create or replace view migration_reconcile.record_comparison as
select
  coalesce(canonical.table_name, legacy.table_name) as table_name,
  coalesce(canonical.record_key, legacy.record_key) as record_key,
  case
    when canonical.record_key is null then 'legacy_only'
    when legacy.record_key is null then 'canonical_only'
    when migration_reconcile.shared_jsonb_projection(
      canonical.payload,
      legacy.payload
    ) = migration_reconcile.shared_jsonb_projection(
      legacy.payload,
      canonical.payload
    ) then 'matching'
    else 'conflict'
  end as classification,
  canonical.payload_hash as canonical_payload_hash,
  legacy.payload_hash as legacy_payload_hash,
  canonical.payload as canonical_payload,
  legacy.payload as legacy_payload
from (
  select *
  from migration_reconcile.source_rows
  where source_project = 'local_delivery'
) canonical
full join (
  select *
  from migration_reconcile.source_rows
  where source_project = 'quote_live'
) legacy
  on canonical.table_name = legacy.table_name
  and canonical.record_key = legacy.record_key
;

create or replace view migration_reconcile.reconciliation_summary as
select
  table_name,
  count(*) filter (where classification = 'canonical_only')
    as canonical_only,
  count(*) filter (where classification = 'legacy_only')
    as legacy_only,
  count(*) filter (where classification = 'matching')
    as matching,
  count(*) filter (where classification = 'conflict')
    as conflicts,
  count(*) as compared_rows
from migration_reconcile.record_comparison
group by table_name
order by table_name;

create or replace view migration_reconcile.unresolved_records as
select comparison.*
from migration_reconcile.record_comparison comparison
left join migration_reconcile.merge_decisions decision
  on decision.table_name = comparison.table_name
  and decision.record_key = comparison.record_key
where comparison.classification in ('legacy_only', 'conflict')
  and decision.record_key is null;

create or replace view migration_reconcile.quote_import_candidates as
select
  source.record_key,
  comparison.classification,
  decision.decision,
  source.payload as legacy_payload,
  migration_reconcile.rewrite_quote_creator(source.payload)
    as rewritten_payload,
  case
    when not source.payload ? 'created_by_user_id'
      or nullif(source.payload ->> 'created_by_user_id', '') is null
      then 'not_required'
    when identity.canonical_user_id is not null
      then 'mapped'
    else 'unmapped'
  end as creator_resolution,
  (
    not source.payload ? 'created_by_user_id'
    or nullif(source.payload ->> 'created_by_user_id', '') is null
    or identity.canonical_user_id is not null
  ) as ready_for_import
from migration_reconcile.source_rows source
join migration_reconcile.record_comparison comparison
  on comparison.table_name = source.table_name
  and comparison.record_key = source.record_key
join migration_reconcile.merge_decisions decision
  on decision.table_name = source.table_name
  and decision.record_key = source.record_key
left join migration_reconcile.identity_map identity
  on identity.legacy_user_id =
    nullif(source.payload ->> 'created_by_user_id', '')::uuid
where source.source_project = 'quote_live'
  and source.table_name = 'custom_delivery_quotes'
  and decision.decision in ('import_legacy', 'merge_reviewed');

comment on schema migration_reconcile is
  'Local-only staging for Local-Delivery and Quote Live reconciliation.';

comment on table migration_reconcile.import_batches is
  'Checksum and count manifest for each encrypted production extraction.';

comment on table migration_reconcile.source_rows is
  'Contains sensitive temporary production projections; never dump into Git.';

comment on table migration_reconcile.source_tables is
  'Exact per-source table manifest, including empty source tables.';

comment on table migration_reconcile.identity_map is
  'Private reviewed Auth UUID mapping; never dump identities into Git.';

comment on view migration_reconcile.quote_import_candidates is
  'Reviewed Quote Live imports with deterministic creator remapping and fail-closed readiness.';
