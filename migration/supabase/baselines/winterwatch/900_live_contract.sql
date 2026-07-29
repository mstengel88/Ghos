-- Reconcile the canonical WinterWatch migration chain with the live managed
-- schema captured read-only on 2026-07-28.
--
-- The managed project contains maintenance_logs, but no corresponding DDL is
-- present in the 36-file local migration history. Keep this as an explicit
-- baseline reconciliation instead of rewriting historical migrations.

create table public.maintenance_logs (
  id uuid not null default gen_random_uuid(),
  equipment_id uuid not null,
  maintenance_type text not null,
  description text,
  cost numeric default 0,
  performed_by_employee_id uuid,
  performed_by_name text,
  service_date timestamp with time zone not null default now(),
  next_service_date date,
  created_at timestamp with time zone not null default now(),
  updated_at timestamp with time zone not null default now(),
  constraint maintenance_logs_pkey primary key (id),
  constraint maintenance_logs_equipment_id_fkey
    foreign key (equipment_id)
    references public.equipment(id)
    on delete cascade,
  constraint maintenance_logs_performed_by_employee_id_fkey
    foreign key (performed_by_employee_id)
    references public.employees(id)
    on delete set null
);

alter table public.maintenance_logs enable row level security;

create policy "Admins and managers can view maintenance logs"
  on public.maintenance_logs
  for select
  using (
    is_admin_or_manager(auth.uid())
    or is_staff(auth.uid())
  );

create policy "Admins and managers can insert maintenance logs"
  on public.maintenance_logs
  for insert
  with check (is_admin_or_manager(auth.uid()));

create policy "Admins and managers can update maintenance logs"
  on public.maintenance_logs
  for update
  using (is_admin_or_manager(auth.uid()))
  with check (is_admin_or_manager(auth.uid()));

create policy "Admins and managers can delete maintenance logs"
  on public.maintenance_logs
  for delete
  using (is_admin_or_manager(auth.uid()));

create trigger update_maintenance_logs_updated_at
  before update on public.maintenance_logs
  for each row
  execute function public.update_updated_at_column();
