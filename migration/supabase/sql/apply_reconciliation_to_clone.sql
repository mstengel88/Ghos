\set ON_ERROR_STOP on
\pset pager off

begin;

create temporary table reconciliation_before_counts (
  table_name text primary key,
  row_count bigint not null
) on commit drop;

do $$
declare
  table_row record;
  current_count bigint;
begin
  for table_row in
    select table_name
    from information_schema.tables
    where table_schema = 'public'
      and table_type = 'BASE TABLE'
    order by table_name
  loop
    execute format(
      'select count(*) from public.%I',
      table_row.table_name
    )
    into current_count;

    insert into reconciliation_before_counts (table_name, row_count)
    values (table_row.table_name, current_count);
  end loop;
end
$$;

do $$
declare
  selected_import_count bigint;
  unexpected_import_count bigint;
begin
  select count(*)
  into selected_import_count
  from migration_reconcile.merge_decisions decision
  join migration_reconcile.record_comparison comparison
    on comparison.table_name = decision.table_name
    and comparison.record_key = decision.record_key
  where decision.decision = 'import_legacy'
    and decision.table_name = 'dispatch_notifications'
    and comparison.classification = 'legacy_only';

  select count(*)
  into unexpected_import_count
  from migration_reconcile.merge_decisions
  where decision in ('import_legacy', 'merge_reviewed')
    and table_name <> 'dispatch_notifications';

  if selected_import_count <> 40 then
    raise exception
      'Expected exactly 40 notification imports, found %',
      selected_import_count;
  end if;
  if unexpected_import_count <> 0 then
    raise exception
      'Unexpected non-notification import decisions: %',
      unexpected_import_count;
  end if;
end
$$;

with selected_notifications as (
  select source.payload
  from migration_reconcile.source_rows source
  join migration_reconcile.merge_decisions decision
    on decision.table_name = source.table_name
    and decision.record_key = source.record_key
  where source.source_project = 'quote_live'
    and source.table_name = 'dispatch_notifications'
    and decision.decision = 'import_legacy'
)
insert into public.dispatch_notifications
select expanded.*
from selected_notifications selected
cross join lateral jsonb_populate_record(
  null::public.dispatch_notifications,
  selected.payload
) expanded;

set constraints all immediate;

do $$
declare
  notification_before bigint;
  notification_after bigint;
  changed_nonnotification_tables bigint;
  imported_payload_mismatches bigint;
begin
  select row_count
  into notification_before
  from reconciliation_before_counts
  where table_name = 'dispatch_notifications';

  select count(*)
  into notification_after
  from public.dispatch_notifications;

  if notification_after <> notification_before + 40 then
    raise exception
      'Notification merge count mismatch: expected %, found %',
      notification_before + 40,
      notification_after;
  end if;

  select count(*)
  into changed_nonnotification_tables
  from reconciliation_before_counts before_count
  where before_count.table_name <> 'dispatch_notifications'
    and before_count.row_count <> (
      migration_reconcile.public_table_row_count(before_count.table_name)
    );

  if changed_nonnotification_tables <> 0 then
    raise exception
      'Merge changed % non-notification table count(s)',
      changed_nonnotification_tables;
  end if;

  select count(*)
  into imported_payload_mismatches
  from migration_reconcile.source_rows source
  join migration_reconcile.merge_decisions decision
    on decision.table_name = source.table_name
    and decision.record_key = source.record_key
  join public.dispatch_notifications notification
    on notification.id::text = source.record_key
  where source.source_project = 'quote_live'
    and source.table_name = 'dispatch_notifications'
    and decision.decision = 'import_legacy'
    and to_jsonb(notification) is distinct from source.payload;

  if imported_payload_mismatches <> 0 then
    raise exception
      'Imported notification payload mismatches: %',
      imported_payload_mismatches;
  end if;

  if (
    select count(*)
    from public.custom_delivery_quotes
  ) <> (
    select row_count
    from reconciliation_before_counts
    where table_name = 'custom_delivery_quotes'
  ) then
    raise exception 'Quote rows changed during notification-only merge';
  end if;

  raise notice
    'Notification-only clone merge verified: 40 imports, zero quote changes.';
end
$$;

commit;
