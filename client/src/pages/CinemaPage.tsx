import { useCallback, useEffect, useRef, useState } from "react";
import { motion } from "motion/react";
import { Section, Panel, Notice, Spinner, Empty } from "@/components/Shell";
import { BookingFlow, TicketCard, type SeatSelection, type TicketResponse, type CheckoutStarted } from "@/components/BookingFlow";
import { post } from "@/lib/api";
import { HallPreview, type PreviewSeat } from "@/components/HallPreview";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/components/useAuth";
import { get } from "@/lib/api";
import { formatDateTime, formatMoney } from "@/lib/format";
import { t, languageName } from "@/lib/i18n";

interface Screening {
  id: string;
  movieTitle: string;
  hall: string;
  startsAtUtc: string;
  seatPrice: number;
  capacity: number;
  seatsTaken: number;
  audioLanguage: string;
  subtitleLanguage?: string | null;
  hallId: string;
  venueName: string;
}

interface PendingCheckout {
  paymentId: string;
  screeningId: string;
  movieTitle: string;
  reference: string;
  amount: number;
  brand: string;
  last4: string;
  seats: SeatSelection[];
  expiresAtUtc: string;
}

interface SeatState {
  row: number;
  number: number;
  isTaken: boolean;
  isMine: boolean;
  /** Paid for but not yet confirmed by code — occupied, but nobody's ticket yet. */
  isHeld: boolean;
}

interface SeatMap {
  screeningId: string;
  movieTitle: string;
  hall: string;
  startsAtUtc: string;
  rows: number;
  seatsPerRow: number;
  seatPrice: number;
  audioLanguage: string;
  subtitleLanguage?: string | null;
  hallId: string;
  venueName: string;
  seats: SeatState[];
}

const seatKey = (row: number, number: number) => `${row}:${number}`;

/** The pending record carries everything the flow needs except the masked address, which
 *  is only used as a reminder of where the code went. */
const toCheckoutStarted = (checkout: PendingCheckout): CheckoutStarted => ({
  paymentId: checkout.paymentId,
  reference: checkout.reference,
  amount: checkout.amount,
  brand: checkout.brand,
  last4: checkout.last4,
  maskedEmail: "",
  expiresAtUtc: checkout.expiresAtUtc,
});

export default function CinemaPage() {
  const { isSignedIn } = useAuth();
  const [screenings, setScreenings] = useState<Screening[]>([]);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [map, setMap] = useState<SeatMap | null>(null);
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [message, setMessage] = useState<{ tone: "ok" | "error" | "info"; text: string } | null>(null);
  // Null until the visitor commits to paying; the flow owns everything after that.
  const [checkoutSeats, setCheckoutSeats] = useState<SeatSelection[] | null>(null);
  const [tickets, setTickets] = useState<TicketResponse[]>([]);
  const [pending, setPending] = useState<PendingCheckout[]>([]);
  const [resuming, setResuming] = useState<PendingCheckout | null>(null);
  const [preview, setPreview] = useState<PreviewSeat[] | null>(null);
  const activeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const requested = new URLSearchParams(window.location.search).get("screening");

    get<Screening[]>("/api/screenings")
      .then((list) => {
        setScreenings(list);

        if (!requested) {
          setActiveId(list[0]?.id ?? null);
          return;
        }

        if (list.some((item) => item.id === requested)) {
          setActiveId(requested);
          return;
        }

        // Asked for a performance that is not in the list. Say so rather than opening a
        // different showing of the same film and letting the visitor book the wrong seat.
        setActiveId(null);
        setMessage({ tone: "error", text: t("cinema.gone") });
      })
      .catch(() => setMessage({ tone: "error", text: t("error.screenings") }))
      .finally(() => setLoading(false));
  }, []);

  const loadMap = useCallback(async (screeningId: string) => {
    setPicked(new Set());
    try {
      setMap(await get<SeatMap>(`/api/screenings/${screeningId}/seats`));
    } catch {
      setMessage({ tone: "error", text: t("error.seatMap") });
    }
  }, []);

  const loadTickets = useCallback(async () => {
    if (!isSignedIn) { setTickets([]); setPending([]); return; }
    const [mine, unfinished] = await Promise.all([
      get<TicketResponse[]>("/api/bookings/mine").catch(() => []),
      get<PendingCheckout[]>("/api/bookings/pending").catch(() => []),
    ]);
    setTickets(mine);
    setPending(unfinished);
  }, [isSignedIn]);

  useEffect(() => { void loadTickets(); }, [loadTickets]);

  useEffect(() => {
    if (activeId) void loadMap(activeId);
  }, [activeId, loadMap]);

  useEffect(() => {
    if (!activeId) return;
    activeRef.current?.scrollIntoView({ block: "nearest", behavior: "smooth" });
  }, [activeId]);

  function toggleSeat(seat: SeatState) {
    if (seat.isTaken) return;
    setPicked((current) => {
      const next = new Set(current);
      const key = seatKey(seat.row, seat.number);
      next.has(key) ? next.delete(key) : next.add(key);
      return next;
    });
  }

  function startCheckout() {
    if (!map || picked.size === 0) return;

    // No account, no checkout. Sent straight to sign-in, carrying the way back so the
    // chosen performance is still on screen afterwards.
    if (!isSignedIn) {
      const back = `/cinema?screening=${map.screeningId}`;
      window.location.href = `/account?returnUrl=${encodeURIComponent(back)}`;
      return;
    }

    setMessage(null);
    setCheckoutSeats([...picked].map((key) => {
      const [row, number] = key.split(":").map(Number);
      return { row, number };
    }));
  }

  const total = map ? map.seatPrice * picked.size : 0;

  return (
    <Section title={t("cinema.title")} lede={t("cinema.lede")}>
      {preview && map ? (
        <HallPreview hallId={map.hallId} seats={preview} onClose={() => setPreview(null)} />
      ) : null}

      {pending.length > 0 && !resuming ? (
        <div className="mb-6 space-y-2">
          {pending.map((checkout) => (
            <Panel key={checkout.paymentId} className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p className="text-sm text-ink">
                  {t("book.unfinished")} — {checkout.movieTitle}
                </p>
                <p className="mt-1 text-xs text-ink-mute">
                  {checkout.reference} · {checkout.seats.map((s) => `${String.fromCharCode(64 + s.row)}${s.number}`).join(", ")}
                  {" · "}
                  {t("book.heldUntil")} {new Date(checkout.expiresAtUtc).toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" })}
                </p>
              </div>
              <div className="flex gap-2">
                <Button size="sm" onClick={() => { setActiveId(checkout.screeningId); setResuming(checkout); }}>
                  {t("book.resume")}
                </Button>
                <Button
                  size="sm"
                  variant="danger"
                  onClick={async () => {
                    await post(`/api/bookings/${checkout.paymentId}/cancel`).catch(() => null);
                    await loadTickets();
                    if (activeId) await loadMap(activeId);
                  }}
                >
                  {t("book.release")}
                </Button>
              </div>
            </Panel>
          ))}
        </div>
      ) : null}

      {loading ? <Spinner label={t("cinema.loading")} /> : null}

      {!loading && screenings.length === 0 ? (
        <Empty title={t("cinema.emptyTitle")} hint={t("cinema.emptyHint")} />
      ) : null}

      {screenings.length > 0 ? (
        <div className="grid gap-6 lg:grid-cols-[260px_1fr]">
          <div className="space-y-2">
            {screenings.map((screening) => {
              const isActive = screening.id === activeId;
              return (
                <button
                  key={screening.id}
                  ref={isActive ? activeRef : undefined}
                  onClick={() => setActiveId(screening.id)}
                  aria-pressed={isActive}
                  aria-current={isActive ? "true" : undefined}
                  className={`w-full rounded-lg border p-3 text-left transition-colors ${
                    isActive
                      ? "border-accent bg-surface-raised ring-2 ring-accent/40"
                      : "border-line hover:border-accent-dim"
                  }`}
                >
                  <p className="font-display text-base text-ink">{screening.movieTitle}</p>
                  <p className="mt-1 text-xs text-ink-mute">
                    {screening.venueName} · {screening.hall} · {formatDateTime(screening.startsAtUtc)}
                  </p>
                  <p className="mt-1 text-xs text-ink-mute">
                    {languageName(screening.audioLanguage)}
                    {screening.subtitleLanguage
                      ? ` · ${t("onDisplay.subtitles")}: ${languageName(screening.subtitleLanguage)}`
                      : ""}
                  </p>
                  <p className="mt-1 text-xs text-accent">
                    {screening.capacity - screening.seatsTaken} of {screening.capacity} free
                  </p>
                </button>
              );
            })}
          </div>

          <Panel>
            {!map ? (
              <Spinner label={t("cinema.drawing")} />
            ) : (
              <>
                <div className="mb-6">
                  <h3 className="font-display text-2xl text-ink">{map.movieTitle}</h3>
                  <p className="text-sm text-ink-mute">
                    {map.venueName} · {map.hall} · {formatDateTime(map.startsAtUtc)} · {formatMoney(map.seatPrice)} a seat
                  </p>
                  <p className="mt-1 text-sm text-accent">
                    {languageName(map.audioLanguage)}
                    {map.subtitleLanguage
                      ? ` · ${t("onDisplay.subtitles")}: ${languageName(map.subtitleLanguage)}`
                      : ""}
                  </p>
                </div>

                <div className="mb-6 overflow-hidden rounded-t-[999px] border-b-2 border-accent-dim bg-gradient-to-b from-accent/20 to-transparent py-2 text-center text-xs tracking-[0.3em] text-accent-dim">
                  screen
                </div>

                <div className="-mx-1 space-y-2 overflow-x-auto px-1 pb-2">
                  {Array.from({ length: map.rows }, (_, r) => r + 1).map((row) => (
                    <div key={row} className="flex w-max items-center gap-2">
                      <span className="w-5 shrink-0 text-xs text-ink-mute">{String.fromCharCode(64 + row)}</span>
                      <div className="flex gap-1.5">
                        {map.seats
                          .filter((seat) => seat.row === row)
                          .map((seat) => {
                            const selected = picked.has(seatKey(seat.row, seat.number));
                            return (
                              <motion.button
                                key={seat.number}
                                type="button"
                                onClick={() => toggleSeat(seat)}
                                disabled={seat.isTaken}
                                whileTap={seat.isTaken ? undefined : { scale: 0.88 }}
                                animate={{ scale: selected ? 1.08 : 1 }}
                                transition={{ type: "spring", stiffness: 420, damping: 22 }}
                                aria-label={`Row ${String.fromCharCode(64 + seat.row)} seat ${seat.number}${
                                  seat.isTaken ? ", taken" : selected ? ", selected" : ", free"
                                }`}
                                aria-pressed={selected}
                                className={`h-8 w-8 shrink-0 rounded-t-md border text-[11px] transition-colors sm:h-7 sm:w-7 sm:text-[10px] ${
                                  seat.isHeld
                                    ? "cursor-not-allowed border-warn/50 bg-warn-bg text-warn/70"
                                    : seat.isTaken
                                      ? seat.isMine
                                        ? "cursor-not-allowed border-accent bg-accent-dim text-surface"
                                        : "cursor-not-allowed border-line bg-line text-ink-mute/50"
                                      : selected
                                        ? "border-accent bg-accent text-surface"
                                        : "border-accent-dim/60 text-ink-mute hover:border-accent"
                                }`}
                              >
                                {seat.number}
                              </motion.button>
                            );
                          })}
                      </div>
                    </div>
                  ))}
                </div>

                <div className="mt-6 flex flex-wrap items-center gap-4 text-xs text-ink-mute">
                  <span className="flex items-center gap-1.5"><i className="h-3 w-3 rounded-sm border border-accent-dim/60" />{t("cinema.free")}</span>
                  <span className="flex items-center gap-1.5"><i className="h-3 w-3 rounded-sm bg-accent" />{t("cinema.selected")}</span>
                  <span className="flex items-center gap-1.5"><i className="h-3 w-3 rounded-sm bg-line" />{t("cinema.taken")}</span>
                  <span className="flex items-center gap-1.5"><i className="h-3 w-3 rounded-sm bg-accent-dim" />{t("cinema.yours")}</span>
                  <span className="flex items-center gap-1.5"><i className="h-3 w-3 rounded-sm bg-warn-bg" />{t("cinema.held")}</span>
                </div>

                {message ? <div className="mt-4"><Notice tone={message.tone}>{message.text}</Notice></div> : null}

                <div className="mt-6 border-t border-line pt-4">
                  {checkoutSeats || resuming ? (
                    <BookingFlow
                      screeningId={map.screeningId}
                      seats={resuming ? resuming.seats : checkoutSeats!}
                      seatPrice={map.seatPrice}
                      resume={resuming ? toCheckoutStarted(resuming) : null}
                      onCancel={() => {
                        setCheckoutSeats(null);
                        setResuming(null);
                        void loadMap(map.screeningId);
                        void loadTickets();
                      }}
                      onBooked={() => {
                        setPicked(new Set());
                        setCheckoutSeats(null);
                        setResuming(null);
                        void loadMap(map.screeningId);
                        void loadTickets();
                      }}
                    />
                  ) : (
                    <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-center sm:justify-between">
                      <p className="text-sm text-ink-mute">
                        {picked.size === 0
                          ? t("cinema.noSeats")
                          : `${picked.size} · ${formatMoney(total)}`}
                      </p>
                      <div className="flex gap-2">
                        <Button
                          variant="outline"
                          disabled={picked.size === 0}
                          onClick={() => {
                            // Every seat picked, in reading order, so the arrows step through
                            // them the way they sit in the row.
                            const chosen = [...picked]
                              .map((key) => {
                                const [row, seat] = key.split(":").map(Number);
                                return { row, seat };
                              })
                              .sort((a, b) => a.row - b.row || a.seat - b.seat);
                            if (chosen.length > 0) setPreview(chosen);
                          }}
                        >
                          {t("view.seeFromHere")}
                        </Button>
                        <Button disabled={picked.size === 0} onClick={startCheckout}>
                          {isSignedIn ? t("book.confirmSeats") : t("cinema.signInToBook")}
                        </Button>
                      </div>
                    </div>
                  )}
                </div>
              </>
            )}
          </Panel>
        </div>
      ) : null}

      {tickets.length > 0 ? (
        <div className="mt-10">
          <h3 className="mb-4 font-display text-2xl text-ink">{t("book.myTickets")}</h3>
          <div className="space-y-4">
            {tickets.map((ticket) => (
              <TicketCard
                key={ticket.reference}
                ticket={ticket}
                onRefunded={() => { void loadTickets(); if (activeId) void loadMap(activeId); }}
              />
            ))}
          </div>
        </div>
      ) : null}
    </Section>
  );
}
