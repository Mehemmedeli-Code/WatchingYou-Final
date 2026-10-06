import { useCallback, useEffect, useState } from "react";
import { Heart } from "lucide-react";
import { Section, Empty, Spinner, Notice } from "@/components/Shell";
import { MovieCard, type MovieListItem } from "@/components/MovieCard";
import { MovieDialog } from "@/components/MovieDialog";
import { get, post, ApiError } from "@/lib/api";
import { loadWatchlist, toggleWatchlist } from "@/lib/watchlist";
import { formatDay, t } from "@/lib/i18n";

interface WatchlistEntry {
  addedAtUtc: string;
  movie: MovieListItem;
}

/** The customer's "watch later" list, on the Favourites page (/favourites). */
export function WatchlistShelf() {
  const [entries, setEntries] = useState<WatchlistEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [dialog, setDialog] = useState<{ id: string; mode: "details" | "watch" } | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setEntries(await get<WatchlistEntry[]>("/api/watchlist"));
      setError(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("watchlist.failed", "Could not load your watchlist."));
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function remove(movie: MovieListItem) {
    setEntries((list) => list?.filter((e) => e.movie.id !== movie.id) ?? null);
    try {
      await toggleWatchlist(movie.id);
    } catch {
      void load();
    }
  }

  async function rent(movie: MovieListItem) {
    setBusyId(movie.id);
    setNotice(null);
    try {
      await post("/api/rentals", { movieId: movie.id, days: 7 });
      setNotice(`${movie.title} — ${t("movie.rented")}`);
      // Rented films leave the list: it was a reminder, and it has done its job.
      await toggleWatchlist(movie.id).catch(() => undefined);
      await Promise.all([load(), loadWatchlist(true)]);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("error.rental"));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <Section title={t("watchlist.title", "Watchlist")} lede={t("watchlist.lede", "Films you saved for later. Renting one takes it off the list.")}>
      {dialog ? (
        <MovieDialog
          movieId={dialog.id}
          mode={dialog.mode}
          onClose={() => setDialog(null)}
          onRent={(id) => {
            const entry = entries?.find((e) => e.movie.id === id);
            if (entry) { setDialog(null); void rent(entry.movie); }
          }}
        />
      ) : null}

      {error ? <Notice tone="error">{error}</Notice> : null}
      {notice ? <div className="mb-4"><Notice tone="ok">{notice}</Notice></div> : null}
      {entries === null && !error ? <Spinner /> : null}

      {entries && entries.length === 0 ? (
        <Empty
          title={t("watchlist.emptyTitle", "Nothing saved yet")}
          hint={t("watchlist.emptyHint", "Press the heart on any film in the catalogue to keep it here.")}
          action={<a href="/#catalogue" className="inline-flex h-9 items-center gap-2 rounded-full border border-line px-4 text-sm text-ink hover:border-accent hover:text-accent"><Heart size={14} aria-hidden />{t("home.hero.cta")}</a>}
        />
      ) : null}

      {entries && entries.length > 0 ? (
        <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-4">
          {entries.map((entry, i) => (
            <MovieCard
              key={entry.movie.id}
              movie={entry.movie}
              index={i}
              note={`${t("watchlist.savedOn", "Saved")} ${formatDay(entry.addedAtUtc)}`}
              saved
              onToggleSave={remove}
              onRent={rent}
              onOpen={(m, mode) => setDialog({ id: m.id, mode })}
              busy={busyId === entry.movie.id}
            />
          ))}
        </div>
      ) : null}
    </Section>
  );
}
