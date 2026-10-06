import { useCallback, useEffect, useMemo, useState } from "react";
import StackSpread from "@/components/ui/stack-spread";
import { HeroEye } from "@/components/ui/hero-eye";
import { MovieCarousel } from "@/components/ui/movie-carousel";
import { MovieCard, type MovieListItem } from "@/components/MovieCard";
import { SearchField } from "@/components/SearchField";
import { Section, Notice, Empty, Spinner } from "@/components/Shell";
import { Pagination } from "@/components/Pagination";
import { useWatchlist } from "@/lib/watchlist";
import { Toaster, type ToastMessage } from "@/components/Toast";
import { MovieDialog } from "@/components/MovieDialog";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/input";
import { useAuth } from "@/components/useAuth";
import { get, post, query, ApiError, type Paged } from "@/lib/api";
import { t } from "@/lib/i18n";

const SORTS = [
  { value: "newest", label: t("sort.newest") },
  { value: "title", label: t("sort.title") },
  { value: "year", label: t("sort.year") },
  { value: "rating", label: t("sort.rating") },
  { value: "price", label: t("sort.price") },
];

interface Recommendation {
  movie: MovieListItem;
  score: number;
  becauseGenre?: string | null;
}

interface RecommendationList {
  personal: boolean;
  items: Recommendation[];
}

const RATINGS = [0, 3, 3.5, 4, 4.5];
const PAGE_SIZE = 12;

/** Filters live in the address bar, so a filtered catalogue can be bookmarked, shared and
 *  survives the reload every Razor navigation causes. */
function readFilters() {
  const q = new URLSearchParams(window.location.search);
  const num = (key: string) => {
    const value = Number(q.get(key));
    return Number.isFinite(value) && value > 0 ? value : undefined;
  };
  return {
    search: q.get("q") ?? "",
    genre: q.get("genre") ?? "all",
    sortBy: q.get("sort") ?? "newest",
    sortDir: q.get("dir") === "asc" ? "asc" : "desc",
    onlyAvailable: q.get("stock") === "1",
    yearFrom: num("from"),
    yearTo: num("to"),
    minRating: num("rating"),
    page: num("page") ?? 1,
  };
}

export default function HomePage() {
  const { isSignedIn } = useAuth();
  const watchlist = useWatchlist();
  const initial = useMemo(readFilters, []);
  const [genres, setGenres] = useState<string[]>([]);
  const [page, setPage] = useState(initial.page);
  const [search, setSearch] = useState(initial.search);
  const [debounced, setDebounced] = useState(initial.search);
  const [genre, setGenre] = useState(initial.genre);
  const [sortBy, setSortBy] = useState(initial.sortBy);
  const [sortDir, setSortDir] = useState(initial.sortDir);
  const [onlyAvailable, setOnlyAvailable] = useState(initial.onlyAvailable);
  const [yearFrom, setYearFrom] = useState<number | undefined>(initial.yearFrom);
  const [yearTo, setYearTo] = useState<number | undefined>(initial.yearTo);
  const [minRating, setMinRating] = useState<number | undefined>(initial.minRating);
  const [recommended, setRecommended] = useState<RecommendationList | null>(null);

  const [data, setData] = useState<Paged<MovieListItem> | null>(null);
  // Fetched once and independent of the filters below: the front shelf should not
  // empty out the moment someone types in the search box.
  const [featured, setFeatured] = useState<MovieListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastMessage | null>(null);
  const [dialog, setDialog] = useState<{ id: string; mode: "details" | "watch" } | null>(null);
  const [rentingId, setRentingId] = useState<string | null>(null);

  const refreshFeatured = useCallback(async () => {
    const page = await get<Paged<MovieListItem>>(
      "/api/movies" + query({ sortBy: "rating", sortDir: "desc", pageSize: 8 }),
    ).catch(() => null);
    if (page) setFeatured(page.items);
  }, []);

  useEffect(() => { void refreshFeatured(); }, [refreshFeatured]);

  const refreshRecommended = useCallback(async () => {
    const list = await get<RecommendationList>("/api/recommendations" + query({ take: 4 })).catch(() => null);
    setRecommended(list && Array.isArray(list.items) ? list : null);
  }, []);

  useEffect(() => { void refreshRecommended(); }, [refreshRecommended, isSignedIn]);

  async function toggleSave(movie: MovieListItem) {
    try {
      const added = await watchlist.toggle(movie.id);
      setToast({
        id: Date.now(),
        tone: "ok",
        text: `${movie.title} — ${added ? t("watchlist.added", "added to your watchlist") : t("watchlist.removed", "removed from your watchlist")}`,
      });
      void refreshRecommended();
    } catch (err) {
      setToast({ id: Date.now(), tone: "error", text: err instanceof ApiError ? err.message : t("watchlist.failed", "Could not update your watchlist.") });
    }
  }

  // Debounce keeps the catalogue responsive without a request per keystroke.
  useEffect(() => {
    if (search === debounced) return;
    const handle = setTimeout(() => {
      setDebounced(search);
      setPage(1);
    }, 300);
    return () => clearTimeout(handle);
  }, [search, debounced]);

  useEffect(() => {
    get<string[]>("/api/movies/genres").then(setGenres).catch(() => setGenres([]));
  }, []);

  const url = useMemo(
    () =>
      "/api/movies" +
      query({ search: debounced, genre, sortBy, sortDir, onlyAvailable, yearFrom, yearTo, minRating, page, pageSize: PAGE_SIZE }),
    [debounced, genre, sortBy, sortDir, onlyAvailable, yearFrom, yearTo, minRating, page],
  );

  // Mirror the filters into the address bar without adding a history entry per keystroke.
  useEffect(() => {
    const q = new URLSearchParams();
    if (debounced) q.set("q", debounced);
    if (genre !== "all") q.set("genre", genre);
    if (sortBy !== "newest") q.set("sort", sortBy);
    if (sortDir !== "desc") q.set("dir", sortDir);
    if (onlyAvailable) q.set("stock", "1");
    if (yearFrom) q.set("from", String(yearFrom));
    if (yearTo) q.set("to", String(yearTo));
    if (minRating) q.set("rating", String(minRating));
    if (page > 1) q.set("page", String(page));
    const text = q.toString();
    const next = window.location.pathname + (text ? `?${text}` : "") + window.location.hash;
    if (next !== window.location.pathname + window.location.search + window.location.hash) {
      window.history.replaceState(null, "", next);
    }
  }, [debounced, genre, sortBy, sortDir, onlyAvailable, yearFrom, yearTo, minRating, page]);

  function clearFilters() {
    setSearch(""); setDebounced(""); setGenre("all"); setOnlyAvailable(false);
    setYearFrom(undefined); setYearTo(undefined); setMinRating(undefined); setPage(1);
  }

  function goToPage(next: number) {
    setPage(next);
    document.getElementById("catalogue")?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  const yearNow = new Date().getFullYear();

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setData(await get<Paged<MovieListItem>>(url));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("error.catalogue"));
    } finally {
      setLoading(false);
    }
  }, [url]);

  useEffect(() => {
    void load();
  }, [load]);

  // A bookmarked page number can outlive the catalogue it pointed into.
  useEffect(() => {
    if (data && data.items.length === 0 && data.totalCount > 0 && page > 1) setPage(1);
  }, [data, page]);

  async function rent(movie: MovieListItem) {
    if (!isSignedIn) {
      window.location.href = "/account";
      return;
    }

    setRentingId(movie.id);
    try {
      await post("/api/rentals", { movieId: movie.id, days: 7 });
      setToast({ id: Date.now(), tone: "ok", text: `${movie.title} — ${t("movie.rented")}` });
      await Promise.all([load(), refreshFeatured(), refreshRecommended()]);
    } catch (err) {
      setToast({
        id: Date.now(),
        tone: "error",
        text: err instanceof ApiError ? err.message : t("error.rental"),
      });
    } finally {
      setRentingId(null);
    }
  }

  return (
    <>
      <Toaster toast={toast} onDismiss={() => setToast(null)} />

      {dialog ? (
        <MovieDialog
          movieId={dialog.id}
          mode={dialog.mode}
          onClose={() => setDialog(null)}
          onRent={(id) => {
            const movie = [...featured, ...(data?.items ?? [])].find((m) => m.id === id);
            if (movie) { setDialog(null); void rent(movie); }
          }}
        />
      ) : null}

      {/* The eye, not a headline: the posters used to scatter across the words and leave
          half a sentence showing. The eye says the name without text to cover. */}
      <StackSpread centerpiece={<HeroEye label={t("home.hero.cta")} />} />
      <h1 className="sr-only">WatchingYou</h1>

      {featured.length > 0 ? (
        <Section title={t("featured.title")} lede={t("featured.lede")}>
          <MovieCarousel
            movies={featured}
            onRent={rent}
            onOpen={(m, mode) => setDialog({ id: m.id, mode })}
            busyId={rentingId}
          />
        </Section>
      ) : null}

      {recommended && recommended.items.length > 0 ? (
        <Section
          title={recommended.personal ? t("recommend.title", "Picked for you") : t("recommend.popular", "Popular right now")}
          lede={recommended.personal
            ? t("recommend.lede", "Chosen from the films you rented, saved and reviewed.")
            : t("recommend.ledeAnon", "Rent or save a few films and this shelf starts learning your taste.")}
        >
          <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
            {recommended.items.map((item, i) => (
              <MovieCard
                key={item.movie.id}
                movie={item.movie}
                index={i}
                note={item.becauseGenre ? `${t("recommend.because", "Because you like")} ${item.becauseGenre}` : null}
                onRent={rent}
                onOpen={(m, mode) => setDialog({ id: m.id, mode })}
                busy={rentingId === item.movie.id}
                saved={watchlist.has(item.movie.id)}
                onToggleSave={isSignedIn ? toggleSave : undefined}
              />
            ))}
          </div>
        </Section>
      ) : null}

      <Section
        title={t("home.title")}
        lede={t("home.lede")}
        className="scroll-mt-20"
      >
        <div id="catalogue" className="mb-3 grid scroll-mt-24 gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <SearchField
            className="sm:col-span-2 lg:col-span-1"
            value={search}
            onChange={setSearch}
            placeholder={t("home.searchPlaceholder")}
          />

          <Select value={genre} onChange={(e) => { setGenre(e.target.value); setPage(1); }} aria-label={t("home.genre")}>
            <option value="all">{t("home.everyGenre")}</option>
            {genres.map((g) => (
              <option key={g} value={g}>{g}</option>
            ))}
          </Select>

          <Select value={sortBy} onChange={(e) => { setSortBy(e.target.value); setPage(1); }} aria-label={t("home.sortBy")}>
            {SORTS.map((s) => (
              <option key={s.value} value={s.value}>{s.label}</option>
            ))}
          </Select>

          <div className="flex gap-2">
            <Button
              variant="outline"
              className="flex-1"
              onClick={() => setSortDir((d) => (d === "desc" ? "asc" : "desc"))}
            >
              {sortDir === "desc" ? t("common.descending") : t("common.ascending")}
            </Button>
            <Button
              variant={onlyAvailable ? "solid" : "outline"}
              className="flex-1"
              aria-pressed={onlyAvailable}
              onClick={() => { setOnlyAvailable((v) => !v); setPage(1); }}
            >
              {t("home.inStock")}
            </Button>
          </div>
        </div>

        <div className="mb-6 grid gap-3 sm:grid-cols-3">
          <Select
            value={yearFrom ?? ""}
            onChange={(e) => { setYearFrom(e.target.value ? Number(e.target.value) : undefined); setPage(1); }}
            aria-label={t("filter.yearFrom", "From year")}
          >
            <option value="">{t("filter.yearFrom", "From year")}</option>
            {[1950, 1970, 1980, 1990, 2000, 2010, 2015, 2020].map((y) => <option key={y} value={y}>{y}+</option>)}
          </Select>
          <Select
            value={yearTo ?? ""}
            onChange={(e) => { setYearTo(e.target.value ? Number(e.target.value) : undefined); setPage(1); }}
            aria-label={t("filter.yearTo", "Up to year")}
          >
            <option value="">{t("filter.yearTo", "Up to year")}</option>
            {[1980, 1990, 2000, 2010, 2015, 2020, yearNow].map((y) => <option key={y} value={y}>≤ {y}</option>)}
          </Select>
          <Select
            value={minRating ?? 0}
            onChange={(e) => { const v = Number(e.target.value); setMinRating(v > 0 ? v : undefined); setPage(1); }}
            aria-label={t("filter.minRating", "Minimum rating")}
          >
            {RATINGS.map((r) => (
              <option key={r} value={r}>{r === 0 ? t("filter.anyRating", "Any rating") : `★ ${r}+`}</option>
            ))}
          </Select>
        </div>

        {error ? <Notice tone="error">{error}</Notice> : null}

        {loading && !data ? <Spinner label={t("home.loading")} /> : null}

        {data && data.items.length === 0 ? (
          <Empty
            title={t("home.emptyTitle")}
            hint={t("home.emptyHint")}
            action={<Button variant="outline" onClick={clearFilters}>{t("common.clearFilters")}</Button>}
          />
        ) : null}

        {data && data.items.length > 0 ? (
          <>
            <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-4">
              {data.items.map((movie, i) => (
                <MovieCard
                  key={movie.id}
                  movie={movie}
                  index={i}
                  onRent={rent}
                  onOpen={(m, mode) => setDialog({ id: m.id, mode })}
                  busy={rentingId === movie.id}
                  saved={watchlist.has(movie.id)}
                  onToggleSave={isSignedIn ? toggleSave : undefined}
                />
              ))}
            </div>

            <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onChange={goToPage} />
          </>
        ) : null}
      </Section>
    </>
  );
}
