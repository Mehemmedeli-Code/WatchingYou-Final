import { useCallback, useEffect, useState } from "react";
import { motion, AnimatePresence } from "motion/react";
import { Section, Panel, Notice, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { useAuth } from "@/components/useAuth";
import { get, put, ApiError, query, type Paged } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { t } from "@/lib/i18n";

interface Rental {
  id: string;
  movieId: string;
  movieTitle: string;
  posterUrl?: string | null;
  rentedAtUtc: string;
  dueAtUtc: string;
  returnedAtUtc?: string | null;
  dailyPrice: number;
  basePrice: number;
  lateFee: number;
  totalDue: number;
  daysOverdue: number;
  extensionCount: number;
  status: "Active" | "Returned" | "Overdue";
}

const FILTERS = ["all", "active", "overdue", "returned"] as const;

export default function RentalsPage() {
  const { isSignedIn } = useAuth();
  const [filter, setFilter] = useState<(typeof FILTERS)[number]>("all");
  const [data, setData] = useState<Paged<Rental> | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);

  const load = useCallback(async () => {
    if (!isSignedIn) {
      setLoading(false);
      return;
    }
    setLoading(true);
    try {
      setData(await get<Paged<Rental>>("/api/rentals/mine" + query({ status: filter === "all" ? undefined : filter, pageSize: 20 })));
    } catch {
      setMessage({ tone: "error", text: t("error.rentals") });
    } finally {
      setLoading(false);
    }
  }, [filter, isSignedIn]);

  useEffect(() => { void load(); }, [load]);

  async function act(rental: Rental, action: "extend" | "return") {
    setBusyId(rental.id);
    setMessage(null);
    try {
      if (action === "extend") {
        await put(`/api/rentals/${rental.id}/extend`, { extraDays: 7 });
        setMessage({ tone: "ok", text: `${rental.movieTitle} is now due a week later.` });
      } else {
        const returned = await put<Rental>(`/api/rentals/${rental.id}/return`);
        setMessage({
          tone: "ok",
          text: returned.lateFee > 0
            ? `${rental.movieTitle} returned. Late fee: ${formatMoney(returned.lateFee)}.`
            : `${rental.movieTitle} returned on time.`,
        });
      }
      await load();
    } catch (err) {
      setMessage({ tone: "error", text: err instanceof ApiError ? err.message : t("error.action") });
    } finally {
      setBusyId(null);
    }
  }

  if (!isSignedIn) {
    return (
      <Section title={t("rentals.title")} lede={t("rentals.ledeSignedOut")}>
        <Empty
          title={t("rentals.emptyTitle")}
          hint={t("rentals.signedOutHint")}
          action={<a href="/account"><Button>{t("nav.signIn")}</Button></a>}
        />
      </Section>
    );
  }

  return (
    <Section title={t("rentals.title")} lede={t("rentals.lede")}>
      <div className="mb-5 flex flex-wrap gap-2">
        {FILTERS.map((f) => (
          <Button key={f} size="sm" variant={filter === f ? "solid" : "outline"} onClick={() => setFilter(f)}>
            {t(`rentals.${f}`, t("common.everything"))}
          </Button>
        ))}
      </div>

      {message ? <div className="mb-4"><Notice tone={message.tone}>{message.text}</Notice></div> : null}
      {loading ? <Spinner label={t("rentals.loading")} /> : null}

      {data && data.items.length === 0 ? (
        <Empty title={t("rentals.emptyTitle")} hint={t("rentals.emptyHint")} action={<a href="/"><Button variant="outline">{t("rentals.goCatalogue")}</Button></a>} />
      ) : null}

      <div className="space-y-3">
        <AnimatePresence initial={false}>
          {data?.items.map((rental) => (
            <motion.div
              key={rental.id}
              layout
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, height: 0 }}
              transition={{ type: "spring", stiffness: 280, damping: 28 }}
            >
              <Panel className="flex flex-wrap items-center justify-between gap-4">
                <div className="min-w-[220px]">
                  <h3 className="font-display text-lg text-ink">{rental.movieTitle}</h3>
                  <p className="mt-1 text-xs text-ink-mute">
                    Rented {formatDate(rental.rentedAtUtc)} · due {formatDate(rental.dueAtUtc)}
                    {rental.extensionCount > 0 ? ` · extended ${rental.extensionCount}×` : ""}
                  </p>
                  <div className="mt-2 flex flex-wrap gap-2">
                    <Badge tone={rental.status === "Overdue" ? "bad" : rental.status === "Returned" ? "neutral" : "good"}>
                      {rental.status}
                      {rental.daysOverdue > 0 ? ` · ${rental.daysOverdue}d late` : ""}
                    </Badge>
                    {rental.lateFee > 0 ? <Badge tone="warn">Late fee {formatMoney(rental.lateFee)}</Badge> : null}
                  </div>
                </div>

                <div className="flex items-center gap-4">
                  <div className="text-right">
                    <p className="text-sm text-ink">{formatMoney(rental.totalDue)}</p>
                    <p className="text-xs text-ink-mute">rental {formatMoney(rental.basePrice)}</p>
                  </div>

                  {rental.status !== "Returned" ? (
                    <div className="flex gap-2">
                      <Button size="sm" variant="outline" disabled={busyId === rental.id || rental.extensionCount >= 2} onClick={() => act(rental, "extend")}>
                        + 7 days
                      </Button>
                      <Button size="sm" disabled={busyId === rental.id} onClick={() => act(rental, "return")}>
                        Return
                      </Button>
                    </div>
                  ) : null}
                </div>
              </Panel>
            </motion.div>
          ))}
        </AnimatePresence>
      </div>
    </Section>
  );
}
