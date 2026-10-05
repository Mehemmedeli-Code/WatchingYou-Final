import { useState } from "react";
import { CalendarRange, Plus, X } from "lucide-react";
import { Panel, Notice } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Field, Select } from "@/components/ui/input";
import { post, ApiError } from "@/lib/api";
import { t, languageName } from "@/lib/i18n";

export interface HallOption { id: string; name: string; format?: string | null; rows: number; seatsPerRow: number }
export interface VenueOption { id: string; name: string; halls: HallOption[] }
interface FilmOption { id: string; title: string }

interface BulkScheduleResult { created: number; skipped: string[] }

const WEEKDAYS = [1, 2, 3, 4, 5, 6, 0];   // Monday first, the way a Baku calendar reads
const weekdayName = (day: number) =>
  new Intl.DateTimeFormat(document.documentElement.lang || "az", { weekday: "short" })
    .format(new Date(Date.UTC(2024, 0, 7 + day)));   // 7 Jan 2024 was a Sunday

const today = () => new Date(Date.now() + 4 * 3600_000).toISOString().slice(0, 10);

/**
 * A run of screenings in one go: one film, one hall, several days, several start times.
 * Times are Baku time. Slots that clash with something already in the hall are skipped and
 * listed, rather than failing the whole batch.
 */
export function BulkScheduleForm({
  movies,
  venues,
  onSaved,
}: {
  movies: FilmOption[];
  venues: VenueOption[];
  onSaved: () => Promise<void> | void;
}) {
  const [movieId, setMovieId] = useState("");
  const [hallId, setHallId] = useState("");
  const [firstDate, setFirstDate] = useState(today());
  const [days, setDays] = useState(7);
  const [times, setTimes] = useState<string[]>(["16:00", "19:30"]);
  const [newTime, setNewTime] = useState("22:00");
  const [weekdays, setWeekdays] = useState<number[]>([]);
  const [price, setPrice] = useState(9);
  const [audio, setAudio] = useState("az");
  const [subtitles, setSubtitles] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<BulkScheduleResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const perWeek = weekdays.length === 0 ? 7 : weekdays.length;
  const estimate = Math.round((days / 7) * perWeek) * times.length;

  async function save() {
    setBusy(true);
    setError(null);
    setResult(null);
    try {
      const outcome = await post<BulkScheduleResult>("/api/admin/screenings/bulk", {
        movieId, hallId, firstDate, days, times, weekdays,
        seatPrice: price, audioLanguage: audio, subtitleLanguage: subtitles || null,
      });
      setResult(outcome);
      await onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("error.action"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Panel>
      <h3 className="flex items-center gap-2 font-display text-lg text-ink">
        <CalendarRange size={16} aria-hidden />
        {t("bulk.title", "Schedule a run")}
      </h3>
      <p className="mt-1 text-xs text-ink-mute">{t("bulk.lede", "Several days and start times at once. Times are Baku time; clashes are skipped.")}</p>

      <div className="mt-4 space-y-3">
        <Field label={t("admin.film")}>
          <Select value={movieId} onChange={(e) => setMovieId(e.target.value)}>
            <option value="">—</option>
            {movies.map((m) => <option key={m.id} value={m.id}>{m.title}</option>)}
          </Select>
        </Field>

        <Field label={t("admin.hall")}>
          <Select value={hallId} onChange={(e) => setHallId(e.target.value)}>
            <option value="">—</option>
            {venues.map((venue) => (
              <optgroup key={venue.id} label={venue.name}>
                {venue.halls.map((h) => (
                  <option key={h.id} value={h.id}>{h.name}{h.format ? ` · ${h.format}` : ""}</option>
                ))}
              </optgroup>
            ))}
          </Select>
        </Field>

        <div className="grid grid-cols-2 gap-3">
          <Field label={t("bulk.first", "First day")}>
            <Input type="date" value={firstDate} min={today()} onChange={(e) => setFirstDate(e.target.value)} />
          </Field>
          <Field label={t("bulk.days", "Days")}>
            <Input type="number" min={1} max={28} value={days} onChange={(e) => setDays(Math.min(28, Math.max(1, Number(e.target.value) || 1)))} />
          </Field>
        </div>

        <Field label={t("bulk.weekdays", "Only on (none = every day)")}>
          <div className="flex flex-wrap gap-1.5">
            {WEEKDAYS.map((day) => {
              const on = weekdays.includes(day);
              return (
                <Button
                  key={day}
                  size="sm"
                  variant={on ? "solid" : "outline"}
                  aria-pressed={on}
                  onClick={() => setWeekdays((list) => on ? list.filter((d) => d !== day) : [...list, day])}
                >
                  {weekdayName(day)}
                </Button>
              );
            })}
          </div>
        </Field>

        <Field label={t("bulk.times", "Start times")}>
          <div className="flex flex-wrap items-center gap-1.5">
            {times.map((time) => (
              <span key={time} className="inline-flex items-center gap-1 rounded-full border border-line px-2.5 py-1 text-sm text-ink">
                {time}
                <button onClick={() => setTimes((list) => list.filter((x) => x !== time))} aria-label={`${t("common.cancel")} ${time}`} className="text-ink-mute hover:text-bad">
                  <X size={12} aria-hidden />
                </button>
              </span>
            ))}
            <Input type="time" value={newTime} onChange={(e) => setNewTime(e.target.value)} className="w-28" />
            <Button
              size="sm"
              variant="outline"
              disabled={!newTime || times.includes(newTime) || times.length >= 8}
              onClick={() => setTimes((list) => [...list, newTime].sort())}
              aria-label={t("bulk.addTime", "Add time")}
            >
              <Plus size={14} aria-hidden />
            </Button>
          </div>
        </Field>

        <div className="grid grid-cols-3 gap-3">
          <Field label={t("admin.seatPrice")}>
            <Input type="number" step="0.5" value={price} onChange={(e) => setPrice(Number(e.target.value))} />
          </Field>
          <Field label={t("onDisplay.language")}>
            <Select value={audio} onChange={(e) => setAudio(e.target.value)}>
              {["az", "en", "ru", "tr"].map((code) => <option key={code} value={code}>{languageName(code)}</option>)}
            </Select>
          </Field>
          <Field label={t("onDisplay.subtitles")}>
            <Select value={subtitles} onChange={(e) => setSubtitles(e.target.value)}>
              <option value="">—</option>
              {["az", "en", "ru", "tr"].map((code) => <option key={code} value={code}>{languageName(code)}</option>)}
            </Select>
          </Field>
        </div>

        {error ? <Notice tone="error">{error}</Notice> : null}
        {result ? (
          <Notice tone={result.created > 0 ? "ok" : "info"}>
            {t("bulk.created", "Created")}: {result.created}
            {result.skipped.length > 0 ? ` · ${t("bulk.skipped", "Skipped")}: ${result.skipped.length}` : ""}
          </Notice>
        ) : null}
        {result && result.skipped.length > 0 ? (
          <ul className="max-h-32 overflow-y-auto text-xs text-ink-mute">
            {result.skipped.map((line) => <li key={line}>• {line}</li>)}
          </ul>
        ) : null}

        <Button className="w-full" disabled={!movieId || !hallId || times.length === 0 || busy} onClick={save}>
          {busy ? t("common.loading") : `${t("bulk.create", "Create")} ≈ ${estimate}`}
        </Button>
      </div>
    </Panel>
  );
}
