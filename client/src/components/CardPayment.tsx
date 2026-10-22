import { useState, type ReactNode } from "react";
import { CheckCircle2, CreditCard, Lock } from "lucide-react";
import { Notice } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Field } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Modal } from "@/components/ui/modal";
import { ApiError } from "@/lib/api";
import { t, formatWhen } from "@/lib/i18n";

/**
 * The card form the seat checkout uses, shared: the fields, the checks that can be made before
 * sending (brand, expiry, length), and a payment sheet for everything else that is paid by card
 * — a rental, three more days, Watching PRO — which ends in a receipt rather than a ticket.
 */

/** Issuer, by BIN. Only the name is shown, not the bank's logo. Add rows as you collect BINs. */
const ISSUERS: { prefix: string; name: string }[] = [
  { prefix: "41697388", name: "Kapital Bank" },
];
export const issuerFor = (digits: string) => ISSUERS.find((issuer) => digits.startsWith(issuer.prefix))?.name ?? null;

/** Visa starts with 4; Mastercard is 51–55 or the 2221–2720 range added in 2017. */
export function brandFor(digits: string): "Visa" | "Mastercard" | null {
  if (digits.startsWith("4")) return "Visa";
  const two = Number(digits.slice(0, 2));
  if (digits.length >= 2 && two >= 51 && two <= 55) return "Mastercard";
  const four = Number(digits.slice(0, 4));
  if (digits.length >= 4 && four >= 2221 && four <= 2720) return "Mastercard";
  return null;
}

/** Returns a reason the expiry cannot be right, or null. */
export function expiryProblem(value: string): string | null {
  const digits = value.replace(/\D/g, "");
  if (digits.length < 4) return null;                       // still typing
  const month = Number(digits.slice(0, 2));
  const year = 2000 + Number(digits.slice(2, 4));
  if (month < 1 || month > 12) return t("book.badMonth");
  const endOfMonth = new Date(year, month, 0, 23, 59, 59);
  return endOfMonth < new Date() ? t("book.expired") : null;
}

/** Groups digits in fours as you type, sixteen at most (Visa and Mastercard). */
export const groupDigits = (value: string) =>
  value.replace(/\D/g, "").slice(0, 16).replace(/(.{4})/g, "$1 ").trim();

export interface CardDetails { number: string; expiryMonth: number; expiryYear: number; cvc: string; holderName: string }

/** The card being typed, and what can already be said about it. */
export function useCard() {
  const [number, setNumber] = useState("");
  const [expiry, setExpiry] = useState("");
  const [cvc, setCvc] = useState("");
  const [holder, setHolder] = useState("");
  const digits = number.replace(/\D/g, "");
  const brand = brandFor(digits);
  const expiryError = expiryProblem(expiry);
  const ready = digits.length === 16 && !!brand && cvc.length === 3 && !expiryError
    && expiry.replace(/\D/g, "").length === 4 && holder.trim().length > 1;
  return {
    number, setNumber, expiry, setExpiry, cvc, setCvc, holder, setHolder,
    digits, brand, issuer: issuerFor(digits), expiryError, ready,
    details(): CardDetails {
      const [month, year] = expiry.split("/").map((part) => Number(part.trim()));
      return { number: digits, expiryMonth: month || 0, expiryYear: year || 0, cvc, holderName: holder.trim() };
    },
    /** The number and CVC leave the page's memory the moment they have been sent. */
    clear() { setNumber(""); setCvc(""); setExpiry(""); },
  };
}

export type Card = ReturnType<typeof useCard>;

export function CardFields({ card }: { card: Card }) {
  const numberHint = card.digits.length >= 6 && !card.brand ? t("book.onlyVisaMc") : "4242 4242 4242 4242";
  return (
    <div className="grid gap-3">
      <Field label={t("book.cardNumber")} hint={numberHint}>
        <div className="relative">
          <Input value={card.number} onChange={(e) => card.setNumber(groupDigits(e.target.value))}
            inputMode="numeric" autoComplete="cc-number" placeholder="0000 0000 0000 0000" className="pr-32" />
          {card.issuer || card.brand ? (
            <span className="pointer-events-none absolute right-2 top-1/2 flex -translate-y-1/2 gap-1">
              {card.issuer ? <Badge tone="warn">{card.issuer}</Badge> : null}
              {card.brand ? <Badge tone="good">{card.brand}</Badge> : null}
            </span>
          ) : null}
        </div>
      </Field>
      <div className="grid grid-cols-2 gap-3">
        <Field label={t("book.expiry")} hint={card.expiryError ?? t("book.expiryFormat", "MM/YY")}>
          <Input value={card.expiry} inputMode="numeric" autoComplete="cc-exp" placeholder="12/29"
            onChange={(e) => {
              const d = e.target.value.replace(/\D/g, "").slice(0, 4);
              card.setExpiry(d.length > 2 ? `${d.slice(0, 2)}/${d.slice(2)}` : d);
            }} />
        </Field>
        <Field label={t("book.cvc")}>
          <Input value={card.cvc} inputMode="numeric" autoComplete="cc-csc" placeholder="123"
            onChange={(e) => card.setCvc(e.target.value.replace(/\D/g, "").slice(0, 3))} />
        </Field>
      </div>
      <Field label={t("book.holder")}>
        <Input value={card.holder} onChange={(e) => card.setHolder(e.target.value)} autoComplete="cc-name" />
      </Field>
    </div>
  );
}

/** What the customer is paying for, as the sheet's summary shows it. */
export interface PaymentItem { title: string; detail?: string; amount: string; icon?: ReactNode }

/**
 * A payment by card in a sheet over the page: the item and price, the card form, Pay. On
 * success it turns into a receipt (no ticket, no QR — there is nothing to show at a door).
 * `pay` sends the card and resolves once the server has taken the payment.
 */
export function PaymentSheet({ item, pay, onClose, onPaid }: {
  item: PaymentItem;
  pay: (card: CardDetails) => Promise<unknown>;
  onClose: () => void;
  /** After the receipt is closed, so the page can reload what was bought. */
  onPaid: () => void;
}) {
  const card = useCard();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [receipt, setReceipt] = useState<{ brand: string; last4: string; at: string } | null>(null);

  async function submit() {
    setBusy(true);
    setError(null);
    const details = card.details();
    try {
      await pay(details);
      setReceipt({ brand: card.brand ?? "", last4: details.number.slice(-4), at: new Date().toISOString() });
      card.clear();
    } catch (err) {
      setError(err instanceof ApiError ? err.message
        : t("error.unreachable", "Could not reach the server. Nothing was charged — please try again in a moment."));
    } finally {
      setBusy(false);
    }
  }

  const close = () => { if (busy) return; if (receipt) onPaid(); onClose(); };

  return (
    <Modal onClose={close} label={t("book.payment")} width="max-w-md">
      <div className="rounded-2xl border border-line bg-surface-raised p-6 shadow-2xl shadow-black/60">
        {receipt ? (
          <div className="text-center">
            <CheckCircle2 size={44} className="mx-auto text-accent" aria-hidden />
            <p className="mt-3 font-display text-xl text-ink">{t("pay.done", "Payment received")}</p>
            <dl className="mt-5 space-y-2 rounded-xl border border-line p-4 text-left text-sm">
              <div className="flex justify-between gap-3"><dt className="text-ink-mute">{t("pay.for", "For")}</dt><dd className="text-right text-ink">{item.title}</dd></div>
              {item.detail ? <div className="flex justify-between gap-3"><dt className="text-ink-mute">{t("pay.what", "What")}</dt><dd className="text-right text-ink">{item.detail}</dd></div> : null}
              <div className="flex justify-between gap-3"><dt className="text-ink-mute">{t("book.total")}</dt><dd className="font-semibold text-ink">{item.amount}</dd></div>
              <div className="flex justify-between gap-3"><dt className="text-ink-mute">{t("pay.card", "Card")}</dt><dd className="text-ink">{receipt.brand} •••• {receipt.last4}</dd></div>
              <div className="flex justify-between gap-3"><dt className="text-ink-mute">{t("pay.when", "Paid")}</dt><dd className="text-ink">{formatWhen(receipt.at)}</dd></div>
            </dl>
            <Button className="mt-5 w-full" onClick={close}>{t("pay.continue", "Continue")}</Button>
          </div>
        ) : (
          <>
            <h3 className="flex items-center gap-2 font-display text-xl text-ink">
              <CreditCard size={18} aria-hidden /> {t("book.payment")}
            </h3>
            <div className="mt-4 flex items-center justify-between gap-3 rounded-xl border border-line bg-surface p-4">
              <div className="flex min-w-0 items-center gap-3">
                {item.icon ? <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-accent/15 text-accent">{item.icon}</span> : null}
                <div className="min-w-0">
                  <p className="truncate font-medium text-ink">{item.title}</p>
                  {item.detail ? <p className="text-xs text-ink-mute">{item.detail}</p> : null}
                </div>
              </div>
              <Badge tone="good">{item.amount}</Badge>
            </div>

            <div className="mt-5"><CardFields card={card} /></div>

            {error ? <div className="mt-3"><Notice tone="error">{error}</Notice></div> : null}
            <div className="mt-3"><Notice tone="info">{t("book.simulated")}</Notice></div>

            <div className="mt-4 flex gap-2">
              <Button className="flex-1" disabled={busy || !card.ready} onClick={submit}>
                <Lock size={14} aria-hidden /> {busy ? t("book.paying") : `${t("book.pay")} ${item.amount}`}
              </Button>
              <Button variant="outline" disabled={busy} onClick={close}>{t("common.cancel")}</Button>
            </div>
          </>
        )}
      </div>
    </Modal>
  );
}
