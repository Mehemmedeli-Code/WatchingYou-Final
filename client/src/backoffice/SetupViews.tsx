import { useState } from "react";
import { Download, Pencil, Plus, Trash2, X } from "lucide-react";
import { get, post, put, del, download, query, type Paged } from "@/lib/api";
import { t, languageName } from "@/lib/i18n";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input, Field, Select } from "@/components/ui/input";
import { BulkScheduleForm, type VenueOption } from "@/components/admin/BulkScheduleForm";
import { PromoAdmin } from "@/components/admin/PromoAdmin";
import { Alert, Card, Kpi, Loading, Page, Table, Td, azn, bakuDate, dateTime, errorText, pct, tariffName, useLoad } from "./shared";
import { CATEGORIES, categoryLabel, type BarItem, type Category } from "./BarView";
import type { TicketType } from "./BoxOfficeView";

interface FilmOption { id: string; title: string }

async function films(): Promise<FilmOption[]> {
  const page = await get<Paged<{ id: string; title: string }>>("/api/movies" + query({ pageSize: 100, sortBy: "title" }));
  return page.items.map((m) => ({ id: m.id, title: m.title }));
}

// ================================================================== schedule

interface AdminScreening {
  id: string; movieTitle: string; venueName: string; hall: string; startsAtUtc: string;
  rows: number; seatsPerRow: number; seatPrice: number; audioLanguage: string; subtitleLanguage?: string | null;
  seatsSold: number; isCancelled: boolean; cancellationReason?: string | null;
}

/** Film programming: what plays where and when. Bulk scheduling fills a week in one go. */
export function ScheduleView() {
  const screenings = useLoad(() => get<AdminScreening[]>("/api/admin/screenings"));
  const options = useLoad(async () => {
    const [movieList, venues] = await Promise.all([films(), get<VenueOption[]>("/api/venues")]);
    return { movieList, venues };
  });
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);

  const now = Date.now();
  const upcoming = (screenings.data ?? []).filter((s) => new Date(s.startsAtUtc).getTime() > now - 3 * 3600_000);

  async function cancel(s: AdminScreening) {
    const reason = window.prompt(t("bo.cancelReason", "Why is it cancelled? Customers will be told."));
    if (!reason) return;
    try {
      await post(`/api/admin/screenings/${s.id}/cancel`, { reason });
      setMessage({ tone: "ok", text: t("bo.cancelled", "Cancelled.") });
      void screenings.reload();
    } catch (err) { setMessage({ tone: "error", text: errorText(err, t("bo.errCancel", "Could not cancel.")) }); }
  }

  return (
    <Page title={t("bo.scheduleTitle", "Schedule")} lede={t("bo.scheduleLede", "Screenings by hall and time. A hall needs 2½ hours between starts.")}>
      {message ? <Alert tone={message.tone}>{message.text}</Alert> : null}
      <div className="grid grid-cols-1 gap-5 xl:grid-cols-[1fr_360px]">
        <Card title={t("bo.upcoming", "Upcoming screenings")} padded={false}>
          {screenings.loading && !screenings.data ? <div className="px-4"><Loading /></div> : null}
          <Table
            head={[
              { label: t("bo.when", "When") }, { label: t("bo.film", "Film") }, { label: t("bo.hall", "Hall") },
              { label: t("bo.language", "Language") }, { label: t("bo.price", "Price"), numeric: true },
              { label: t("bo.sold", "Sold"), numeric: true }, { label: "" },
            ]}
            empty={t("bo.noUpcoming", "Nothing scheduled.")}
          >
            {upcoming.map((s) => (
              <tr key={s.id} className={s.isCancelled ? "opacity-50" : undefined}>
                <Td>{dateTime(s.startsAtUtc)}</Td>
                <Td className="text-ink">{s.movieTitle}</Td>
                <Td className="text-ink-mute">{s.venueName} · {s.hall}</Td>
                <Td>{languageName(s.audioLanguage)}{s.subtitleLanguage ? ` / ${s.subtitleLanguage.toUpperCase()}` : ""}</Td>
                <Td numeric>{azn(s.seatPrice)}</Td>
                <Td numeric>{s.seatsSold} / {s.rows * s.seatsPerRow}</Td>
                <Td numeric>
                  {s.isCancelled
                    ? <Badge tone="bad">{t("bo.cancelledBadge", "Cancelled")}</Badge>
                    : <Button size="sm" variant="ghost" onClick={() => void cancel(s)}>{t("bo.cancel", "Cancel")}</Button>}
                </Td>
              </tr>
            ))}
          </Table>
        </Card>

        <Card title={t("bo.bulkSchedule", "Schedule a run")}>
          {options.data ? (
            <BulkScheduleForm movies={options.data.movieList} venues={options.data.venues} onSaved={() => screenings.reload()} />
          ) : <Loading />}
        </Card>
      </div>
    </Page>
  );
}

// ================================================================== pricing

/** Ticket types sold at the counter, as a percentage of each screening's price, plus the
 *  promo codes the website accepts. */
type TariffDraft = {
  id?: string; name: string; nameAz: string; nameRu: string; nameTr: string;
  percentOfBase: string; sortOrder: string; isActive: boolean;
};

export function PricingView() {
  const types = useLoad(() => get<TicketType[]>("/api/backoffice/ticket-types?all=true"));
  const [draft, setDraft] = useState<TariffDraft | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    if (!draft) return;
    setError(null);
    const body = {
      name: draft.name, nameAz: draft.nameAz || null, nameRu: draft.nameRu || null, nameTr: draft.nameTr || null,
      percentOfBase: Number(draft.percentOfBase), sortOrder: Number(draft.sortOrder) || 0, isActive: draft.isActive,
    };
    try {
      if (draft.id) await put(`/api/backoffice/ticket-types/${draft.id}`, body);
      else await post("/api/backoffice/ticket-types", body);
      setDraft(null);
      void types.reload();
    } catch (err) { setError(errorText(err, t("bo.errSave", "Could not save."))); }
  }

  const edit = (ty: TicketType) => setDraft({
    id: ty.id, name: ty.name, nameAz: ty.nameAz ?? "", nameRu: ty.nameRu ?? "", nameTr: ty.nameTr ?? "",
    percentOfBase: String(ty.percentOfBase), sortOrder: String(ty.sortOrder), isActive: ty.isActive,
  });

  return (
    <Page title={t("bo.pricingTitle", "Prices & tariffs")} lede={t("bo.pricingLede", "Ticket types are a percentage of the screening price, so one child tariff works for every show.")}>
      <Card title={t("bo.ticketTypes", "Ticket types")}
        actions={<Button size="sm" onClick={() => setDraft({ name: "", nameAz: "", nameRu: "", nameTr: "", percentOfBase: "100", sortOrder: String((types.data?.length ?? 0) + 1), isActive: true })}><Plus size={14} />{t("bo.add", "Add")}</Button>}
        padded={false}>
        <Table head={[{ label: t("bo.name", "Name") }, { label: t("bo.percent", "% of price"), numeric: true }, { label: t("bo.example", "At 10 ₼"), numeric: true }, { label: t("bo.status", "Status") }, { label: "" }]}>
          {(types.data ?? []).map((ty) => (
            <tr key={ty.id}>
              <Td>
                <span className="text-ink">{tariffName(ty)}</span>
                {/* Every language at a glance, so a missing translation is easy to spot. */}
                <span className="mt-0.5 block text-xs text-ink-mute">
                  EN {ty.name} · AZ {ty.nameAz || "—"} · RU {ty.nameRu || "—"} · TR {ty.nameTr || "—"}
                </span>
              </Td>
              <Td numeric>{ty.percentOfBase}%</Td>
              <Td numeric>{azn(Math.round(10 * ty.percentOfBase) / 100)}</Td>
              <Td>{ty.isActive ? <Badge tone="good">{t("bo.active", "On sale")}</Badge> : <Badge>{t("bo.inactive", "Off")}</Badge>}</Td>
              <Td numeric>
                <Button size="sm" variant="ghost" aria-label={t("bo.edit", "Edit")} onClick={() => edit(ty)}><Pencil size={14} /></Button>
                <Button size="sm" variant="ghost" aria-label={t("bo.delete", "Delete")} onClick={async () => { if (window.confirm(`${tariffName(ty)}?`)) { await del(`/api/backoffice/ticket-types/${ty.id}`); void types.reload(); } }}><Trash2 size={14} /></Button>
              </Td>
            </tr>
          ))}
        </Table>
        {draft ? (
          <div className="space-y-3 border-t border-line p-4">
            <p className="text-xs text-ink-mute">{t("bo.tariffNamesHint", "Name the tariff in each language. English is required; an empty language shows the English name.")}</p>
            <div className="grid gap-3 sm:grid-cols-4">
              <Field label="EN *"><Input value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} /></Field>
              <Field label="AZ"><Input value={draft.nameAz} onChange={(e) => setDraft({ ...draft, nameAz: e.target.value })} /></Field>
              <Field label="RU"><Input value={draft.nameRu} onChange={(e) => setDraft({ ...draft, nameRu: e.target.value })} /></Field>
              <Field label="TR"><Input value={draft.nameTr} onChange={(e) => setDraft({ ...draft, nameTr: e.target.value })} /></Field>
            </div>
            <div className="grid gap-3 sm:grid-cols-[120px_100px_auto_auto] sm:items-end">
              <Field label={t("bo.percent", "% of price")}><Input inputMode="decimal" value={draft.percentOfBase} onChange={(e) => setDraft({ ...draft, percentOfBase: e.target.value })} /></Field>
              <Field label={t("bo.order", "Order")}><Input inputMode="numeric" value={draft.sortOrder} onChange={(e) => setDraft({ ...draft, sortOrder: e.target.value })} /></Field>
              <label className="flex h-10 items-center gap-2 text-sm text-ink-mute"><input type="checkbox" checked={draft.isActive} onChange={(e) => setDraft({ ...draft, isActive: e.target.checked })} />{t("bo.active", "On sale")}</label>
              <div className="flex gap-2">
                <Button onClick={() => void save()}>{t("common.save", "Save")}</Button>
                <Button variant="ghost" onClick={() => setDraft(null)}><X size={16} /></Button>
              </div>
            </div>
            {error ? <Alert tone="error">{error}</Alert> : null}
          </div>
        ) : null}
      </Card>

      <Card title={t("bo.promoCodes", "Promo codes (online)")}>
        <PromoAdmin />
      </Card>
    </Page>
  );
}

// ================================================================== bar menu & stock

type ItemDraft = { id?: string; name: string; category: Category; price: string; costPrice: string; stock: string; lowStockThreshold: string; isActive: boolean };

/** The bar menu, with cost prices for the margin report and stock for the till. */
export function MenuView() {
  const items = useLoad(() => get<BarItem[]>("/api/backoffice/bar/items"));
  const [draft, setDraft] = useState<ItemDraft | null>(null);
  const [error, setError] = useState<string | null>(null);

  const list = items.data ?? [];
  const stockValue = list.reduce((sum, i) => sum + i.stock * i.costPrice, 0);

  async function save() {
    if (!draft) return;
    setError(null);
    const body = {
      name: draft.name, category: draft.category, price: Number(draft.price), costPrice: Number(draft.costPrice) || 0,
      stock: Number(draft.stock) || 0, lowStockThreshold: Number(draft.lowStockThreshold) || 0, isActive: draft.isActive,
    };
    try {
      if (draft.id) await put(`/api/backoffice/bar/items/${draft.id}`, body);
      else await post("/api/backoffice/bar/items", body);
      setDraft(null);
      void items.reload();
    } catch (err) { setError(errorText(err, t("bo.errSave", "Could not save."))); }
  }

  async function restock(item: BarItem) {
    const answer = window.prompt(`${item.name}: ${t("bo.restockHowMany", "how many arrived?")}`, "24");
    const quantity = Number(answer);
    if (!answer || !Number.isFinite(quantity) || quantity <= 0) return;
    try { await post(`/api/backoffice/bar/items/${item.id}/restock`, { quantity }); void items.reload(); }
    catch (err) { setError(errorText(err, t("bo.errRestock", "Could not restock."))); }
  }

  const edit = (i: BarItem) => setDraft({
    id: i.id, name: i.name, category: i.category, price: String(i.price), costPrice: String(i.costPrice),
    stock: String(i.stock), lowStockThreshold: String(i.lowStockThreshold), isActive: i.isActive,
  });

  return (
    <Page title={t("bo.menuTitle", "Bar menu & stock")} lede={t("bo.menuLede", "Prices, cost prices and stock levels. Restocking adds to the count, so a sale in progress is never overwritten.")}
      actions={<Button size="sm" onClick={() => setDraft({ name: "", category: "Snacks", price: "", costPrice: "", stock: "0", lowStockThreshold: "10", isActive: true })}><Plus size={14} />{t("bo.addItem", "Add item")}</Button>}>
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Kpi label={t("bo.items", "Items")} value={String(list.length)} />
        <Kpi label={t("bo.lowStock", "Running low")} value={String(list.filter((i) => i.isLow && i.isActive).length)} tone={list.some((i) => i.isLow && i.isActive) ? "warn" : undefined} />
        <Kpi label={t("bo.stockValue", "Stock at cost")} value={azn(stockValue)} />
      </div>
      {error ? <Alert tone="error">{error}</Alert> : null}

      {draft ? (
        <Card title={draft.id ? t("bo.editItem", "Edit item") : t("bo.addItem", "Add item")}>
          <div className="grid gap-3 sm:grid-cols-4">
            <div className="sm:col-span-2"><Field label={t("bo.name", "Name")}><Input value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} /></Field></div>
            <Field label={t("bo.category", "Category")}>
              <Select value={draft.category} onChange={(e) => setDraft({ ...draft, category: e.target.value as Category })}>
                {CATEGORIES.map((c) => <option key={c} value={c}>{categoryLabel(c)}</option>)}
              </Select>
            </Field>
            <label className="flex h-10 items-center gap-2 self-end text-sm text-ink-mute"><input type="checkbox" checked={draft.isActive} onChange={(e) => setDraft({ ...draft, isActive: e.target.checked })} />{t("bo.active", "On sale")}</label>
            <Field label={t("bo.price", "Price")}><Input inputMode="decimal" value={draft.price} onChange={(e) => setDraft({ ...draft, price: e.target.value })} /></Field>
            <Field label={t("bo.cost", "Cost price")}><Input inputMode="decimal" value={draft.costPrice} onChange={(e) => setDraft({ ...draft, costPrice: e.target.value })} /></Field>
            <Field label={t("bo.stock", "Stock")}><Input inputMode="numeric" value={draft.stock} onChange={(e) => setDraft({ ...draft, stock: e.target.value })} /></Field>
            <Field label={t("bo.lowAt", "Warn at")}><Input inputMode="numeric" value={draft.lowStockThreshold} onChange={(e) => setDraft({ ...draft, lowStockThreshold: e.target.value })} /></Field>
          </div>
          <div className="mt-4 flex gap-2">
            <Button onClick={() => void save()}>{t("common.save", "Save")}</Button>
            <Button variant="ghost" onClick={() => setDraft(null)}>{t("common.cancel", "Cancel")}</Button>
          </div>
        </Card>
      ) : null}

      <Card padded={false}>
        {items.loading && !items.data ? <div className="px-4"><Loading /></div> : null}
        <Table head={[
          { label: t("bo.name", "Name") }, { label: t("bo.category", "Category") },
          { label: t("bo.price", "Price"), numeric: true }, { label: t("bo.cost", "Cost price"), numeric: true },
          { label: t("bo.marginPct", "Margin"), numeric: true }, { label: t("bo.stock", "Stock"), numeric: true }, { label: "" },
        ]}>
          {list.map((i) => (
            <tr key={i.id} className={i.isActive ? undefined : "opacity-50"}>
              <Td className="text-ink">{i.name}</Td>
              <Td>{categoryLabel(i.category)}</Td>
              <Td numeric>{azn(i.price)}</Td>
              <Td numeric>{azn(i.costPrice)}</Td>
              <Td numeric>{pct(i.price ? ((i.price - i.costPrice) * 100) / i.price : 0)}</Td>
              <Td numeric>{i.isLow ? <Badge tone={i.stock === 0 ? "bad" : "warn"}>{i.stock}</Badge> : i.stock}</Td>
              <Td numeric>
                <Button size="sm" variant="outline" onClick={() => void restock(i)}>+ {t("bo.restock", "Restock")}</Button>
                <Button size="sm" variant="ghost" aria-label={t("bo.edit", "Edit")} onClick={() => edit(i)}><Pencil size={14} /></Button>
                <Button size="sm" variant="ghost" aria-label={t("bo.delete", "Delete")} onClick={async () => { if (window.confirm(`${i.name}?`)) { await del(`/api/backoffice/bar/items/${i.id}`); void items.reload(); } }}><Trash2 size={14} /></Button>
              </Td>
            </tr>
          ))}
        </Table>
      </Card>
    </Page>
  );
}

// ================================================================== distributors

interface FilmDeal { id: string; movieId: string; movieTitle: string; distributor: string; sharePercent: number; note?: string | null }
interface SettlementRow {
  movieId: string; movieTitle: string; distributor?: string | null; sharePercent: number; hasDeal: boolean;
  screenings: number; tickets: number; gross: number; distributorDue: number; cinemaNet: number;
}
interface Settlement { from: string; to: string; rows: SettlementRow[]; gross: number; distributorDue: number; cinemaNet: number }

/** Film rental terms and the settlement owed. The distributor's share is taken from the net
 *  box office — tickets after refunds — of performances that played in the period. */
export function DistributorsView() {
  const [from, setFrom] = useState(bakuDate(-29));
  const [to, setTo] = useState(bakuDate());
  const deals = useLoad(() => get<FilmDeal[]>("/api/backoffice/deals"));
  const report = useLoad(() => get<Settlement>(`/api/backoffice/reports/settlement${query({ from, to })}`), [from, to]);
  const movieList = useLoad(films);
  const [draft, setDraft] = useState({ movieId: "", distributor: "", sharePercent: "50", note: "" });
  const [error, setError] = useState<string | null>(null);

  async function save() {
    setError(null);
    try {
      await post("/api/backoffice/deals", { ...draft, sharePercent: Number(draft.sharePercent), note: draft.note || null });
      setDraft({ movieId: "", distributor: "", sharePercent: "50", note: "" });
      void deals.reload(); void report.reload();
    } catch (err) { setError(errorText(err, t("bo.errSave", "Could not save."))); }
  }

  return (
    <Page title={t("bo.distributorsTitle", "Distributors & settlement")}
      lede={t("bo.distributorsLede", "Each film's distributor takes an agreed share of the box office. This is what you owe them.")}>
      <Card title={t("bo.settlement", "Settlement")}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="h-8 w-auto" aria-label={t("bo.from", "From")} />
            <span className="text-ink-mute">–</span>
            <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="h-8 w-auto" aria-label={t("bo.to", "To")} />
            <Button size="sm" variant="outline" onClick={() => void download(`/api/backoffice/reports/settlement.csv${query({ from, to })}`, "settlement.csv")}>
              <Download size={14} />CSV
            </Button>
          </div>
        }
        padded={false}>
        {report.data ? (
          <>
            <div className="grid grid-cols-3 gap-3 p-4">
              <Kpi label={t("bo.gross", "Net box office")} value={azn(report.data.gross)} />
              <Kpi label={t("bo.due", "Owed to distributors")} value={azn(report.data.distributorDue)} />
              <Kpi label={t("bo.cinemaNet", "Cinema keeps")} value={azn(report.data.cinemaNet)} tone="good" />
            </div>
            <Table head={[
              { label: t("bo.film", "Film") }, { label: t("bo.distributor", "Distributor") }, { label: t("bo.share", "Share"), numeric: true },
              { label: t("bo.showsCol", "Shows"), numeric: true }, { label: t("bo.tickets", "Tickets"), numeric: true },
              { label: t("bo.gross", "Net box office"), numeric: true }, { label: t("bo.due", "Owed to distributors"), numeric: true },
              { label: t("bo.cinemaNet", "Cinema keeps"), numeric: true },
            ]} empty={t("bo.nothingPlayed", "Nothing played in this period.")}>
              {report.data.rows.map((r) => (
                <tr key={r.movieId}>
                  <Td className="text-ink">{r.movieTitle}</Td>
                  <Td>{r.hasDeal ? r.distributor : <Badge tone="warn">{t("bo.noDeal", "No deal")}</Badge>}</Td>
                  <Td numeric>{r.sharePercent}%</Td>
                  <Td numeric>{r.screenings}</Td>
                  <Td numeric>{r.tickets}</Td>
                  <Td numeric>{azn(r.gross)}</Td>
                  <Td numeric>{azn(r.distributorDue)}</Td>
                  <Td numeric>{azn(r.cinemaNet)}</Td>
                </tr>
              ))}
            </Table>
          </>
        ) : <div className="px-4"><Loading /></div>}
      </Card>

      <Card title={t("bo.deals", "Film deals")} padded={false}>
        <div className="grid gap-3 border-b border-line p-4 sm:grid-cols-[1.4fr_1.2fr_90px_1fr_auto] sm:items-end">
          <Field label={t("bo.film", "Film")}>
            <Select value={draft.movieId} onChange={(e) => setDraft({ ...draft, movieId: e.target.value })}>
              <option value="">—</option>
              {(movieList.data ?? []).map((m) => <option key={m.id} value={m.id}>{m.title}</option>)}
            </Select>
          </Field>
          <Field label={t("bo.distributor", "Distributor")}><Input value={draft.distributor} onChange={(e) => setDraft({ ...draft, distributor: e.target.value })} /></Field>
          <Field label={t("bo.share", "Share") + " %"}><Input inputMode="decimal" value={draft.sharePercent} onChange={(e) => setDraft({ ...draft, sharePercent: e.target.value })} /></Field>
          <Field label={t("bo.note", "Note")}><Input value={draft.note} onChange={(e) => setDraft({ ...draft, note: e.target.value })} /></Field>
          <Button disabled={!draft.movieId || !draft.distributor} onClick={() => void save()}>{t("common.save", "Save")}</Button>
          {error ? <div className="sm:col-span-5"><Alert tone="error">{error}</Alert></div> : null}
        </div>
        <Table head={[{ label: t("bo.film", "Film") }, { label: t("bo.distributor", "Distributor") }, { label: t("bo.share", "Share"), numeric: true }, { label: t("bo.note", "Note") }, { label: "" }]}
          empty={t("bo.noDeals", "No deals yet.")}>
          {(deals.data ?? []).map((d) => (
            <tr key={d.id}>
              <Td className="text-ink">{d.movieTitle}</Td>
              <Td>{d.distributor}</Td>
              <Td numeric>{d.sharePercent}%</Td>
              <Td className="text-ink-mute">{d.note ?? ""}</Td>
              <Td numeric>
                <Button size="sm" variant="ghost" aria-label={t("bo.edit", "Edit")} onClick={() => setDraft({ movieId: d.movieId, distributor: d.distributor, sharePercent: String(d.sharePercent), note: d.note ?? "" })}><Pencil size={14} /></Button>
                <Button size="sm" variant="ghost" aria-label={t("bo.delete", "Delete")} onClick={async () => { if (window.confirm(`${d.movieTitle}?`)) { await del(`/api/backoffice/deals/${d.id}`); void deals.reload(); void report.reload(); } }}><Trash2 size={14} /></Button>
              </Td>
            </tr>
          ))}
        </Table>
      </Card>
    </Page>
  );
}
