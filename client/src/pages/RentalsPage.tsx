import { useCallback, useEffect, useState } from "react";
import { motion, AnimatePresence } from "motion/react";
import { Section, Panel, Notice, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { useAuth } from "@/components/useAuth";
import { get, put, ApiError, query, type Paged } from "@/lib/api";
import { formatDate, formatUsd } from "@/lib/format";
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
        await put(`/api/rentals/${rental.id}/extend`, {});
        setMessage({ tone: "ok", text: `${rental.movieTitle}: ${t("rentals.extended", "three more days are yours.")}` });
      } else {
        await put<Rental>(`/api/rentals/${rental.id}/return`);
        setMessage({ tone: "ok", text: `${rental.movieTitle}: ${t("rentals.returned", "returned. Thank you!")}` });
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
                    {t("rentals.rentedOn", "Rented")} {formatDate(rental.rentedAtUtc)}
                    {rental.status === "Returned"
                      ? ` · ${t("rentals.returnedOn", "returned")} ${formatDate(rental.returnedAtUtc!)}`
                      : ` · ${t("rentals.until", "yours until")} ${formatDate(rental.dueAtUtc)}`}
                  </p>
                  <div className="mt-2 flex flex-wrap gap-2">
                    <Badge tone={rental.status === "Overdue" ? "warn" : rental.status === "Returned" ? "neutral" : "good"}>
                      {rental.status === "Overdue"
                        ? t("rentals.decide", "3 days are up — your choice")
                        : rental.status === "Returned"
                          ? t("rentals.statusReturned", "Returned")
                          : t("rentals.statusActive", "Watching")}
                    </Badge>
                  </div>
                </div>

                <div className="flex flex-wrap items-center gap-4">
                  <div className="text-right">
                    <p className="text-sm text-ink">{formatUsd(rental.basePrice)}</p>
                    <p className="text-xs text-ink-mute">{formatUsd(0.5)} / 3 {t("pro.days", "days")}</p>
                  </div>
                  {rental.status === "Overdue" ? (
                    // The offer only exists once the paid days are over. Until they answer,
                    // nothing more is charged.
                    <div className="flex gap-2">
                      <Button size="sm" disabled={busyId === rental.id} onClick={() => act(rental, "extend")}>
                        +3 {t("pro.days", "days")} · {formatUsd(0.5)}
                      </Button>
                      <Button size="sm" variant="outline" disabled={busyId === rental.id} onClick={() => act(rental, "return")}>
                        {t("rentals.giveBack", "Return")}
                      </Button>
                    </div>
                  ) : rental.status === "Active" ? (
                    <Button size="sm" variant="outline" disabled={busyId === rental.id} onClick={() => act(rental, "return")}>
                      {t("rentals.giveBack", "Return")}
                    </Button>
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
