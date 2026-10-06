import { useState } from "react";
import { Download } from "lucide-react";
import { get, download, query } from "@/lib/api";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { RevenueChart, type DayRevenue } from "./RevenueChart";
import { Alert, Card, Kpi, Loading, Page, Table, Td, azn, bakuDate, pct, tariffName, useLoad } from "./shared";
import type { TicketType } from "./BoxOfficeView";

interface SalesReport {
  from: string;
  to: string;
  totals: {
    boxOffice: number; online: number; counter: number; refunded: number; tickets: number;
    bar: number; barCost: number; barMargin: number; barReceipts: number; spendPerHead: number; total: number;
  };
  daily: DayRevenue[];
  byFilm: { movieTitle: string; tickets: number; gross: number }[];
  byHall: { venueName: string; hall: string; screenings: number; tickets: number; capacity: number; occupancy: number; gross: number }[];
  byTicketType: { ticketType: string; tickets: number; revenue: number }[];
  topBar: { name: string; quantity: number; revenue: number; margin: number }[];
}

const PRESETS = [
  { key: "7", days: 7 },
  { key: "30", days: 30 },
  { key: "90", days: 90 },
] as const;

/** Sales over a period, dated by when the money was taken (Baku time). */
export function ReportsView() {
  const [from, setFrom] = useState(bakuDate(-6));
  const [to, setTo] = useState(bakuDate());
  const { data, error, loading } = useLoad(() => get<SalesReport>(`/api/backoffice/reports/sales${query({ from, to })}`), [from, to]);
  // Sold seats keep the stable English name; this turns it back into the current language.
  const tariffs = useLoad(() => get<TicketType[]>("/api/backoffice/ticket-types?all=true"));
  const localTariff = (name: string) => {
    const type = tariffs.data?.find((ty) => ty.name === name);
    return type ? tariffName(type) : name;
  };

  return (
    <Page
      title={t("bo.reportsTitle", "Reports")}
      lede={t("bo.reportsLede", "Box office and bar together, by day, film, hall and tariff.")}
      actions={
        <div className="flex flex-wrap items-center gap-2">
          <div className="flex gap-1 rounded-full border border-line p-1">
            {PRESETS.map((p) => {
              const start = bakuDate(-(p.days - 1));
              const on = from === start && to === bakuDate();
              return (
                <button key={p.key} type="button" onClick={() => { setFrom(start); setTo(bakuDate()); }}
                  className={cn("h-7 rounded-full px-3 text-xs", on ? "bg-accent text-surface" : "text-ink-mute hover:text-ink")}>
                  {p.days} {t("bo.days", "days")}
                </button>
              );
            })}
          </div>
          <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="h-8 w-auto" aria-label={t("bo.from", "From")} />
          <span className="text-ink-mute">–</span>
          <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="h-8 w-auto" aria-label={t("bo.to", "To")} />
          <Button size="sm" variant="outline" onClick={() => void download(`/api/admin/export/bookings.csv${query({ from, to })}`, "bookings.csv")}>
            <Download size={14} />{t("bo.bookingsCsv", "Bookings CSV")}
          </Button>
        </div>
      }
    >
      {error ? <Alert tone="error">{error}</Alert> : null}
      {loading && !data ? <Loading /> : null}
      {data ? (
        <>
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
            <Kpi label={t("bo.revenueTotal", "Total revenue")} value={azn(data.totals.total)} />
            <Kpi label={t("bo.boxOffice", "Box office")} value={azn(data.totals.boxOffice)}
              hint={`${t("bo.online", "Online")} ${azn(data.totals.online)} · ${t("bo.counter", "Counter")} ${azn(data.totals.counter)}`} />
            <Kpi label={t("bo.bar", "Bar")} value={azn(data.totals.bar)} hint={`${t("bo.margin", "margin")} ${azn(data.totals.barMargin)}`} />
            <Kpi label={t("bo.kpiTickets", "Tickets sold")} value={String(data.totals.tickets)}
              hint={data.totals.refunded > 0 ? `${t("bo.refunded", "Refunded")} ${azn(data.totals.refunded)}` : undefined} />
            <Kpi label={t("bo.kpiSph", "Spend per head")} value={azn(data.totals.spendPerHead)} hint={t("bo.sphHint", "Bar revenue ÷ tickets sold")} />
          </div>

          <Card title={t("bo.byDay", "By day")}><RevenueChart data={data.daily} /></Card>

          <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
            <Card title={t("bo.byFilm", "By film")} padded={false}>
              <Table head={[{ label: t("bo.film", "Film") }, { label: t("bo.tickets", "Tickets"), numeric: true }, { label: t("bo.gross", "Net box office"), numeric: true }]}
                empty={t("bo.noSales", "No sales in this period.")}>
                {data.byFilm.map((f) => (
                  <tr key={f.movieTitle}><Td className="text-ink">{f.movieTitle}</Td><Td numeric>{f.tickets}</Td><Td numeric>{azn(f.gross)}</Td></tr>
                ))}
              </Table>
            </Card>

            <Card title={t("bo.byTariff", "By ticket type")} padded={false}>
              <Table head={[{ label: t("bo.ticketType", "Ticket type") }, { label: t("bo.tickets", "Tickets"), numeric: true }, { label: t("bo.revenue", "Revenue"), numeric: true }]}
                empty={t("bo.noSales", "No sales in this period.")}>
                {data.byTicketType.map((r) => (
                  <tr key={r.ticketType}>
                    <Td className="text-ink">{r.ticketType === "Online" ? t("bo.online", "Online") : localTariff(r.ticketType)}</Td>
                    <Td numeric>{r.tickets}</Td><Td numeric>{azn(r.revenue)}</Td>
                  </tr>
                ))}
              </Table>
            </Card>
          </div>

          <Card title={t("bo.byHall", "By hall")} padded={false}>
            <Table head={[
              { label: t("bo.hall", "Hall") }, { label: t("bo.showsCol", "Shows"), numeric: true },
              { label: t("bo.tickets", "Tickets"), numeric: true }, { label: t("bo.occupancy", "Occupancy"), numeric: true },
              { label: t("bo.gross", "Net box office"), numeric: true },
            ]} empty={t("bo.noSales", "No sales in this period.")}>
              {data.byHall.map((h) => (
                <tr key={`${h.venueName}-${h.hall}`}>
                  <Td className="text-ink">{h.venueName} · {h.hall}</Td>
                  <Td numeric>{h.screenings}</Td>
                  <Td numeric>{h.tickets} / {h.capacity}</Td>
                  <Td numeric>{pct(h.occupancy)}</Td>
                  <Td numeric>{azn(h.gross)}</Td>
                </tr>
              ))}
            </Table>
          </Card>

          <Card title={t("bo.topBar", "Bar best sellers")} padded={false}>
            <Table head={[
              { label: t("bo.name", "Name") }, { label: t("bo.qty", "Qty"), numeric: true },
              { label: t("bo.revenue", "Revenue"), numeric: true }, { label: t("bo.margin", "margin"), numeric: true },
            ]} empty={t("bo.noSales", "No sales in this period.")}>
              {data.topBar.map((b) => (
                <tr key={b.name}><Td className="text-ink">{b.name}</Td><Td numeric>{b.quantity}</Td><Td numeric>{azn(b.revenue)}</Td><Td numeric>{azn(b.margin)}</Td></tr>
              ))}
            </Table>
          </Card>
        </>
      ) : null}
    </Page>
  );
}
