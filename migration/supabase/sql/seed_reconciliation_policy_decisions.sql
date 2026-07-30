\set ON_ERROR_STOP on

-- These decisions implement the already-documented ownership policy. Quote
-- records with substantive differences and every legacy-only quote remain
-- intentionally unresolved for human review.

insert into migration_reconcile.merge_decisions (
  table_name,
  record_key,
  decision,
  decision_notes,
  decided_by
)
select
  unresolved.table_name,
  unresolved.record_key,
  case
    when unresolved.table_name = 'Session'
      then 'exclude_environment_state'
    when unresolved.classification = 'legacy_only'
      and unresolved.table_name = 'dispatch_notifications'
      and notification.order_reference_valid
      and notification.route_reference_valid
      then 'import_legacy'
    when unresolved.classification = 'legacy_only'
      then 'archive_legacy'
    else 'keep_canonical'
  end,
  case
    when unresolved.table_name = 'Session'
      then 'Shopify session state is reauthorized and never copied.'
    when unresolved.classification = 'legacy_only'
      and unresolved.table_name = 'dispatch_notifications'
      and notification.order_reference_valid
      and notification.route_reference_valid
      then 'Legacy notification references existing canonical entities.'
    when unresolved.classification = 'legacy_only'
      then 'Legacy-only non-quote row is retained in the encrypted archive.'
    when unresolved.table_name = 'product_source_map'
      then 'Canonical Shopify-refreshed product data remains authoritative.'
    else 'Local-Delivery current operational state remains authoritative.'
  end,
  'documented-owner-policy-v1'
from migration_reconcile.unresolved_records unresolved
left join migration_reconcile.legacy_notification_import_candidates notification
  on unresolved.table_name = 'dispatch_notifications'
  and notification.record_key = unresolved.record_key
where unresolved.table_name <> 'custom_delivery_quotes'
on conflict (table_name, record_key) do nothing;

-- A duplicate quote that differs only by creator UUID is the same business
-- quote after identity reconciliation. Keep the canonical Local-Delivery row.
insert into migration_reconcile.merge_decisions (
  table_name,
  record_key,
  decision,
  decision_notes,
  decided_by
)
select
  comparison.table_name,
  comparison.record_key,
  'keep_canonical',
  'Duplicate quote differs only by remappable creator identity UUID.',
  'documented-owner-policy-v1'
from migration_reconcile.record_comparison comparison
where comparison.table_name = 'custom_delivery_quotes'
  and comparison.classification = 'conflict'
  and migration_reconcile.shared_jsonb_projection(
    comparison.canonical_payload - 'created_by_user_id',
    comparison.legacy_payload - 'created_by_user_id'
  ) = migration_reconcile.shared_jsonb_projection(
    comparison.legacy_payload - 'created_by_user_id',
    comparison.canonical_payload - 'created_by_user_id'
  )
on conflict (table_name, record_key) do nothing;

do $$
begin
  if exists (
    select 1
    from migration_reconcile.merge_decisions decision
    join migration_reconcile.record_comparison comparison
      on comparison.table_name = decision.table_name
      and comparison.record_key = decision.record_key
    where decision.decided_by = 'documented-owner-policy-v1'
      and comparison.table_name = 'custom_delivery_quotes'
      and comparison.classification = 'legacy_only'
  ) then
    raise exception
      'Policy scaffold must not decide legacy-only quotes';
  end if;

  if exists (
    select 1
    from migration_reconcile.merge_decisions decision
    join migration_reconcile.legacy_notification_import_candidates notification
      on notification.record_key = decision.record_key
    where decision.table_name = 'dispatch_notifications'
      and decision.decision = 'import_legacy'
      and (
        not notification.order_reference_valid
        or not notification.route_reference_valid
      )
  ) then
    raise exception
      'Policy scaffold selected a notification with invalid references';
  end if;

  raise notice
    'Documented ownership policy decisions seeded; business quote review remains fail-closed.';
end
$$;
