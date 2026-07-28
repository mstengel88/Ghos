import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type, x-supabase-client-platform, x-supabase-client-platform-version, x-supabase-client-runtime, x-supabase-client-runtime-version",
  "Content-Type": "application/json",
};

const LOADRITE_BASE = "https://apicloud.loadrite-myinsighthq.com/api/v2";
const PROJECT_URL = "https://dlayrpnmfnbjlxgnkczv.supabase.co";
const DEFAULT_SITE = Deno.env.get("LOADRITE_SYNC_SITE") ??
  "Green Hills Landscape - Menomonee Falls";
const DEFAULT_LOOKBACK_DAYS = Number.parseInt(
  Deno.env.get("LOADRITE_SYNC_LOOKBACK_DAYS") ?? "7",
  10,
);

interface LoadriteLoadingRecord {
  Event?: string;
  Id?: string;
  Product?: string;
  Sequence?: string;
  Time?: string;
  UserData1?: string;
  UserData2?: string;
  UserData3?: string;
  Weight?: string;
}

interface LoadGroup {
  ticketNumber: string;
  product: string;
  customer: string;
  truck: string;
  totalWeight: number;
  time: string;
  bucketWeights: number[];
  note: string;
}

function formatDate(time: string): string {
  const d = time ? new Date(time) : new Date();
  return d.toLocaleString("en-US", {
    month: "2-digit",
    day: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: true,
  });
}

function groupRecordsIntoTickets(records: LoadriteLoadingRecord[]): LoadGroup[] {
  const groups: LoadGroup[] = [];
  let currentBuckets: number[] = [];
  let currentProduct = "";
  let currentCustomer = "";
  let currentTruck = "";
  let currentNote = "";

  for (const rec of records) {
    if (rec.Event === "Add") {
      currentBuckets.push(parseFloat(rec.Weight ?? "0"));
      currentProduct = rec.Product ?? currentProduct;
      currentCustomer = rec.UserData1 ?? currentCustomer;
      currentTruck = rec.UserData2 ?? currentTruck;
      currentNote = rec.UserData3 ?? currentNote;
    } else if (rec.Event === "Short Total") {
      groups.push({
        ticketNumber: rec.Sequence ?? `LR-${rec.Id}`,
        product: rec.Product ?? currentProduct,
        customer: rec.UserData1 ?? currentCustomer,
        truck: rec.UserData2 ?? currentTruck,
        totalWeight: parseFloat(rec.Weight ?? "0"),
        time: rec.Time ?? "",
        bucketWeights: [...currentBuckets],
        note: currentNote,
      });
      currentBuckets = [];
      currentProduct = "";
      currentCustomer = "";
      currentTruck = "";
      currentNote = "";
    }
  }

  return groups;
}

function groupToTicketRow(group: LoadGroup, userId: string) {
  return {
    id: group.ticketNumber,
    source: "loadrite",
    user_id: userId,
    job_number: group.ticketNumber,
    job_name: group.note,
    date_time: formatDate(group.time),
    total_amount: group.totalWeight.toFixed(2),
    total_unit: "Ton",
    customer: group.customer,
    product: group.product,
    truck: group.truck && group.truck !== "NOT SPECIFIED" ? group.truck : "-",
    note: group.note,
    bucket: group.bucketWeights.map((w, i) => `B${i + 1}: ${w}`).join(", "),
    status: "pending",
  };
}

function dateOnly(value: Date) {
  return value.toISOString().split("T")[0];
}

async function fetchLoadingRecords(
  token: string,
  site: string,
  fromDate: string,
  toDate: string,
): Promise<LoadriteLoadingRecord[]> {
  const params = new URLSearchParams({
    Site: site,
    FromLocalTime: `${fromDate} 00:00:00`,
    ToLocalTime: `${toDate} 23:59:59`,
  });

  const response = await fetch(`${LOADRITE_BASE}/Loading?${params.toString()}`, {
    method: "GET",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
    },
  });

  if (response.status === 404 || response.status === 204) {
    return [];
  }

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`Loadrite API error (${response.status}): ${text}`);
  }

  const payload = await response.json();
  if (Array.isArray(payload)) {
    return payload as LoadriteLoadingRecord[];
  }

  if (
    payload && typeof payload === "object" &&
    Array.isArray(payload.data)
  ) {
    return payload.data as LoadriteLoadingRecord[];
  }

  return [];
}

Deno.serve(async (req: Request) => {
  if (req.method === "OPTIONS") {
    return new Response(null, { headers: corsHeaders });
  }

  try {
    const supabaseUrl = Deno.env.get("SUPABASE_URL") ?? PROJECT_URL;
    const serviceRoleKey = Deno.env.get("LOADRITE_SYNC_SERVICE_ROLE_KEY") ??
      Deno.env.get("SERVICE_ROLE_KEY") ??
      Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
    const loadriteToken = Deno.env.get("LOADRITE_API_TOKEN");
    const syncUserId = Deno.env.get("LOADRITE_SYNC_USER_ID");

    if (!serviceRoleKey || !loadriteToken || !syncUserId) {
      return new Response(
        JSON.stringify({
          error:
            "Missing one or more required secrets: LOADRITE_SYNC_SERVICE_ROLE_KEY, LOADRITE_API_TOKEN, LOADRITE_SYNC_USER_ID",
        }),
        { status: 500, headers: corsHeaders },
      );
    }

    const body = req.method === "POST"
      ? await req.json().catch(() => ({}))
      : {};
    const site = typeof body.site === "string" && body.site.trim()
      ? body.site.trim()
      : DEFAULT_SITE;
    const toDate = typeof body.endDate === "string" && body.endDate.trim()
      ? body.endDate.trim()
      : dateOnly(new Date());

    const defaultStartDate = dateOnly(
      new Date(
        Date.now() -
          Math.max(DEFAULT_LOOKBACK_DAYS, 1) * 24 * 60 * 60 * 1000,
      ),
    );
    const fromDate =
      typeof body.startDate === "string" && body.startDate.trim()
        ? body.startDate.trim()
        : defaultStartDate;

    const records = await fetchLoadingRecords(
      loadriteToken,
      site,
      fromDate,
      toDate,
    );
    const groups = groupRecordsIntoTickets(records);
    const rows = groups.map((group) => groupToTicketRow(group, syncUserId));

    const adminClient = createClient(supabaseUrl, serviceRoleKey);

    if (rows.length > 0) {
      const { error: upsertErr } = await adminClient
        .from("tickets")
        .upsert(rows, { onConflict: "id", ignoreDuplicates: true });

      if (upsertErr) {
        throw upsertErr;
      }
    }

    return new Response(
      JSON.stringify({
        success: true,
        site,
        fromDate,
        toDate,
        records: records.length,
        groupedTickets: groups.length,
        insertedCandidates: rows.length,
      }),
      { status: 200, headers: corsHeaders },
    );
  } catch (error) {
    console.error("loadrite-sync failed", error);
    const message = error instanceof Error
      ? error.message
      : "Failed to sync from Loadrite";
    return new Response(JSON.stringify({ error: message }), {
      status: 500,
      headers: corsHeaders,
    });
  }
});
