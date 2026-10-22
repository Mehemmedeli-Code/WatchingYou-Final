import { useEffect, useState } from "react";
import { Eye, Heart } from "lucide-react";
import { Section, Panel, Empty, Spinner } from "@/components/Shell";
import { VideoPlayer } from "@/components/VideoPlayer";
import { useAuth } from "@/components/useAuth";
import { del, get, post, put } from "@/lib/api";
import { t, formatWhen } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import type { ShortFilmSummary, ShortFilmOrigin } from "@/lib/shorts";

/** A gallery card from the server: the film, its maker's handle and picture, and its likes. */
interface GalleryFilm {
  film: ShortFilmSummary;
  authorUsername?: string | null;
  authorAvatarUrl?: string | null;
  likes: number;
  likedByMe: boolean;
}

/**
 * One component, two pages. AI Catalog and Human Craft differ only in which origin they
 * ask for, so sharing the view keeps them from drifting apart as either one is polished.
 * Each card reads like a post: who shared it on top (a tap opens their profile), the film,
 * then the like and the view count.
 */
export function Gallery({ origin }: { origin: ShortFilmOrigin }) {
  const [items, setItems] = useState<GalleryFilm[] | null>(null);
  const { isSignedIn } = useAuth();

  const isAi = origin === "AiGenerated";
  const endpoint = isAi ? "/api/gallery/ai" : "/api/gallery/human";

  useEffect(() => {
    get<GalleryFilm[]>(endpoint).then(setItems).catch(() => setItems([]));
  }, [endpoint]);

  // Optimistic: the heart turns at once and the server's count settles it.
  async function toggleLike(item: GalleryFilm) {
    if (!isSignedIn) { location.href = "/account"; return; }
    const like = !item.likedByMe;
    const update = (patch: Partial<GalleryFilm>) =>
      setItems((list) => list?.map((x) => (x.film.id === item.film.id ? { ...x, ...patch } : x)) ?? null);
    update({ likedByMe: like, likes: item.likes + (like ? 1 : -1) });
    const url = `/api/shorts/${item.film.id}/like`;
    const result = await (like ? put<{ likes: number }>(url) : del<{ likes: number }>(url)).catch(() => null);
    update(result ? { likedByMe: like, likes: result.likes } : { likedByMe: item.likedByMe, likes: item.likes });
  }

  // Every play is a view; the server's count replaces the one on the card.
  async function countView(id: string) {
    const result = await post<{ views: number }>(`/api/shorts/${id}/view`, {}).catch(() => null);
    if (result) setItems((list) => list?.map((x) => (x.film.id === id ? { ...x, film: { ...x.film, viewCount: result.views } } : x)) ?? null);
  }

  return (
    <Section
      title={isAi ? t("gallery.ai.title") : t("gallery.human.title")}
      lede={isAi ? t("gallery.ai.lede") : t("gallery.human.lede")}
    >
      {items === null ? <Spinner label={t("common.loading")} /> : null}

      {/* Empty until the first film is approved: say how one gets here. */}
      {items?.length === 0 ? (
        <Empty title={t("gallery.empty")} hint={t("gallery.emptyHint", "Be the first: upload your film in Studio. It appears here once it is approved.")}
          action={<a href="/studio" className="inline-flex h-9 items-center rounded-full bg-accent px-4 text-sm font-semibold text-surface hover:bg-accent-bright">{t("profile.shareFirst", "Upload your first film")}</a>} />
      ) : null}

      <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
        {items?.map((item) => {
          const { film } = item;
          const profile = item.authorUsername ? `/u/${encodeURIComponent(item.authorUsername)}` : undefined;
          return (
            <Panel key={film.id}>
              <a href={profile} className={cn("mb-3 flex items-center gap-2.5", profile && "group")}>
                <img src={item.authorAvatarUrl || "/icons/avatar.svg"} alt="" className="h-9 w-9 shrink-0 rounded-full border border-line object-cover" />
                <span className="min-w-0">
                  <span className="block truncate text-sm font-semibold text-ink group-hover:text-accent">
                    {item.authorUsername ? `@${item.authorUsername}` : film.authorName}
                  </span>
                  {film.approvedAtUtc ? (
                    <span className="block text-xs text-ink-mute">{formatWhen(film.approvedAtUtc, { dateStyle: "medium" })}</span>
                  ) : null}
                </span>
              </a>

              <VideoPlayer src={film.streamUrl} title={film.title} onWatch={() => void countView(film.id)} />

              <div className="mt-3 flex items-center gap-4 text-sm">
                <button type="button" onClick={() => void toggleLike(item)} aria-pressed={item.likedByMe}
                  aria-label={item.likedByMe ? t("gallery.unlike", "Unlike") : t("gallery.like", "Like")}
                  className="flex items-center gap-1.5 text-ink transition hover:text-accent">
                  <Heart size={20} aria-hidden className={cn("transition-transform active:scale-125", item.likedByMe && "fill-bad text-bad")} />
                  <span className="font-semibold">{item.likes}</span>
                </button>
                <span className="flex items-center gap-1.5 text-xs text-ink-mute">
                  <Eye size={14} aria-hidden /> {film.viewCount} {t("common.views")}
                </span>
              </div>

              <h3 className="mt-2 font-display text-xl text-ink">{film.title}</h3>
              {film.synopsis ? <p className="mt-1 text-sm text-ink-mute">{film.synopsis}</p> : null}
            </Panel>
          );
        })}
      </div>
    </Section>
  );
}

export default function AiCatalogPage() {
  return <Gallery origin="AiGenerated" />;
}
