\set ON_ERROR_STOP on
\pset pager off

begin;

do $$
begin
  if exists (
    select 1
    from migration_reconcile.import_batches
  ) or exists (
    select 1
    from migration_reconcile.source_rows
  ) or exists (
    select 1
    from migration_reconcile.merge_decisions
  ) or exists (
    select 1
    from migration_reconcile.identity_map
  ) then
    raise exception
      'Refusing synthetic reconciliation test because staging is not empty';
  end if;
end
$$;

insert into migration_reconcile.import_batches (
  source_project,
  extracted_at,
  source_table_count,
  source_row_count,
  manifest_sha256
)
values
  (
    'local_delivery',
    '2026-07-27 22:45:00-05',
    1,
    3,
    repeat('a', 64)
  ),
  (
    'quote_live',
    '2026-07-27 22:45:00-05',
    2,
    6,
    repeat('b', 64)
  );

insert into migration_reconcile.source_rows (
  source_project,
  table_name,
  record_key,
  payload
)
values
  (
    'local_delivery',
    'dispatch_orders',
    'fixture-matching',
    '{"id":"fixture-matching","status":"scheduled","route_id":"route-1","canonical_note":"kept"}'
  ),
  (
    'quote_live',
    'dispatch_orders',
    'fixture-matching',
    '{"id":"fixture-matching","status":"scheduled","route_id":"route-1"}'
  ),
  (
    'local_delivery',
    'dispatch_orders',
    'fixture-canonical-only',
    '{"id":"fixture-canonical-only","status":"scheduled"}'
  ),
  (
    'quote_live',
    'dispatch_orders',
    'fixture-legacy-only',
    '{"id":"fixture-legacy-only","status":"delivered"}'
  ),
  (
    'local_delivery',
    'dispatch_orders',
    'fixture-conflict',
    '{"id":"fixture-conflict","status":"scheduled","route_id":"route-2"}'
  ),
  (
    'quote_live',
    'dispatch_orders',
    'fixture-conflict',
    '{"id":"fixture-conflict","status":"delivered","route_id":"route-2"}'
  ),
  (
    'quote_live',
    'custom_delivery_quotes',
    'fixture-quote-mapped',
    '{"id":"fixture-quote-mapped","quote_total_cents":10000,"created_by_user_id":"10000000-0000-0000-0000-000000000001"}'
  ),
  (
    'quote_live',
    'custom_delivery_quotes',
    'fixture-quote-unmapped',
    '{"id":"fixture-quote-unmapped","quote_total_cents":20000,"created_by_user_id":"30000000-0000-0000-0000-000000000001"}'
  ),
  (
    'quote_live',
    'custom_delivery_quotes',
    'fixture-quote-no-owner',
    '{"id":"fixture-quote-no-owner","quote_total_cents":30000}'
  );

do $$
declare
  canonical_only_count bigint;
  legacy_only_count bigint;
  matching_count bigint;
  conflict_count bigint;
  unresolved_count bigint;
begin
  select
    count(*) filter (where classification = 'canonical_only'),
    count(*) filter (where classification = 'legacy_only'),
    count(*) filter (where classification = 'matching'),
    count(*) filter (where classification = 'conflict')
  into
    canonical_only_count,
    legacy_only_count,
    matching_count,
    conflict_count
  from migration_reconcile.record_comparison
  where table_name = 'dispatch_orders';

  if canonical_only_count <> 1
     or legacy_only_count <> 1
     or matching_count <> 1
     or conflict_count <> 1 then
    raise exception
      'Unexpected classification counts: canonical=%, legacy=%, matching=%, conflicts=%',
      canonical_only_count,
      legacy_only_count,
      matching_count,
      conflict_count;
  end if;

  select count(*)
  into unresolved_count
  from migration_reconcile.unresolved_records;

  if unresolved_count <> 5 then
    raise exception
      'Expected five unresolved records before review, found %',
      unresolved_count;
  end if;
end
$$;

insert into migration_reconcile.identity_map (
  legacy_user_id,
  canonical_user_id,
  normalized_email,
  decision_notes,
  decided_by
)
values (
  '10000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000001',
  'mapped-user@example.invalid',
  'Synthetic divergent identity mapping.',
  'clean-room-test'
);

insert into migration_reconcile.merge_decisions (
  table_name,
  record_key,
  decision,
  decision_notes,
  decided_by
)
values
  (
    'dispatch_orders',
    'fixture-legacy-only',
    'archive_legacy',
    'Synthetic test decision for a historical legacy-only order.',
    'clean-room-test'
  ),
  (
    'dispatch_orders',
    'fixture-conflict',
    'keep_canonical',
    'Synthetic test confirms current Dispatch v2 state remains authoritative.',
    'clean-room-test'
  ),
  (
    'custom_delivery_quotes',
    'fixture-quote-mapped',
    'import_legacy',
    'Synthetic quote with a reviewed creator mapping.',
    'clean-room-test'
  ),
  (
    'custom_delivery_quotes',
    'fixture-quote-unmapped',
    'import_legacy',
    'Synthetic quote deliberately retained in the quarantine test.',
    'clean-room-test'
  ),
  (
    'custom_delivery_quotes',
    'fixture-quote-no-owner',
    'import_legacy',
    'Synthetic quote without a creator reference.',
    'clean-room-test'
  );

do $$
declare
  batch record;
  actual_table_count bigint;
  actual_row_count bigint;
  unresolved_count bigint;
  ready_quote_count bigint;
  blocked_quote_count bigint;
  rewritten_creator text;
begin
  for batch in
    select *
    from migration_reconcile.import_batches
  loop
    select
      count(distinct table_name),
      count(*)
    into actual_table_count, actual_row_count
    from migration_reconcile.source_rows
    where source_project = batch.source_project;

    if actual_table_count <> batch.source_table_count
       or actual_row_count <> batch.source_row_count then
      raise exception
        'Synthetic manifest mismatch for %: expected % tables/% rows, found % tables/% rows',
        batch.source_project,
        batch.source_table_count,
        batch.source_row_count,
        actual_table_count,
        actual_row_count;
    end if;
  end loop;

  select count(*)
  into unresolved_count
  from migration_reconcile.unresolved_records;

  if unresolved_count <> 0 then
    raise exception
      'Expected every synthetic conflict to have a decision, found % unresolved',
      unresolved_count;
  end if;

  select
    count(*) filter (where ready_for_import),
    count(*) filter (where not ready_for_import)
  into ready_quote_count, blocked_quote_count
  from migration_reconcile.quote_import_candidates;

  if ready_quote_count <> 2 or blocked_quote_count <> 1 then
    raise exception
      'Expected two ready quote imports and one quarantined quote, found % ready/% blocked',
      ready_quote_count,
      blocked_quote_count;
  end if;

  select rewritten_payload ->> 'created_by_user_id'
  into rewritten_creator
  from migration_reconcile.quote_import_candidates
  where record_key = 'fixture-quote-mapped';

  if rewritten_creator <> '20000000-0000-0000-0000-000000000001' then
    raise exception
      'Mapped quote creator was not rewritten to the canonical UUID';
  end if;

  raise notice
    'Synthetic reconciliation, identity rewrite, and quarantine behavior verified.';
end
$$;

rollback;

do $$
begin
  if exists (
    select 1
    from migration_reconcile.import_batches
  ) or exists (
    select 1
    from migration_reconcile.source_rows
  ) or exists (
    select 1
    from migration_reconcile.merge_decisions
  ) or exists (
    select 1
    from migration_reconcile.identity_map
  ) then
    raise exception
      'Synthetic reconciliation fixtures remained after rollback';
  end if;

  raise notice 'Synthetic reconciliation fixtures rolled back.';
end
$$;
