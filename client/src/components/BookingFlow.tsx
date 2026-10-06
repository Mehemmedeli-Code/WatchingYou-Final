import { useEffect, useState } from "react";
import QRCode from "qrcode";
import { CreditCard, Download, ExternalLink, Sparkles, Ticket } from "lucide-react";
import { Panel, Notice } from "@/components/Shell";
import { RefundPanel } from "@/components/RefundPanel";
import { Button } from "@/components/ui/button";
import { Input, Field } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { download, post, ApiError } from "@/lib/api";
import { formatMoney } from "@/lib/format";
import { t, formatWhen, languageName } from "@/lib/i18n";
import { openExternal } from "@/lib/platform";

export interface SeatSelection { row: number; number: number }

export interface CheckoutStarted {
  paymentId: string;
  reference: string;
  amount: number;
  brand: string;
  last4: string;
  maskedEmail: string;
  expiresAtUtc: string;
  subtotal?: number;
  promoDiscount?: number;
  pointsRedeemed?: number;
  pointsDiscount?: number;
}

/** The server's price for the basket as it stands — the same arithmetic the checkout uses. */
export interface CheckoutQuote {
  subtotal: number;
  promoDiscount: number;
  promoMessage?: string | null;
  promoApplied: boolean;
  pointsBalance: number;
  pointsUsed: number;
  pointsDiscount: number;
  total: number;
  pointsEarned: number;
  stripeEnabled: boolean;
}

interface StripeCheckoutStarted {
  paymentId: string;
  reference: string;
  amount: number;
  url: string;
  expiresAtUtc: string;
}

export interface TicketSeat {
  row: number;
  number: number;
  label: string;
  qrPayload: string;
}

export interface TicketResponse {
  paymentId: string;
  reference: string;
  movieTitle: string;
  hall: string;
  startsAtUtc: string;
  audioLanguage: string;
  subtitleLanguage?: string | null;
  seats: TicketSeat[];
  amount: number;
  brand: string;
  last4: string;
  confirmedAtUtc: string;
}

const seatLabel = (seat: SeatSelection) => `${String.fromCharCode(64 + seat.row)}${seat.number}`;

/**
 * Issuer, by BIN. Only the name is shown, not the bank's logo — a trademark belongs to its
 * owner and should be dropped in as a licensed asset, not redrawn from memory. Add rows here
 * as you collect more BINs.
 */
const ISSUERS: { prefix: string; name: string }[] = [
  { prefix: "41697388", name: "Kapital Bank" },
];

const issuerFor = (digits: string) =>
  ISSUERS.find((issuer) => digits.startsWith(issuer.prefix))?.name ?? null;

/** Visa starts with 4; Mastercard is 51–55 or the 2221–2720 range added in 2017. */
function brandFor(digits: string): "Visa" | "Mastercard" | null {
  if (digits.startsWith("4")) return "Visa";
  const two = Number(digits.slice(0, 2));
  if (digits.length >= 2 && two >= 51 && two <= 55) return "Mastercard";
  const four = Number(digits.slice(0, 4));
  if (digits.length >= 4 && four >= 2221 && four <= 2720) return "Mastercard";
  return null;
}

/** Returns a reason the expiry cannot be right, or null. */
function expiryProblem(value: string): string | null {
  const digits = value.replace(/\D/g, "");
  if (digits.length < 4) return null;                       // still typing

  const month = Number(digits.slice(0, 2));
  const year = 2000 + Number(digits.slice(2, 4));
  if (month < 1 || month > 12) return t("book.badMonth");

  const now = new Date();
  const endOfMonth = new Date(year, month, 0, 23, 59, 59);
  return endOfMonth < now ? t("book.expired") : null;
}

/** Groups digits in fours as you type. Nothing is validated here — the server decides. */
const groupDigits = (value: string) =>
  // Sixteen is the ceiling: Visa and Mastercard are both sixteen digits, and anything
  // longer is a typo rather than a card we accept.
  value.replace(/\D/g, "").slice(0, 16).replace(/(.{4})/g, "$1 ").trim();

/**
 * Pay, then confirm with the code that arrives by e-mail, then the ticket.
 *
 * The card never reaches this component's state in a form anyone could persist: it is read
 * from the inputs, posted once, and the fields are cleared. The server keeps only the brand
 * and the last four digits.
 */
export function BookingFlow({
  screeningId,
  seats,
  seatPrice,
  resume,
  onCancel,
  onBooked,
}: {
  screeningId: string;
  seats: SeatSelection[];
  seatPrice: number;
  /** An unfinished checkout the server still knows about — skip straight to the code. */
  resume?: CheckoutStarted | null;
  onCancel: () => void;
  onBooked: () => void;
}) {
  // A reload between paying and confirming used to lose the checkout entirely, leaving the
  // seats held with no way back to them. The handle is small and non-secret — the code
  // itself only ever exists in the customer's inbox — so parking it here is safe.
  const RESUME_KEY = `wy.checkout.${screeningId}`;

  const [step, setStep] = useState<"payment" | "code" | "ticket">(resume ? "code" : "payment");
  const [checkout, setCheckout] = useState<CheckoutStarted | null>(resume ?? null);
  const [ticket, setTicket] = useState<TicketResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [number, setNumber] = useState("");
  const [expiry, setExpiry] = useState("");
  const [cvc, setCvc] = useState("");
  const [holder, setHolder] = useState("");
  const [code, setCode] = useState("");

  // Discounts. The typed code only counts once "Apply" is pressed, so the price does not
  // jump about with every keystroke.
  const [promoInput, setPromoInput] = useState("");
  const [promo, setPromo] = useState("");
  const [usePoints, setUsePoints] = useState(false);
  const [quote, setQuote] = useState<CheckoutQuote | null>(null);

  const total = quote?.total ?? seatPrice * seats.length;

  useEffect(() => {
    if (step !== "payment") return;
    let cancelled = false;
    post<CheckoutQuote>(`/api/screenings/${screeningId}/quote`, {
      seats: seats.length, promoCode: promo || null, usePoints,
    })
      .then((q) => { if (!cancelled) setQuote(q); })
      .catch(() => { if (!cancelled) setQuote(null); });
    return () => { cancelled = true; };
  }, [screeningId, seats.length, promo, usePoints, step]);

  const promoRejected = !!promo && !!quote && !quote.promoApplied;

  const digits = number.replace(/\D/g, "");
  const brand = brandFor(digits);
  const issuer = issuerFor(digits);
  const expiryError = expiryProblem(expiry);
  const numberHint =
    digits.length >= 6 && !brand ? t("book.onlyVisaMc") : "4242 4242 4242 4242";

  const canPay =
    digits.length === 16 && !!brand && cvc.length === 3 && !expiryError &&
    expiry.replace(/\D/g, "").length === 4 && holder.trim().length > 1;

  useEffect(() => {
    if (resume) return;                                  // the server already told us
    const saved = sessionStorage.getItem(RESUME_KEY);
    if (!saved) return;
    try {
      const parsed = JSON.parse(saved) as CheckoutStarted;
      if (new Date(parsed.expiresAtUtc) > new Date()) {
        setCheckout(parsed);
        setStep("code");
      } else {
        sessionStorage.removeItem(RESUME_KEY);
      }
    } catch {
      sessionStorage.removeItem(RESUME_KEY);
    }
  }, [RESUME_KEY, resume]);

  async function abandon() {
    if (checkout) {
      await post(`/api/bookings/${checkout.paymentId}/cancel`).catch(() => null);
      sessionStorage.removeItem(RESUME_KEY);
    }
    onCancel();
  }

  async function pay() {
    const [month, year] = expiry.split("/").map((part) => Number(part.trim()));
    setBusy(true);
    setError(null);
    try {
      const started = await post<CheckoutStarted>(`/api/screenings/${screeningId}/checkout`, {
        seats,
        card: {
          number: number.replace(/\s/g, ""),
          expiryMonth: month || 0,
          expiryYear: year || 0,
          cvc,
          holderName: holder,
        },
        promoCode: promo || null,
        usePoints,
      });
      // Cleared the moment they are no longer needed.
      setNumber(""); setCvc(""); setExpiry("");
      sessionStorage.setItem(RESUME_KEY, JSON.stringify(started));
      setCheckout(started);
      setStep("code");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("error.booking"));
    } finally {
      setBusy(false);
    }
  }

  /** Stripe hosts the card form; this page only holds the seats and sends the browser there.
   *  CinemaPage picks the booking up again when Stripe sends the browser back. */
  async function payWithStripe() {
    setBusy(true);
    setError(null);
    try {
      const started = await post<StripeCheckoutStarted>(`/api/screenings/${screeningId}/checkout/stripe`, {
        seats, promoCode: promo || null, usePoints,
      });
      await openExternal(started.url);
      setBusy(false);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("error.booking"));
      setBusy(false);
    }
  }

  async function confirm() {
    if (!checkout) return;
    setBusy(true);
    setError(null);
    try {
      setTicket(await post<TicketResponse>(`/api/bookings/${checkout.paymentId}/confirm`, { code }));
      sessionStorage.removeItem(RESUME_KEY);
      setStep("ticket");
      onBooked();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("error.booking"));
    } finally {
      setBusy(false);
    }
  }

  if (step === "ticket" && ticket) return <TicketCard ticket={ticket} onDone={onCancel} />;

  if (step === "code" && checkout) {
    return (
      <Panel>
        <h3 className="font-display text-xl text-ink">{t("book.codeTitle")}</h3>
        <p className="mt-1 text-sm text-ink-mute">{t("book.codeLede")}</p>
        <p className="mt-1 text-xs text-ink-mute">{checkout.maskedEmail}</p>

        <div className="mt-4 flex flex-wrap items-center gap-2">
          <Badge>{t("book.reference")}: {checkout.reference}</Badge>
          <Badge tone="warn">
            {t("book.heldUntil")} {formatWhen(checkout.expiresAtUtc, { hour: "2-digit", minute: "2-digit" })}
          </Badge>
        </div>

        <div className="mt-5 max-w-xs">
          <Field label={t("account.code")}>
            <Input
              value={code}
              onChange={(e) => setCode(e.target.value.replace(/\D/g, "").slice(0, 6))}
              inputMode="numeric"
              autoComplete="one-time-code"
              placeholder="000000"
            />
          </Field>
        </div>

        {error ? <div className="mt-3"><Notice tone="error">{error}</Notice></div> : null}

        <div className="mt-4 flex gap-2">
          <Button disabled={busy || code.length !== 6} onClick={confirm}>
            {busy ? t("common.loading") : t("book.confirmSeats")}
          </Button>
          <Button variant="outline" onClick={abandon}>{t("common.cancel")}</Button>
        </div>
      </Panel>
    );
  }

  return (
    <Panel>
      <h3 className="flex items-center gap-2 font-display text-xl text-ink">
        <CreditCard size={18} aria-hidden />
        {t("book.payment")}
      </h3>
      <p className="mt-1 text-sm text-ink-mute">{t("book.paymentLede")}</p>

      <div className="mt-4 flex flex-wrap items-center gap-2">
        <Badge>{seats.map(seatLabel).join(", ")}</Badge>
        <Badge tone="good">{t("book.total")}: {formatMoney(total)}</Badge>
      </div>

      {/* Discounts: a promo code and loyalty points, priced by the server as they change. */}
      <div className="mt-5 max-w-md rounded-xl border border-line p-4">
        <div className="flex gap-2">
          <Input
            value={promoInput}
            onChange={(e) => setPromoInput(e.target.value.toUpperCase().replace(/[^A-Z0-9_-]/g, "").slice(0, 32))}
            placeholder={t("promo.placeholder", "Promo code")}
            aria-label={t("promo.placeholder", "Promo code")}
          />
          {promo ? (
            <Button variant="outline" onClick={() => { setPromo(""); setPromoInput(""); }}>{t("promo.remove", "Remove")}</Button>
          ) : (
            <Button variant="outline" disabled={!promoInput} onClick={() => setPromo(promoInput)}>{t("promo.apply", "Apply")}</Button>
          )}
        </div>
        {promoRejected && quote?.promoMessage ? <p className="mt-2 text-xs text-bad">{quote.promoMessage}</p> : null}

        {quote && quote.pointsBalance > 0 ? (
          <label className="mt-3 flex cursor-pointer items-center gap-2 text-sm text-ink">
            <input type="checkbox" checked={usePoints} onChange={(e) => setUsePoints(e.target.checked)} className="accent-[var(--color-accent)]" />
            <Sparkles size={14} className="text-accent" aria-hidden />
            {t("loyalty.use", "Use my points")} ({quote.pointsBalance})
          </label>
        ) : null}

        {quote ? (
          <dl className="mt-3 space-y-1 border-t border-line pt-3 text-sm">
            <div className="flex justify-between text-ink-mute"><dt>{t("price.subtotal", "Subtotal")}</dt><dd>{formatMoney(quote.subtotal)}</dd></div>
            {quote.promoDiscount > 0 ? (
              <div className="flex justify-between text-good"><dt>{t("price.promo", "Promo code")} {promo}</dt><dd>−{formatMoney(quote.promoDiscount)}</dd></div>
            ) : null}
            {quote.pointsDiscount > 0 ? (
              <div className="flex justify-between text-good"><dt>{quote.pointsUsed} {t("price.points", "points")}</dt><dd>−{formatMoney(quote.pointsDiscount)}</dd></div>
            ) : null}
            <div className="flex justify-between font-medium text-ink"><dt>{t("book.total")}</dt><dd>{formatMoney(quote.total)}</dd></div>
            {quote.pointsEarned > 0 ? (
              <p className="pt-1 text-xs text-ink-mute">{t("loyalty.earns", "You will earn")} {quote.pointsEarned} {t("price.points", "points")}</p>
            ) : null}
          </dl>
        ) : null}
      </div>

      <div className="mt-5 grid max-w-md gap-3">
        <Field label={t("book.cardNumber")} hint={numberHint}>
          <div className="relative">
            <Input
              value={number}
              onChange={(e) => setNumber(groupDigits(e.target.value))}
              inputMode="numeric"
              autoComplete="cc-number"
              placeholder="0000 0000 0000 0000"
              className="pr-32"
            />
            {issuer || brand ? (
              <span className="pointer-events-none absolute right-2 top-1/2 flex -translate-y-1/2 gap-1">
                {issuer ? <Badge tone="warn">{issuer}</Badge> : null}
                {brand ? <Badge tone="good">{brand}</Badge> : null}
              </span>
            ) : null}
          </div>
        </Field>

        <div className="grid grid-cols-2 gap-3">
          <Field label={t("book.expiry")} hint={expiryError ?? "MM/YY"}>
            <Input
              value={expiry}
              onChange={(e) => {
                const digits = e.target.value.replace(/\D/g, "").slice(0, 4);
                setExpiry(digits.length > 2 ? `${digits.slice(0, 2)}/${digits.slice(2)}` : digits);
              }}
              inputMode="numeric"
              autoComplete="cc-exp"
              placeholder="12/29"
            />
          </Field>
          <Field label={t("book.cvc")}>
            <Input
              value={cvc}
              onChange={(e) => setCvc(e.target.value.replace(/\D/g, "").slice(0, 3))}
              inputMode="numeric"
              autoComplete="cc-csc"
              placeholder="123"
            />
          </Field>
        </div>

        <Field label={t("book.holder")}>
          <Input value={holder} onChange={(e) => setHolder(e.target.value)} autoComplete="cc-name" />
        </Field>
      </div>

      {error ? <div className="mt-3"><Notice tone="error">{error}</Notice></div> : null}
      <div className="mt-3"><Notice tone="info">{t("book.simulated")}</Notice></div>

      <div className="mt-4 flex flex-wrap gap-2">
        <Button disabled={busy || !canPay || promoRejected} onClick={pay}>
          {busy ? t("book.paying") : `${t("book.pay")} ${formatMoney(total)}`}
        </Button>
        <Button variant="outline" onClick={abandon}>{t("book.back")}</Button>
      </div>

      {quote?.stripeEnabled ? (
        <div className="mt-5 max-w-md border-t border-line pt-4">
          <p className="mb-2 text-xs text-ink-mute">{t("stripe.or", "Or pay on Stripe's secure page — the card never touches this site.")}</p>
          <Button variant="outline" disabled={busy || promoRejected} onClick={payWithStripe}>
            <ExternalLink size={14} aria-hidden />
            {t("stripe.pay", "Pay with Stripe")} · {formatMoney(total)}
          </Button>
        </div>
      ) : null}
    </Panel>
  );
}

export function TicketCard({
  ticket,
  onDone,
  onRefunded,
}: {
  ticket: TicketResponse;
  onDone?: () => void;
  onRefunded?: () => void;
}) {
  const [codes, setCodes] = useState<Record<string, string>>({});
  const [downloading, setDownloading] = useState(false);

  useEffect(() => {
    // One code per seat, rendered locally: the payloads are short, and generating them here
    // means the tickets still draw if the network drops on the way to the cinema.
    let cancelled = false;

    Promise.all(
      ticket.seats.map(async (seat) => [
        seat.label,
        await QRCode.toDataURL(seat.qrPayload, {
          width: 320,
          margin: 1,
          color: { dark: "#0A0C0A", light: "#FFFFFF" },
        }),
      ] as const),
    )
      .then((pairs) => { if (!cancelled) setCodes(Object.fromEntries(pairs)); })
      .catch(() => { if (!cancelled) setCodes({}); });

    return () => { cancelled = true; };
  }, [ticket.seats]);

  return (
    <Panel>
      <h3 className="flex items-center gap-2 font-display text-xl text-ink">
        <Ticket size={18} aria-hidden />
        {t("book.ticket")}
      </h3>

      <div className="mt-4">
        <p className="font-display text-2xl text-ink">{ticket.movieTitle}</p>
        <p className="mt-1 text-sm text-ink-mute">
          {ticket.hall} · {formatWhen(ticket.startsAtUtc)}
        </p>
        <p className="mt-1 text-sm text-accent">
          {languageName(ticket.audioLanguage)}
          {ticket.subtitleLanguage ? ` · ${t("onDisplay.subtitles")}: ${languageName(ticket.subtitleLanguage)}` : ""}
        </p>

        <dl className="mt-3 flex flex-wrap gap-x-6 gap-y-1.5 text-sm">
          <div className="flex gap-2">
            <dt className="text-ink-mute">{t("book.reference")}:</dt>
            <dd className="font-display tracking-widest text-accent">{ticket.reference}</dd>
          </div>
          <div className="flex gap-2">
            <dt className="text-ink-mute">{t("book.total")}:</dt>
            <dd className="text-ink">
              {formatMoney(ticket.amount)} · {ticket.last4 ? `${ticket.brand} ···· ${ticket.last4}` : "Stripe"}
            </dd>
          </div>
        </dl>
      </div>

      {/* One card per seat, so two people arriving separately each have something to show. */}
      <div className="mt-5 flex flex-wrap gap-4">
        {ticket.seats.map((seat) => (
          <div key={seat.label} className="rounded-xl border border-line p-3 text-center">
            {codes[seat.label] ? (
              <img
                src={codes[seat.label]}
                alt={`${ticket.reference} · ${seat.label}`}
                className="h-36 w-36 rounded-lg bg-white p-2"
              />
            ) : (
              <div className="h-36 w-36 animate-pulse rounded-lg bg-line" />
            )}
            <p className="mt-2 font-display text-lg text-ink">{seat.label}</p>
          </div>
        ))}
      </div>

      <p className="mt-3 text-xs text-ink-mute">{t("book.showQr")}</p>

      <Button
        className="mt-3"
        size="sm"
        variant="outline"
        disabled={downloading}
        onClick={async () => {
          setDownloading(true);
          try {
            await download(`/api/bookings/${ticket.paymentId}/ticket.pdf`, `WatchingYou-${ticket.reference}.pdf`);
          } finally {
            setDownloading(false);
          }
        }}
      >
        <Download size={14} aria-hidden />
        {downloading ? t("common.loading") : t("ticket.pdf", "Download PDF ticket")}
      </Button>

      <RefundPanel paymentId={ticket.paymentId} onRefunded={onRefunded} />

      {onDone ? (
        <Button className="mt-5" variant="outline" onClick={onDone}>{t("book.newBooking")}</Button>
      ) : null}
    </Panel>
  );
}
