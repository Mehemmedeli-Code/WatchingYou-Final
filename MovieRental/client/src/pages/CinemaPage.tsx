import { useCallback, useEffect, useState } from "react";
import { motion } from "motion/react";
import { Section, Panel, Notice, Spinner, Empty } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/components/useAuth";
import { get, post, ApiError } from "@/lib/api";
import { formatDateTime, formatMoney } from "@/lib/format";
import { t } from "@/lib/i18n";

interface Screening {
  id: string;
  movieTitle: string;
  hall: string;
  startsAtUtc: string;
  seatPrice: number;
  capacity: number;
  seatsTaken: number;
}

interface SeatState {
  row: number;
  number: number;
  isTaken: boolean;
  isMine: boolean;
}

interface SeatMap {
  screeningId: string;
  movieTitle: string;
  hall: string;
  startsAtUtc: string;
  rows: number;
  seatsPerRow: number;
  seatPrice: number;
  seats: SeatState[];
}

const seatKey = (row: number, number: number) => `${row}:${number}`;

export default function CinemaPage() {
  const { isSignedIn } = useAuth();
  const [screenings, setScreenings] = useState<Screening[]>([]);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [map, setMap] = useState<SeatMap | null>(null);
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [message, setMessage] = useState<{ tone: "ok" | "error" | "info"; text: string } | null>(null);
  const [booking, setBooking] = useState(false);

  useEffect(() => {
    get<Screening[]>("/api/screenings")
      .then((list) => {
        setScreenings(list);
        setActiveId(list[0]?.id ?? null);
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

  useEffect(() => {
    if (activeId) void loadMap(activeId);
  }, [activeId, loadMap]);

  function toggleSeat(seat: SeatState) {
    if (seat.isTaken) return;
    setPicked((current) => {
      const next = new Set(current);
      const key = seatKey(seat.row, seat.number);
      next.has(key) ? next.delete(key) : next.add(key);
      return next;
    });
  }

  async function book() {
    if (!map || picked.size === 0) return;
    if (!isSignedIn) {
      window.location.href = "/account";
      return;
    }

    setBooking(true);
    setMessage(null);
    const seats = [...picked].map((key) => {
      const [row, number] = key.split(":").map(Number);
      return { row, number };
    });

    try {
      await post(`/api/screenings/${map.screeningId}/bookings`, { seats });
      setMessage({ tone: "ok", text: `Booked ${seats.length} seat${seats.length === 1 ? "" : "s"}. See you there.` });
      await loadMap(map.screeningId);
    } catch (err) {
      setMessage({ tone: "error", text: err instanceof ApiError ? err.message : t("error.booking") });
      await loadMap(map.screeningId);
    } finally {
      setBooking(false);
    }
  }

  const total = map ? map.seatPrice * picked.size : 0;

  return (
    <Section title={t("cinema.title")} lede={t("cinema.lede")}>
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
                  onClick={() => setActiveId(screening.id)}
                  aria-pressed={isActive}
                  className={`w-full rounded-lg border p-3 text-left transition-colors ${
                    isActive ? "border-accent bg-surface-raised" : "border-line hover:border-accent-dim"
                  }`}
                >
                  <p className="font-display text-base text-ink">{screening.movieTitle}</p>
                  <p className="mt-1 text-xs text-ink-mute">
                    {screening.hall} · {formatDateTime(screening.startsAtUtc)}
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
                    {map.hall} · {formatDateTime(map.startsAtUtc)} · {formatMoney(map.seatPrice)} a seat
                  </p>
                </div>

                <div className="mb-6 overflow-hidden rounded-t-[999px] border-b-2 border-accent-dim bg-gradient-to-b from-accent/20 to-transparent py-2 text-center text-xs tracking-[0.3em] text-accent-dim">
                  screen
                </div>

                <div className="space-y-2 overflow-x-auto pb-2">
                  {Array.from({ length: map.rows }, (_, r) => r + 1).map((row) => (
                    <div key={row} className="flex items-center gap-2">
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
                                className={`h-7 w-7 rounded-t-md border text-[10px] transition-colors ${
                                  seat.isTaken
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
                  <span className="flex items-center gap-1.5"><i className="h-3 w-3 rounded-sm bg-accent-dim" />Yours</span>
                </div>

                {message ? <div className="mt-4"><Notice tone={message.tone}>{message.text}</Notice></div> : null}

                <div className="mt-6 flex flex-wrap items-center justify-between gap-4 border-t border-line pt-4">
                  <p className="text-sm text-ink-mute">
                    {picked.size === 0
                      ? t("cinema.noSeats")
                      : `${picked.size} seat${picked.size === 1 ? "" : "s"} · ${formatMoney(total)}`}
                  </p>
                  <Button disabled={picked.size === 0 || booking} onClick={book}>
                    {booking ? t("cinema.booking") : isSignedIn ? t("cinema.confirm") : t("cinema.signInToBook")}
                  </Button>
                </div>
              </>
            )}
          </Panel>
        </div>
      ) : null}
    </Section>
  );
}
