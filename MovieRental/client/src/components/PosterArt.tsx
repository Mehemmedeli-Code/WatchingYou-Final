import type { ReactNode } from "react";

/**
 * Eight vector poster faces for the scatter hero. Drawn rather than photographed: the
 * brief asks for clean vector art, and inline SVG means the hero has no image requests
 * to wait on and stays crisp at any card size.
 */

const INK = "var(--color-surface)";
const RAISED = "var(--color-surface-raised)";
const BRASS = "var(--color-accent)";
const DIM = "var(--color-accent-dim)";
const PAPER = "var(--color-ink)";

function Frame({ children, bg = RAISED }: { children: ReactNode; bg?: string }) {
  return (
    <svg viewBox="0 0 100 140" preserveAspectRatio="xMidYMid slice" className="h-full w-full" aria-hidden="true">
      <rect width="100" height="140" fill={bg} />
      {children}
      <rect x="3" y="3" width="94" height="134" fill="none" stroke={DIM} strokeWidth="0.6" opacity="0.5" />
    </svg>
  );
}

const Noir = () => (
  <Frame bg={INK}>
    <path d="M0 90 L100 40 L100 140 L0 140 Z" fill={RAISED} />
    <circle cx="70" cy="34" r="15" fill={BRASS} opacity="0.9" />
    <rect x="18" y="66" width="9" height="52" fill={INK} />
    <path d="M14 66 h17 l-2 -8 h-13 Z" fill={INK} />
    <path d="M0 118 h100" stroke={DIM} strokeWidth="0.8" />
  </Frame>
);

const Western = () => (
  <Frame>
    <rect y="84" width="100" height="56" fill="#10261A" />
    <circle cx="50" cy="60" r="24" fill={BRASS} />
    <path d="M0 84 L22 62 L40 84 Z" fill={INK} opacity="0.85" />
    <path d="M46 84 L72 54 L100 84 Z" fill={INK} opacity="0.7" />
    <path d="M8 104 v22 M16 104 v22 M24 104 v22" stroke={DIM} strokeWidth="1.4" />
  </Frame>
);

const SciFi = () => (
  <Frame bg="#081410">
    <circle cx="50" cy="58" r="30" fill="none" stroke={BRASS} strokeWidth="0.8" />
    <circle cx="50" cy="58" r="20" fill="none" stroke={DIM} strokeWidth="0.6" />
    <ellipse cx="50" cy="58" rx="42" ry="8" fill="none" stroke={BRASS} strokeWidth="0.7" opacity="0.7" />
    <circle cx="50" cy="58" r="7" fill={BRASS} />
    <path d="M50 100 l10 30 h-20 Z" fill={PAPER} opacity="0.8" />
  </Frame>
);

const Romance = () => (
  <Frame bg="#0D2318">
    <circle cx="38" cy="56" r="17" fill={BRASS} opacity="0.85" />
    <circle cx="62" cy="56" r="17" fill={PAPER} opacity="0.75" />
    <circle cx="50" cy="56" r="9" fill="#123021" />
    <path d="M20 104 q30 -18 60 0" fill="none" stroke={DIM} strokeWidth="1.2" />
  </Frame>
);

const Documentary = () => (
  <Frame>
    <rect x="16" y="42" width="52" height="34" rx="3" fill={INK} stroke={DIM} strokeWidth="0.7" />
    <circle cx="34" cy="59" r="9" fill={BRASS} />
    <circle cx="34" cy="59" r="4" fill={INK} />
    <path d="M68 50 l18 -8 v34 l-18 -8 Z" fill={DIM} />
    <rect x="16" y="90" width="68" height="2" fill={PAPER} opacity="0.35" />
    <rect x="16" y="98" width="44" height="2" fill={PAPER} opacity="0.2" />
  </Frame>
);

const Horror = () => (
  <Frame bg="#0A120D">
    <path d="M50 20 C74 20 84 44 78 72 C72 100 60 118 50 126 C40 118 28 100 22 72 C16 44 26 20 50 20 Z" fill={RAISED} />
    <circle cx="40" cy="62" r="5" fill={BRASS} />
    <circle cx="60" cy="62" r="5" fill={BRASS} />
    <path d="M38 88 q12 10 24 0" fill="none" stroke={DIM} strokeWidth="1.6" />
  </Frame>
);

const Animation = () => (
  <Frame bg="#0E1F16">
    <circle cx="34" cy="48" r="14" fill={BRASS} />
    <circle cx="66" cy="70" r="20" fill={DIM} opacity="0.7" />
    <circle cx="42" cy="98" r="11" fill={PAPER} opacity="0.6" />
    <path d="M10 130 q40 -26 82 -6" fill="none" stroke={BRASS} strokeWidth="1" />
  </Frame>
);

const Musical = () => (
  <Frame bg="#14291C">
    {[0, 1, 2, 3, 4].map((i) => (
      <rect key={i} x={14 + i * 16} y={100 - i * 9} width="8" height={24 + i * 12} fill={i % 2 ? DIM : BRASS} />
    ))}
    <circle cx="70" cy="34" r="11" fill="none" stroke={PAPER} strokeWidth="1.2" opacity="0.6" />
  </Frame>
);

export const POSTERS: { key: string; alt: string; render: () => ReactNode }[] = [
  { key: "musical", alt: "Musical poster", render: () => <Musical /> },
  { key: "animation", alt: "Animation poster", render: () => <Animation /> },
  { key: "horror", alt: "Horror poster", render: () => <Horror /> },
  { key: "documentary", alt: "Documentary poster", render: () => <Documentary /> },
  { key: "romance", alt: "Romance poster", render: () => <Romance /> },
  { key: "scifi", alt: "Science fiction poster", render: () => <SciFi /> },
  { key: "western", alt: "Western poster", render: () => <Western /> },
  { key: "noir", alt: "Film noir poster", render: () => <Noir /> },
];
