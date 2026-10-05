import { useEffect, useState } from "react";
import { Sparkles } from "lucide-react";
import { Section, Panel } from "@/components/Shell";
import { get } from "@/lib/api";
import { formatMoney } from "@/lib/format";
import { formatWhen, t } from "@/lib/i18n";

interface LoyaltyLine { points: number; reason: string; reference?: string | null; atUtc: string }
interface LoyaltySummary {
  balance: number;
  held: number;
  spendable: number;
  manatPerPoint: number;
  pointsPerManat: number;
  maxShare: number;
  history: LoyaltyLine[];
}

const REASONS: Record<string, [string, string]> = {
  Earned: ["loyalty.reason.earned", "Earned"],
  Redeemed: ["loyalty.reason.redeemed", "Spent"],
  Reversed: ["loyalty.reason.reversed", "Taken back (refund)"],
  Restored: ["loyalty.reason.restored", "Returned (refund)"],
  Adjusted: ["loyalty.reason.adjusted", "Adjusted"],
};

/** Points balance and the ledger behind it, on the account page. */
export function LoyaltyPanel() {
  const [data, setData] = useState<LoyaltySummary | null>(null);

  useEffect(() => {
    get<LoyaltySummary>("/api/loyalty").then(setData).catch(() => setData(null));
  }, []);

  if (!data) return null;

  return (
    <Section title={t("loyalty.title", "Loyalty points")} lede={t("loyalty.lede", "Every manat you spend on cinema tickets earns a point. Spend them at checkout.")}>
      <div className="grid gap-4 md:grid-cols-[280px_1fr]">
        <Panel>
          <p className="flex items-center gap-2 text-sm text-ink-mute"><Sparkles size={14} className="text-accent" aria-hidden />{t("loyalty.balance", "Balance")}</p>
          <p className="mt-1 font-display text-4xl text-ink">{data.balance}</p>
          <p className="mt-1 text-sm text-ink-mute">≈ {formatMoney(data.balance * data.manatPerPoint)}</p>
          {data.held > 0 ? (
            <p className="mt-2 text-xs text-warn">{data.held} {t("loyalty.held", "held by an unfinished checkout")}</p>
          ) : null}
          <p className="mt-4 text-xs text-ink-mute">
            {t("loyalty.rules", "1 point per manat paid · 20 points = 1 manat off · up to half of a booking")}
          </p>
        </Panel>

        <Panel>
          {data.history.length === 0 ? (
            <p className="text-sm text-ink-mute">{t("loyalty.empty", "No points yet — book a cinema seat to start collecting.")}</p>
          ) : (
            <ul className="divide-y divide-line text-sm">
              {data.history.map((line, i) => {
                const [key, fallback] = REASONS[line.reason] ?? [line.reason, line.reason];
                return (
                  <li key={i} className="flex items-center justify-between gap-3 py-2">
                    <span className="text-ink">
                      {t(key, fallback)}
                      {line.reference ? <span className="ml-2 text-xs text-ink-mute">{line.reference}</span> : null}
                    </span>
                    <span className="flex items-center gap-3">
                      <span className="text-xs text-ink-mute">{formatWhen(line.atUtc)}</span>
                      <span className={line.points >= 0 ? "w-14 text-right font-medium text-good" : "w-14 text-right font-medium text-bad"}>
                        {line.points > 0 ? `+${line.points}` : line.points}
                      </span>
                    </span>
                  </li>
                );
              })}
            </ul>
          )}
        </Panel>
      </div>
    </Section>
  );
}
