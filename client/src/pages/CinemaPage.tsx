import { useCallback, useEffect, useRef, useState } from "react";
import { motion } from "motion/react";
import { Section, Panel, Notice, Spinner, Empty } from "@/components/Shell";
import { BookingFlow, TicketCard, type SeatSelection, type TicketResponse, type CheckoutStarted } from "@/components/BookingFlow";
import { post, ApiError } from "@/lib/api";
import { HallPreview, type PreviewSeat } from "@/components/HallPreview";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/components/useAuth";
import { get } from "@/lib/api";
import { loadOfflineTickets, saveOfflineTickets } from "@/lib/offlineTickets";
import { formatDateTime, formatMoney } from "@/lib/format";
import { t, languageName } from "@/lib/i18n";
import { goToSignIn, onAppResume } from "@/lib/platform";

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
  /** "Card" or "Stripe". A Stripe hold is finished on Stripe's page, not with a code. */
  provider?: string;
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

/** Where the visitor was in a booking: the show, the seats, and whether they had moved on
 *  to paying. Kept so that a trip to another page (or a language switch, which reloads)
 *  brings them back to the same place. Nothing here is secret — no card data, ever. */
interface BookingDraft { screeningId: string; seats: string[]; atCheckout: boolean; savedAt: number }
const DRAFT_KEY = "wy.cinema.draft";
const DRAFT_TTL_MS = 30 * 60_000;

function readDraft(): BookingDraft | null {
  try {
    const raw = localStorage.getItem(DRAFT_KEY);
    if (!raw) return null;
    const draft = JSON.parse(raw) as BookingDraft;
    if (!draft.screeningId || !Array.isArray(draft.seats) || Date.now() - draft.savedAt > DRAFT_TTL_MS) {
      localStorage.removeItem(DRAFT_KEY);
      return null;
    }
    return draft;
  } catch {
    return null;
  }
}

function writeDraft(draft: BookingDraft) {
  try {
    if (draft.seats.length > 0) localStorage.setItem(DRAFT_KEY, JSON.stringify(draft));
    else localStorage.removeItem(DRAFT_KEY);
  } catch { /* storage blocked: the booking still works, it just is not remembered */ }
}

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

/** hideTickets: the phone app shows tickets on a tab of their own, so not twice. */
export default function CinemaPage({ hideTickets = false }: { hideTickets?: boolean } = {}) {
  const { isSignedIn, isAdmin } = useAuth();
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
  // Read once; consumed by the first seat map of the same show, so later changes of show
  // start empty as before.
  const draftRef = useRef<BookingDraft | null>(readDraft());
  const [draftSettled, setDraftSettled] = useState(false);
  // Set when the schedule could not be fetched at all — no signal, server down. Kept apart
  // from "fetched, and empty": the two need different words and only one needs a retry.
  const [loadFailed, setLoadFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const requested = new URLSearchParams(window.location.search).get("screening");
    setLoading(true);
    setLoadFailed(false);

    get<Screening[]>("/api/screenings")
      .then((list) => {
        setMessage(null);
        setScreenings(list);

        if (!requested) {
          const draftShow = draftRef.current?.screeningId;
          setActiveId(draftShow && list.some((item) => item.id === draftShow) ? draftShow : list[0]?.id ?? null);
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
      .catch(() => setLoadFailed(true))
      .finally(() => setLoading(false));
  }, [reloadKey]);

  // Back in the foreground (the phone app), a schedule that failed to load tries again.
  useEffect(() => onAppResume(() => { if (loadFailed) setReloadKey((k) => k + 1); }), [loadFailed]);

  const loadMap = useCallback(async (screeningId: string) => {
    setPicked(new Set());
    try {
      const loaded = await get<SeatMap>(`/api/screenings/${screeningId}/seats`);
      setMap(loaded);

      // Back to an unfinished booking: the seats return, minus any somebody else bought in
      // the meantime, and the payment step reopens if that is where the visitor was.
      const draft = draftRef.current;
      if (draft && draft.screeningId === screeningId) {
        draftRef.current = null;
        const free = new Set(loaded.seats.filter((seat) => !seat.isTaken).map((seat) => seatKey(seat.row, seat.number)));
        const still = draft.seats.filter((key) => free.has(key));
        const started = new Date(loaded.startsAtUtc).getTime() <= Date.now();
        if (!started && still.length > 0) {
          setPicked(new Set(still));
          if (still.length < draft.seats.length) {
            setMessage({ tone: "info", text: t("cinema.someTaken", "Some of your seats were sold meanwhile — check the selection.") });
          } else if (draft.atCheckout) {
            setCheckoutSeats(still.map((key) => {
              const [row, number] = key.split(":").map(Number);
              return { row, number };
            }));
          }
        }
      }
    } catch {
      setMessage({ tone: "error", text: t("error.seatMap") });
    } finally {
      setDraftSettled(true);
    }
  }, []);

  // Remember where the booking is whenever it changes — but only after the saved one has had
  // its chance to come back, or the empty first render would wipe it.
  useEffect(() => {
    if (!draftSettled || !map) return;
    writeDraft({ screeningId: map.screeningId, seats: [...picked], atCheckout: checkoutSeats !== null, savedAt: Date.now() });
  }, [draftSettled, map, picked, checkoutSeats]);

  const [offlineSince, setOfflineSince] = useState<string | null>(null);

  const loadTickets = useCallback(async () => {
    // No network, so no session either: show the tickets this browser saved last time.
    if (!isSignedIn) {
      const saved = !navigator.onLine ? loadOfflineTickets() : null;
      setTickets(saved?.tickets ?? []);
      setOfflineSince(saved ? saved.savedAt : null);
      setPending([]);
      return;
    }
    const [mine, unfinished] = await Promise.all([
      get<TicketResponse[] | null>("/api/bookings/mine").catch(() => null),
      get<PendingCheckout[]>("/api/bookings/pending").catch(() => []),
    ]);
    if (mine) {
      setTickets(mine);
      saveOfflineTickets(mine);
      setOfflineSince(null);
    } else {
      const saved = loadOfflineTickets();
      setTickets(saved?.tickets ?? []);
      setOfflineSince(saved ? saved.savedAt : null);
    }
    setPending(unfinished);
  }, [isSignedIn]);

  useEffect(() => { void loadTickets(); }, [loadTickets]);

  // Left the page while waiting for the e-mailed code: open that step again by itself
  // rather than making the visitor find the "resume" button. Once per visit.
  const autoResumed = useRef(false);
  useEffect(() => {
    if (autoResumed.current || resuming || pending.length === 0) return;
    const mine = pending.find((checkout) => {
      if (checkout.provider === "Stripe") return false;
      try {
        const saved = JSON.parse(sessionStorage.getItem(`wy.checkout.${checkout.screeningId}`) ?? "null");
        return saved?.paymentId === checkout.paymentId;
      } catch { return false; }
    });
    if (!mine) return;
    autoResumed.current = true;
    setActiveId(mine.screeningId);
    setResuming(mine);
  }, [pending, resuming]);

  // In the phone app: coming back from Stripe's page (or from the background). Stripe's return
  // address is the website, which the app never sees, so the app asks instead — for each
  // unfinished Stripe checkout, "was this paid?". The server checks with Stripe; a paid one
  // becomes a ticket on the spot, an unpaid one is left alone.
  useEffect(() => onAppResume(() => {
    void (async () => {
      const unfinished = await get<PendingCheckout[]>("/api/bookings/pending").catch(() => [] as PendingCheckout[]);
      for (const checkout of unfinished.filter((c) => c.provider === "Stripe")) {
        try {
          setStripeTicket(await post<TicketResponse>(`/api/bookings/${checkout.paymentId}/stripe/confirm`, { sessionId: null }));
          setMessage({ tone: "ok", text: t("stripe.paid", "Paid. Your tickets are below and on their way by e-mail.") });
        } catch {
          /* not paid (yet) — it stays in the unfinished list with its own button */
        }
      }
      await loadTickets();
    })();
  }), [loadTickets]);

  // Back from Stripe. The URL only says which booking it was; whether it was paid is asked
  // of Stripe by the server, never taken from the address bar.
  const [stripeTicket, setStripeTicket] = useState<TicketResponse | null>(null);
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const outcome = params.get("stripe");
    const paymentId = params.get("payment");
    const sessionId = params.get("session_id");
    if (!outcome || !paymentId || !isSignedIn) return;

    // Tidy the address first, so a reload does not repeat any of this.
    params.delete("stripe"); params.delete("payment"); params.delete("session_id");
    const rest = params.toString();
    window.history.replaceState(null, "", window.location.pathname + (rest ? `?${rest}` : ""));

    if (outcome === "cancel") {
      void post(`/api/bookings/${paymentId}/cancel`).catch(() => null).then(() => loadTickets());
      setMessage({ tone: "info", text: t("stripe.cancelled", "Payment cancelled. The seats are back on sale.") });
      return;
    }

    setMessage({ tone: "info", text: t("stripe.checking", "Checking the payment with Stripe…") });
    post<TicketResponse>(`/api/bookings/${paymentId}/stripe/confirm`, { sessionId })
      .then((ticket) => {
        setStripeTicket(ticket);
        setMessage({ tone: "ok", text: t("stripe.paid", "Paid. Your tickets are below and on their way by e-mail.") });
        void loadTickets();
      })
      .catch((err) => setMessage({ tone: "error", text: err instanceof ApiError ? err.message : t("error.booking") }));
  }, [isSignedIn, loadTickets]);

  useEffect(() => {
    if (activeId) void loadMap(activeId);
  }, [activeId, loadMap]);

  useEffect(() => {
    if (!activeId) return;
    activeRef.current?.scrollIntoView({ block: "nearest", behavior: "smooth" });
  }, [activeId]);

  // The list keeps shows for a while after they start (someone may be looking for the one
  // they are late for), but they can no longer be sold. Deciding that here rather than
  // at payment means nobody picks seats and types a card number only to be refused.
  const hasStarted = (iso: string) => new Date(iso).getTime() <= Date.now();
  const mapStarted = map ? hasStarted(map.startsAtUtc) : false;

  function toggleSeat(seat: SeatState) {
    if (seat.isTaken || mapStarted) return;
    setPicked((current) => {
      const next = new Set(current);
      const key = seatKey(seat.row, seat.number);
      next.has(key) ? next.delete(key) : next.add(key);
      return next;
    });
  }

  function startCheckout() {
    if (!map || picked.size === 0 || mapStarted) return;

    // No account, no checkout. Sent straight to sign-in, carrying the way back so the
    // chosen performance is still on screen afterwards.
    if (!isSignedIn) {
      const back = `/cinema?screening=${map.screeningId}`;
      goToSignIn(back);
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
                {checkout.provider === "Stripe" ? (
                  <Button
                    size="sm"
                    onClick={async () => {
                      try {
                        setStripeTicket(await post<TicketResponse>(`/api/bookings/${checkout.paymentId}/stripe/confirm`, { sessionId: null }));
                        await loadTickets();
                      } catch (err) {
                        setMessage({ tone: "error", text: err instanceof ApiError ? err.message : t("error.booking") });
                      }
                    }}
                  >
                    {t("stripe.check", "Check payment")}
                  </Button>
                ) : (
                  <Button size="sm" onClick={() => { setActiveId(checkout.screeningId); setResuming(checkout); }}>
                    {t("book.resume")}
                  </Button>
                )}
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

      {stripeTicket ? (
        <div className="mb-6"><TicketCard ticket={stripeTicket} onDone={() => setStripeTicket(null)} /></div>
      ) : null}

      {message && !map ? <div className="mb-4"><Notice tone={message.tone}>{message.text}</Notice></div> : null}

      {loading ? <Spinner label={t("cinema.loading")} /> : null}

      {!loading && loadFailed ? (
        <Empty
          title={t("error.screenings")}
          hint={t("cinema.offlineHint", "Check your internet connection and try again.")}
          action={<Button onClick={() => setReloadKey((k) => k + 1)}>{t("common.retry", "Try again")}</Button>}
        />
      ) : null}

      {!loading && !loadFailed && screenings.length === 0 ? (
        <Empty
          title={t("cinema.emptyTitle")}
          hint={isAdmin ? t("cinema.emptyHint") : t("cinema.emptyHintPublic", "New showtimes are coming soon — check back later.")}
        />
      ) : null}

      {screenings.length > 0 ? (
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-[260px_1fr]">
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
                  {hasStarted(screening.startsAtUtc) ? (
                    <p className="mt-1 text-xs text-warn">{t("cinema.started", "Started — no longer on sale")}</p>
                  ) : (
                    <p className="mt-1 text-xs text-accent">
                      {screening.capacity - screening.seatsTaken} / {screening.capacity} {t("cinema.free", "free")}
                    </p>
                  )}
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
                    {map.venueName} · {map.hall} · {formatDateTime(map.startsAtUtc)} · {formatMoney(map.seatPrice)} {t("cinema.perSeat", "a seat")}
                  </p>
                  <p className="mt-1 text-sm text-accent">
                    {languageName(map.audioLanguage)}
                    {map.subtitleLanguage
                      ? ` · ${t("onDisplay.subtitles")}: ${languageName(map.subtitleLanguage)}`
                      : ""}
                  </p>
                </div>

                {mapStarted ? (
                  <div className="mb-4"><Notice tone="info">{t("cinema.startedLong", "This show has already started, so its seats are no longer on sale. Pick a later one.")}</Notice></div>
                ) : null}
                <div className="mb-6 overflow-hidden rounded-t-[999px] border-b-2 border-accent-dim bg-gradient-to-b from-accent/20 to-transparent py-2 text-center text-xs tracking-[0.3em] text-accent-dim">
                  {t("cinema.screen", "screen")}
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
                                disabled={seat.isTaken || mapStarted}
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

      {tickets.length > 0 && !hideTickets ? (
        <div className="mt-10">
          <h3 className="mb-4 font-display text-2xl text-ink">{t("book.myTickets")}</h3>
          {offlineSince ? (
            <div className="mb-4">
              <Notice tone="info">{t("offline.tickets", "You are offline. These are the tickets saved on this device.")}</Notice>
            </div>
          ) : null}
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
