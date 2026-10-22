import { useCallback, useEffect, useState, type ReactNode } from "react";
import { RotateCcw } from "lucide-react";
import { cn } from "@/lib/utils";
import { ApiError } from "@/lib/api";
import { formatDateIn, lang, t } from "@/lib/i18n";

/**
 * Pieces every back-office screen shares. The back office is denser than the public site —
 * tables, tills, numbers read at a glance — so it has its own small kit rather than the
 * roomy Section/Panel pair the catalogue pages use.
 */

// ------------------------------------------------------------------ formatting

/** The cinema is in Baku and so are its shifts: every time on these screens is Baku time,
 *  whatever the browser's own zone is. */
export const TIME_ZONE = "Asia/Baku";

const manat = new Intl.NumberFormat("az-AZ", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
export const azn = (value: number) => `${manat.format(value)} ₼`;

export const time = (iso: string) =>
  new Intl.DateTimeFormat(lang, { hour: "2-digit", minute: "2-digit", timeZone: TIME_ZONE }).format(new Date(iso));

export const dateTime = (iso: string) =>
  formatDateIn(new Date(iso), { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", timeZone: TIME_ZONE });

/** Today in Baku as yyyy-MM-dd, the shape every back-office date parameter takes. */
export function bakuDate(offsetDays = 0): string {
  const shifted = new Date(Date.now() + 4 * 3600_000 + offsetDays * 86_400_000);
  return shifted.toISOString().slice(0, 10);
}

/** A tariff's name in the interface language, falling back to its stable English name. */
export function tariffName(type: { name: string; nameAz?: string | null; nameRu?: string | null; nameTr?: string | null }): string {
  const local = lang === "az" ? type.nameAz : lang === "ru" ? type.nameRu : lang === "tr" ? type.nameTr : null;
  return local || type.name;
}

export const pct = (value: number) => `${value.toFixed(value >= 10 ? 0 : 1)}%`;

export function errorText(err: unknown, fallback: string): string {
  if (err instanceof ApiError) {
    const first = err.fieldErrors ? Object.values(err.fieldErrors)[0]?.[0] : undefined;
    return first ?? err.message;
  }
  return fallback;
}

// ------------------------------------------------------------------ data

/** Loads on mount and on demand. Errors are kept, not thrown, so one failing panel does not
 *  blank the whole screen. */
export function useLoad<T>(load: () => Promise<T>, deps: unknown[] = []) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  // eslint-disable-next-line react-hooks/exhaustive-deps
  const reload = useCallback(async () => {
    setLoading(true);
    try {
      setData(await load());
      setError(null);
    } catch (err) {
      setError(errorText(err, t("bo.loadFailed", "Could not load this.")));
    } finally {
      setLoading(false);
    }
  }, deps);

  useEffect(() => { void reload(); }, [reload]);
  return { data, error, loading, reload, setData };
}

// ------------------------------------------------------------------ layout

export function Page({ title, lede, actions, children }: {
  title: string; lede?: string; actions?: ReactNode; children: ReactNode;
}) {
  return (
    <div className="mx-auto w-full max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
      <div className="mb-5 flex flex-wrap items-end justify-between gap-3">
        <div className="max-w-[70ch]">
          <h1 className="font-display text-2xl font-semibold tracking-tight text-ink">{title}</h1>
          {lede ? <p className="mt-1 text-sm text-ink-mute">{lede}</p> : null}
        </div>
        {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
      </div>
      <div className="space-y-5">{children}</div>
    </div>
  );
}

export function Card({ title, actions, children, className, padded = true }: {
  title?: string; actions?: ReactNode; children: ReactNode; className?: string; padded?: boolean;
}) {
  return (
    <section className={cn("rounded-[var(--radius-card)] border border-line bg-surface-raised", className)}>
      {title || actions ? (
        <header className="flex flex-wrap items-center justify-between gap-2 border-b border-line px-4 py-3">
          {title ? <h2 className="text-sm font-semibold text-ink">{title}</h2> : <span />}
          {actions}
        </header>
      ) : null}
      <div className={padded ? "p-4" : undefined}>{children}</div>
    </section>
  );
}

export function Kpi({ label, value, hint, tone }: {
  label: string; value: string; hint?: string; tone?: "good" | "warn" | "bad";
}) {
  const toneClass = tone === "good" ? "text-good" : tone === "warn" ? "text-warn" : tone === "bad" ? "text-bad" : "text-ink";
  return (
    <div className="rounded-[var(--radius-card)] border border-line bg-surface-raised px-4 py-3">
      <p className="text-xs text-ink-mute">{label}</p>
      <p className={cn("mt-1 font-display text-2xl font-semibold tabular-nums", toneClass)}>{value}</p>
      {hint ? <p className="mt-0.5 text-xs text-ink-mute">{hint}</p> : null}
    </div>
  );
}

export function Alert({ tone = "info", children }: { tone?: "info" | "error" | "ok"; children: ReactNode }) {
  const tones = {
    info: "border-line text-ink-mute",
    error: "border-bad bg-bad-bg/40 text-bad",
    ok: "border-good bg-good-bg/40 text-good",
  } as const;
  return (
    <div role={tone === "error" ? "alert" : "status"} className={cn("rounded-md border px-3 py-2 text-sm", tones[tone])}>
      {children}
    </div>
  );
}

export function Loading() {
  return (
    <div className="flex items-center gap-3 py-8 text-sm text-ink-mute">
      <span className="h-4 w-4 animate-spin rounded-full border-2 border-line border-t-accent" aria-hidden />
      {t("common.loading", "Loading")}
    </div>
  );
}

/** A plain data table. Numbers right-aligned and tabular so columns of money line up. */
export function Table({ head, children, empty }: {
  head: { label: string; numeric?: boolean }[]; children: ReactNode; empty?: string;
}) {
  const hasRows = Array.isArray(children) ? children.length > 0 : Boolean(children);
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[32rem] text-left text-sm">
        <thead className="text-xs text-ink-mute">
          <tr className="border-b border-line">
            {head.map((h, i) => (
              <th key={i} className={cn("px-3 py-2 font-medium", h.numeric && "text-right")}>{h.label}</th>
            ))}
          </tr>
        </thead>
        <tbody className="tabular-nums">
          {hasRows ? children : (
            <tr><td colSpan={head.length} className="px-3 py-6 text-center text-ink-mute">{empty ?? "—"}</td></tr>
          )}
        </tbody>
      </table>
    </div>
  );
}

export const Td = ({ children, numeric, className }: { children: ReactNode; numeric?: boolean; className?: string }) => (
  <td className={cn("border-b border-line/60 px-3 py-2", numeric && "text-right", className)}>{children}</td>
);

// ------------------------------------------------------------------ till bits

export type Tender = "Cash" | "Card";

/** Cash or card, and for cash the amount handed over so the till can say the change. */
export function TenderPicker({ total, tender, onTender, received, onReceived }: {
  total: number; tender: Tender; onTender: (t: Tender) => void;
  received: string; onReceived: (v: string) => void;
}) {
  const given = Number(received.replace(",", "."));
  const change = tender === "Cash" && received !== "" && !Number.isNaN(given) ? given - total : null;
  const quick = [5, 10, 20, 50, 100].filter((note) => note >= total).slice(0, 3);

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-2" role="radiogroup" aria-label={t("bo.tender", "Payment")}>
        {(["Cash", "Card"] as const).map((option) => (
          <button
            key={option}
            type="button"
            role="radio"
            aria-checked={tender === option}
            onClick={() => onTender(option)}
            className={cn(
              "h-11 rounded-md border text-sm font-medium transition-colors",
              tender === option ? "border-accent bg-accent/10 text-accent" : "border-line text-ink-mute hover:text-ink",
            )}
          >
            {option === "Cash" ? t("bo.cash", "Cash") : t("bo.card", "Card")}
          </button>
        ))}
      </div>
      {tender === "Cash" ? (
        <div className="space-y-2">
          <label className="block space-y-1">
            <span className="text-xs text-ink-mute">{t("bo.received", "Cash received")}</span>
            <input
              inputMode="decimal"
              value={received}
              onChange={(e) => onReceived(e.target.value)}
              placeholder={total.toFixed(2)}
              className="h-10 w-full rounded-md border border-line bg-surface px-3 text-sm tabular-nums text-ink focus:border-accent focus:outline-none"
            />
          </label>
          {quick.length ? (
            <div className="flex gap-1.5">
              {quick.map((note) => (
                <button key={note} type="button" onClick={() => onReceived(String(note))}
                  className="h-8 flex-1 rounded-md border border-line text-xs text-ink-mute hover:border-accent hover:text-accent">
                  {note} ₼
                </button>
              ))}
            </div>
          ) : null}
          {change !== null ? (
            <p className={cn("text-sm tabular-nums", change < 0 ? "text-bad" : "text-ink")}>
              {change < 0 ? t("bo.short", "Short by") : t("bo.change", "Change")}: <strong>{azn(Math.abs(change))}</strong>
            </p>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

/**
 * The button a cashier presses more than any other: after every sale, before the next
 * customer. So it is the biggest thing on the screen, a thumb-sized target on a touch till,
 * and it takes focus the moment the sale completes — Enter or Space starts the next one
 * without reaching for the mouse.
 */
export function NewSaleButton({ onClick }: { onClick: () => void }) {
  return (
    <button
      type="button"
      autoFocus
      onClick={onClick}
      className={cn(
        "flex h-20 w-full items-center justify-center gap-3 rounded-2xl bg-accent text-xl font-semibold text-surface",
        "shadow-lg shadow-accent/20 transition-colors hover:bg-accent-bright active:scale-[0.99]",
        "focus-visible:outline-4 focus-visible:outline-offset-2 focus-visible:outline-accent",
      )}
    >
      <RotateCcw size={26} aria-hidden />
      {t("bo.newSale", "New sale")}
    </button>
  );
}

/** Opens a small print window with a till receipt. Browsers print what they are shown, so
 *  the receipt is a page of its own at 80 mm — the width of a thermal till roll. */
export function printReceipt(title: string, lines: [string, string][], footer: string[] = []) {
  const win = window.open("", "_blank", "width=380,height=640");
  if (!win) return;
  const escape = (s: string) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]!);
  win.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>${escape(title)}</title>
    <style>
      @page { size: 80mm auto; margin: 4mm; }
      body { font: 12px/1.45 ui-monospace, Menlo, Consolas, monospace; color: #000; width: 72mm; margin: 0 auto; }
      h1 { font-size: 14px; text-align: center; margin: 0 0 6px; }
      .row { display: flex; justify-content: space-between; gap: 8px; }
      hr { border: 0; border-top: 1px dashed #000; margin: 6px 0; }
      p { margin: 2px 0; text-align: center; }
    </style></head><body>
    <h1>WatchingYou</h1><p>${escape(title)}</p><hr/>
    ${lines.map(([a, b]) => `<div class="row"><span>${escape(a)}</span><span>${escape(b)}</span></div>`).join("")}
    <hr/>${footer.map((f) => `<p>${escape(f)}</p>`).join("")}
    <script>window.onload = () => { window.print(); }<\/script>
    </body></html>`);
  win.document.close();
}
