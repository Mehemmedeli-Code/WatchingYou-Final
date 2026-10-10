import { useCallback, useEffect, useState } from "react";
import { Download, X } from "lucide-react";
import { Modal } from "@/components/ui/modal";
import { Section, Panel, Notice, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Field, Textarea, Select } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { VectorBarChart, type ChartSeries } from "@/components/VectorChart";
import { useAuth } from "@/components/useAuth";
import { get, post, put, patch, del, query, download, ApiError, type Paged } from "@/lib/api";
import { BulkScheduleForm } from "@/components/admin/BulkScheduleForm";
import { PromoAdmin } from "@/components/admin/PromoAdmin";
import { formatDate, formatMoney } from "@/lib/format";
import { t, languageName, genreName } from "@/lib/i18n";
import { VideoPlayer } from "@/components/VideoPlayer";
import { UserAdmin } from "@/components/UserAdmin";
import { AuditTrail } from "@/components/AuditTrail";
import { statusTone, type ShortFilmDetail } from "@/lib/shorts";
import type { MovieListItem } from "@/components/MovieCard";
import type { MovieDetail } from "@/components/MovieDialog";
import { ProSubscribers } from "@/components/ProSubscribers";
import { askConfirm, askText } from "@/lib/dialog";

interface CinemaTotals {
  revenueThisMonth: number;
  ticketsThisMonth: number;
  upcomingScreenings: number;
  activePromoCodes: number;
}

interface RentalStats {
  activeCount: number;
  overdueCount: number;
  returnedCount: number;
  outstandingLateFees: number;
  revenueThisMonth: number;
}

interface OverdueRental {
  id: string;
  movieTitle: string;
  userId: string;
  dueAtUtc: string;
  lateFee: number;
  daysLate: number;
}

interface Venue {
  id: string;
  name: string;
  city: string;
  halls: { id: string; name: string; format?: string | null; rows: number; seatsPerRow: number }[];
}

interface Screening {
  id: string;
  movieId: string;
  movieTitle: string;
  hallId: string;
  venueName: string;
  hall: string;
  startsAtUtc: string;
  rows: number;
  seatsPerRow: number;
  seatPrice: number;
  audioLanguage: string;
  subtitleLanguage?: string | null;
  seatsSold: number;
  isCancelled: boolean;
  cancellationReason?: string | null;
  refundWindowHours?: number | null;
  refundFeePercent?: number | null;
  refundNote?: string | null;
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
  videoUrl: "",
  dailyPrice: 2.5,
  totalCopies: 3,
};

export default function AdminPage() {
  const { isAdmin, isSignedIn } = useAuth();
  const [stats, setStats] = useState<RentalStats | null>(null);
  const [cinemaTotals, setCinemaTotals] = useState<CinemaTotals | null>(null);
  const [charts, setCharts] = useState<ChartSeries[]>([]);
  const [movies, setMovies] = useState<Paged<MovieListItem> | null>(null);
  const [shorts, setShorts] = useState<ShortFilmDetail[]>([]);
  const [screenings, setScreenings] = useState<Screening[]>([]);
  const [venues, setVenues] = useState<Venue[]>([]);
  const [editing, setEditing] = useState<MovieListItem | null>(null);
  const [editingScreening, setEditingScreening] = useState<Screening | null>(null);
  const [overdue, setOverdue] = useState<OverdueRental[] | null>(null);
  const [showDeleted, setShowDeleted] = useState(false);
  const [draft, setDraft] = useState(BLANK);
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    if (!isAdmin) { setLoading(false); return; }
    setLoading(true);
    try {
      const [statsData, chartData, movieData, shortData, screeningData, venueData, overdueData] = await Promise.all([
        get<RentalStats>("/api/admin/rentals/stats"),
        get<ChartSeries[]>("/api/admin/analytics/overview"),
        get<Paged<MovieListItem>>("/api/movies" + query({ includeDeleted: showDeleted, pageSize: 24, sortBy: "title" })),
        get<ShortFilmDetail[]>("/api/admin/shorts/queue"),
        get<Screening[]>("/api/admin/screenings"),
        get<Venue[]>("/api/venues"),
        get<OverdueRental[]>("/api/admin/rentals/overdue").catch(() => []),
      ]);
      setCinemaTotals(await get<CinemaTotals>("/api/admin/analytics/cinema-totals").catch(() => null));
      setStats(statsData);
      setCharts(chartData);
      setMovies(movieData);
      setShorts(shortData);
      setScreenings(screeningData);
      setVenues(venueData);
      setOverdue(overdueData);
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
      <Section
        title={t("admin.whereTitle")}
        lede={t("admin.whereLede")}
        actions={
          <div className="flex flex-wrap gap-2">
            <Button
              size="sm"
              variant="outline"
              onClick={() => run(() => download("/api/admin/export/bookings.csv", "watchingyou-bookings.csv"), t("export.done", "Download started."))}
            >
              <Download size={14} aria-hidden />
              {t("export.bookings", "Bookings CSV")}
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() => run(() => download("/api/admin/export/rentals.csv", "watchingyou-rentals.csv"), t("export.done", "Download started."))}
            >
              <Download size={14} aria-hidden />
              {t("export.rentals", "Rentals CSV")}
            </Button>
          </div>
        }
      >
        {loading ? <Spinner label={t("common.loading")} /> : null}
        {message ? <div className="mb-4"><Notice tone={message.tone}>{message.text}</Notice></div> : null}

        {stats ? (
          <div className="mb-8 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {[
              { label: t("admin.tile.active"), value: String(stats.activeCount) },
              { label: t("admin.tile.overdue"), value: String(stats.overdueCount) },
              { label: t("admin.tile.returned"), value: String(stats.returnedCount) },
              { label: t("admin.tile.lateFees"), value: formatMoney(stats.outstandingLateFees) },
              { label: t("admin.tile.revenue"), value: formatMoney(stats.revenueThisMonth) },
              ...(cinemaTotals ? [
                { label: t("admin.tile.ticketRevenue", "Ticket revenue this month"), value: formatMoney(cinemaTotals.revenueThisMonth) },
                { label: t("admin.tile.tickets", "Tickets sold this month"), value: String(cinemaTotals.ticketsThisMonth) },
                { label: t("admin.tile.upcoming", "Upcoming screenings"), value: String(cinemaTotals.upcomingScreenings) },
              ] : []),
            ].map((tile) => (
              <Panel key={tile.label} className="py-4">
                <p className="font-display text-2xl text-accent">{tile.value}</p>
                <p className="mt-1 text-xs text-ink-mute">{tile.label}</p>
              </Panel>
            ))}
          </div>
        ) : null}

        <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
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
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_340px]">
          <div className="-mx-1 overflow-x-auto rounded-xl border border-line">
            <table className="w-full min-w-[34rem] text-left text-sm">
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
                      <p className="text-xs text-ink-mute">{genreName(movie.genre)} · {movie.releaseYear}</p>
                      {movie.isDeleted ? <Badge tone="bad" className="mt-1">{t("admin.removed")}</Badge> : null}
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-2">
                        <Input
                          type="number"
                          min={0}
                          defaultValue={movie.totalCopies}
                          className="h-8 w-20"
                          aria-label={t("admin.totalCopiesOf", "Total copies of {0}").replace("{0}", movie.title)}
                          onBlur={(e) => {
                            const next = Number(e.target.value);
                            if (next !== movie.totalCopies) {
                              void run(() => patch(`/api/admin/movies/${movie.id}/stock`, { totalCopies: next }), t("admin.stockUpdated", "Stock updated."));
                            }
                          }}
                        />
                        <span className="text-xs text-ink-mute">{movie.availableCopies} {t("admin.free")}</span>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-ink-mute">{formatMoney(movie.dailyPrice)}</td>
                    <td className="px-4 py-3 text-right">
                      {movie.isDeleted ? (
                        <Button size="sm" variant="outline" onClick={() => run(() => post(`/api/admin/movies/${movie.id}/restore`), t("admin.titleRestored", "Title restored."))}>
                          {t("common.restore")}
                        </Button>
                      ) : (
                        <div className="flex justify-end gap-2">
                          <Button size="sm" variant="outline" onClick={() => setEditing(movie)}>
                            {t("admin.edit")}
                          </Button>
                          <Button size="sm" variant="danger" onClick={() => run(() => del(`/api/admin/movies/${movie.id}`), t("admin.titleRemoved", "Title removed."))}>
                            {t("common.remove")}
                          </Button>
                        </div>
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
              <Field label={t("field.videoUrl")} hint={t("admin.videoHint", "YouTube, Vimeo or a direct .mp4")}><Input value={draft.videoUrl} onChange={(e) => setDraft({ ...draft, videoUrl: e.target.value })} /></Field>
              <Field label={t("field.trailerUrl")}><Input value={draft.trailerUrl} onChange={(e) => setDraft({ ...draft, trailerUrl: e.target.value })} /></Field>
              <Field label={t("field.description")}><Textarea value={draft.description} onChange={(e) => setDraft({ ...draft, description: e.target.value })} /></Field>

              <Button
                className="w-full"
                disabled={!draft.title}
                onClick={() => run(async () => { await post("/api/admin/movies", draft); setDraft(BLANK); }, t("admin.titleAdded", "Title added to the catalogue."))}
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

      {editing ? (
        <EditMovieDialog
          movie={editing}
          onClose={() => setEditing(null)}
          onSaved={async () => { setEditing(null); await load(); }}
        />
      ) : null}

      {editingScreening ? (
        <EditScreeningDialog
          screening={editingScreening}
          venues={venues}
          onClose={() => setEditingScreening(null)}
          onSaved={async () => { setEditingScreening(null); await load(); }}
        />
      ) : null}

      <Section title={t("admin.overdue")} lede={t("admin.overdueLede")}>
        <OverdueList rentals={overdue} />
      </Section>

      <Section title={t("admin.users")} lede={t("admin.usersLede")}>
        <UserAdmin />
      </Section>

      <Section title={t("proAdmin.title", "Watching PRO subscribers")} lede={t("proAdmin.lede", "Everyone who has paid for Watching PRO, when it ends and what they paid.")}>
        <ProSubscribers />
      </Section>

      <Section title={t("admin.audit")} lede={t("admin.auditLede")}>
        <AuditTrail />
      </Section>

      <Section
        title={t("admin.screenings")}
        lede={t("admin.screeningsLede")}
      >
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_340px]">
          <div className="-mx-1 overflow-x-auto rounded-xl border border-line">
            <table className="w-full min-w-[34rem] text-left text-sm">
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
                        {screening.venueName} · {screening.hall} · {formatDate(screening.startsAtUtc)}
                      </p>
                    </td>
                    <td className="px-4 py-3">
                      <Badge tone="warn">{languageName(screening.audioLanguage)}</Badge>
                      {screening.subtitleLanguage ? (
                        <Badge className="ml-1">{languageName(screening.subtitleLanguage)}</Badge>
                      ) : null}
                      {screening.isCancelled ? (
                        <Badge tone="bad" className="ml-1">{t("admin.cancelled")}</Badge>
                      ) : null}
                    </td>
                    <td className="px-4 py-3 text-ink-mute">
                      {screening.seatsSold}/{screening.rows * screening.seatsPerRow}
                    </td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex justify-end gap-2">
                        {/* Cancelling is not deleting: it makes every booking fully refundable
                            and keeps the record, which is what customers are owed. */}
                        {!screening.isCancelled ? (
                          <Button
                            size="sm"
                            variant="outline"
                            onClick={async () => {
                              const reason = await askText(t("admin.suspendReason"));
                              if (!reason) return;
                              void run(
                                () => post(`/api/admin/screenings/${screening.id}/cancel`, { reason }),
                                t("admin.cancelled"),
                              );
                            }}
                          >
                            {t("admin.cancelScreening")}
                          </Button>
                        ) : null}

                        <Button size="sm" variant="outline" onClick={() => setEditingScreening(screening)}>
                          {t("admin.edit")}
                        </Button>

                        <Button
                          size="sm"
                          variant="danger"
                          onClick={async () => {
                            if (!(await askConfirm(t("admin.deleteScreeningConfirm", "Delete this screening? This cannot be undone."), { danger: true }))) return;
                            run(() => del(`/api/admin/screenings/${screening.id}`), t("admin.screeningRemoved", "Screening removed."));
                          }}
                        >
                          {t("common.reject")}
                        </Button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="space-y-6">
            <NewScreeningForm
              movies={movies?.items ?? []}
              venues={venues}
              onSaved={() => run(async () => undefined, t("admin.screeningScheduled", "Screening scheduled."))}
            />
            <BulkScheduleForm movies={movies?.items ?? []} venues={venues} onSaved={load} />
          </div>
        </div>
      </Section>

      <Section title={t("promo.title", "Promo codes")} lede={t("promo.lede", "Discounts for cinema bookings. Each use is counted when a booking is confirmed.")}>
        <PromoAdmin />
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
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_1fr]">
        <div>
          <VideoPlayer src={film.streamUrl} title={film.title} />
          <p className="mt-3 font-display text-lg text-ink">{film.title}</p>
          <p className="text-xs text-ink-mute">
            {film.authorName} · {film.hoursLeft}h · {formatDate(film.reviewDeadlineUtc)}
          </p>
          {film.synopsis ? <p className="mt-2 text-sm text-ink-mute">{film.synopsis}</p> : null}
          <Badge tone={statusTone(film.status)} className="mt-2">{t(`status.${film.status.charAt(0).toLowerCase()}${film.status.slice(1)}`, film.status)}</Badge>
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
                      {t(`outcome.${check.outcome}`, check.outcome)}
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
  venues,
  onSaved,
}: {
  movies: MovieListItem[];
  venues: Venue[];
  onSaved: () => Promise<void> | void;
}) {
  const [movieId, setMovieId] = useState("");
  const [hallId, setHallId] = useState("");
  const [startsAt, setStartsAt] = useState("");
  const [audio, setAudio] = useState("az");
  const [subtitles, setSubtitles] = useState("");
  const [price, setPrice] = useState(9);
  const [refundWindow, setRefundWindow] = useState("");
  const [refundFee, setRefundFee] = useState("");
  const [refundNote, setRefundNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    setBusy(true);
    setError(null);
    try {
      await post("/api/admin/screenings", {
        movieId,
        hallId,
        startsAtUtc: new Date(startsAt).toISOString(),
        seatPrice: price,
        audioLanguage: audio,
        subtitleLanguage: subtitles || null,
        refundWindowHours: refundWindow === "" ? null : Number(refundWindow),
        refundFeePercent: refundFee === "" ? null : Number(refundFee),
        refundNote: refundNote || null,
      });
      setStartsAt("");
      await onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("admin.screeningNotSaved", "The screening was not saved."));
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

        <Field label={t("admin.hall")} hint={t("admin.hallHint")}>
          <Select value={hallId} onChange={(e) => setHallId(e.target.value)}>
            <option value="">—</option>
            {venues.map((venue) => (
              <optgroup key={venue.id} label={venue.name}>
                {venue.halls.map((h) => (
                  <option key={h.id} value={h.id}>
                    {h.name}{h.format ? ` · ${h.format}` : ""} · {h.rows * h.seatsPerRow}
                  </option>
                ))}
              </optgroup>
            ))}
          </Select>
        </Field>

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

        {/* Blank means the site defaults — 48 hours and 30%. Filled in, this performance
            carries its own rule, which is why each one shows a Rules section. */}
        <div className="grid grid-cols-2 gap-3">
          <Field label={t("admin.refundWindow")} hint="48">
            <Input
              type="number"
              value={refundWindow}
              onChange={(e) => setRefundWindow(e.target.value)}
              placeholder="48"
            />
          </Field>
          <Field label={t("admin.refundFee")} hint="30">
            <Input
              type="number"
              value={refundFee}
              onChange={(e) => setRefundFee(e.target.value)}
              placeholder="30"
            />
          </Field>
        </div>

        <Field label={t("admin.refundNote")}>
          <Input value={refundNote} onChange={(e) => setRefundNote(e.target.value)} />
        </Field>

        {error ? <Notice tone="error">{error}</Notice> : null}

        <Button className="w-full" disabled={!movieId || !hallId || !startsAt || busy} onClick={save}>
          {busy ? t("common.loading") : t("common.save")}
        </Button>
      </div>
    </Panel>
  );
}

/**
 * Editing a title. `PUT /api/admin/movies/{id}` existed from the start but nothing on the site
 * called it, so changing a synopsis or adding a video link meant opening Swagger.
 *
 * Stock is not here: it has its own endpoint because it is the one field that changes while
 * copies are out on loan, and it must not be overwritten by a form somebody opened ten minutes
 * ago.
 */
function EditMovieDialog({
  movie,
  onClose,
  onSaved,
}: {
  movie: MovieListItem;
  onClose: () => void;
  onSaved: () => void | Promise<void>;
}) {
  const [detail, setDetail] = useState<MovieDetail | null>(null);
  const [draft, setDraft] = useState<Record<string, string | number>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // The public detail no longer carries the video address (it is for renters and PRO
    // members only), so the editor asks /watch for it — as an admin it is always allowed.
    // Without this, saving the form would blank the film's video.
    Promise.all([
      get<MovieDetail>(`/api/movies/${movie.id}`),
      // No fallback on failure: better an error than a form that would save an empty video.
      get<{ videoUrl?: string | null }>(`/api/movies/${movie.id}/watch`),
    ])
      .then(([detailFound, watch]) => {
        const found = { ...detailFound, videoUrl: watch.videoUrl ?? null };
        setDetail(found);
        setDraft({
          title: found.title,
          description: found.description,
          genre: found.genre,
          releaseYear: found.releaseYear,
          durationMinutes: found.durationMinutes,
          director: found.director ?? "",
          posterUrl: found.posterUrl ?? "",
          trailerUrl: found.trailerUrl ?? "",
          videoUrl: found.videoUrl ?? "",
          dailyPrice: found.dailyPrice,
        });
      })
      .catch(() => setError(t("error.action")));
  }, [movie.id]);

  async function save() {
    setBusy(true);
    setError(null);
    try {
      await put(`/api/admin/movies/${movie.id}`, draft);
      await onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("error.action"));
    } finally {
      setBusy(false);
    }
  }

  const field = (key: string, label: string, type: "text" | "number" = "text") => (
    <Field label={label}>
      <Input
        type={type}
        value={String(draft[key] ?? "")}
        onChange={(e) => setDraft((d) => ({ ...d, [key]: type === "number" ? Number(e.target.value) : e.target.value }))}
      />
    </Field>
  );

  return (
    <Modal onClose={onClose} width="max-w-lg">
        <Panel>
          <div className="flex items-start justify-between gap-3">
            <p className="font-display text-xl text-ink">{t("admin.editMovie")}</p>
            <button onClick={onClose} aria-label={t("common.cancel")} className="text-ink-mute hover:text-ink">
              <X size={18} aria-hidden />
            </button>
          </div>

          {!detail ? (
            <div className="mt-4"><Spinner label={t("common.loading")} /></div>
          ) : (
            <div className="mt-4 space-y-3">
              {field("title", t("admin.title"))}
              <Field label={t("admin.description")}>
                <Textarea
                  rows={3}
                  value={String(draft.description ?? "")}
                  onChange={(e) => setDraft((d) => ({ ...d, description: e.target.value }))}
                />
              </Field>
              <div className="grid grid-cols-2 gap-3">
                {field("genre", t("admin.genre"))}
                {field("releaseYear", t("admin.year"), "number")}
                {field("durationMinutes", t("admin.duration"), "number")}
                {field("dailyPrice", t("admin.price"), "number")}
              </div>
              {field("director", t("admin.director"))}
              {field("posterUrl", t("admin.poster"))}
              {field("trailerUrl", t("admin.trailer"))}
              {field("videoUrl", t("admin.video"))}

              {error ? <Notice tone="error">{error}</Notice> : null}

              <div className="flex gap-2">
                <Button size="sm" disabled={busy} onClick={save}>
                  {busy ? t("common.loading") : t("common.save")}
                </Button>
                <Button size="sm" variant="outline" onClick={onClose}>{t("common.cancel")}</Button>
              </div>
            </div>
          )}
        </Panel>
    </Modal>
  );
}

/** Moving a screening, changing its price or its refund rule. */
function EditScreeningDialog({
  screening,
  venues,
  onClose,
  onSaved,
}: {
  screening: Screening;
  venues: Venue[];
  onClose: () => void;
  onSaved: () => void | Promise<void>;
}) {
  // datetime-local wants local wall time; toISOString() alone gives UTC, which then got read
  // back as local on save and moved the screening by the UTC offset on every edit.
  const local = (iso: string) => {
    const d = new Date(iso);
    return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
  };

  const [hallId, setHallId] = useState(screening.hallId);
  const [startsAt, setStartsAt] = useState(local(screening.startsAtUtc));
  const [price, setPrice] = useState(screening.seatPrice);
  const [audio, setAudio] = useState(screening.audioLanguage);
  const [subtitles, setSubtitles] = useState(screening.subtitleLanguage ?? "");
  const [refundWindow, setRefundWindow] = useState(screening.refundWindowHours?.toString() ?? "");
  const [refundFee, setRefundFee] = useState(screening.refundFeePercent?.toString() ?? "");
  const [refundNote, setRefundNote] = useState(screening.refundNote ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    setBusy(true);
    setError(null);
    try {
      await put(`/api/admin/screenings/${screening.id}`, {
        movieId: screening.movieId,
        hallId,
        startsAtUtc: new Date(startsAt).toISOString(),
        seatPrice: price,
        audioLanguage: audio,
        subtitleLanguage: subtitles || null,
        refundWindowHours: refundWindow === "" ? null : Number(refundWindow),
        refundFeePercent: refundFee === "" ? null : Number(refundFee),
        refundNote: refundNote || null,
      });
      await onSaved();
    } catch (err) {
      // The server refuses a hall change once seats are sold; its message says so.
      setError(err instanceof ApiError ? err.message : t("error.action"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal onClose={onClose} width="max-w-md">
        <Panel>
          <div className="flex items-start justify-between gap-3">
            <div>
              <p className="font-display text-xl text-ink">{t("admin.editScreening")}</p>
              <p className="mt-1 text-xs text-ink-mute">{screening.movieTitle}</p>
            </div>
            <button onClick={onClose} aria-label={t("common.cancel")} className="text-ink-mute hover:text-ink">
              <X size={18} aria-hidden />
            </button>
          </div>

          <div className="mt-4 space-y-3">
            <Field label={t("admin.hall")} hint={t("admin.hallHint")}>
              <Select value={hallId} onChange={(e) => setHallId(e.target.value)}>
                {venues.map((venue) => (
                  <optgroup key={venue.id} label={venue.name}>
                    {venue.halls.map((h) => (
                      <option key={h.id} value={h.id}>{h.name}</option>
                    ))}
                  </optgroup>
                ))}
              </Select>
            </Field>

            <Field label={t("admin.startsAt")}>
              <Input type="datetime-local" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
            </Field>

            <div className="grid grid-cols-2 gap-3">
              <Field label={t("admin.seatPrice")}>
                <Input type="number" step="0.5" value={price} onChange={(e) => setPrice(Number(e.target.value))} />
              </Field>
              <Field label={t("admin.audio")}>
                <Input value={audio} onChange={(e) => setAudio(e.target.value)} />
              </Field>
              <Field label={t("admin.subtitles")}>
                <Input value={subtitles} onChange={(e) => setSubtitles(e.target.value)} />
              </Field>
              <Field label={t("admin.refundWindow")} hint="48">
                <Input type="number" value={refundWindow} onChange={(e) => setRefundWindow(e.target.value)} />
              </Field>
              <Field label={t("admin.refundFee")} hint="30">
                <Input type="number" value={refundFee} onChange={(e) => setRefundFee(e.target.value)} />
              </Field>
            </div>

            <Field label={t("admin.refundNote")}>
              <Input value={refundNote} onChange={(e) => setRefundNote(e.target.value)} />
            </Field>

            {error ? <Notice tone="error">{error}</Notice> : null}

            <div className="flex gap-2">
              <Button size="sm" disabled={busy} onClick={save}>
                {busy ? t("common.loading") : t("common.save")}
              </Button>
              <Button size="sm" variant="outline" onClick={onClose}>{t("common.cancel")}</Button>
            </div>
          </div>
        </Panel>
    </Modal>
  );
}

/** Who has not brought a film back, and what it has cost them so far. */
function OverdueList({ rentals }: { rentals: OverdueRental[] | null }) {
  if (rentals === null) return <Spinner label={t("common.loading")} />;
  if (rentals.length === 0) return <Empty title={t("admin.noOverdue")} hint="" />;

  return (
    <div className="-mx-1 overflow-x-auto rounded-xl border border-line">
      <table className="w-full min-w-[34rem] text-left text-sm">
        <thead className="border-b border-line text-xs uppercase tracking-wide text-ink-mute">
          <tr>
            <th className="px-3 py-2">{t("admin.title")}</th>
            <th className="px-3 py-2">{t("rentals.due")}</th>
            <th className="px-3 py-2">{t("rentals.lateFee")}</th>
          </tr>
        </thead>
        <tbody>
          {rentals.map((rental) => (
            <tr key={rental.id} className="border-b border-line/60 last:border-0">
              <td className="px-3 py-2 text-ink">{rental.movieTitle}</td>
              <td className="px-3 py-2 text-ink-mute">{formatDate(rental.dueAtUtc)}</td>
              <td className="px-3 py-2 text-bad">{formatMoney(rental.lateFee)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
