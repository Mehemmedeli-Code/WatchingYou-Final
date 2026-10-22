import { useCallback, useEffect, useState } from "react";
import { Section, Panel, Notice, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Field, Textarea, Select } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { VectorBarChart, type ChartSeries } from "@/components/VectorChart";
import { useAuth } from "@/components/useAuth";
import { get, post, put, patch, del, query, ApiError, type Paged } from "@/lib/api";
import { formatDate, formatMoney } from "@/lib/format";
import { t, languageName } from "@/lib/i18n";
import { VideoPlayer } from "@/components/VideoPlayer";
import { statusTone, type ShortFilmDetail } from "@/lib/shorts";
import type { MovieListItem } from "@/components/MovieCard";

interface RentalStats {
  activeCount: number;
  overdueCount: number;
  returnedCount: number;
  outstandingLateFees: number;
  revenueThisMonth: number;
}

interface Screening {
  id: string;
  movieId: string;
  movieTitle: string;
  hall: string;
  startsAtUtc: string;
  rows: number;
  seatsPerRow: number;
  seatPrice: number;
  audioLanguage: string;
  subtitleLanguage?: string | null;
  seatsSold: number;
}

const BLANK = {
  title: "",
  description: "",
  genre: "Drama",
  releaseYear: new Date().getFullYear(),
  durationMinutes: 100,
  director: "",
  posterUrl: "",
  trailerUrl: "",
  dailyPrice: 2.5,
  totalCopies: 3,
};

export default function AdminPage() {
  const { isAdmin, isSignedIn } = useAuth();
  const [stats, setStats] = useState<RentalStats | null>(null);
  const [charts, setCharts] = useState<ChartSeries[]>([]);
  const [movies, setMovies] = useState<Paged<MovieListItem> | null>(null);
  const [shorts, setShorts] = useState<ShortFilmDetail[]>([]);
  const [screenings, setScreenings] = useState<Screening[]>([]);
  const [showDeleted, setShowDeleted] = useState(false);
  const [draft, setDraft] = useState(BLANK);
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    if (!isAdmin) { setLoading(false); return; }
    setLoading(true);
    try {
      const [statsData, chartData, movieData, shortData, screeningData] = await Promise.all([
        get<RentalStats>("/api/admin/rentals/stats"),
        get<ChartSeries[]>("/api/admin/analytics/overview"),
        get<Paged<MovieListItem>>("/api/movies" + query({ includeDeleted: showDeleted, pageSize: 24, sortBy: "title" })),
        get<ShortFilmDetail[]>("/api/admin/shorts/queue"),
        get<Screening[]>("/api/admin/screenings"),
      ]);
      setStats(statsData);
      setCharts(chartData);
      setMovies(movieData);
      setShorts(shortData);
      setScreenings(screeningData);
    } catch (err) {
      setMessage({ tone: "error", text: err instanceof ApiError ? err.message : t("error.dashboard") });
    } finally {
      setLoading(false);
    }
  }, [isAdmin, showDeleted]);

  useEffect(() => { void load(); }, [load]);

  async function run(action: () => Promise<unknown>, successText: string) {
    setMessage(null);
    try {
      await action();
      setMessage({ tone: "ok", text: successText });
      await load();
    } catch (err) {
      setMessage({ tone: "error", text: err instanceof ApiError ? err.message : t("error.action") });
    }
  }

  if (!isSignedIn || !isAdmin) {
    return (
      <Section title={t("nav.admin")}>
        <Empty
          title={t("admin.accessTitle")}
          hint="admin@reelandrow.test / Admin1234"
          action={<a href="/account"><Button>{t("nav.signIn")}</Button></a>}
        />
      </Section>
    );
  }

  return (
    <>
      <Section title={t("admin.whereTitle")} lede={t("admin.whereLede")}>
        {loading ? <Spinner label={t("common.loading")} /> : null}
        {message ? <div className="mb-4"><Notice tone={message.tone}>{message.text}</Notice></div> : null}

        {stats ? (
          <div className="mb-8 grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
            {[
              { label: t("admin.tile.active"), value: String(stats.activeCount) },
              { label: t("admin.tile.overdue"), value: String(stats.overdueCount) },
              { label: t("admin.tile.returned"), value: String(stats.returnedCount) },
              { label: t("admin.tile.lateFees"), value: formatMoney(stats.outstandingLateFees) },
              { label: t("admin.tile.revenue"), value: formatMoney(stats.revenueThisMonth) },
            ].map((tile) => (
              <Panel key={tile.label} className="py-4">
                <p className="font-display text-2xl text-accent">{tile.value}</p>
                <p className="mt-1 text-xs text-ink-mute">{tile.label}</p>
              </Panel>
            ))}
          </div>
        ) : null}

        <div className="grid gap-4 lg:grid-cols-3">
          {charts.map((series) => (
            <VectorBarChart key={series.key} series={series} />
          ))}
        </div>
      </Section>

      <Section
        title={t("admin.inventory")}
        lede={t("admin.inventoryLede")}
        actions={
          <Button variant="outline" size="sm" aria-pressed={showDeleted} onClick={() => setShowDeleted((v) => !v)}>
            {showDeleted ? t("admin.hideRemoved") : t("admin.showRemoved")}
          </Button>
        }
      >
        <div className="grid gap-6 lg:grid-cols-[1fr_340px]">
          <div className="overflow-x-auto rounded-xl border border-line">
            <table className="w-full text-left text-sm">
              <thead className="bg-surface-raised text-xs text-ink-mute">
                <tr>
                  <th className="px-4 py-3 font-medium">{t("admin.col.title")}</th>
                  <th className="px-4 py-3 font-medium">{t("admin.col.stock")}</th>
                  <th className="px-4 py-3 font-medium">{t("admin.col.price")}</th>
                  <th className="px-4 py-3 font-medium"></th>
                </tr>
              </thead>
              <tbody>
                {movies?.items.map((movie) => (
                  <tr key={movie.id} className="border-t border-line">
                    <td className="px-4 py-3">
                      <p className="text-ink">{movie.title}</p>
                      <p className="text-xs text-ink-mute">{movie.genre} · {movie.releaseYear}</p>
                      {movie.isDeleted ? <Badge tone="bad" className="mt-1">{t("admin.removed")}</Badge> : null}
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-2">
                        <Input
                          type="number"
                          min={0}
                          defaultValue={movie.totalCopies}
                          className="h-8 w-20"
                          aria-label={`Total copies of ${movie.title}`}
                          onBlur={(e) => {
                            const next = Number(e.target.value);
                            if (next !== movie.totalCopies) {
                              void run(() => patch(`/api/admin/movies/${movie.id}/stock`, { totalCopies: next }), "Stock updated.");
                            }
                          }}
                        />
                        <span className="text-xs text-ink-mute">{movie.availableCopies} {t("admin.free")}</span>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-ink-mute">{formatMoney(movie.dailyPrice)}</td>
                    <td className="px-4 py-3 text-right">
                      {movie.isDeleted ? (
                        <Button size="sm" variant="outline" onClick={() => run(() => post(`/api/admin/movies/${movie.id}/restore`), "Title restored.")}>
                          {t("common.restore")}
                        </Button>
                      ) : (
                        <Button size="sm" variant="danger" onClick={() => run(() => del(`/api/admin/movies/${movie.id}`), "Title removed.")}>
                          {t("common.remove")}
                        </Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Panel>
            <h3 className="font-display text-lg text-ink">{t("admin.addTitle")}</h3>
            <div className="mt-4 space-y-3">
              <Field label={t("admin.col.title")}><Input value={draft.title} onChange={(e) => setDraft({ ...draft, title: e.target.value })} /></Field>
              <div className="grid grid-cols-2 gap-3">
                <Field label={t("home.genre")}><Input value={draft.genre} onChange={(e) => setDraft({ ...draft, genre: e.target.value })} /></Field>
                <Field label={t("field.year")}><Input type="number" value={draft.releaseYear} onChange={(e) => setDraft({ ...draft, releaseYear: Number(e.target.value) })} /></Field>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <Field label={t("field.minutes")}><Input type="number" value={draft.durationMinutes} onChange={(e) => setDraft({ ...draft, durationMinutes: Number(e.target.value) })} /></Field>
                <Field label={t("field.copies")}><Input type="number" value={draft.totalCopies} onChange={(e) => setDraft({ ...draft, totalCopies: Number(e.target.value) })} /></Field>
              </div>
              <Field label={t("sort.price")}><Input type="number" step="0.25" value={draft.dailyPrice} onChange={(e) => setDraft({ ...draft, dailyPrice: Number(e.target.value) })} /></Field>
              <Field label={t("field.posterUrl")}><Input value={draft.posterUrl} onChange={(e) => setDraft({ ...draft, posterUrl: e.target.value })} /></Field>
              <Field label={t("field.description")}><Textarea value={draft.description} onChange={(e) => setDraft({ ...draft, description: e.target.value })} /></Field>

              <Button
                className="w-full"
                disabled={!draft.title}
                onClick={() => run(async () => { await post("/api/admin/movies", draft); setDraft(BLANK); }, "Title added to the catalogue.")}
              >
                {t("admin.addToCatalogue")}
              </Button>
            </div>
          </Panel>
        </div>
      </Section>

      <Section
        title={t("admin.queue")}
        lede={t("admin.queueLede")}
      >
        {shorts.length === 0 ? (
          <Empty title={t("security.empty")} hint={t("admin.queueEmpty")} />
        ) : (
          <div className="space-y-4">
            {shorts.map((entry) => (
              <ShortDecisionCard key={entry.film.id} entry={entry} onDecided={load} onError={setMessage} />
            ))}
          </div>
        )}
      </Section>

      <Section
        title={t("admin.screenings")}
        lede={t("admin.screeningsLede")}
      >
        <div className="grid gap-6 lg:grid-cols-[1fr_340px]">
          <div className="overflow-x-auto rounded-xl border border-line">
            <table className="w-full text-left text-sm">
              <thead className="bg-surface-raised text-xs text-ink-mute">
                <tr>
                  <th className="px-4 py-3 font-medium">{t("admin.film")}</th>
                  <th className="px-4 py-3 font-medium">{t("onDisplay.language")}</th>
                  <th className="px-4 py-3 font-medium">{t("admin.col.seats")}</th>
                  <th className="px-4 py-3 font-medium"></th>
                </tr>
              </thead>
              <tbody>
                {screenings.map((screening) => (
                  <tr key={screening.id} className="border-t border-line">
                    <td className="px-4 py-3">
                      <p className="text-ink">{screening.movieTitle}</p>
                      <p className="text-xs text-ink-mute">
                        {screening.hall} · {formatDate(screening.startsAtUtc)}
                      </p>
                    </td>
                    <td className="px-4 py-3">
                      <Badge tone="warn">{languageName(screening.audioLanguage)}</Badge>
                      {screening.subtitleLanguage ? (
                        <Badge className="ml-1">{languageName(screening.subtitleLanguage)}</Badge>
                      ) : null}
                    </td>
                    <td className="px-4 py-3 text-ink-mute">
                      {screening.seatsSold}/{screening.rows * screening.seatsPerRow}
                    </td>
                    <td className="px-4 py-3 text-right">
                      <Button
                        size="sm"
                        variant="danger"
                        onClick={() => run(() => del(`/api/admin/screenings/${screening.id}`), "Screening removed.")}
                      >
                        {t("common.reject")}
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <NewScreeningForm
            movies={movies?.items ?? []}
            onSaved={() => run(async () => undefined, "Screening scheduled.")}
          />
        </div>
      </Section>

    </>
  );
}

/**
 * The final decision, made against the stored inspection rather than a title and a hunch.
 * Approving something Security flagged is allowed but never silent — the server rejects it
 * without a written reason, and the reason travels to the uploader in the e-mail.
 */
function ShortDecisionCard({
  entry,
  onDecided,
  onError,
}: {
  entry: ShortFilmDetail;
  onDecided: () => Promise<void> | void;
  onError: (message: { tone: "ok" | "error"; text: string }) => void;
}) {
  const { film, report } = entry;
  const [note, setNote] = useState("");
  const [origin, setOrigin] = useState(film.origin);
  const [busy, setBusy] = useState(false);

  const flagged = report?.verdict === "Flagged";

  async function decide(approve: boolean) {
    setBusy(true);
    try {
      await put(`/api/admin/shorts/${film.id}/decision`, {
        approve,
        note: note || null,
        correctOriginTo: origin === film.origin ? null : origin,
      });
      await onDecided();
    } catch (err) {
      onError({ tone: "error", text: err instanceof ApiError ? err.message : t("error.action") });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Panel>
      <div className="grid gap-6 lg:grid-cols-[1fr_1fr]">
        <div>
          <VideoPlayer src={film.streamUrl} title={film.title} />
          <p className="mt-3 font-display text-lg text-ink">{film.title}</p>
          <p className="text-xs text-ink-mute">
            {film.authorName} · {film.hoursLeft}h · {formatDate(film.reviewDeadlineUtc)}
          </p>
          {film.synopsis ? <p className="mt-2 text-sm text-ink-mute">{film.synopsis}</p> : null}
          <Badge tone={statusTone(film.status)} className="mt-2">{film.status}</Badge>
        </div>

        <div>
          <h4 className="font-display text-lg text-ink">{t("studio.report")}</h4>
          {report ? (
            <>
              <p className="mt-1 text-xs text-ink-mute">
                {report.reviewerName} · {formatDate(report.completedAtUtc)}
              </p>
              {report.summary ? <p className="mt-2 text-sm text-ink">{report.summary}</p> : null}

              <ul className="mt-3 space-y-1">
                {report.checks.map((check) => (
                  <li key={check.check} className="flex items-start justify-between gap-3 text-sm">
                    <span className="text-ink-mute">
                      {t(`check.${check.check}`, check.check)}
                      {check.note ? <em className="block text-xs">{check.note}</em> : null}
                    </span>
                    <Badge tone={check.outcome === "Fail" ? "bad" : check.outcome === "Pass" ? "good" : undefined}>
                      {check.outcome}
                    </Badge>
                  </li>
                ))}
              </ul>
            </>
          ) : (
            <p className="mt-2 text-sm text-ink-mute">{t("studio.noReport")}</p>
          )}

          <div className="mt-4 space-y-3">
            <Field label={t("studio.origin")}>
              <Select value={origin} onChange={(e) => setOrigin(e.target.value as typeof origin)}>
                <option value="HandCrafted">{t("studio.originHuman")}</option>
                <option value="AiGenerated">{t("studio.originAi")}</option>
              </Select>
            </Field>

            <Field label={t("admin.noteToUploader")} hint={flagged ? t("admin.overrideNote") : undefined}>
              <Textarea value={note} onChange={(e) => setNote(e.target.value)} />
            </Field>

            <div className="flex gap-2">
              <Button size="sm" disabled={busy} onClick={() => decide(true)}>{t("common.approve")}</Button>
              <Button size="sm" variant="danger" disabled={busy} onClick={() => decide(false)}>
                {t("common.reject")}
              </Button>
            </div>
          </div>
        </div>
      </div>
    </Panel>
  );
}

/** Screenings are what Movies on Display reads, so this is where that page gets its content. */
function NewScreeningForm({
  movies,
  onSaved,
}: {
  movies: MovieListItem[];
  onSaved: () => Promise<void> | void;
}) {
  const [movieId, setMovieId] = useState("");
  const [hall, setHall] = useState("Hall A");
  const [startsAt, setStartsAt] = useState("");
  const [audio, setAudio] = useState("az");
  const [subtitles, setSubtitles] = useState("");
  const [price, setPrice] = useState(9);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    setBusy(true);
    setError(null);
    try {
      await post("/api/admin/screenings", {
        movieId,
        hall,
        startsAtUtc: new Date(startsAt).toISOString(),
        rows: 8,
        seatsPerRow: 12,
        seatPrice: price,
        audioLanguage: audio,
        subtitleLanguage: subtitles || null,
      });
      setStartsAt("");
      await onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "The screening was not saved.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Panel>
      <h3 className="font-display text-lg text-ink">{t("admin.scheduleScreening")}</h3>
      <div className="mt-4 space-y-3">
        <Field label={t("admin.film")}>
          <Select value={movieId} onChange={(e) => setMovieId(e.target.value)}>
            <option value="">—</option>
            {movies.map((movie) => (
              <option key={movie.id} value={movie.id}>{movie.title}</option>
            ))}
          </Select>
        </Field>

        <Field label={t("admin.hall")}><Input value={hall} onChange={(e) => setHall(e.target.value)} /></Field>

        <Field label={t("admin.startsAt")}>
          <Input type="datetime-local" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
        </Field>

        <div className="grid grid-cols-2 gap-3">
          <Field label={t("onDisplay.language")}>
            <Select value={audio} onChange={(e) => setAudio(e.target.value)}>
              {["az", "en", "ru", "tr"].map((code) => (
                <option key={code} value={code}>{languageName(code)}</option>
              ))}
            </Select>
          </Field>
          <Field label={t("onDisplay.subtitles")}>
            <Select value={subtitles} onChange={(e) => setSubtitles(e.target.value)}>
              <option value="">—</option>
              {["az", "en", "ru", "tr"].map((code) => (
                <option key={code} value={code}>{languageName(code)}</option>
              ))}
            </Select>
          </Field>
        </div>

        <Field label={t("admin.seatPrice")}>
          <Input type="number" step="0.5" value={price} onChange={(e) => setPrice(Number(e.target.value))} />
        </Field>

        {error ? <Notice tone="error">{error}</Notice> : null}

        <Button className="w-full" disabled={!movieId || !startsAt || busy} onClick={save}>
          {busy ? t("common.loading") : t("common.save")}
        </Button>
      </div>
    </Panel>
  );
}
