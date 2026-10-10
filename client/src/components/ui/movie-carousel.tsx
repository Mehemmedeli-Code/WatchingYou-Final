import { useCallback, useEffect, useRef, useState } from "react";
import { Info, Play, Ticket } from "lucide-react";
import { GenrePoster } from "@/components/PosterArt";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import type { MovieListItem } from "@/components/MovieCard";
import { t, genreName } from "@/lib/i18n";
import { cn } from "@/lib/utils";

/**
 * A ribbon of posters receding into the distance, turned by moving the pointer across it.
 *
 * Every card carries the *same* rotation rather than mirroring around the middle — that is
 * what makes it read as one long ribbon seen at an angle instead of a symmetric fan. Depth
 * follows the signed offset, so the left end stands near and the right end falls away.
 *
 * The film named under the ribbon is the card at its near end — the one fully in view. It used
 * to be the card in the middle, which the nearer cards to its left partly covered, so the
 * poster people saw and the title they read were two different films. Cards that have passed
 * the near end fade out to the left instead of standing in front of it.
 *
 * It moves only when asked to: a held mouse drag, a two-finger swipe on a trackpad, or a finger
 * on a phone. It used to follow the pointer on hover, which meant it slid away whenever the
 * cursor merely crossed it on the way down the page — the carousel reacting to somebody who
 * was not using it.
 */

const SPACING = 74;       // px between neighbours — a fraction of a card's width, so they overlap
const TILT = -34;         // degrees, the same for every card
const DEPTH = 96;         // px of recession per step, signed
const VISIBLE = 9;        // cards drawn each way
const WHEEL_STEP = 0.012; // cards per pixel of horizontal wheel travel

export function MovieCarousel({
  movies,
  onRent,
  onOpen,
  busyId,
}: {
  movies: MovieListItem[];
  onRent?: (movie: MovieListItem) => void;
  onOpen?: (movie: MovieListItem, mode: "details" | "watch") => void;
  busyId?: string | null;
}) {
  const total = movies.length;

  // Where the ribbon sits, in cards. Kept in a ref as well as state: the pointer handler needs
  // to read it every frame without re-subscribing.
  const [position, setPosition] = useState(0);
  const [dragging, setDragging] = useState(false);
  const target = useRef(0);
  const frame = useRef<HTMLDivElement>(null);
  const drag = useRef<{ startX: number; startPosition: number; moved: boolean } | null>(null);

  // Eased towards the target rather than snapped to it, so the ribbon has weight and a twitchy
  // mouse does not make it judder.
  useEffect(() => {
    let raf = 0;
    const tick = () => {
      setPosition((p) => (Math.abs(target.current - p) < 0.001 ? p : p + (target.current - p) * 0.12));
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, []);

  const nudge = useCallback((by: number) => { target.current += by; }, []);

  useEffect(() => {
    const element = frame.current;
    if (!element) return;

    const onWheel = (event: WheelEvent) => {
      // Only a sideways gesture turns the ribbon. A vertical one belongs to the page, so
      // somebody scrolling down past the carousel is never caught by it.
      if (Math.abs(event.deltaX) <= Math.abs(event.deltaY)) return;
      event.preventDefault();
      target.current += event.deltaX * WHEEL_STEP;
    };

    element.addEventListener("wheel", onWheel, { passive: false });
    return () => element.removeEventListener("wheel", onWheel);
  }, []);

  // A gesture that stops between cards settles onto the nearest one.
  useEffect(() => {
    if (dragging) return;
    const timer = setTimeout(() => { target.current = Math.round(target.current); }, 140);
    return () => clearTimeout(timer);
  }, [dragging, position]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (!frame.current?.contains(document.activeElement)) return;
      if (event.key === "ArrowRight") { event.preventDefault(); nudge(1); }
      if (event.key === "ArrowLeft") { event.preventDefault(); nudge(-1); }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [nudge]);

  if (total === 0) return null;

  const active = ((Math.round(position) % total) + total) % total;
  const current = movies[active];

  // One handler for mouse and finger alike. Nothing happens until a button or a finger is
  // down, so passing the cursor over the ribbon leaves it exactly where it was.
  function onPointerDown(event: React.PointerEvent) {
    if (event.pointerType === "mouse" && event.button !== 0) return;
    drag.current = { startX: event.clientX, startPosition: target.current, moved: false };
  }

  function onPointerMove(event: React.PointerEvent) {
    if (!drag.current) return;
    const delta = event.clientX - drag.current.startX;

    // The pointer is captured only once it has actually travelled. Capturing on press would
    // redirect the click that follows to the frame, and tapping a card would stop opening it.
    if (!drag.current.moved && Math.abs(delta) > 4) {
      drag.current.moved = true;
      event.currentTarget.setPointerCapture?.(event.pointerId);
      setDragging(true);
    }

    if (drag.current.moved) target.current = drag.current.startPosition - delta / SPACING;
  }

  function onPointerUp() {
    setDragging(false);
    // Cleared on the next tick, not now: the click that ends a drag fires straight after this
    // and needs to see that the pointer moved, so it is not taken for a tap on a card.
    setTimeout(() => { drag.current = null; }, 0);
  }

  function offsetOf(index: number) {
    let offset = index - position;
    while (offset > total / 2) offset -= total;
    while (offset < -total / 2) offset += total;
    return offset;
  }

  return (
    <div>
      <div
        ref={frame}
        role="group"
        aria-roledescription="carousel"
        aria-label={t("home.carousel")}
        tabIndex={0}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
        className={cn(
          "relative h-[420px] select-none overflow-hidden sm:h-[520px]",
          "focus:outline-none focus-visible:ring-1 focus-visible:ring-accent",
          dragging ? "cursor-grabbing" : "cursor-grab",
        )}
        // pan-y leaves vertical swipes to the browser, so a thumb scrolling the page on a phone
        // is never taken for a turn of the ribbon; sideways swipes come to the handler.
        style={{ perspective: "1200px", touchAction: "pan-y" }}
      >
        <div className="absolute inset-0" style={{ transformStyle: "preserve-3d" }}>
          {movies.map((movie, index) => {
            const offset = offsetOf(index);
            // Only the active card and those behind it; one that has just passed fades out.
            if (offset <= -0.95 || offset > VISIBLE) return null;

            const isCentre = Math.abs(offset) < 0.5;

            return (
              <button
                key={movie.id}
                type="button"
                aria-hidden={!isCentre}
                aria-label={isCentre ? movie.title : undefined}
                tabIndex={-1}
                onClick={() => {
                  if (drag.current?.moved) return;
                  if (isCentre) onOpen?.(movie, "details");
                  else nudge(offset);
                }}
                // Taller than the frame on purpose: the ribbon runs off the top and bottom
                // rather than sitting inside a box, which is what gives it scale.
                className="absolute left-[30%] top-1/2 h-[520px] w-[280px] origin-center overflow-hidden rounded-lg border border-line/60 bg-surface-raised shadow-2xl sm:h-[640px] sm:w-[330px]"
                style={{
                  transform: [
                    "translate(-50%, -50%)",
                    `translateX(${offset * SPACING}px)`,
                    // Signed, not absolute: the ribbon recedes in one direction instead of
                    // folding symmetrically about the middle.
                    `translateZ(${-offset * DEPTH}px)`,
                    `rotateY(${TILT}deg)`,
                  ].join(" "),
                  zIndex: 200 - Math.round(offset * 10),
                  // Solid, so no poster shows through the one in front of it; only the last
                  // card at each end fades in or out as the ribbon turns.
                  opacity: offset < 0 ? 1 + offset : Math.min(1, Math.max(0, VISIBLE + 0.5 - offset)),
                  transition: dragging ? "none" : "opacity 300ms",
                }}
              >
                {movie.posterUrl ? (
                  <img src={movie.posterUrl} alt="" loading="lazy" decoding="async" className="h-full w-full object-cover" draggable={false} />
                ) : (
                  <GenrePoster genre={movie.genre} />
                )}

                {/* A wash towards the far end, so the ribbon fades into depth instead of
                    ending on a hard edge. */}
                <span
                  className="pointer-events-none absolute inset-0"
                  style={{ background: `rgba(10,12,10,${Math.min(0.72, Math.max(0, offset) * 0.11)})` }}
                />
              </button>
            );
          })}
        </div>
      </div>

      <div className="mt-6 flex flex-wrap items-center justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-display text-2xl text-ink">{current.title}</p>
          <p className="mt-1 flex flex-wrap items-center gap-1.5 text-xs text-ink-mute">
            <Badge>{genreName(current.genre)}</Badge>
            <span>{current.releaseYear}</span>
            <span>·</span>
            <span>{current.durationMinutes} {t("movie.min")}</span>
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          {onOpen ? (
            <Button size="sm" variant="outline" onClick={() => onOpen(current, "details")}>
              <Info size={14} aria-hidden />
              {t("movie.details")}
            </Button>
          ) : null}

          {onOpen && (current.hasVideo || current.trailerUrl) ? (
            <Button size="sm" variant="outline" onClick={() => onOpen(current, "watch")}>
              <Play size={14} aria-hidden />
              {t("movie.watch")}
            </Button>
          ) : null}

          {onRent ? (
            <Button size="sm" disabled={busyId === current.id || current.availableCopies === 0} onClick={() => onRent(current)}>
              <Ticket size={14} aria-hidden />
              {busyId === current.id ? t("common.loading") : t("movie.rent")}
            </Button>
          ) : null}
        </div>
      </div>
    </div>
  );
}

export default MovieCarousel;
