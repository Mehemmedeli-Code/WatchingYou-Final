import { useEffect, useMemo, useState } from "react";
import { Printer, Ticket } from "lucide-react";
import { get, post, download } from "@/lib/api";
import { t, languageName } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Alert, Card, Loading, NewSaleButton, Page, TenderPicker, azn, bakuDate, errorText, printReceipt, tariffName, time, useLoad, type Tender,
} from "./shared";
import type { ShiftSummary } from "./ShiftView";
import { NeedShift } from "./ShiftView";

interface BoxOfficeScreening {
  id: string;
  movieTitle: string;
  venueName: string;
  hall: string;
  format?: string | null;
  startsAtUtc: string;
  seatPrice: number;
  capacity: number;
  seatsSold: number;
  audioLanguage: string;
  subtitleLanguage?: string | null;
  isOnSale: boolean;
}

interface SeatState { row: number; number: number; isTaken: boolean; isHeld: boolean }
interface SeatMap { screeningId: string; rows: number; seatsPerRow: number; seatPrice: number; seats: SeatState[] }
export interface TicketType {
  id: string; name: string; percentOfBase: number; sortOrder: number; isActive: boolean;
  nameAz?: string | null; nameRu?: string | null; nameTr?: string | null;
}

interface SaleResult {
  paymentId: string;
  reference: string;
  total: number;
  change?: number | null;
  ticket: { movieTitle: string; hall: string; startsAtUtc: string; seats: { label: string }[] };
}

const rowLabel = (row: number) => String.fromCharCode(64 + row);
const priceFor = (base: number, type?: TicketType) => Math.round(base * (type?.percentOfBase ?? 100)) / 100;

/**
 * The box office till. Pick a show, click seats on the map, choose a ticket type per seat,
 * take the money. Sales write the same rows the website does, so the seat vanishes from the
 * online map the moment it is sold here.
 */
export function BoxOfficeView({ shift, onSold }: { shift: ShiftSummary | null; onSold: () => void }) {
  const [day, setDay] = useState(bakuDate());
  const screenings = useLoad(() => get<BoxOfficeScreening[]>(`/api/backoffice/box-office/screenings?date=${day}`), [day]);
  const types = useLoad(() => get<TicketType[]>("/api/backoffice/ticket-types"));

  const [selected, setSelected] = useState<BoxOfficeScreening | null>(null);
  const [map, setMap] = useState<SeatMap | null>(null);
  const [basket, setBasket] = useState<Record<string, string>>({});   // "row-number" -> ticketTypeId
  const [tender, setTender] = useState<Tender>("Cash");
  const [received, setReceived] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ tone: "error" | "ok"; text: string } | null>(null);
  const [sale, setSale] = useState<SaleResult | null>(null);

  const defaultType = types.data?.[0]?.id ?? "";
  const typeById = useMemo(() => new Map((types.data ?? []).map((ty) => [ty.id, ty])), [types.data]);

  async function loadMap(screening: BoxOfficeScreening) {
    setMap(await get<SeatMap>(`/api/screenings/${screening.id}/seats`));
  }

  useEffect(() => {
    setBasket({});
    setMap(null);
    setSale(null);
    if (selected) void loadMap(selected).catch((err) => setMessage({ tone: "error", text: errorText(err, t("bo.errSeatMap", "Seat map failed to load.")) }));
  }, [selected]);

  const lines = Object.entries(basket).map(([key, typeId]) => {
    const [row, number] = key.split("-").map(Number);
    return { key, row, number, typeId, price: priceFor(map?.seatPrice ?? 0, typeById.get(typeId)) };
  }).sort((a, b) => a.row - b.row || a.number - b.number);
  const total = lines.reduce((sum, l) => sum + l.price, 0);

  function toggle(seat: SeatState) {
    if (seat.isTaken) return;
    const key = `${seat.row}-${seat.number}`;
    setBasket((current) => {
      const next = { ...current };
      if (next[key]) delete next[key];
      else next[key] = defaultType;
      return next;
    });
    setMessage(null);
  }

  async function sell() {
    if (!selected || lines.length === 0) return;
    setBusy(true); setMessage(null);
    try {
      const result = await post<SaleResult>("/api/backoffice/box-office/sell", {
        screeningId: selected.id,
        seats: lines.map((l) => ({ row: l.row, number: l.number, ticketTypeId: l.typeId })),
        tender,
        cashReceived: tender === "Cash" && received !== "" ? Number(received.replace(",", ".")) : null,
      });
      setSale(result);
      setBasket({});
      setReceived("");
      onSold();
      await loadMap(selected);
      void screenings.reload();
    } catch (err) {
      setMessage({ tone: "error", text: errorText(err, t("bo.saleFailed", "The sale did not go through.")) });
      await loadMap(selected).catch(() => undefined);
    } finally {
      setBusy(false);
    }
  }

  if (!shift) return <NeedShift title={t("bo.boxOfficeTitle", "Box office")} />;

  return (
    <Page
      title={t("bo.boxOfficeTitle", "Box office")}
      lede={t("bo.boxOfficeLede", "Sell seats to walk-in customers. Sold seats disappear from the online map at once.")}
      actions={
        <div className="flex gap-1 rounded-full border border-line p-1">
          {[0, 1, 2].map((offset) => {
            const value = bakuDate(offset);
            const label = offset === 0 ? t("bo.today", "Today") : offset === 1 ? t("bo.tomorrow", "Tomorrow") : value.slice(5).split("-").reverse().join(".");
            return (
              <button key={offset} type="button" onClick={() => { setDay(value); setSelected(null); }}
                className={cn("h-7 rounded-full px-3 text-xs", day === value ? "bg-accent text-surface" : "text-ink-mute hover:text-ink")}>
                {label}
              </button>
            );
          })}
        </div>
      }
    >
      {screenings.error ? <Alert tone="error">{screenings.error}</Alert> : null}

      <div className="grid grid-cols-1 gap-5 xl:grid-cols-[300px_1fr_320px]">
        {/* Shows */}
        <Card title={t("bo.shows", "Shows")} padded={false}>
          {screenings.loading && !screenings.data ? <div className="px-4"><Loading /></div> : null}
          <ul className="max-h-[70vh] divide-y divide-line overflow-y-auto">
            {(screenings.data ?? []).map((s) => (
              <li key={s.id}>
                <button type="button" disabled={!s.isOnSale} onClick={() => setSelected(s)}
                  className={cn("w-full px-4 py-3 text-left transition-colors disabled:opacity-40",
                    selected?.id === s.id ? "bg-accent/10" : "hover:bg-line/40")}>
                  <div className="flex items-baseline justify-between gap-2">
                    <span className="font-display text-lg tabular-nums text-ink">{time(s.startsAtUtc)}</span>
                    <span className="text-xs tabular-nums text-ink-mute">{s.capacity - s.seatsSold} {t("bo.free", "free")}</span>
                  </div>
                  <p className="text-sm text-ink">{s.movieTitle}</p>
                  <p className="text-xs text-ink-mute">
                    {s.venueName} · {s.hall}{s.format ? ` · ${s.format}` : ""} · {languageName(s.audioLanguage)}
                    {s.subtitleLanguage ? ` / ${s.subtitleLanguage.toUpperCase()}` : ""} · {azn(s.seatPrice)}
                  </p>
                </button>
              </li>
            ))}
            {screenings.data && screenings.data.length === 0 ? (
              <li className="px-4 py-8 text-center text-sm text-ink-mute">{t("bo.noShowsDay", "No shows on this day.")}</li>
            ) : null}
          </ul>
        </Card>

        {/* Seat map */}
        <Card title={selected ? `${selected.movieTitle} · ${time(selected.startsAtUtc)} · ${selected.hall}` : t("bo.seatMap", "Seat map")}>
          {!selected ? (
            <p className="py-16 text-center text-sm text-ink-mute">{t("bo.pickShow", "Pick a show on the left.")}</p>
          ) : !map ? <Loading /> : (
            <SeatGrid map={map} basket={basket} onToggle={toggle} />
          )}
        </Card>

        {/* Basket */}
        <Card title={t("bo.basket", "Basket")}>
          {sale ? (
            <div className="space-y-4">
              <Alert tone="ok">{t("bo.sold", "Sold")} · <strong>{sale.reference}</strong></Alert>
              <NewSaleButton onClick={() => setSale(null)} />
              <dl className="space-y-1 text-sm">
                <div className="flex justify-between"><dt className="text-ink-mute">{t("bo.seats", "Seats")}</dt><dd className="text-ink">{sale.ticket.seats.map((s) => s.label).join(", ")}</dd></div>
                <div className="flex justify-between"><dt className="text-ink-mute">{t("bo.total", "Total")}</dt><dd className="tabular-nums text-ink">{azn(sale.total)}</dd></div>
                {sale.change != null ? (
                  <div className="flex justify-between text-base"><dt className="text-ink-mute">{t("bo.change", "Change")}</dt><dd className="font-semibold tabular-nums text-accent">{azn(sale.change)}</dd></div>
                ) : null}
              </dl>
              <div className="grid gap-2">
                <Button variant="outline" onClick={() => void download(`/api/backoffice/box-office/${sale.paymentId}/ticket.pdf`, `WatchingYou-${sale.reference}.pdf`)}>
                  <Ticket size={16} />{t("bo.printTickets", "Tickets (PDF)")}
                </Button>
                <Button variant="outline" onClick={() => printReceipt(sale.reference, [
                  [sale.ticket.movieTitle, ""],
                  [`${sale.ticket.hall} · ${time(sale.ticket.startsAtUtc)}`, ""],
                  [t("bo.seats", "Seats"), sale.ticket.seats.map((s) => s.label).join(" ")],
                  [t("bo.total", "Total"), azn(sale.total)],
                  ...(sale.change != null ? [[t("bo.change", "Change"), azn(sale.change)] as [string, string]] : []),
                ], [shift.cashierName])}>
                  <Printer size={16} />{t("bo.receipt", "Receipt")}
                </Button>
              </div>
            </div>
          ) : (
            <div className="space-y-4">
              {lines.length === 0 ? (
                <p className="text-sm text-ink-mute">{t("bo.basketEmpty", "Click free seats on the map.")}</p>
              ) : (
                <ul className="space-y-2">
                  {lines.map((line) => (
                    <li key={line.key} className="flex items-center gap-2 text-sm">
                      <Badge>{rowLabel(line.row)}{line.number}</Badge>
                      <select
                        value={line.typeId}
                        onChange={(e) => setBasket((b) => ({ ...b, [line.key]: e.target.value }))}
                        className="h-8 flex-1 rounded-md border border-line bg-surface px-2 text-xs text-ink focus:border-accent focus:outline-none"
                        aria-label={t("bo.ticketType", "Ticket type")}
                      >
                        {(types.data ?? []).map((ty) => <option key={ty.id} value={ty.id}>{tariffName(ty)}</option>)}
                      </select>
                      <span className="w-20 text-right tabular-nums text-ink">{azn(line.price)}</span>
                    </li>
                  ))}
                </ul>
              )}

              <div className="flex items-baseline justify-between border-t border-line pt-3">
                <span className="text-sm text-ink-mute">{t("bo.total", "Total")}</span>
                <span className="font-display text-2xl font-semibold tabular-nums text-ink">{azn(total)}</span>
              </div>

              <TenderPicker total={total} tender={tender} onTender={setTender} received={received} onReceived={setReceived} />

              {message ? <Alert tone={message.tone}>{message.text}</Alert> : null}

              <Button size="lg" className="w-full" disabled={busy || lines.length === 0} onClick={() => void sell()}>
                {busy ? t("bo.selling", "Selling…") : `${t("bo.sell", "Sell")} · ${azn(total)}`}
              </Button>
            </div>
          )}
        </Card>
      </div>
    </Page>
  );
}

function SeatGrid({ map, basket, onToggle }: {
  map: SeatMap; basket: Record<string, string>; onToggle: (seat: SeatState) => void;
}) {
  const aisleAfter = Math.floor(map.seatsPerRow / 2);
  const rows = Array.from({ length: map.rows }, (_, i) => i + 1);

  return (
    <div className="space-y-4">
      <div className="mx-auto h-1.5 w-2/3 rounded-full bg-gradient-to-r from-transparent via-accent to-transparent opacity-70" aria-hidden />
      <p className="text-center text-[11px] uppercase tracking-[0.2em] text-ink-mute">{t("bo.screen", "Screen")}</p>

      <div className="overflow-x-auto pb-2">
        <div className="mx-auto w-max space-y-1.5">
          {rows.map((row) => (
            <div key={row} className="flex items-center gap-1.5">
              <span className="w-5 text-right text-xs text-ink-mute">{rowLabel(row)}</span>
              {map.seats.filter((s) => s.row === row).map((seat) => {
                const key = `${seat.row}-${seat.number}`;
                const chosen = Boolean(basket[key]);
                return (
                  <button
                    key={key}
                    type="button"
                    onClick={() => onToggle(seat)}
                    disabled={seat.isTaken}
                    aria-pressed={chosen}
                    aria-label={`${rowLabel(seat.row)}${seat.number}${seat.isTaken ? ` — ${t("bo.taken", "taken")}` : ""}`}
                    title={`${rowLabel(seat.row)}${seat.number}`}
                    className={cn(
                      "h-7 w-7 rounded-t-md rounded-b-sm text-[10px] tabular-nums transition-colors",
                      seat.number === aisleAfter + 1 && "ml-4",
                      seat.isTaken
                        ? seat.isHeld ? "cursor-not-allowed bg-warn-bg text-warn" : "cursor-not-allowed bg-line text-ink-mute/50"
                        : chosen ? "bg-accent text-surface" : "border border-line text-ink-mute hover:border-accent hover:text-accent",
                    )}
                  >
                    {seat.number}
                  </button>
                );
              })}
              <span className="w-5 text-xs text-ink-mute">{rowLabel(row)}</span>
            </div>
          ))}
        </div>
      </div>

      <div className="flex flex-wrap justify-center gap-4 text-xs text-ink-mute">
        <span className="flex items-center gap-1.5"><span className="h-3 w-3 rounded-sm border border-line" />{t("bo.legendFree", "Free")}</span>
        <span className="flex items-center gap-1.5"><span className="h-3 w-3 rounded-sm bg-accent" />{t("bo.legendChosen", "In basket")}</span>
        <span className="flex items-center gap-1.5"><span className="h-3 w-3 rounded-sm bg-line" />{t("bo.legendSold", "Sold")}</span>
        <span className="flex items-center gap-1.5"><span className="h-3 w-3 rounded-sm bg-warn-bg" />{t("bo.legendHeld", "Held online")}</span>
      </div>
    </div>
  );
}
