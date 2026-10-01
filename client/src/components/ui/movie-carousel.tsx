import { useCallback, useEffect, useRef, useState } from "react";
import { Clapperboard, Info, Play, Ticket } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import type { MovieListItem } from "@/components/MovieCard";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";

/**
 * A ribbon of posters receding into the distance, turned by moving the pointer across it.
 *
 * Every card carries the *same* rotation rather than mirroring around the middle — that is
 * what makes it read as one long ribbon seen at an angle instead of a symmetric fan. Depth
 * follows the signed offset, so the left end stands near and the right end falls away.
 *
 * Moving the pointer steers it; no button is held. A drag still works for touch, where there
 * is no hover to read, and the arrow keys work for anyone not using either.
 */

const SPACING = 74;       // px between neighbours — a fraction of a card's width, so they overlap
const TILT = -34;         // degrees, the same for every card
const DEPTH = 96;         // px of recession per step, signed
const VISIBLE = 9;        // cards drawn each way
const REACH = 3.2;        // how many cards a full sweep of the frame travels

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
  const [steering, setSteering] = useState(false);
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

  function onPointerMove(event: React.PointerEvent) {
    if (drag.current) {
      const delta = event.clientX - drag.current.startX;
      if (Math.abs(delta) > 4) drag.current.moved = true;
      target.current = drag.current.startPosition - delta / SPACING;
      return;
    }

    // Hover steering. Pointer position across the frame maps straight onto a span of cards, so
    // the same place always shows the same part of the ribbon — a speed-based spin would drift
    // and never come back to where you left it.
    const box = event.currentTarget.getBoundingClientRect();
    const ratio = (event.clientX - box.left) / box.width;
    target.current = (ratio - 0.5) * 2 * REACH;
    setSteering(true);
  }

  function onPointerDown(event: React.PointerEvent) {
    drag.current = { startX: event.clientX, startPosition: target.current, moved: false };
    (event.target as Element).setPointerCapture?.(event.pointerId);
  }

  function onPointerUp() {
    drag.current = null;
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
        onPointerMove={onPointerMove}
        onPointerDown={onPointerDown}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
        onPointerLeave={() => { setSteering(false); onPointerUp(); }}
        className={cn(
          "relative h-[420px] cursor-ew-resize select-none overflow-hidden sm:h-[520px]",
          "focus:outline-none focus-visible:ring-1 focus-visible:ring-accent",
        )}
        style={{ perspective: "1200px", touchAction: "pan-y" }}
      >
        <div className="absolute inset-0" style={{ transformStyle: "preserve-3d" }}>
          {movies.map((movie, index) => {
            const offset = offsetOf(index);
            if (Math.abs(offset) > VISIBLE) return null;

            const isCentre = Math.abs(offset) < 0.5;

            return (
              <button
                key={movie.id}
                type="button"
                aria-hidden={!isCentre}
                tabIndex={-1}
                onClick={() => {
                  if (drag.current?.moved) return;
                  if (isCentre) onOpen?.(movie, "details");
                  else nudge(offset);
                }}
                // Taller than the frame on purpose: the ribbon runs off the top and bottom
                // rather than sitting inside a box, which is what gives it scale.
                className="absolute left-1/2 top-1/2 h-[520px] w-[280px] origin-center overflow-hidden rounded-lg border border-line/60 bg-surface-raised shadow-2xl sm:h-[640px] sm:w-[330px]"
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
                  opacity: Math.max(0, 1 - Math.abs(offset) / (VISIBLE + 1)),
                  transition: steering || drag.current ? "none" : "opacity 300ms",
                }}
              >
                {movie.posterUrl ? (
                  <img src={movie.posterUrl} alt="" className="h-full w-full object-cover" draggable={false} />
                ) : (
                  <span className="flex h-full w-full items-center justify-center bg-surface text-accent-dim">
                    <Clapperboard size={44} strokeWidth={1.2} aria-hidden />
                  </span>
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
            <Badge>{current.genre}</Badge>
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

          {onOpen && current.hasVideo ? (
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
