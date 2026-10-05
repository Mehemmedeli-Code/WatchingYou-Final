import { useCallback, useEffect, useState } from "react";
import { TicketPercent } from "lucide-react";
import { Panel, Notice, Empty } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Field, Select } from "@/components/ui/input";
import { get, post, put, ApiError } from "@/lib/api";
import { formatMoney } from "@/lib/format";
import { formatDay, t } from "@/lib/i18n";

interface PromoCodeRow {
  id: string;
  code: string;
  description?: string | null;
  percentOff?: number | null;
  amountOff?: number | null;
  minSubtotal: number;
  validFromUtc?: string | null;
  validUntilUtc?: string | null;
  maxRedemptions?: number | null;
  redemptions: number;
  isActive: boolean;
}

/** Promo codes for cinema bookings: create, see how often each was used, switch off. */
export function PromoAdmin() {
  const [rows, setRows] = useState<PromoCodeRow[] | null>(null);
  const [code, setCode] = useState("");
  const [description, setDescription] = useState("");
  const [kind, setKind] = useState<"percent" | "amount">("percent");
  const [value, setValue] = useState(10);
  const [minSubtotal, setMinSubtotal] = useState(0);
  const [until, setUntil] = useState("");
  const [limit, setLimit] = useState("");
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setRows(await get<PromoCodeRow[]>("/api/admin/promos").catch(() => []));
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function create() {
    setBusy(true);
    setMessage(null);
    try {
      await post("/api/admin/promos", {
        code,
        description: description || null,
        percentOff: kind === "percent" ? value : null,
        amountOff: kind === "amount" ? value : null,
        minSubtotal,
        validFromUtc: null,
        validUntilUtc: until ? new Date(`${until}T23:59:59+04:00`).toISOString() : null,
        maxRedemptions: limit === "" ? null : Number(limit),
      });
      setMessage({ tone: "ok", text: `${code.toUpperCase()} ${t("promo.created", "created")}` });
      setCode(""); setDescription(""); setLimit(""); setUntil("");
      await load();
    } catch (err) {
      const fields = err instanceof ApiError && err.fieldErrors ? Object.values(err.fieldErrors).flat().join(" ") : "";
      setMessage({ tone: "error", text: fields || (err instanceof ApiError ? err.message : t("error.action")) });
    } finally {
      setBusy(false);
    }
  }

  async function toggle(row: PromoCodeRow) {
    await put(`/api/admin/promos/${row.id}/active`, { active: !row.isActive }).catch(() => null);
    await load();
  }

  return (
    <div className="grid gap-6 lg:grid-cols-[1fr_340px]">
      <div>
        {rows && rows.length === 0 ? (
          <Empty title={t("promo.emptyTitle", "No promo codes yet")} hint={t("promo.emptyHint", "Create one on the right; customers type it at checkout.")} />
        ) : null}
        {rows && rows.length > 0 ? (
          <div className="-mx-1 overflow-x-auto rounded-xl border border-line">
            <table className="w-full min-w-[34rem] text-left text-sm">
              <thead className="bg-surface-raised text-xs text-ink-mute">
                <tr>
                  <th className="px-4 py-3 font-medium">{t("promo.code", "Code")}</th>
                  <th className="px-4 py-3 font-medium">{t("promo.discount", "Discount")}</th>
                  <th className="px-4 py-3 font-medium">{t("promo.used", "Used")}</th>
                  <th className="px-4 py-3 font-medium">{t("promo.until", "Valid until")}</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {rows.map((row) => (
                  <tr key={row.id} className={row.isActive ? "" : "opacity-55"}>
                    <td className="px-4 py-3">
                      <span className="font-display tracking-wider text-ink">{row.code}</span>
                      {row.description ? <p className="text-xs text-ink-mute">{row.description}</p> : null}
                    </td>
                    <td className="px-4 py-3 text-ink">
                      {row.percentOff ? `${row.percentOff}%` : formatMoney(row.amountOff ?? 0)}
                      {row.minSubtotal > 0 ? <span className="ml-1 text-xs text-ink-mute">≥ {formatMoney(row.minSubtotal)}</span> : null}
                    </td>
                    <td className="px-4 py-3 text-ink-mute">{row.redemptions}{row.maxRedemptions ? ` / ${row.maxRedemptions}` : ""}</td>
                    <td className="px-4 py-3 text-ink-mute">{row.validUntilUtc ? formatDay(row.validUntilUtc) : "—"}</td>
                    <td className="px-4 py-3 text-right">
                      <Button size="sm" variant={row.isActive ? "danger" : "outline"} onClick={() => toggle(row)}>
                        {row.isActive ? t("promo.disable", "Switch off") : t("promo.enable", "Switch on")}
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </div>

      <Panel>
        <h3 className="flex items-center gap-2 font-display text-lg text-ink">
          <TicketPercent size={16} aria-hidden />
          {t("promo.new", "New promo code")}
        </h3>
        <div className="mt-4 space-y-3">
          <Field label={t("promo.code", "Code")}>
            <Input value={code} onChange={(e) => setCode(e.target.value.toUpperCase().replace(/[^A-Z0-9_-]/g, "").slice(0, 32))} placeholder="AUTUMN10" />
          </Field>
          <Field label={t("promo.description", "Note (optional)")}>
            <Input value={description} onChange={(e) => setDescription(e.target.value)} maxLength={200} />
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label={t("promo.kind", "Type")}>
              <Select value={kind} onChange={(e) => setKind(e.target.value as "percent" | "amount")}>
                <option value="percent">%</option>
                <option value="amount">AZN</option>
              </Select>
            </Field>
            <Field label={t("promo.value", "Value")}>
              <Input type="number" min={0.5} step={kind === "percent" ? 1 : 0.5} value={value} onChange={(e) => setValue(Number(e.target.value))} />
            </Field>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <Field label={t("promo.min", "Minimum spend")}>
              <Input type="number" min={0} step={0.5} value={minSubtotal} onChange={(e) => setMinSubtotal(Number(e.target.value))} />
            </Field>
            <Field label={t("promo.limit", "Max uses")}>
              <Input type="number" min={1} value={limit} onChange={(e) => setLimit(e.target.value)} placeholder="∞" />
            </Field>
          </div>
          <Field label={t("promo.until", "Valid until")}>
            <Input type="date" value={until} onChange={(e) => setUntil(e.target.value)} />
          </Field>

          {message ? <Notice tone={message.tone}>{message.text}</Notice> : null}

          <Button className="w-full" disabled={!code || value <= 0 || busy} onClick={create}>
            {busy ? t("common.loading") : t("promo.create", "Create code")}
          </Button>
        </div>
      </Panel>
    </div>
  );
}
