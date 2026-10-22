import { useCallback, useEffect, useState } from "react";
import { Clock, Languages, Ticket } from "lucide-react";
import { Section, Panel, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Select } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { get, query } from "@/lib/api";
import { t, formatWhen, formatDay, languageName } from "@/lib/i18n";
import { formatMoney } from "@/lib/format";

interface Showtime {
  screeningId: string;
  startsAtUtc: string;
  hall: string;
  audioLanguage: string;
  subtitleLanguage?: string | null;
  seatPrice: number;
  seatsLeft: number;
}

interface OnDisplayItem {
  movieId: string;
  movieTitle: string;
  languages: string[];
  showtimes: Showtime[];
}

const LANGUAGES = ["az", "en", "ru", "tr"];

export default function OnDisplayPage() {
  const [items, setItems] = useState<OnDisplayItem[] | null>(null);
  const [language, setLanguage] = useState("");
  const [search, setSearch] = useState("");
  const [days, setDays] = useState(14);

  const load = useCallback(async () => {
    setItems(null);
    const to = new Date(Date.now() + days * 86_400_000).toISOString();
    const data = await get<OnDisplayItem[]>(
      "/api/on-display" + query({ language, search, to }),
    ).catch(() => []);
    setItems(data);
  }, [language, search, days]);

  // Debounced so typing a film name does not fire a request per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => void load(), 250);
    return () => clearTimeout(timer);
  }, [load]);

  return (
    <Section title={t("onDisplay.title")} lede={t("onDisplay.lede")}>
      <div className="mb-6 grid gap-3 sm:grid-cols-3">
        <Input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t("common.search")}
          aria-label={t("common.search")}
        />
        <Select value={language} onChange={(e) => setLanguage(e.target.value)} aria-label={t("onDisplay.language")}>
          <option value="">{t("common.all")}</option>
          {LANGUAGES.map((code) => (
            <option key={code} value={code}>{languageName(code)}</option>
          ))}
        </Select>
        <Select value={days} onChange={(e) => setDays(Number(e.target.value))} aria-label={t("field.window")}>
          <option value={2}>48h</option>
          <option value={7}>7</option>
          <option value={14}>14</option>
          <option value={30}>30</option>
        </Select>
      </div>

      {items === null ? <Spinner label={t("common.loading")} /> : null}
      {items?.length === 0 ? <Empty title={t("onDisplay.empty")} hint="" /> : null}

      <div className="space-y-5">
        {items?.map((item) => (
          <Panel key={item.movieId}>
            <div className="flex flex-wrap items-baseline justify-between gap-3">
              <h3 className="font-display text-xl text-ink">{item.movieTitle}</h3>
              <p className="flex items-center gap-1.5 text-xs text-ink-mute">
                <Languages size={13} aria-hidden />
                {item.languages.map(languageName).join(" · ")}
              </p>
            </div>

            <div className="mt-4 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
              {item.showtimes.map((show) => (
                <div key={show.screeningId} className="rounded-lg border border-line p-3">
                  <p className="flex items-center gap-1.5 text-sm text-ink">
                    <Clock size={13} aria-hidden />
                    {formatDay(show.startsAtUtc)} · {formatWhen(show.startsAtUtc, { hour: "2-digit", minute: "2-digit" })}
                  </p>
                  <p className="mt-1 text-xs text-ink-mute">{show.hall}</p>

                  <div className="mt-2 flex flex-wrap gap-1.5">
                    <Badge tone="warn">{languageName(show.audioLanguage)}</Badge>
                    {show.subtitleLanguage ? (
                      <Badge>{t("onDisplay.subtitles")}: {languageName(show.subtitleLanguage)}</Badge>
                    ) : null}
                  </div>

                  <p className="mt-2 text-xs text-ink-mute">
                    {formatMoney(show.seatPrice)} · {show.seatsLeft} {t("onDisplay.seatsLeft")}
                  </p>

                  <a href={`/cinema?screening=${show.screeningId}`} className="mt-3 inline-flex">
                    <Button size="sm" variant="outline" disabled={show.seatsLeft === 0}>
                      <Ticket size={14} aria-hidden />
                      {t("onDisplay.book")}
                    </Button>
                  </a>
                </div>
              ))}
            </div>
          </Panel>
        ))}
      </div>
    </Section>
  );
}
