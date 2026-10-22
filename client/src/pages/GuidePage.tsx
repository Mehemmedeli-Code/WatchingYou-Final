import { useEffect, useState } from "react";
import { ArrowLeft, Film, ListOrdered, Play } from "lucide-react";
import { MovieDialog } from "@/components/MovieDialog";
import { Button } from "@/components/ui/button";
import { get, query } from "@/lib/api";
import { GUIDES, type Guide, type Lang } from "@/lib/guides";
import { formatDateIn, lang, t } from "@/lib/i18n";
import { cn } from "@/lib/utils";

const poster = (slug: string) => `/api/posters/${slug}`;
const here = (): Lang => (["az", "en", "ru", "tr"].includes(lang) ? lang : "en") as Lang;
const published = (iso: string) => formatDateIn(new Date(`${iso}T12:00:00Z`), { day: "numeric", month: "long", year: "numeric", timeZone: "UTC" });

/** The guide in the address (?g=), so a guide can be linked and the back button works. */
function useOpenGuide(): [Guide | null, (slug: string | null) => void] {
  const read = () => GUIDES.find((g) => g.slug === new URLSearchParams(location.search).get("g")) ?? null;
  const [open, setOpen] = useState<Guide | null>(read);
  useEffect(() => {
    const onPop = () => setOpen(read());
    window.addEventListener("popstate", onPop);
    return () => window.removeEventListener("popstate", onPop);
  }, []);
  return [open, (slug) => {
    history.pushState(null, "", slug ? `/guide?g=${slug}` : "/guide");
    setOpen(slug ? GUIDES.find((g) => g.slug === slug) ?? null : null);
    window.scrollTo({ top: 0 });
  }];
}

export default function GuidePage() {
  const [open, go] = useOpenGuide();
  return open ? <Article guide={open} onBack={() => go(null)} onOpen={go} /> : <Index onOpen={go} />;
}

/** The guide's mark: a detective in a fedora, one eye behind a monocle. */
function Detective({ size = 18 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <path d="M6.6 10.2 7.7 4.8c.2-1 1.2-1.5 2.1-1.2l1.2.4c.6.2 1.4.2 2 0l1.2-.4c.9-.3 1.9.2 2.1 1.2l1.1 5.4" />
      <path d="M7.3 7.7c3 .8 6.4.8 9.4 0" />
      <path d="M2.8 10.6c3.2 1.6 15.2 1.6 18.4 0" />
      <path d="M7.2 12.4c0 4.2 2.1 7.4 4.8 7.4s4.8-3.2 4.8-7.4" />
      <path d="M8.9 14.4h1.8" />
      <circle cx="14.4" cy="14.4" r="1.9" />
      <path d="M16.2 15.1c1.2 1.6 1.2 3.6-.2 5.4" />
      <path d="M4.6 22c.9-1.7 2.4-2.5 4.4-2.6M19.4 22c-.9-1.7-2.4-2.5-4.4-2.6" />
    </svg>
  );
}

/** The posters are small (about 250 px wide); stretched across a wide frame they blur. So the
 *  frame gets a soft wash of the poster, and the poster itself sits on it near its own size. */
function Cover({ slug, className, side = "center", plain }: { slug: string; className?: string; side?: "center" | "right"; plain?: boolean }) {
  const [failed, setFailed] = useState(false);
  return (
    <div className={cn("relative overflow-hidden bg-gradient-to-br from-accent-dim/40 via-surface-raised to-surface", className)}>
      {failed ? <Film size={32} className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 text-accent/50" aria-hidden /> : plain ? (
        <img src={poster(slug)} alt="" loading="lazy" onError={() => setFailed(true)} className="h-full w-full object-cover" />
      ) : (
        <>
          <img src={poster(slug)} alt="" aria-hidden loading="lazy" onError={() => setFailed(true)}
            className="absolute inset-0 h-full w-full scale-125 object-cover opacity-70 blur-2xl saturate-150" />
          <div className="absolute inset-0 bg-gradient-to-r from-black/50 via-black/10 to-black/50" aria-hidden />
          <img src={poster(slug)} alt="" loading="lazy"
            className={cn("absolute aspect-[2/3] max-h-[420px] rounded-lg object-cover shadow-2xl shadow-black/60 ring-1 ring-white/15 transition duration-500 group-hover:scale-[1.03]",
              // On the right it leaves room below for the entry's button.
              side === "right" ? "right-[5%] top-[6%] h-[68%]" : "left-1/2 top-1/2 h-[86%] -translate-x-1/2 -translate-y-1/2")} />
        </>
      )}
    </div>
  );
}

function Index({ onOpen }: { onOpen: (slug: string) => void }) {
  const l = here();
  const newest = [...GUIDES].sort((a, b) => b.published.localeCompare(a.published));
  const [lead, ...rest] = newest;

  return (
    <div className="mx-auto w-full max-w-6xl px-4 py-10 sm:px-6">
      <p className="flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.18em] text-accent">
        <Detective size={18} /> {t("guide.kicker", "Guide")}
      </p>
      <h1 className="mt-2 font-display text-3xl text-ink sm:text-4xl">{t("guide.title", "Watch-order guides")}</h1>
      <p className="mt-2 max-w-[60ch] text-sm text-ink-mute">{t("guide.lede", "Series and directors, in the order worth watching them — each film one click from the catalogue.")}</p>

      <div className="mt-8 grid gap-8 lg:grid-cols-[1fr_340px]">
        <button type="button" onClick={() => onOpen(lead.slug)} className="group text-left">
          <Cover slug={lead.cover} className="aspect-[16/8] rounded-2xl border border-line" />
          <h2 className="mt-4 font-display text-2xl text-ink transition group-hover:text-accent sm:text-3xl">{lead.title[l]}</h2>
          <p className="mt-2 text-sm text-ink-mute">{lead.teaser[l]}</p>
          <p className="mt-2 text-xs text-ink-mute">{t("guide.byline", "WatchingYou editors")} · {published(lead.published)}</p>
        </button>

        <aside>
          <h2 className="font-display text-xl text-ink">{t("guide.latest", "Latest articles")}</h2>
          <ul className="mt-3 divide-y divide-line">
            {rest.slice(0, 5).map((g) => (
              <li key={g.slug}>
                <button type="button" onClick={() => onOpen(g.slug)} className="group flex w-full gap-3 py-3 text-left">
                  <Cover slug={g.cover} plain className="h-16 w-12 shrink-0 rounded-md" />
                  <span className="min-w-0">
                    <span className="line-clamp-2 font-medium text-ink transition group-hover:text-accent">{g.title[l]}</span>
                    <span className="mt-1 block text-xs text-ink-mute">{published(g.published)}</span>
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </aside>
      </div>

      <h2 className="mt-12 flex items-center gap-2 font-display text-2xl text-ink">
        <ListOrdered size={20} className="text-accent" aria-hidden /> {t("guide.ordered", "Ordered lists")}
      </h2>
      <div className="mt-4 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
        {newest.map((g) => (
          <button key={g.slug} type="button" onClick={() => onOpen(g.slug)} className="group text-left">
            <div className="relative">
              <Cover slug={g.cover} className="aspect-[16/9] rounded-xl border border-line" />
              <span className="absolute right-2 top-2 rounded-full bg-black/70 px-2 py-0.5 text-[11px] font-semibold text-white">
                {g.entries.length} {t("guide.films", "films")}
              </span>
            </div>
            <p className="mt-2 text-xs text-ink-mute">{t("guide.byline", "WatchingYou editors")} · {published(g.published)}</p>
            <h3 className="mt-1 font-semibold text-ink transition group-hover:text-accent">{g.title[l]}</h3>
          </button>
        ))}
      </div>
    </div>
  );
}

interface FoundMovie { id: string; slug: string }

function Article({ guide, onBack, onOpen }: { guide: Guide; onBack: () => void; onOpen: (slug: string) => void }) {
  const l = here();
  const [watching, setWatching] = useState<string | null>(null);
  const [looking, setLooking] = useState<string | null>(null);
  const others = GUIDES.filter((g) => g.slug !== guide.slug).slice(0, 3);

  // The guide knows a film by its slug; the dialog needs its id, which differs per database.
  async function watch(slug: string, title: string) {
    setLooking(slug);
    const found = await get<{ items: FoundMovie[] }>("/api/movies" + query({ search: title, pageSize: 12 })).catch(() => null);
    setLooking(null);
    const movie = found?.items.find((m) => m.slug === slug);
    if (movie) setWatching(movie.id);
  }

  return (
    <article className="mx-auto w-full max-w-4xl px-4 py-8 sm:px-6">
      {watching ? <MovieDialog movieId={watching} mode="details" onClose={() => setWatching(null)} /> : null}

      <button type="button" onClick={onBack} className="mb-4 inline-flex items-center gap-1.5 text-sm text-ink-mute hover:text-accent">
        <ArrowLeft size={15} aria-hidden /> {t("guide.all", "All guides")}
      </button>

      <Cover slug={guide.cover} className="aspect-[16/7] rounded-2xl border border-line" />
      <h1 className="mt-6 font-display text-3xl leading-tight text-ink sm:text-4xl">{guide.title[l]}</h1>
      <p className="mt-3 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-ink-mute">
        <span className="flex items-center gap-2">
          <span className="flex h-8 w-8 items-center justify-center rounded-full bg-accent/15 text-accent"><Detective size={18} /></span>
          <span className="font-medium text-ink">{t("guide.byline", "WatchingYou editors")}</span>
        </span>
        <span aria-hidden>·</span>
        <span>{t("guide.published", "Published")} {published(guide.published)}</span>
      </p>

      <div className="mt-6 space-y-4 text-[15px] leading-relaxed text-ink/90">
        {guide.intro[l].map((p, i) => <p key={i}>{p}</p>)}
      </div>

      <h2 className="mt-10 flex items-center gap-2 font-display text-2xl text-ink">
        <ListOrdered size={20} className="text-accent" aria-hidden /> {guide.orderNote[l]}
      </h2>

      <ol className="mt-6 space-y-10">
        {guide.entries.map((entry, i) => (
          <li key={entry.slug}>
            <div className="relative overflow-hidden rounded-2xl border border-line">
              <Cover slug={entry.slug} side="right" className="aspect-[16/8]" />
              <div className="absolute inset-0 bg-gradient-to-t from-black/85 via-black/20 to-transparent" aria-hidden />
              <span className="absolute left-4 top-3 font-display text-5xl font-extrabold text-white drop-shadow-lg sm:text-6xl">
                {String(i + 1).padStart(2, "0")}
              </span>
              <div className="absolute inset-x-0 bottom-0 flex items-end gap-3 p-4">
                <div className="min-w-0 flex-1">
                  <h3 className="font-display text-xl text-white sm:text-2xl">{entry.title}</h3>
                  <p className="text-sm text-white/80">{entry.year}</p>
                </div>
                <Button size="sm" disabled={looking === entry.slug} onClick={() => void watch(entry.slug, entry.title)}>
                  <Play size={14} aria-hidden /> {t("guide.open", "Open the film")}
                </Button>
              </div>
            </div>
            <p className="mt-3 text-[15px] leading-relaxed text-ink/90">{entry.note[l]}</p>
          </li>
        ))}
      </ol>

      <h2 className="mt-14 font-display text-xl text-ink">{t("guide.more", "More guides")}</h2>
      <div className="mt-3 grid gap-4 sm:grid-cols-3">
        {others.map((g) => (
          <button key={g.slug} type="button" onClick={() => onOpen(g.slug)} className="group text-left">
            <Cover slug={g.cover} className="aspect-[16/9] rounded-xl border border-line" />
            <p className="mt-2 text-sm font-semibold text-ink transition group-hover:text-accent">{g.title[l]}</p>
          </button>
        ))}
      </div>
    </article>
  );
}
