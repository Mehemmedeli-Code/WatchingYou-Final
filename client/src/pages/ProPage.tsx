import { useCallback, useEffect, useState } from "react";
import { Check, Crown, Film, Infinity as InfinityIcon, Clock3 } from "lucide-react";
import { Section, Panel, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { useAuth } from "@/components/useAuth";
import { get, post } from "@/lib/api";
import { formatDate, formatUsd, PRO_PRICE } from "@/lib/format";
import { t } from "@/lib/i18n";
import { ProSubscribers } from "@/components/ProSubscribers";
import { PaymentSheet } from "@/components/CardPayment";

interface ProPayment {
  id: string;
  paidAtUtc: string;
  startsAtUtc: string;
  endsAtUtc: string;
  amount: number;
  currency: string;
  cardBrand: string;
  cardLast4: string;
}

interface ProStatus {
  plan: string;
  active: boolean;
  endsAtUtc?: string | null;
  daysLeft: number;
  monthlyPrice: number;
  rentalPrice: number;
  rentalDays: number;
  currency: string;
  payments: ProPayment[];
}

/** Watching PRO: the two ways to watch side by side, and the form to join. */
export default function ProPage() {
  const { isSignedIn, isSecurity: isStaff } = useAuth();
  const [status, setStatus] = useState<ProStatus | null>(null);
  const [paying, setPaying] = useState(false);

  const load = useCallback(async () => {
    if (!isSignedIn || isStaff) return;
    setStatus(await get<ProStatus>("/api/pro").catch(() => null));
  }, [isSignedIn]);

  useEffect(() => { void load(); }, [load]);

  // Arrived from the welcome page having picked PRO: open the payment straight away, once,
  // unless the account already has it. The flag leaves the address so a reload does not reopen it.
  useEffect(() => {
    if (!status || new URLSearchParams(location.search).get("checkout") !== "1") return;
    history.replaceState(null, "", location.pathname);
    if (!status.active) setPaying(true);
  }, [status]);

  // Staff watch everything already; what they need is the subscriber list, not the offer.
  if (isStaff) {
    return (
      <Section title="Watching PRO" lede={t("proAdmin.staffNote", "Staff accounts watch every film without a subscription. The subscribers are listed on the Admin and Security pages.")}>
        <ProSubscribers />
      </Section>
    );
  }

  const free = [
    t("pro.free1", "Rent any film for 3 days — $0.50"),
    t("pro.free2", "When the 3 days are up: +3 days for $0.50, or return it"),
    t("pro.free3", "Nothing is charged while you decide"),
  ];
  const paid = [
    t("pro.pro1", "Every film in the catalogue"),
    t("pro.pro2", "Watch as often as you like, for a whole month"),
    t("pro.pro3", "No rentals, no due dates, no extra charges"),
    t("pro.pro4", "Does not renew by itself — your card is not kept"),
  ];

  return (
    <Section title="Watching PRO" lede={t("pro.lede", "Every film, as often as you like. $5 a month.")}>
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <Panel>
          <p className="flex items-center gap-2 font-display text-lg text-ink">
            <Film size={18} aria-hidden /> {t("pro.freePlan", "Free account")}
          </p>
          <p className="mt-2 font-display text-3xl text-ink">
            {formatUsd(0.5)} <span className="text-base text-ink-mute">/ 3 {t("pro.days", "days")}</span>
          </p>
          <ul className="mt-4 space-y-2">
            {free.map((line) => (
              <li key={line} className="flex items-start gap-2 text-sm text-ink-mute">
                <Clock3 size={16} className="mt-0.5 shrink-0" aria-hidden /> {line}
              </li>
            ))}
          </ul>
        </Panel>

        <Panel className="border-accent">
          <div className="flex items-center justify-between gap-3">
            <p className="flex items-center gap-2 font-display text-lg text-ink">
              <Crown size={18} className="text-accent" aria-hidden /> Watching PRO
            </p>
            {status?.active ? <Badge tone="good">{t("pro.active", "Active")}</Badge> : null}
          </div>
          <p className="mt-2 font-display text-3xl text-ink">
            {formatUsd(5)} <span className="text-base text-ink-mute">/ {t("pro.month", "month")}</span>
          </p>
          <ul className="mt-4 space-y-2">
            {paid.map((line) => (
              <li key={line} className="flex items-start gap-2 text-sm text-ink">
                <Check size={16} className="mt-0.5 shrink-0 text-accent" aria-hidden /> {line}
              </li>
            ))}
          </ul>

          <div className="mt-5 border-t border-line pt-4">
            {!isSignedIn ? (
              <a href="/account?returnUrl=%2Fpro"><Button className="w-full">{t("pro.signInToJoin", "Sign in to join")}</Button></a>
            ) : status === null ? (
              <Spinner label={t("common.loading")} />
            ) : (
              <>
                {status.active && status.endsAtUtc ? (
                  <p className="mb-3 flex items-center gap-2 text-sm text-ink">
                    <InfinityIcon size={16} className="text-accent" aria-hidden />
                    {t("pro.until", "Every film is yours until")} <strong>{formatDate(status.endsAtUtc)}</strong>
                    <span className="text-ink-mute">({status.daysLeft} {t("pro.days", "days")})</span>
                  </p>
                ) : null}
                <Button className="w-full" onClick={() => setPaying(true)}>
                  {status.active
                    ? `${t("pro.addMonth", "Add another month")} · ${formatUsd(PRO_PRICE)}`
                    : `${t("pro.join", "Get Watching PRO")} · ${formatUsd(PRO_PRICE)}`}
                </Button>
                {paying ? (
                  <PaymentSheet
                    item={{ title: "Watching PRO", detail: t("pay.proDetail", "One month, every film"), amount: formatUsd(PRO_PRICE), icon: <Crown size={18} aria-hidden /> }}
                    pay={async (card) => { setStatus(await post<ProStatus>("/api/pro/subscribe", { card })); }}
                    onClose={() => setPaying(false)}
                    onPaid={() => void load()}
                  />
                ) : null}
              </>
            )}
          </div>
        </Panel>
      </div>

      {status && status.payments.length > 0 ? (
        <Panel className="mt-6">
          <p className="font-display text-lg text-ink">{t("pro.history", "Payments")}</p>
          <ul className="mt-3 divide-y divide-line">
            {status.payments.map((p) => (
              <li key={p.id} className="flex flex-wrap items-center justify-between gap-2 py-2 text-sm">
                <span className="text-ink">{formatDate(p.startsAtUtc)} → {formatDate(p.endsAtUtc)}</span>
                <span className="text-ink-mute">{p.cardBrand} •••• {p.cardLast4}</span>
                <span className="text-ink">{formatUsd(p.amount)}</span>
              </li>
            ))}
          </ul>
        </Panel>
      ) : null}
    </Section>
  );
}
