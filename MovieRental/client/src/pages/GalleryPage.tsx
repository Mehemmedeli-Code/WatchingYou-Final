import { useEffect, useState } from "react";
import { Eye } from "lucide-react";
import { Section, Panel, Empty, Spinner } from "@/components/Shell";
import { VideoPlayer } from "@/components/VideoPlayer";
import { get } from "@/lib/api";
import { t, formatWhen } from "@/lib/i18n";
import type { ShortFilmSummary, ShortFilmOrigin } from "@/lib/shorts";

/**
 * One component, two pages. AI Catalog and Human Craft differ only in which origin they
 * ask for, so sharing the view keeps them from drifting apart as either one is polished.
 */
export function Gallery({ origin }: { origin: ShortFilmOrigin }) {
  const [films, setFilms] = useState<ShortFilmSummary[] | null>(null);

  const isAi = origin === "AiGenerated";
  const endpoint = isAi ? "/api/gallery/ai" : "/api/gallery/human";

  useEffect(() => {
    get<ShortFilmSummary[]>(endpoint).then(setFilms).catch(() => setFilms([]));
  }, [endpoint]);

  return (
    <Section
      title={isAi ? t("gallery.ai.title") : t("gallery.human.title")}
      lede={isAi ? t("gallery.ai.lede") : t("gallery.human.lede")}
    >
      {films === null ? <Spinner label={t("common.loading")} /> : null}

      {films?.length === 0 ? <Empty title={t("gallery.empty")} hint="" /> : null}

      <div className="grid gap-6 md:grid-cols-2">
        {films?.map((film) => (
          <Panel key={film.id}>
            <VideoPlayer src={film.streamUrl} title={film.title} />

            <h3 className="mt-4 font-display text-xl text-ink">{film.title}</h3>
            <p className="mt-1 text-xs text-ink-mute">
              {film.authorName}
              {film.approvedAtUtc ? ` · ${formatWhen(film.approvedAtUtc, { dateStyle: "medium" })}` : null}
            </p>

            {film.synopsis ? <p className="mt-3 text-sm text-ink-mute">{film.synopsis}</p> : null}

            <p className="mt-3 flex items-center gap-1.5 text-xs text-ink-mute">
              <Eye size={13} aria-hidden />
              {film.viewCount} {t("common.views")}
            </p>
          </Panel>
        ))}
      </div>
    </Section>
  );
}

export default function AiCatalogPage() {
  return <Gallery origin="AiGenerated" />;
}
