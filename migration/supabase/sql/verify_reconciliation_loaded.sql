\set ON_ERROR_STOP on
\pset pager off

do $$
declare
  batch record;
  actual_table_count bigint;
  actual_row_count bigint;
begin
  if (
    select count(*)
    from migration_reconcile.import_batches
    where source_project in ('local_delivery', 'quote_live')
  ) <> 2 then
    raise exception
      'Both source import manifests are required';
  end if;

  for batch in
    select *
    from migration_reconcile.import_batches
  loop
    select
      count(*),
      coalesce(sum(source_row_count), 0)
    into actual_table_count, actual_row_count
    from migration_reconcile.source_tables
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

    if exists (
      select 1
      from migration_reconcile.source_tables source_table
      left join (
        select
          source_project,
          table_name,
          count(*) as loaded_row_count
        from migration_reconcile.source_rows
        group by source_project, table_name
      ) loaded
        on loaded.source_project = source_table.source_project
        and loaded.table_name = source_table.table_name
      where source_table.source_project = batch.source_project
        and coalesce(loaded.loaded_row_count, 0)
          <> source_table.source_row_count
    ) then
      raise exception
        'Per-table source manifest mismatch for %',
        batch.source_project;
    end if;
  end loop;

  if (
    select count(*)
    from migration_reconcile.record_comparison
  ) = 0 then
    raise exception 'The reconciliation comparison is empty';
  end if;

  raise notice
    'Both exact source manifests and every per-table row count are verified.';
end
$$;
