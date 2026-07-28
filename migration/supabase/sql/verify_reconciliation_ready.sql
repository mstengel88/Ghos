\set ON_ERROR_STOP on
\pset pager off

select *
from migration_reconcile.reconciliation_summary;

select
  classification,
  count(*) as record_count
from migration_reconcile.record_comparison
group by classification
order by classification;

select
  table_name,
  count(*) as unresolved_count
from migration_reconcile.unresolved_records
group by table_name
order by table_name;

do $$
declare
  batch record;
  actual_table_count bigint;
  actual_row_count bigint;
  unresolved_count bigint;
  blocked_quote_count bigint;
begin
  if (
    select count(*)
    from migration_reconcile.import_batches
    where source_project in ('local_delivery', 'quote_live')
  ) <> 2 then
    raise exception
      'Reconciliation is not ready: both source import manifests are required';
  end if;

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
        'Import manifest mismatch for %: expected % tables/% rows, found % tables/% rows',
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
      'Reconciliation is not ready: % legacy-only or conflicting rows lack decisions',
      unresolved_count;
  end if;

  select count(*)
  into blocked_quote_count
  from migration_reconcile.quote_import_candidates
  where not ready_for_import;

  if blocked_quote_count <> 0 then
    raise exception
      'Reconciliation is not ready: % reviewed quote import(s) have unmapped creators',
      blocked_quote_count;
  end if;

  raise notice 'Every legacy-only and conflicting row has a merge decision.';
end
$$;
