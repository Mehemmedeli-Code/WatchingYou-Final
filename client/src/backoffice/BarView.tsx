import { useMemo, useState } from "react";
import { Minus, Plus, Printer, Trash2 } from "lucide-react";
import { get, post } from "@/lib/api";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Alert, Card, Loading, NewSaleButton, Page, TenderPicker, azn, dateTime, errorText, printReceipt, useLoad, type Tender } from "./shared";
import { NeedShift, type ShiftSummary } from "./ShiftView";

export type Category = "Popcorn" | "Drinks" | "Snacks" | "Combo";

export interface BarItem {
  id: string;
  name: string;
  category: Category;
  price: number;
  costPrice: number;
  stock: number;
  lowStockThreshold: number;
  isActive: boolean;
  isLow: boolean;
}

interface BarSale {
  id: string;
  reference: string;
  total: number;
  change?: number | null;
  tender: Tender;
  atUtc: string;
  lines: { name: string; quantity: number; unitPrice: number; lineTotal: number }[];
}

export const CATEGORIES: Category[] = ["Popcorn", "Drinks", "Snacks", "Combo"];
export const categoryLabel = (c: Category) => ({
  Popcorn: t("bo.catPopcorn", "Popcorn"),
  Drinks: t("bo.catDrinks", "Drinks"),
  Snacks: t("bo.catSnacks", "Snacks"),
  Combo: t("bo.catCombo", "Combos"),
})[c];

/** The bar till: big buttons, a running basket, cash or card. Stock is checked again on the
 *  server when the sale goes through, so the count shown here is a guide, not a promise. */
export function BarView({ shift, onSold }: { shift: ShiftSummary | null; onSold: () => void }) {
  const items = useLoad(() => get<BarItem[]>("/api/backoffice/bar/items"));
  const [category, setCategory] = useState<Category | "All">("All");
  const [basket, setBasket] = useState<Record<string, number>>({});
  const [tender, setTender] = useState<Tender>("Cash");
  const [received, setReceived] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sale, setSale] = useState<BarSale | null>(null);

  const byId = useMemo(() => new Map((items.data ?? []).map((i) => [i.id, i])), [items.data]);
  const visible = (items.data ?? []).filter((i) => i.isActive && (category === "All" || i.category === category));
  const lines = Object.entries(basket).map(([id, quantity]) => ({ item: byId.get(id)!, quantity })).filter((l) => l.item);
  const total = lines.reduce((sum, l) => sum + l.item.price * l.quantity, 0);

  const add = (id: string, delta: number) => {
    setSale(null);
    setError(null);
    setBasket((b) => {
      const next = { ...b, [id]: (b[id] ?? 0) + delta };
      if (next[id] <= 0) delete next[id];
      return next;
    });
  };

  async function sell() {
    setBusy(true); setError(null);
    try {
      const result = await post<BarSale>("/api/backoffice/bar/sell", {
        lines: lines.map((l) => ({ itemId: l.item.id, quantity: l.quantity })),
        tender,
        cashReceived: tender === "Cash" && received !== "" ? Number(received.replace(",", ".")) : null,
      });
      setSale(result);
      setBasket({});
      setReceived("");
      onSold();
      void items.reload();
    } catch (err) {
      setError(errorText(err, t("bo.saleFailed", "The sale did not go through.")));
      void items.reload();
    } finally {
      setBusy(false);
    }
  }

  if (!shift) return <NeedShift title={t("bo.barTitle", "Bar")} />;

  return (
    <Page title={t("bo.barTitle", "Bar")} lede={t("bo.barLede", "Popcorn, drinks and combos. Stock goes down with every sale.")}>
      {items.error ? <Alert tone="error">{items.error}</Alert> : null}
      <div className="grid grid-cols-1 gap-5 lg:grid-cols-[1fr_340px]">
        <div className="space-y-4">
          <div className="flex flex-wrap gap-2">
            {(["All", ...CATEGORIES] as const).map((c) => (
              <button key={c} type="button" onClick={() => setCategory(c)}
                className={cn("h-9 rounded-full border px-4 text-sm",
                  category === c ? "border-accent bg-accent/10 text-accent" : "border-line text-ink-mute hover:text-ink")}>
                {c === "All" ? t("bo.all", "All") : categoryLabel(c)}
              </button>
            ))}
          </div>

          {items.loading && !items.data ? <Loading /> : null}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4">
            {visible.map((item) => {
              const inBasket = basket[item.id] ?? 0;
              const out = item.stock - inBasket <= 0;
              return (
                <button key={item.id} type="button" disabled={out} onClick={() => add(item.id, 1)}
                  className={cn(
                    "relative flex min-h-28 flex-col justify-between rounded-[var(--radius-card)] border p-3 text-left transition-colors",
                    "disabled:cursor-not-allowed disabled:opacity-40",
                    inBasket ? "border-accent bg-accent/10" : "border-line bg-surface-raised hover:border-accent",
                  )}>
                  <span className="pr-6 text-sm font-medium text-ink">{item.name}</span>
                  <span className="mt-2 block">
                    <span className="block whitespace-nowrap font-display text-lg font-semibold tabular-nums text-ink">{azn(item.price)}</span>
                    <span className={cn("block text-[11px] tabular-nums", item.isLow ? "text-warn" : "text-ink-mute")}>
                      {item.stock} {t("bo.inStock", "in stock")}
                    </span>
                  </span>
                  {inBasket ? (
                    <span className="absolute right-2 top-2 flex h-6 min-w-6 items-center justify-center rounded-full bg-accent px-1.5 text-xs font-semibold text-surface">
                      {inBasket}
                    </span>
                  ) : null}
                </button>
              );
            })}
          </div>
        </div>

        <Card title={t("bo.basket", "Basket")}>
          {sale ? (
            <div className="space-y-4">
              <Alert tone="ok">{t("bo.sold", "Sold")} · <strong>{sale.reference}</strong></Alert>
              <NewSaleButton onClick={() => setSale(null)} />
              <p className="text-sm text-ink-mute">{t("bo.total", "Total")} <span className="tabular-nums text-ink">{azn(sale.total)}</span></p>
              {sale.change != null ? (
                <p className="text-base">{t("bo.change", "Change")}: <strong className="tabular-nums text-accent">{azn(sale.change)}</strong></p>
              ) : null}
              <Button variant="outline" className="w-full" onClick={() => printReceipt(sale.reference,
                [...sale.lines.map((l) => [`${l.quantity} × ${l.name}`, azn(l.lineTotal)] as [string, string]),
                 [t("bo.total", "Total"), azn(sale.total)],
                 ...(sale.change != null ? [[t("bo.change", "Change"), azn(sale.change)] as [string, string]] : [])],
                [dateTime(sale.atUtc), shift.cashierName])}>
                <Printer size={16} />{t("bo.receipt", "Receipt")}
              </Button>
            </div>
          ) : (
            <div className="space-y-4">
              {lines.length === 0 ? (
                <p className="text-sm text-ink-mute">{t("bo.barEmpty", "Tap an item to add it.")}</p>
              ) : (
                <ul className="space-y-2">
                  {lines.map(({ item, quantity }) => (
                    <li key={item.id} className="flex items-center gap-2 text-sm">
                      <span className="flex-1 text-ink">{item.name}</span>
                      <button type="button" aria-label="−" onClick={() => add(item.id, -1)} className="grid h-7 w-7 place-items-center rounded-md border border-line text-ink-mute hover:text-ink"><Minus size={14} /></button>
                      <span className="w-6 text-center tabular-nums">{quantity}</span>
                      <button type="button" aria-label="+" onClick={() => add(item.id, 1)} disabled={quantity >= item.stock} className="grid h-7 w-7 place-items-center rounded-md border border-line text-ink-mute hover:text-ink disabled:opacity-40"><Plus size={14} /></button>
                      <span className="w-16 text-right tabular-nums">{azn(item.price * quantity)}</span>
                    </li>
                  ))}
                </ul>
              )}
              {lines.length ? (
                <button type="button" onClick={() => setBasket({})} className="flex items-center gap-1.5 text-xs text-ink-mute hover:text-bad">
                  <Trash2 size={12} />{t("bo.clear", "Clear basket")}
                </button>
              ) : null}

              <div className="flex items-baseline justify-between border-t border-line pt-3">
                <span className="text-sm text-ink-mute">{t("bo.total", "Total")}</span>
                <span className="font-display text-2xl font-semibold tabular-nums text-ink">{azn(total)}</span>
              </div>
              <TenderPicker total={total} tender={tender} onTender={setTender} received={received} onReceived={setReceived} />
              {error ? <Alert tone="error">{error}</Alert> : null}
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
