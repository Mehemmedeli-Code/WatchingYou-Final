import { useCallback, useEffect, useMemo, useState } from "react";
import StackSpread from "@/components/ui/stack-spread";
import { MovieCarousel } from "@/components/ui/movie-carousel";
import { MovieCard, type MovieListItem } from "@/components/MovieCard";
import { SearchField } from "@/components/SearchField";
import { Section, Notice, Empty, Spinner } from "@/components/Shell";
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

export default function HomePage() {
  const { isSignedIn } = useAuth();
  const [genres, setGenres] = useState<string[]>([]);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  const [genre, setGenre] = useState("all");
  const [sortBy, setSortBy] = useState("newest");
  const [sortDir, setSortDir] = useState("desc");
  const [onlyAvailable, setOnlyAvailable] = useState(false);

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

  // Debounce keeps the catalogue responsive without a request per keystroke.
  useEffect(() => {
    const handle = setTimeout(() => {
      setDebounced(search);
      setPage(1);
    }, 300);
    return () => clearTimeout(handle);
  }, [search]);

  useEffect(() => {
    get<string[]>("/api/movies/genres").then(setGenres).catch(() => setGenres([]));
  }, []);

  const url = useMemo(
    () =>
      "/api/movies" +
      query({ search: debounced, genre, sortBy, sortDir, onlyAvailable, page, pageSize: 12 }),
    [debounced, genre, sortBy, sortDir, onlyAvailable, page],
  );

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

  async function rent(movie: MovieListItem) {
    if (!isSignedIn) {
      window.location.href = "/account";
      return;
    }

    setRentingId(movie.id);
    try {
      await post("/api/rentals", { movieId: movie.id, days: 7 });
      setToast({ id: Date.now(), tone: "ok", text: `${movie.title} — ${t("movie.rented")}` });
      await Promise.all([load(), refreshFeatured()]);
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

      <StackSpread
        headline={t("home.hero.a")}
        headlineMuted={t("home.hero.b")}
        headlineTail={t("home.hero.c")}
        subtitle={t("home.hero.subtitle")}
      >
        <a href="#catalogue" className="inline-flex h-11 items-center rounded-full bg-accent px-6 text-sm font-semibold text-surface">
          Browse the catalogue
        </a>
      </StackSpread>

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

      <Section
        title={t("home.title")}
        lede={t("home.lede")}
        className="scroll-mt-20"
      >
        <div id="catalogue" className="mb-6 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
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

        {error ? <Notice tone="error">{error}</Notice> : null}

        {loading && !data ? <Spinner label={t("home.loading")} /> : null}

        {data && data.items.length === 0 ? (
          <Empty
            title={t("home.emptyTitle")}
            hint={t("home.emptyHint")}
            action={<Button variant="outline" onClick={() => { setSearch(""); setGenre("all"); setOnlyAvailable(false); }}>{t("common.clearFilters")}</Button>}
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
                />
              ))}
            </div>

            <nav className="mt-8 flex items-center justify-between gap-4" aria-label={t("common.pages")}>
              <Button variant="outline" size="sm" disabled={!data.hasPrevious} onClick={() => setPage((p) => p - 1)}>
                Previous
              </Button>
              <p className="text-sm text-ink-mute">
                Page {data.page} of {data.totalPages} · {data.totalCount} titles
              </p>
              <Button variant="outline" size="sm" disabled={!data.hasNext} onClick={() => setPage((p) => p + 1)}>
                Next
              </Button>
            </nav>
          </>
        ) : null}
      </Section>
    </>
  );
}
