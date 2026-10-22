import { RefreshCw } from "lucide-react";
import { get } from "@/lib/api";
import { t } from "@/lib/i18n";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { RevenueChart, type DayRevenue } from "./RevenueChart";
import { Alert, Card, Kpi, Loading, Page, Table, Td, azn, pct, time, useLoad } from "./shared";
import type { BarItem } from "./BarView";

interface Dashboard {
  day: string;
  boxOffice: number;
  boxOfficeOnline: number;
  boxOfficeCounter: number;
  tickets: number;
  bar: number;
  barReceipts: number;
  barMargin: number;
  spendPerHead: number;
  screeningsToday: number;
  occupancyToday: number;
  openShifts: { id: string; cashierName: string; openedAtUtc: string; salesTotal: number }[];
  lowStock: BarItem[];
  screenings: { id: string; startsAtUtc: string; movieTitle: string; venueName: string; hall: string; sold: number; capacity: number }[];
  lastSevenDays: DayRevenue[];
}

/** The manager's first screen: today's money, today's shows, who is on a till, what is
 *  running out. Everything here is a link to somewhere that can act on it. */
export function DashboardView({ go }: { go: (route: string) => void }) {
  const { data, error, loading, reload } = useLoad(() => get<Dashboard>("/api/backoffice/dashboard"));

  return (
    <Page
      title={t("bo.dashboard", "Dashboard")}
      lede={t("bo.dashboardLede", "Today at a glance, in Baku time.")}
      actions={<Button size="sm" variant="outline" onClick={() => void reload()}><RefreshCw size={14} />{t("bo.refresh", "Refresh")}</Button>}
    >
      {error ? <Alert tone="error">{error}</Alert> : null}
      {loading && !data ? <Loading /> : null}
      {data ? (
        <>
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <Kpi label={t("bo.kpiBoxOffice", "Box office today")} value={azn(data.boxOffice)}
              hint={`${t("bo.online", "Online")} ${azn(data.boxOfficeOnline)} · ${t("bo.counter", "Counter")} ${azn(data.boxOfficeCounter)}`} />
            <Kpi label={t("bo.kpiTickets", "Tickets sold")} value={String(data.tickets)}
              hint={`${data.screeningsToday} ${t("bo.showsToday", "shows today")} · ${pct(data.occupancyToday)} ${t("bo.full", "full")}`} />
            <Kpi label={t("bo.kpiBar", "Bar today")} value={azn(data.bar)}
              hint={`${data.barReceipts} ${t("bo.receipts", "receipts")} · ${t("bo.margin", "margin")} ${azn(data.barMargin)}`} />
            <Kpi label={t("bo.kpiSph", "Spend per head")} value={azn(data.spendPerHead)}
              hint={t("bo.sphHint", "Bar revenue ÷ tickets sold")} />
          </div>

          <div className="grid grid-cols-1 gap-5 lg:grid-cols-[1.6fr_1fr]">
            <Card title={t("bo.lastSeven", "Last 7 days")}>
              <RevenueChart data={data.lastSevenDays} />
            </Card>

            <div className="space-y-5">
              <Card title={t("bo.openShifts", "Open shifts")}
                actions={<Button size="sm" variant="ghost" onClick={() => go("shifts")}>{t("bo.history", "History")}</Button>}>
                {data.openShifts.length === 0 ? (
                  <p className="text-sm text-ink-mute">{t("bo.noOpenShifts", "No till is open.")}</p>
                ) : (
                  <ul className="space-y-2 text-sm">
                    {data.openShifts.map((s) => (
                      <li key={s.id} className="flex items-center justify-between gap-2">
                        <span className="text-ink">{s.cashierName}<span className="ml-2 text-xs text-ink-mute">{t("bo.since", "since")} {time(s.openedAtUtc)}</span></span>
                        <span className="tabular-nums text-ink-mute">{azn(s.salesTotal)}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </Card>

              <Card title={t("bo.lowStock", "Running low")}
                actions={<Button size="sm" variant="ghost" onClick={() => go("menu")}>{t("bo.restock", "Restock")}</Button>}>
                {data.lowStock.length === 0 ? (
                  <p className="text-sm text-ink-mute">{t("bo.stockFine", "Everything is stocked.")}</p>
                ) : (
                  <ul className="space-y-2 text-sm">
                    {data.lowStock.map((item) => (
                      <li key={item.id} className="flex items-center justify-between gap-2">
                        <span className="text-ink">{item.name}</span>
                        <Badge tone={item.stock === 0 ? "bad" : "warn"}>
                          {item.stock === 0 ? t("bo.soldOut", "Sold out") : `${item.stock} ${t("bo.left", "left")}`}
                        </Badge>
                      </li>
                    ))}
                  </ul>
                )}
              </Card>
            </div>
          </div>

          <Card title={t("bo.todaysShows", "Today's shows")} padded={false}
            actions={<Button size="sm" variant="ghost" onClick={() => go("box-office")}>{t("bo.sellTickets", "Sell tickets")}</Button>}>
            <Table
              head={[
                { label: t("bo.time", "Time") }, { label: t("bo.film", "Film") }, { label: t("bo.hall", "Hall") },
                { label: t("bo.sold", "Sold"), numeric: true }, { label: t("bo.occupancy", "Occupancy"), numeric: true },
              ]}
              empty={t("bo.noShowsToday", "Nothing on the schedule today.")}
            >
              {data.screenings.map((s) => {
                const share = s.capacity ? (s.sold * 100) / s.capacity : 0;
                return (
                  <tr key={s.id}>
                    <Td>{time(s.startsAtUtc)}</Td>
                    <Td className="text-ink">{s.movieTitle}</Td>
                    <Td className="text-ink-mute">{s.venueName} · {s.hall}</Td>
                    <Td numeric>{s.sold} / {s.capacity}</Td>
                    <Td numeric>
                      <span className="inline-flex items-center gap-2">
                        <span className="h-1.5 w-16 overflow-hidden rounded-full bg-line" aria-hidden>
                          <span className="block h-full rounded-full bg-series-1" style={{ width: `${Math.min(100, share)}%` }} />
                        </span>
                        {pct(share)}
                      </span>
                    </Td>
                  </tr>
                );
              })}
            </Table>
          </Card>
        </>
      ) : null}
    </Page>
  );
}
