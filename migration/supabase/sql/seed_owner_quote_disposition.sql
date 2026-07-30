\set ON_ERROR_STOP on

-- Owner direction: the four quotes in the human review queue are not needed.
-- Do not import the three Quote Live-only rows. For the duplicate conflict,
-- retain the existing Local-Delivery row and discard only the legacy copy.

do $$
declare
  legacy_only_quote_count bigint;
  conflicting_quote_count bigint;
  unresolved_nonquote_count bigint;
begin
  select
    count(*) filter (
      where table_name = 'custom_delivery_quotes'
        and classification = 'legacy_only'
    ),
    count(*) filter (
      where table_name = 'custom_delivery_quotes'
        and classification = 'conflict'
    ),
    count(*) filter (where table_name <> 'custom_delivery_quotes')
  into
    legacy_only_quote_count,
    conflicting_quote_count,
    unresolved_nonquote_count
  from migration_reconcile.unresolved_records;

  if legacy_only_quote_count <> 3
     or conflicting_quote_count <> 1
     or unresolved_nonquote_count <> 0 then
    raise exception
      'Owner quote baseline changed: expected 3 legacy-only, 1 conflict, 0 non-quote; found %, %, %',
      legacy_only_quote_count,
      conflicting_quote_count,
      unresolved_nonquote_count;
  end if;
end
$$;

insert into migration_reconcile.merge_decisions (
  table_name,
  record_key,
  decision,
  decision_notes,
  decided_by
)
select
  table_name,
  record_key,
  case classification
    when 'legacy_only' then 'archive_legacy'
    when 'conflict' then 'keep_canonical'
  end,
  case classification
    when 'legacy_only'
      then 'Owner directed that the legacy-only quote is not needed and must not be imported.'
    when 'conflict'
      then 'Owner directed that the reviewed quote is not needed; retain the existing canonical row and discard the legacy copy.'
  end,
  'owner-direction-no-quote-import'
from migration_reconcile.unresolved_records
where table_name = 'custom_delivery_quotes'
on conflict (table_name, record_key) do nothing;

do $$
declare
  owner_decision_count bigint;
  unresolved_count bigint;
  quote_import_count bigint;
begin
  select count(*)
  into owner_decision_count
  from migration_reconcile.merge_decisions
  where decided_by = 'owner-direction-no-quote-import';

  select count(*)
  into unresolved_count
  from migration_reconcile.unresolved_records;

  select count(*)
  into quote_import_count
  from migration_reconcile.quote_import_candidates;

  if owner_decision_count <> 4 then
    raise exception
      'Expected four owner quote dispositions, found %',
      owner_decision_count;
  end if;
  if unresolved_count <> 0 then
    raise exception
      'Expected zero unresolved records after owner disposition, found %',
      unresolved_count;
  end if;
  if quote_import_count <> 0 then
    raise exception
      'No Quote Live quote may be imported under the owner disposition';
  end if;

  raise notice
    'Owner quote disposition verified: no Quote Live quotes will be imported.';
end
$$;
