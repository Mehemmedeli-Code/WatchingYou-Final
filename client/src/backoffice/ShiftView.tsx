import { useState } from "react";
import { Lock, Printer, Unlock } from "lucide-react";
import { get, post } from "@/lib/api";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input, Field, Textarea } from "@/components/ui/input";
import { Alert, Card, Kpi, Loading, Page, Table, Td, azn, dateTime, errorText, printReceipt, useLoad } from "./shared";

export interface ShiftSummary {
  id: string;
  cashierName: string;
  openedAtUtc: string;
  closedAtUtc?: string | null;
  openingFloat: number;
  ticketsSold: number;
  ticketCash: number;
  ticketCard: number;
  barReceipts: number;
  barCash: number;
  barCard: number;
  salesCash: number;
  salesCard: number;
  salesTotal: number;
  expectedCash: number;
  countedCash?: number | null;
  variance?: number | null;
  note?: string | null;
}

/** Shown in place of a till when no shift is open. Selling outside a shift would leave money
 *  that belongs to no drawer and no person. */
export function NeedShift({ title }: { title: string }) {
  return (
    <Page title={title}>
      <Card>
        <div className="flex flex-col items-center gap-3 py-10 text-center">
          <Lock className="text-ink-mute" size={28} aria-hidden />
          <p className="font-display text-lg text-ink">{t("bo.noShiftTitle", "The till is closed")}</p>
          <p className="max-w-[46ch] text-sm text-ink-mute">{t("bo.noShiftHint", "Open a cash shift with the float in the drawer before selling.")}</p>
          <Button onClick={() => { window.location.hash = "#/shift"; }}><Unlock size={16} />{t("bo.openShift", "Open shift")}</Button>
        </div>
      </Card>
    </Page>
  );
}

function reportLines(s: ShiftSummary): [string, string][] {
  return [
    [t("bo.opened", "Opened"), dateTime(s.openedAtUtc)],
    ...(s.closedAtUtc ? [[t("bo.closed", "Closed"), dateTime(s.closedAtUtc)] as [string, string]] : []),
    [t("bo.float", "Opening float"), azn(s.openingFloat)],
    [`${t("bo.tickets", "Tickets")} (${s.ticketsSold})`, ""],
    [`  ${t("bo.cash", "Cash")}`, azn(s.ticketCash)],
    [`  ${t("bo.card", "Card")}`, azn(s.ticketCard)],
    [`${t("bo.bar", "Bar")} (${s.barReceipts})`, ""],
    [`  ${t("bo.cash", "Cash")}`, azn(s.barCash)],
    [`  ${t("bo.card", "Card")}`, azn(s.barCard)],
    [t("bo.salesTotal", "Sales total"), azn(s.salesTotal)],
    [t("bo.expectedCash", "Expected in drawer"), azn(s.expectedCash)],
    ...(s.countedCash != null ? [
      [t("bo.counted", "Counted"), azn(s.countedCash)] as [string, string],
      [t("bo.variance", "Variance"), azn(s.variance ?? 0)] as [string, string],
    ] : []),
  ];
}

export function ShiftView({ shift, onChange }: { shift: ShiftSummary | null; onChange: (s: ShiftSummary | null) => void }) {
  const [float, setFloat] = useState("100");
  const [counted, setCounted] = useState("");
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [closed, setClosed] = useState<ShiftSummary | null>(null);

  async function open() {
    setBusy(true); setError(null);
    try {
      onChange(await post<ShiftSummary>("/api/backoffice/shift/open", { openingFloat: Number(float.replace(",", ".")) || 0 }));
      setClosed(null);
    } catch (err) { setError(errorText(err, t("bo.errOpenShift", "Could not open the shift."))); }
    finally { setBusy(false); }
  }

  async function close() {
    setBusy(true); setError(null);
    try {
      const z = await post<ShiftSummary>("/api/backoffice/shift/close", { countedCash: Number(counted.replace(",", ".")), note: note || null });
      setClosed(z);
      onChange(null);
      setCounted(""); setNote("");
    } catch (err) { setError(errorText(err, t("bo.errCloseShift", "Could not close the shift."))); }
    finally { setBusy(false); }
  }

  async function refresh() {
    onChange((await get<ShiftSummary | undefined>("/api/backoffice/shift")) ?? null);
  }

  return (
    <Page title={t("bo.shiftTitle", "Cash shift")}
      lede={t("bo.shiftLede", "Open with the float, sell, close with the counted drawer. Closing prints the Z report and cannot be undone.")}>
      {error ? <Alert tone="error">{error}</Alert> : null}

      {closed ? (
        <Card title={`${t("bo.zReport", "Z report")} · ${closed.cashierName}`}
          actions={<Button size="sm" variant="outline" onClick={() => printReceipt(t("bo.zReport", "Z report"), reportLines(closed), [closed.cashierName])}><Printer size={14} />{t("bo.print", "Print")}</Button>}>
          <ShiftFigures s={closed} />
        </Card>
      ) : null}

      {!shift ? (
        <Card title={t("bo.openShift", "Open shift")}>
          <div className="flex max-w-md flex-wrap items-end gap-3">
            <div className="flex-1">
              <Field label={t("bo.float", "Opening float")} hint={t("bo.floatHint", "Cash in the drawer before the first sale.")}>
                <Input inputMode="decimal" value={float} onChange={(e) => setFloat(e.target.value)} />
              </Field>
            </div>
            <Button disabled={busy} onClick={() => void open()}><Unlock size={16} />{t("bo.openShift", "Open shift")}</Button>
          </div>
        </Card>
      ) : (
        <>
          <Card title={`${t("bo.xReport", "X report")} · ${shift.cashierName}`}
            actions={
              <div className="flex gap-2">
                <Button size="sm" variant="ghost" onClick={() => void refresh()}>{t("bo.refresh", "Refresh")}</Button>
                <Button size="sm" variant="outline" onClick={() => printReceipt(t("bo.xReport", "X report"), reportLines(shift), [shift.cashierName])}><Printer size={14} />{t("bo.print", "Print")}</Button>
              </div>
            }>
            <ShiftFigures s={shift} />
          </Card>

          <Card title={t("bo.closeShift", "Close shift (Z report)")}>
            <div className="grid max-w-xl gap-3">
              <Field label={t("bo.countedCash", "Cash counted in the drawer")}
                hint={`${t("bo.expectedCash", "Expected in drawer")}: ${azn(shift.expectedCash)}`}>
                <Input inputMode="decimal" value={counted} onChange={(e) => setCounted(e.target.value)} placeholder={shift.expectedCash.toFixed(2)} />
              </Field>
              {counted !== "" ? (
                <p className="text-sm">
                  {t("bo.variance", "Variance")}:{" "}
                  <VarianceText value={Number(counted.replace(",", ".")) - shift.expectedCash} />
                </p>
              ) : null}
              <Field label={t("bo.note", "Note")}>
                <Textarea value={note} onChange={(e) => setNote(e.target.value)} className="min-h-16" />
              </Field>
              <div>
                <Button variant="danger" disabled={busy || counted === ""} onClick={() => void close()}>
                  <Lock size={16} />{t("bo.closeShiftButton", "Close shift")}
                </Button>
              </div>
            </div>
          </Card>
        </>
      )}
    </Page>
  );
}

function VarianceText({ value }: { value: number }) {
  const rounded = Math.round(value * 100) / 100;
  return (
    <strong className={cn("tabular-nums", rounded === 0 ? "text-good" : rounded < 0 ? "text-bad" : "text-warn")}>
      {rounded > 0 ? "+" : ""}{azn(rounded)}
      {rounded === 0 ? ` · ${t("bo.balanced", "balanced")}` : rounded < 0 ? ` · ${t("bo.shortDrawer", "short")}` : ` · ${t("bo.over", "over")}`}
    </strong>
  );
}

function ShiftFigures({ s }: { s: ShiftSummary }) {
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Kpi label={t("bo.salesTotal", "Sales total")} value={azn(s.salesTotal)} hint={`${t("bo.cash", "Cash")} ${azn(s.salesCash)} · ${t("bo.card", "Card")} ${azn(s.salesCard)}`} />
        <Kpi label={t("bo.tickets", "Tickets")} value={String(s.ticketsSold)} hint={azn(s.ticketCash + s.ticketCard)} />
        <Kpi label={t("bo.barReceipts", "Bar receipts")} value={String(s.barReceipts)} hint={azn(s.barCash + s.barCard)} />
        <Kpi label={t("bo.expectedCash", "Expected in drawer")} value={azn(s.expectedCash)} hint={`${t("bo.float", "Opening float")} ${azn(s.openingFloat)}`} />
      </div>
      {s.countedCash != null ? (
        <p className="text-sm text-ink-mute">
          {t("bo.counted", "Counted")} <span className="tabular-nums text-ink">{azn(s.countedCash)}</span> · {t("bo.variance", "Variance")} <VarianceText value={s.variance ?? 0} />
          {s.note ? <> · <em>{s.note}</em></> : null}
        </p>
      ) : null}
    </div>
  );
}

/** Every shift, newest first — the manager's view of who took what, and whether drawers balanced. */
export function ShiftHistoryView() {
  const { data, error, loading } = useLoad(() => get<ShiftSummary[]>("/api/backoffice/shifts?take=100"));
  return (
    <Page title={t("bo.shiftHistory", "Shift history")} lede={t("bo.shiftHistoryLede", "Every till shift with its Z report figures.")}>
      {error ? <Alert tone="error">{error}</Alert> : null}
      {loading && !data ? <Loading /> : null}
      <Card padded={false}>
        <Table
          head={[
            { label: t("bo.cashier", "Cashier") }, { label: t("bo.opened", "Opened") }, { label: t("bo.closed", "Closed") },
            { label: t("bo.tickets", "Tickets"), numeric: true }, { label: t("bo.salesTotal", "Sales total"), numeric: true },
            { label: t("bo.expectedCash", "Expected in drawer"), numeric: true }, { label: t("bo.counted", "Counted"), numeric: true },
            { label: t("bo.variance", "Variance"), numeric: true },
          ]}
          empty={t("bo.noShifts", "No shifts yet.")}
        >
          {(data ?? []).map((s) => (
            <tr key={s.id}>
              <Td className="text-ink">{s.cashierName}</Td>
              <Td>{dateTime(s.openedAtUtc)}</Td>
              <Td>{s.closedAtUtc ? dateTime(s.closedAtUtc) : <span className="text-good">{t("bo.openNow", "open")}</span>}</Td>
              <Td numeric>{s.ticketsSold}</Td>
              <Td numeric>{azn(s.salesTotal)}</Td>
              <Td numeric>{azn(s.expectedCash)}</Td>
              <Td numeric>{s.countedCash != null ? azn(s.countedCash) : "—"}</Td>
              <Td numeric>{s.variance != null ? <VarianceText value={s.variance} /> : "—"}</Td>
            </tr>
          ))}
        </Table>
      </Card>
    </Page>
  );
}
