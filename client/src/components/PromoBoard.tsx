import { useEffect, useId, useState, type ReactNode } from "react";
import { ArrowRight } from "lucide-react";
import { get } from "@/lib/api";
import { formatDateIn, t } from "@/lib/i18n";
import { cn } from "@/lib/utils";

export interface Banner {
  id: string;
  theme: "Brand" | "Christmas" | "Novruz" | "Ramadan";
  isActive: boolean;
  sortOrder: number;
  headline?: string | null;
  subline?: string | null;
  promoCode?: string | null;
  percentOff?: number | null;
  amountOff?: number | null;
  startsOn?: string | null;
  endsOn?: string | null;
  showing: boolean;
}

/** How each theme looks: the paper, the ink, and the drawing on the right. */
const LOOKS: Record<Banner["theme"], { paper: string; ink: string; accent: string; button: string; Art: (p: { id: string }) => ReactNode }> = {
  Christmas: {
    paper: "linear-gradient(135deg, #F2333A 0%, #D81E28 55%, #A9101A 100%)",
    ink: "#FFFFFF", accent: "#FFE7A3", button: "bg-white text-[#C8151F] hover:bg-[#FFF3F3]", Art: ChristmasArt,
  },
  Novruz: {
    paper: "linear-gradient(135deg, #0F5A3A 0%, #0B4029 60%, #072818 100%)",
    ink: "#FFFFFF", accent: "#F6C453", button: "bg-[#F6C453] text-[#0B3B25] hover:bg-[#FFD777]", Art: NovruzArt,
  },
  Ramadan: {
    paper: "linear-gradient(135deg, #16205A 0%, #1F1650 55%, #2E1763 100%)",
    ink: "#FFFFFF", accent: "#F3C969", button: "bg-[#F3C969] text-[#1B1446] hover:bg-[#FFD98A]", Art: RamadanArt,
  },
  Brand: {
    paper: "radial-gradient(120% 140% at 85% 50%, #0E3B24 0%, #06140D 45%, #030605 100%)",
    ink: "#FFFFFF", accent: "#22E07A", button: "bg-[#22E07A] text-[#03140A] hover:bg-[#4BF09A]", Art: BrandArt,
  },
};

function dayMonth(iso: string) {
  return formatDateIn(new Date(`${iso}T12:00:00Z`), { day: "numeric", month: "long", timeZone: "UTC" });
}

/** One board, drawn. Used on the catalogue and as the admin's preview. */
export function BannerSlide({ banner, className }: { banner: Banner; className?: string }) {
  const id = useId().replace(/:/g, "");
  const look = LOOKS[banner.theme];
  const key = banner.theme.toLowerCase();
  const brand = banner.theme === "Brand";

  const discount = banner.percentOff
    ? t("banner.percent", "{n}% off").replace("{n}", String(Math.round(banner.percentOff)))
    : banner.amountOff ? t("banner.amount", "{n} ₼ off").replace("{n}", banner.amountOff.toFixed(2)) : null;
  const headline = banner.headline || (brand ? t("banner.brand.headline", "Films, cinema tickets — all in one place") : discount ?? t(`banner.${key}.headline`));
  const subline = banner.subline || (brand ? t("banner.brand.subline", "The catalogue, the cinema and Watching PRO on one site.") : t("banner.ticketsLine", "On every cinema ticket"));
  const dates = banner.startsOn && banner.endsOn ? `${dayMonth(banner.startsOn)} – ${dayMonth(banner.endsOn)}` : null;

  return (
    <div className={cn("relative isolate h-full w-full overflow-hidden", className)} style={{ background: look.paper, color: look.ink }}>
      <div aria-hidden className="pointer-events-none absolute inset-y-0 right-0 -z-10 w-[62%] sm:w-[55%]">
        <look.Art id={id} />
      </div>
      <div className="flex h-full max-w-[64%] flex-col justify-center gap-1.5 px-6 py-5 sm:max-w-[56%] sm:px-9">
        <p className="text-[11px] font-semibold uppercase tracking-[0.18em]" style={{ color: look.accent }}>
          {t(`banner.${key}.kicker`)}
        </p>
        <p className="font-display text-2xl font-extrabold leading-[1.05] sm:text-4xl">{headline}</p>
        <p className="text-sm opacity-90 sm:text-base">{subline}</p>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          {dates ? <span className="text-sm font-medium opacity-90">{dates}</span> : null}
          {banner.promoCode ? (
            <span className="rounded-md border border-dashed px-2 py-0.5 font-mono text-xs font-bold tracking-wider"
              style={{ borderColor: look.accent, color: look.accent }}>
              {t("banner.code", "Code")}: {banner.promoCode}
            </span>
          ) : null}
        </div>
        <a href={brand ? "/on-display" : "/cinema"}
          className={cn("mt-3 inline-flex w-fit items-center gap-1.5 rounded-full px-4 py-2 text-sm font-semibold shadow-lg transition", look.button)}>
          {brand ? t("banner.brand.cta", "See what's on") : t("banner.cta", "Book a seat")}
          <ArrowRight size={15} aria-hidden />
        </a>
      </div>
    </div>
  );
}

/** The boards that are on today, one at a time, turning every seven seconds. */
export function PromoBanners() {
  const [banners, setBanners] = useState<Banner[]>([]);
  const [index, setIndex] = useState(0);
  const [paused, setPaused] = useState(false);

  useEffect(() => {
    void get<Banner[]>("/api/banners").then((list) => setBanners(Array.isArray(list) ? list : [])).catch(() => setBanners([]));
  }, []);

  useEffect(() => {
    if (banners.length < 2 || paused || window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) return;
    const timer = setInterval(() => setIndex((i) => (i + 1) % banners.length), 7000);
    return () => clearInterval(timer);
  }, [banners.length, paused]);

  if (banners.length === 0) return null;

  return (
    <section aria-roledescription="carousel" aria-label={t("banner.label", "Offers")}
      className="relative mb-8 h-56 overflow-hidden rounded-2xl border border-line shadow-xl shadow-black/30 sm:h-64"
      onMouseEnter={() => setPaused(true)} onMouseLeave={() => setPaused(false)}
      onFocus={() => setPaused(true)} onBlur={() => setPaused(false)}>
      {banners.map((banner, i) => (
        <div key={banner.id} role="group" aria-roledescription="slide" aria-label={`${i + 1} / ${banners.length}`}
          aria-hidden={i !== index} inert={i !== index}
          className={cn("absolute inset-0 transition-all duration-700 ease-out", i === index ? "opacity-100" : "pointer-events-none opacity-0 translate-x-4")}>
          <BannerSlide banner={banner} />
        </div>
      ))}
      {banners.length > 1 ? (
        <div className="absolute bottom-3 right-4 z-10 flex gap-1.5">
          {banners.map((banner, i) => (
            <button key={banner.id} type="button" onClick={() => setIndex(i)}
              aria-label={`${i + 1} / ${banners.length}`} aria-current={i === index}
              className={cn("h-2 rounded-full bg-white/70 transition-all hover:bg-white", i === index ? "w-6 bg-white" : "w-2")} />
          ))}
        </div>
      ) : null}
    </section>
  );
}

// ------------------------------------------------------------------ the drawings

function ChristmasArt({ id }: { id: string }) {
  const tree = (x: number, s: number) => (
    <g transform={`translate(${x} ${168 - 60 * s}) scale(${s})`}>
      <path d="M30 0 L52 34 H42 L60 62 H48 L68 92 H-8 L12 62 H0 L18 34 H8 Z" fill="#F6F9FC" />
      <path d="M30 0 L52 34 H42 L60 62 H48 L68 92 H30 Z" fill="#DCE6F0" />
    </g>
  );
  return (
    <svg viewBox="0 0 400 220" preserveAspectRatio="xMaxYMid slice" className="h-full w-full">
      <defs>
        <linearGradient id={`${id}-star`} x1="0" y1="0" x2="1" y2="1"><stop offset="0" stopColor="#FF5A5F" /><stop offset="1" stopColor="#B80F1A" /></linearGradient>
        <radialGradient id={`${id}-ball`} cx="0.35" cy="0.35" r="0.7"><stop offset="0" stopColor="#FFFFFF" /><stop offset="1" stopColor="#E7EDF3" /></radialGradient>
      </defs>
      {[[40, 30, 2], [120, 18, 1.5], [210, 50, 2.2], [300, 22, 1.6], [360, 70, 2], [170, 95, 1.4], [80, 110, 1.8], [250, 120, 1.3]].map(([x, y, r], i) => (
        <circle key={i} cx={x} cy={y} r={r} fill="#FFFFFF" opacity={0.75} />
      ))}
      {tree(70, 1.05)}{tree(150, 0.8)}{tree(250, 1.25)}{tree(330, 0.9)}
      <path d="M0 175 C70 150 130 168 200 160 C280 150 330 168 400 158 V220 H0 Z" fill="#FFFFFF" />
      <path d="M0 192 C90 178 170 196 260 186 C320 180 360 190 400 184 V220 H0 Z" fill="#EAF0F6" />
      <g transform="translate(205 120) rotate(-12)">
        <polygon points="0,-46 13,-15 46,-14 20,7 29,40 0,21 -29,40 -20,7 -46,-14 -13,-15" fill={`url(#${id}-star)`} />
        <polygon points="0,-46 13,-15 0,0" fill="#FF8A8E" opacity="0.55" />
        <polygon points="46,-14 20,7 0,0" fill="#7E0A12" opacity="0.35" />
      </g>
      <g transform="translate(360 18)">
        <line x1="0" y1="-20" x2="0" y2="10" stroke="#FFFFFF" strokeOpacity="0.6" strokeWidth="1.5" />
        <circle cx="0" cy="34" r="26" fill={`url(#${id}-ball)`} />
        <path d="M-24 26 C-8 34 8 34 24 26 M-25 40 C-8 48 8 48 25 40" stroke="#E2343B" strokeWidth="6" fill="none" />
      </g>
    </svg>
  );
}

function NovruzArt({ id }: { id: string }) {
  const blades = Array.from({ length: 34 }, (_, i) => {
    const x = 196 + (i - 17) * 3.4;
    const lean = (i - 17) * 1.8;
    const h = 62 + ((i * 37) % 26);
    return <path key={i} d={`M${x} 150 Q${x + lean * 0.4} ${150 - h * 0.6} ${x + lean} ${150 - h}`} stroke={i % 3 ? "#5CCB5F" : "#9BE36D"} strokeWidth="2.4" fill="none" strokeLinecap="round" />;
  });
  return (
    <svg viewBox="0 0 400 220" preserveAspectRatio="xMaxYMid slice" className="h-full w-full">
      <defs>
        <radialGradient id={`${id}-glow`} cx="0.5" cy="0.6" r="0.5"><stop offset="0" stopColor="#FFB547" stopOpacity="0.55" /><stop offset="1" stopColor="#FFB547" stopOpacity="0" /></radialGradient>
        <linearGradient id={`${id}-flame`} x1="0" y1="1" x2="0" y2="0"><stop offset="0" stopColor="#FF6A1A" /><stop offset="0.6" stopColor="#FFB020" /><stop offset="1" stopColor="#FFE38A" /></linearGradient>
      </defs>
      {/* buta, faint, in the corner */}
      {[[60, 40], [120, 160], [350, 40]].map(([x, y], i) => (
        <path key={i} transform={`translate(${x} ${y}) scale(0.9)`} d="M0 0 C18 -18 40 -6 34 16 C30 32 10 40 -4 30 C-14 22 -10 8 0 8 C6 8 8 14 4 18" stroke="#F6C453" strokeOpacity="0.22" strokeWidth="2" fill="none" />
      ))}
      <circle cx="330" cy="150" r="70" fill={`url(#${id}-glow)`} />
      {/* the bonfire */}
      <g transform="translate(330 168)">
        <path d="M-30 6 L30 -6 M-30 -6 L30 6" stroke="#6B3B1F" strokeWidth="7" strokeLinecap="round" />
        <path d="M0 -70 C14 -48 26 -34 22 -14 C20 -2 10 4 0 4 C-12 4 -22 -4 -22 -16 C-22 -30 -10 -36 -8 -52 C-4 -44 -2 -40 0 -36 C2 -46 0 -58 0 -70 Z" fill={`url(#${id}-flame)`} />
        <path d="M0 -34 C6 -24 10 -16 8 -8 C6 -2 3 0 0 0 C-5 0 -9 -4 -8 -10 C-7 -16 -2 -20 0 -34 Z" fill="#FFF3B8" />
      </g>
      {/* the semeni on its plate, with a red ribbon */}
      {blades}
      <ellipse cx="196" cy="152" rx="66" ry="14" fill="#C9962B" />
      <ellipse cx="196" cy="148" rx="60" ry="10" fill="#E9B949" />
      <path d="M150 128 C170 136 222 136 242 128 L244 140 C222 148 170 148 148 140 Z" fill="#D92B2B" />
      <path d="M196 132 l-14 -10 l2 16 Z M196 132 l14 -10 l-2 16 Z" fill="#B71C1C" />
      {/* pakhlava */}
      {[[100, 184], [126, 192], [262, 188]].map(([x, y], i) => (
        <g key={i} transform={`translate(${x} ${y})`}>
          <polygon points="0,-12 14,0 0,12 -14,0" fill="#C88A2E" />
          <polygon points="0,-12 14,0 0,0" fill="#E2A94A" />
          <circle r="2.6" fill="#7A4A1A" />
        </g>
      ))}
    </svg>
  );
}

function RamadanArt({ id }: { id: string }) {
  const lantern = (x: number, drop: number, s: number) => (
    <g transform={`translate(${x} 0)`}>
      <line x1="0" y1="0" x2="0" y2={drop} stroke="#F3C969" strokeOpacity="0.7" strokeWidth="1.2" />
      <g transform={`translate(0 ${drop}) scale(${s})`}>
        <circle cy="34" r="26" fill={`url(#${id}-light)`} />
        <path d="M-6 0 H6 L10 8 H-10 Z" fill="#E0B455" />
        <path d="M-14 8 H14 L18 22 L14 52 H-14 L-18 22 Z" fill="#FFCF6B" fillOpacity="0.92" stroke="#B98A2A" strokeWidth="1.5" />
        <path d="M-14 8 L0 52 L14 8 M-18 22 H18" stroke="#B98A2A" strokeWidth="1.2" fill="none" />
        <path d="M-10 52 H10 L6 60 H-6 Z" fill="#E0B455" />
      </g>
    </g>
  );
  return (
    <svg viewBox="0 0 400 220" preserveAspectRatio="xMaxYMid slice" className="h-full w-full">
      <defs>
        <radialGradient id={`${id}-light`} cx="0.5" cy="0.5" r="0.5"><stop offset="0" stopColor="#FFD27A" stopOpacity="0.55" /><stop offset="1" stopColor="#FFD27A" stopOpacity="0" /></radialGradient>
        <mask id={`${id}-moon`}><rect width="400" height="220" fill="white" /><circle cx="318" cy="58" r="36" fill="black" /></mask>
      </defs>
      {[[60, 30], [150, 70], [230, 24], [380, 120], [110, 130], [270, 140], [340, 180]].map(([x, y], i) => (
        <path key={i} transform={`translate(${x} ${y}) scale(${i % 2 ? 0.7 : 1})`} d="M0 -6 L1.6 -1.6 L6 0 L1.6 1.6 L0 6 L-1.6 1.6 L-6 0 L-1.6 -1.6 Z" fill="#FFE6A8" opacity="0.9" />
      ))}
      <circle cx="300" cy="70" r="42" fill="#F3C969" mask={`url(#${id}-moon)`} />
      {lantern(120, 34, 1)}{lantern(190, 70, 1.25)}{lantern(250, 22, 0.85)}
      {/* a quiet skyline */}
      <path d="M0 220 V196 H40 V178 C40 160 70 160 70 178 V196 H110 V150 L114 140 L118 150 V196 H160 V186 C160 162 210 162 210 186 V196 H260 V170 L264 160 L268 170 V196 H320 V184 C320 170 350 170 350 184 V196 H400 V220 Z" fill="#0C1033" opacity="0.85" />
    </svg>
  );
}

function BrandArt({ id }: { id: string }) {
  return (
    <svg viewBox="0 0 400 220" preserveAspectRatio="xMaxYMid slice" className="h-full w-full">
      <defs>
        <filter id={`${id}-glow`} x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="6" result="b" /><feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
        <radialGradient id={`${id}-iris`} cx="0.5" cy="0.5" r="0.5"><stop offset="0" stopColor="#0B2A18" /><stop offset="0.7" stopColor="#0E4D2C" /><stop offset="1" stopColor="#22E07A" /></radialGradient>
      </defs>
      {/* a film strip passing behind */}
      <g transform="translate(40 150) rotate(-14)" opacity="0.5">
        <rect width="420" height="44" fill="#0A1A11" stroke="#1F6B40" />
        {Array.from({ length: 21 }, (_, i) => <rect key={i} x={6 + i * 20} y="5" width="10" height="7" rx="1.5" fill="#1F6B40" />)}
        {Array.from({ length: 21 }, (_, i) => <rect key={i} x={6 + i * 20} y="32" width="10" height="7" rx="1.5" fill="#1F6B40" />)}
      </g>
      <g transform="translate(270 96)" filter={`url(#${id}-glow)`}>
        <path d="M-92 0 C-50 -58 50 -58 92 0 C50 58 -50 58 -92 0 Z" fill="none" stroke="#22E07A" strokeWidth="6" />
        <circle r="34" fill={`url(#${id}-iris)`} stroke="#22E07A" strokeWidth="4" />
        <circle r="13" fill="#03140A" />
        <circle cx="-9" cy="-10" r="5" fill="#FFFFFF" opacity="0.9" />
      </g>
      {[[150, 40], [370, 30], [390, 170], [180, 190]].map(([x, y], i) => (
        <circle key={i} cx={x} cy={y} r={i % 2 ? 1.6 : 2.4} fill="#22E07A" opacity="0.7" />
      ))}
    </svg>
  );
}
