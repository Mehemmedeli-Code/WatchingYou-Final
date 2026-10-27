import { useEffect, useState } from "react";
import { MovieDialog } from "@/components/MovieDialog";
import { get, query } from "@/lib/api";
import { formatDateIn, genreName, lang, t } from "@/lib/i18n";
import { cn } from "@/lib/utils";

interface Story { title: string; summary: string | null; link: string; source: string; image: string | null; published: string | null; lang: string }
interface Edition { date: string; number: number; stories: Story[]; sources: string[] }
interface Film { id: string; title: string; slug: string; genre: string; releaseYear: number; durationMinutes: number }

/** The paper keeps its newsprint colours in every theme: it is meant to look printed. */
const RULE = "border-[#1b1712]";
const RED = "text-[#b3122b]";

// Playfair Display for the headlines and Lora for the text: both carry Azerbaijani, Turkish and
// Cyrillic letters. Georgia stands in until they arrive, or if Google Fonts cannot be reached.
const FONTS = "https://fonts.googleapis.com/css2?family=Playfair+Display:ital,wght@0,700;0,900;1,700&family=Lora:ital,wght@0,400;0,600;1,400&display=swap";

/** What Tailwind cannot say: the paper's grain, the ticker's motion and the drop cap. */
const STYLE = `
.wy-paper { background-color:#f4eee1; color:#1b1712; font-family:Lora,Georgia,serif;
  background-image:radial-gradient(rgba(120,90,40,.07) 1px,transparent 1px),radial-gradient(ellipse at center,transparent 55%,rgba(120,90,40,.13));
  background-size:3px 3px,100% 100%; }
.wy-display { font-family:"Playfair Display",Georgia,serif; }
.wy-ticker { display:inline-flex; gap:3rem; white-space:nowrap; animation:wy-ticker 70s linear infinite; }
.wy-ticker-wrap:hover .wy-ticker { animation-play-state:paused; }
@keyframes wy-ticker { from { transform:translateX(0) } to { transform:translateX(-50%) } }
@media (prefers-reduced-motion:reduce) { .wy-ticker { animation:none } }
.wy-dropcap::first-letter { float:left; font-family:"Playfair Display",Georgia,serif; font-weight:900; font-size:3.6em; line-height:.8; margin:.08em .1em 0 0; color:#b3122b; }
.wy-photo { filter:sepia(.18) saturate(.85) contrast(1.03); transition:filter .4s, transform .6s; }
a:hover .wy-photo, .wy-photo-wrap:hover .wy-photo { filter:none; transform:scale(1.03); }
`;

function useNewsFonts() {
  useEffect(() => {
    if (document.querySelector(`link[href="${FONTS}"]`)) return;
    const link = Object.assign(document.createElement("link"), { rel: "stylesheet", href: FONTS });
    document.head.appendChild(link);
  }, []);
}

/** The day's film, the same for everyone all day: the catalogue in a fixed order, picked by date. */
function useFilmOfTheDay(date: string | undefined) {
  const [film, setFilm] = useState<Film | null>(null);
  useEffect(() => {
    if (!date) return;
    const page = (n: number) => get<{ items: Film[]; totalCount: number }>("/api/movies" + query({ sortBy: "title", page: n, pageSize: 1 }));
    const day = Math.floor(Date.parse(`${date}T00:00:00Z`) / 86_400_000);
    void page(1)
      .then(({ totalCount }) => (totalCount ? page((day % totalCount) + 1) : null))
      .then((found) => setFilm(found?.items[0] ?? null))
      .catch(() => setFilm(null));
  }, [date]);
  return film;
}

export default function NewsPage() {
  const [edition, setEdition] = useState<Edition | null | "failed">(null);
  const [watching, setWatching] = useState<string | null>(null);
  useNewsFonts();

  useEffect(() => {
    void get<Edition>("/api/news" + query({ lang })).then(setEdition).catch(() => setEdition("failed"));
  }, []);

  const loaded = edition && edition !== "failed" ? edition : null;
  const film = useFilmOfTheDay(loaded?.date);
  const stories = loaded?.stories ?? [];
  // The lead is the newest story with a picture; the rest follow in the paper's order.
  const lead = stories.find((s) => s.image) ?? stories[0];
  const rest = stories.filter((s) => s !== lead);
  const [left, right, grid, brief] = [rest.slice(0, 3), rest.slice(3, 6), rest.slice(6, 15), rest.slice(15)];

  return (
    <article className="wy-paper mx-auto my-4 max-w-6xl px-4 py-6 shadow-[0_2px_0_#d8ceb8,0_20px_60px_rgba(0,0,0,.45)] sm:my-6 sm:px-10 sm:py-8">
      <style>{STYLE}</style>
      <Masthead date={loaded?.date} number={loaded?.number} />

      {!edition ? (
        <p className="wy-display py-28 text-center text-2xl italic opacity-70">{t("news.loading")}</p>
      ) : !lead ? (
        <p className="py-28 text-center text-lg italic">{t("news.empty")}</p>
      ) : (
        <>
          <Ticker stories={rest.slice(0, 8)} />
          <Lead story={lead} />

          <div className={cn("mt-8 grid gap-8 border-t-[3px] pt-6 lg:grid-cols-[1fr_1.1fr_1fr] lg:gap-0 lg:divide-x lg:divide-[#1b1712]/25", RULE)}>
            <Column stories={left} className="lg:pr-6" />
            <div className="lg:px-6">
              {film ? <FilmOfTheDay film={film} onOpen={() => setWatching(film.id)} /> : <Column stories={right.slice(0, 1)} />}
            </div>
            <Column stories={film ? right : right.slice(1)} className="lg:pl-6" />
          </div>

          {grid.length > 0 && (
            <section className="mt-10">
              <SectionTitle>{t("news.today")}</SectionTitle>
              <div className="mt-6 grid gap-x-8 gap-y-10 sm:grid-cols-2 lg:grid-cols-3">
                {grid.map((s) => <StoryCard key={s.link} story={s} picture />)}
              </div>
            </section>
          )}

          {brief.length > 0 && (
            <section className="mt-12">
              <SectionTitle>{t("news.brief")}</SectionTitle>
              <ol className="mt-6 gap-10 sm:columns-2 lg:columns-3">
                {brief.map((s, i) => (
                  <li key={s.link} className="mb-4 flex break-inside-avoid gap-3 border-b border-[#1b1712]/20 pb-4">
                    <span className={cn("wy-display w-8 shrink-0 text-3xl font-black leading-none", RED)}>{i + 1}</span>
                    <div>
                      <a href={s.link} target="_blank" rel="noopener noreferrer" className="wy-display text-[1.05rem] font-bold leading-snug hover:underline">{s.title}</a>
                      <Byline story={s} />
                    </div>
                  </li>
                ))}
              </ol>
            </section>
          )}

          <footer className={cn("mt-10 border-t-[3px] pt-1", RULE)}>
            <div className={cn("flex flex-wrap items-baseline justify-between gap-2 border-t pt-3 font-sans text-[11px] leading-relaxed opacity-75", RULE)}>
              <p><strong className="uppercase tracking-widest">{t("news.sources")}</strong> · {loaded?.sources.join(" · ")}</p>
              <p className="italic">{t("news.foreign")}</p>
            </div>
          </footer>
        </>
      )}

      {watching ? <MovieDialog movieId={watching} mode="details" onClose={() => setWatching(null)} /> : null}
    </article>
  );
}

function Masthead({ date, number }: { date?: string; number?: number }) {
  const day = date ? formatDateIn(new Date(`${date}T12:00:00Z`), { weekday: "long", day: "numeric", month: "long", year: "numeric", timeZone: "UTC" }) : "";
  return (
    <header className="text-center">
      <div className={cn("flex flex-wrap items-center justify-between gap-x-4 gap-y-1 border-b pb-2 font-sans text-[10px] font-bold uppercase tracking-[.18em] sm:text-[11px]", RULE)}>
        <span>{t("news.city")}</span>
        <span className="hidden sm:inline">{day}</span>
        <span>{t("news.number")} {number ?? "—"} · {t("news.free")}</span>
      </div>
      <h1 className={cn("wy-display mt-4 text-[clamp(2.6rem,10vw,6.5rem)] font-black leading-[.9] tracking-tight", RED)}>
        {t("news.title")}
      </h1>
      <p className="mt-3 flex items-center justify-center gap-3 text-sm italic sm:text-base">
        <span aria-hidden className={RED}>✦</span>{t("news.motto")}<span aria-hidden className={RED}>✦</span>
      </p>
      <p className="mt-1 font-sans text-[10px] font-bold uppercase tracking-[.18em] sm:hidden">{day}</p>
      <div className={cn("mt-4 border-t-[3px] pt-[3px]", RULE)}><div className={cn("border-t", RULE)} /></div>
    </header>
  );
}

/** The latest headlines running along a red band, twice over so the loop has no seam. */
function Ticker({ stories }: { stories: Story[] }) {
  if (!stories.length) return null;
  const items = (copy: string) => stories.map((s) => (
    <a key={s.link + copy} href={s.link} target="_blank" rel="noopener noreferrer" className="hover:underline">
      <span className="mr-3 opacity-60">●</span>{s.title}
    </a>
  ));
  return (
    <div className="mt-4 flex items-stretch overflow-hidden bg-[#1b1712] font-sans text-[13px] text-[#f4eee1]">
      <span className="z-10 shrink-0 bg-[#b3122b] px-3 py-2 font-black uppercase tracking-widest">{t("news.breaking")}</span>
      <div className="wy-ticker-wrap relative flex-1 overflow-hidden py-2">
        <div className="wy-ticker pl-4">{items("")}{items("+")}</div>
      </div>
    </div>
  );
}

function Lead({ story }: { story: Story }) {
  const dropCap = story.summary && /^\p{L}/u.test(story.summary);
  return (
    <section className="mt-6">
      <p className={cn("text-center font-sans text-[11px] font-black uppercase tracking-[.3em]", RED)}>{t("news.lead")}</p>
      <a href={story.link} target="_blank" rel="noopener noreferrer" className="group block">
        <h2 className="wy-display mx-auto mt-2 max-w-5xl text-balance text-center text-[clamp(1.9rem,5.2vw,3.9rem)] font-black leading-[1.04] group-hover:underline">{story.title}</h2>
      </a>
      <div className="mt-6 grid gap-6 md:grid-cols-[1.7fr_1fr]">
        {story.image && (
          <figure>
            <a href={story.link} target="_blank" rel="noopener noreferrer" className="block overflow-hidden">
              <Picture src={story.image} className="aspect-[16/9]" />
            </a>
            <figcaption className="mt-1.5 border-b border-[#1b1712]/30 pb-1.5 text-right text-xs italic opacity-70">{story.source}</figcaption>
          </figure>
        )}
        <div className={cn("flex flex-col", !story.image && "mx-auto max-w-3xl md:col-span-2")}>
          <Byline story={story} />
          {story.summary && <p className={cn("mt-3 text-[1.12rem] leading-[1.7]", dropCap && "wy-dropcap")}>{story.summary}</p>}
          <ReadMore href={story.link} />
        </div>
      </div>
    </section>
  );
}

function Column({ stories, className }: { stories: Story[]; className?: string }) {
  return (
    <div className={cn("divide-y divide-[#1b1712]/25", className)}>
      {stories.map((s, i) => <StoryCard key={s.link} story={s} picture={i === 0} className={i > 0 ? "pt-5" : "pb-5"} />)}
    </div>
  );
}

function StoryCard({ story, picture, className }: { story: Story; picture?: boolean; className?: string }) {
  return (
    <div className={cn("pb-5", className)}>
      {picture && story.image && (
        <a href={story.link} target="_blank" rel="noopener noreferrer" className="mb-3 block overflow-hidden">
          <Picture src={story.image} className="aspect-[16/10]" />
        </a>
      )}
      <a href={story.link} target="_blank" rel="noopener noreferrer" className="hover:underline">
        <h3 className="wy-display text-[1.3rem] font-bold leading-[1.2]">{story.title}</h3>
      </a>
      <Byline story={story} />
      {story.summary && <p className="mt-2 line-clamp-4 text-[.95rem] leading-relaxed opacity-90">{story.summary}</p>}
    </div>
  );
}

function FilmOfTheDay({ film, onOpen }: { film: Film; onOpen: () => void }) {
  return (
    <aside className="border-[3px] border-double border-[#b3122b] bg-[#fbf7ee] p-5 text-center shadow-[inset_0_0_0_6px_#fbf7ee,inset_0_0_0_7px_rgba(179,18,43,.35)]">
      <p className={cn("font-sans text-[11px] font-black uppercase tracking-[.3em]", RED)}>★ {t("news.filmOfDay")} ★</p>
      <button type="button" onClick={onOpen} className="group mt-4 block w-full">
        <div className="wy-photo-wrap mx-auto w-44 overflow-hidden shadow-[0_12px_24px_rgba(0,0,0,.35)] transition-transform duration-500 group-hover:-rotate-1">
          <img src={`/api/posters/${film.slug}`} alt="" className="wy-photo aspect-[2/3] w-full object-cover" />
        </div>
        <h3 className="wy-display mt-4 text-[1.7rem] font-black leading-tight group-hover:underline">{film.title}</h3>
      </button>
      <p className="mt-1 font-sans text-[11px] font-semibold uppercase tracking-widest opacity-70">
        {film.releaseYear} · {genreName(film.genre)} · {film.durationMinutes} {t("movie.min")}
      </p>
      <button type="button" onClick={onOpen} className="mt-4 bg-[#b3122b] px-5 py-2 font-sans text-xs font-black uppercase tracking-widest text-white transition-colors hover:bg-[#1b1712]">
        {t("news.watch")} →
      </button>
    </aside>
  );
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex items-center gap-4">
      <span className={cn("h-0 flex-1 border-t-[3px] border-double", RULE)} />
      <h2 className="wy-display text-center text-2xl font-black italic sm:text-3xl">{children}</h2>
      <span className={cn("h-0 flex-1 border-t-[3px] border-double", RULE)} />
    </div>
  );
}

function Byline({ story }: { story: Story }) {
  const when = story.published ? formatDateIn(new Date(story.published), { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" }) : null;
  return (
    <p className="mt-1.5 font-sans text-[10.5px] font-bold uppercase tracking-[.12em] opacity-75">
      {/* Source names are English: "Variety" in capitals is VARIETY, not the Turkic VARİETY. */}
      <span lang="en" className={RED}>{story.source}</span>
      {when && <> · {when}</>}
      {story.lang !== lang && <span className={cn("ml-1.5 rounded-sm border px-1 py-px text-[9px]", RULE)}>{story.lang.toUpperCase()}</span>}
    </p>
  );
}

function ReadMore({ href }: { href: string }) {
  return (
    <a href={href} target="_blank" rel="noopener noreferrer" className={cn("mt-4 self-start border-b-2 border-current pb-0.5 font-sans text-xs font-black uppercase tracking-widest hover:opacity-70", RED)}>
      {t("news.readMore")} →
    </a>
  );
}

/** Pictures come from the sources' own servers; one that will not load is simply left out. */
function Picture({ src, className }: { src: string; className?: string }) {
  const [failed, setFailed] = useState(false);
  if (failed) return null;
  return <img src={src} alt="" loading="lazy" referrerPolicy="no-referrer" onError={() => setFailed(true)} className={cn("wy-photo w-full object-cover", className)} />;
}
