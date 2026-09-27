import { useCallback, useEffect, useState } from "react";
import { Undo2, Info } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Notice } from "@/components/Shell";
import { get, post, ApiError } from "@/lib/api";
import { formatMoney } from "@/lib/format";
import { t } from "@/lib/i18n";

interface RefundTerms {
  outcome: "Full" | "PartialWithFee" | "TooLate" | "AlreadyRefunded" | "NotConfirmed" | "AlreadyUsed" | "Started";
  refund: number;
  fee: number;
  hoursUntilStart: number;
  allowed: boolean;
  windowHours: number;
  feePercent: number;
  note?: string | null;
  screeningCancelled: boolean;
}

/**
 * The rules, and the button.
 *
 * The figures come from the server rather than being recomputed here: the cut-off moves while
 * somebody reads the page, and a quote the client worked out for itself would eventually
 * promise a refund the server then refuses.
 */
export function RefundPanel({ paymentId, onRefunded }: { paymentId: string; onRefunded?: () => void }) {
  const [terms, setTerms] = useState<RefundTerms | null>(null);
  const [asking, setAsking] = useState(false);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);

  const load = useCallback(async (id: string) => {
    setTerms(await get<RefundTerms>(`/api/bookings/${id}/refund-terms`).catch(() => null));
  }, []);

  useEffect(() => { void load(paymentId); }, [paymentId, load]);

  if (!terms) return null;

  const policy = t("refund.policyLine")
    .replace("{window}", String(terms.windowHours))
    .replace("{fee}", String(Math.round(terms.feePercent)));

  async function refund() {
    setBusy(true);
    setMessage(null);
    try {
      await post(`/api/bookings/${paymentId}/refund`, { reason: reason || null });
      setMessage({ tone: "ok", text: t("refund.done") });
      setAsking(false);
      onRefunded?.();
    } catch (err) {
      setMessage({ tone: "error", text: err instanceof ApiError ? err.message : t("error.action") });
      await load(paymentId);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mt-5 border-t border-line pt-4">
      <p className="flex items-center gap-1.5 text-sm text-ink">
        <Info size={14} aria-hidden />
        {t("refund.rules")}
      </p>

      <p className="mt-1 text-xs leading-relaxed text-ink-mute">{policy}</p>

      {/* A screening may set its own window or fee, so its own note comes after the general
          rule rather than replacing it. */}
      {terms.note ? <p className="mt-1 text-xs text-warn">{terms.note}</p> : null}
      {terms.screeningCancelled ? (
        <p className="mt-2 text-sm text-accent">{t("refund.cancelled")}</p>
      ) : null}

      {message ? <div className="mt-3"><Notice tone={message.tone}>{message.text}</Notice></div> : null}

      {terms.allowed ? (
        <div className="mt-3">
          <div className="flex flex-wrap items-center gap-2">
            <Badge tone="good">{t("refund.youGet")}: {formatMoney(terms.refund)}</Badge>
            {terms.fee > 0 ? <Badge tone="warn">{formatMoney(terms.fee)} {t("refund.feeKept")}</Badge> : null}
            {!terms.screeningCancelled ? (
              <span className="text-xs text-ink-mute">{terms.hoursUntilStart} {t("refund.hoursLeft")}</span>
            ) : null}
          </div>

          {asking ? (
            <div className="mt-3 flex flex-wrap gap-2">
              <Input
                className="max-w-xs"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                placeholder={t("admin.suspendReason")}
              />
              <Button size="sm" variant="danger" disabled={busy} onClick={refund}>
                {busy ? t("common.loading") : t("refund.confirm")}
              </Button>
              <Button size="sm" variant="outline" onClick={() => setAsking(false)}>{t("common.cancel")}</Button>
            </div>
          ) : (
            <Button className="mt-3" size="sm" variant="outline" onClick={() => setAsking(true)}>
              <Undo2 size={14} aria-hidden />
              {t("refund.request")}
            </Button>
          )}
        </div>
      ) : (
        <p className="mt-3 text-sm text-ink-mute">
          {t(`refund.no.${terms.outcome}`, t("refund.no.TooLate"))}
        </p>
      )}
    </div>
  );
}
