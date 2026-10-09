import { motion } from "motion/react";
import { Heart, Info, Play, Star } from "lucide-react";
import { GenrePoster } from "@/components/PosterArt";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { formatRuntime, formatUsd } from "@/lib/format";
import { useAuth } from "@/components/useAuth";
import { t } from "@/lib/i18n";
import { isApp } from "@/lib/platform";

export interface MovieListItem {
  id: string;
  title: string;
  slug: string;
  genre: string;
  releaseYear: number;
  durationMinutes: number;
  dailyPrice: number;
  availableCopies: number;
  totalCopies: number;
  averageRating: number;
  reviewCount: number;
  posterUrl?: string | null;
  isDeleted: boolean;
  hasVideo: boolean;
  /** What the Watch button plays when the film itself is not on the site. */
  trailerUrl?: string | null;
}

export function MovieCard({
  movie,
  index,
  onRent,
  onOpen,
  busy,
  saved,
  onToggleSave,
  note,
}: {
  movie: MovieListItem;
  index: number;
  /** Whether the film is on the customer's watch-later list. */
  saved?: boolean;
  /** Present only when someone is signed in — an anonymous heart would have nowhere to go. */
  onToggleSave?: (movie: MovieListItem) => void;
  /** A short line above the title, used by recommendations to say why a film is there. */
  note?: string | null;
  onRent?: (movie: MovieListItem) => void;
  /** Opens the dialog, either on the description or straight into the player. */
  onOpen?: (movie: MovieListItem, mode: "details" | "watch") => void;
  busy?: boolean;
}) {
  const { isSecurity: isStaff } = useAuth();
  const available = movie.availableCopies > 0;
  const playable = movie.hasVideo || !!movie.trailerUrl;

  return (
    <motion.article
      // A single orchestrated entrance for the grid: each card lands a beat after the one
      // before it, then nothing moves again until the reader acts.
      initial={{ opacity: 0, y: 14 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ type: "spring", stiffness: 260, damping: 26, delay: Math.min(index * 0.04, 0.4) }}
      className="flex flex-col overflow-hidden rounded-xl border border-line bg-surface-raised"
    >
      <div className="relative aspect-[2/3] overflow-hidden bg-surface">
        {movie.posterUrl ? (
          <img src={movie.posterUrl} alt="" className="h-full w-full object-cover" loading="lazy" />
        ) : (
          <GenrePoster genre={movie.genre} />
        )}
        <span className="absolute left-3 top-3 rounded-full bg-surface/85 px-2.5 py-1 text-xs text-ink-mute">
          {movie.genre}
        </span>
        {onToggleSave ? (
          <button
            type="button"
            onClick={() => onToggleSave(movie)}
            aria-pressed={saved ?? false}
            aria-label={saved ? t("watchlist.remove", "Remove from watchlist") : t("watchlist.add", "Add to watchlist")}
            title={saved ? t("watchlist.remove", "Remove from watchlist") : t("watchlist.add", "Add to watchlist")}
            className="absolute right-3 top-3 grid h-9 w-9 place-items-center rounded-full bg-surface/85 text-ink transition hover:scale-105 focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent"
          >
            <Heart size={16} className={saved ? "text-bad" : "text-ink-mute"} fill={saved ? "currentColor" : "none"} aria-hidden />
          </button>
        ) : null}
      </div>

      <div className="flex flex-1 flex-col gap-3 p-4">
        <div>
          {note ? <p className="mb-1 text-[11px] font-medium uppercase tracking-wide text-accent">{note}</p> : null}
          <h3 className="font-display text-lg leading-snug text-ink">{movie.title}</h3>
          <p className="mt-1 text-xs text-ink-mute">
            {movie.releaseYear} · {formatRuntime(movie.durationMinutes)}
          </p>
        </div>

        <div className="flex items-center gap-2 text-xs text-ink-mute">
          <Star size={13} className="text-accent" fill="currentColor" aria-hidden />
          {movie.reviewCount > 0 ? (
            <span>
              {movie.averageRating.toFixed(1)} · {movie.reviewCount} {t("movie.reviews", "reviews")}
            </span>
          ) : (
            <span>{t("movie.noReviews", "No reviews yet")}</span>
          )}
        </div>

        <div className="mt-auto pt-2">
          <div className="flex items-center justify-between gap-3">
            {/* Every rental is $0.50 for three days now; staff never rent, so they see no price,
                and neither does the phone app, which sells no films. */}
            {isStaff || isApp() ? <span /> : (
              <p className="text-sm text-ink">{formatUsd(0.5)}<span className="text-ink-mute"> / 3 {t("pro.days", "days")}</span></p>
            )}
            <Badge tone={available ? "good" : "bad"}>
              {available ? `${movie.availableCopies} ${t("movie.onShelf")}` : t("movie.allOut")}
            </Badge>
          </div>

          {/* Renting and watching are different intentions, so they get different buttons.
              Watch stays disabled until a film actually has a video behind it — a button
              that opens an empty player is worse than one that is visibly not ready. */}
          <div className="mt-3 flex flex-wrap gap-2">
            {onOpen ? (
              <Button size="sm" variant="outline" onClick={() => onOpen(movie, "details")}>
                <Info size={14} aria-hidden />
                {t("movie.details")}
              </Button>
            ) : null}

            {onOpen ? (
              <Button
                size="sm"
                variant={playable ? "solid" : "outline"}
                disabled={!playable}
                title={playable ? undefined : t("movie.noVideo")}
                onClick={() => onOpen(movie, "watch")}
              >
                <Play size={14} aria-hidden />
                {t("movie.watch")}
              </Button>
            ) : null}

            {onRent && !isStaff ? (
              <Button size="sm" variant="outline" disabled={!available || busy} onClick={() => onRent(movie)}>
                {busy ? t("movie.renting") : t("movie.rent")}
              </Button>
            ) : null}
          </div>
        </div>
      </div>
    </motion.article>
  );
}
