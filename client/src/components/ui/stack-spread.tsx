// Adapted from the Hyperiux Vault "stack spread" pattern.
// Changes for this project: the card faces are the eight letters of WATCHING instead of remote
// photographs, the palette follows the project's tokens, and the copy is passed in as
// props so the same stage can headline different pages.

"use client";

import {
  motion,
  useScroll,
  useTransform,
  useReducedMotion,
  useMotionValue,
  useSpring,
  useMotionValueEvent,
  type MotionValue,
} from "motion/react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { LetterCard } from "@/components/ui/letter-card";

// The eight cards spell WATCHING around the eye: W A T C along the top, H I N G beneath it,
// so the word reads left to right, top to bottom. On a phone the two columns read the same
// way, row by row: W A / T C / H I / N G.
const WORD = "WATCHING";
const COLS = [-27, -9, 9, 27];      // vw from the centre
const ROWS = [-23, 29];             // vh from the centre: above the eye, below it — clear of the header
const SM_ROWS = [-40, -19, 20, 40]; // phone layout, two letters per row

// Where each card sits in the stack before the scatter, and how far it is fanned.
const FAN: { x: number; y: number; r: number }[] = [
  { x: -8, y: -10, r: -18 }, { x: 14, y: -10, r: 20 }, { x: -16, y: 0, r: -4 }, { x: 1, y: -10, r: -2 },
  { x: 18, y: 1, r: 6 }, { x: -6, y: 10, r: 6 }, { x: 8, y: 7, r: 3 }, { x: 20, y: 12, r: -7 },
];

// array order = stack order, back (z 2) -> front (z 9)
const CARDS: StackSpreadCard[] = [...WORD].map((letter, i) => ({
  item: { node: <LetterCard letter={letter} />, alt: letter },
  stackOffset: { x: FAN[i].x, y: FAN[i].y },
  stackRotate: FAN[i].r,
  target: { x: COLS[i % 4], y: ROWS[Math.floor(i / 4)], rotate: 0, scale: 1, w: 11, h: 18 },
  targetSm: { x: i % 2 === 0 ? -22 : 22, y: SM_ROWS[Math.floor(i / 2)] },
  z: 2 + i,
}));

// ---------------------------------------------------------------------------
// Mechanism
// ---------------------------------------------------------------------------

const SCATTER_START = 0.12;
const SCATTER_END = 0.9;

const PARALLAX_X = 2.6;
const PARALLAX_Y = 2.2;
const PARALLAX_SPRING = { stiffness: 90, damping: 22, mass: 0.6 };
const parallaxDepth = (i: number, total: number) => (total <= 1 ? 1 : 0.55 + (i / (total - 1)) * 0.75);

const RESPONSIVE = {
  desktop: {
    scale: null as number | null,
    small: false,
    colX: null as number | null,
    card: null as { w: number; h: number } | null,
  },
  small: {
    scale: 0.72,
    small: true,
    colX: 22,
    card: { w: 40, h: 20 },
  },
};

function useResponsive() {
  const [r, setR] = useState(RESPONSIVE.desktop);
  useEffect(() => {
    // Touch vs. mouse, not raw width: a narrow but mouse-driven frame keeps the desktop
    // scatter and pointer parallax; only real touch devices drop to the stacked column.
    const mq = window.matchMedia("(pointer: coarse)");
    const read = () => setR(mq.matches ? RESPONSIVE.small : RESPONSIVE.desktop);
    read();
    mq.addEventListener("change", read);
    return () => mq.removeEventListener("change", read);
  }, []);
  return r;
}

function usePointerParallax(active: boolean, enabled: boolean) {
  const rawX = useMotionValue(0);
  const rawY = useMotionValue(0);
  const x = useSpring(rawX, PARALLAX_SPRING);
  const y = useSpring(rawY, PARALLAX_SPRING);

  useEffect(() => {
    if (!enabled) return;

    if (!active) {
      rawX.set(0);
      rawY.set(0);
      return;
    }

    const onMove = (event: PointerEvent) => {
      rawX.set((event.clientX / window.innerWidth) * 2 - 1);
      rawY.set((event.clientY / window.innerHeight) * 2 - 1);
    };
    const onLeave = () => {
      rawX.set(0);
      rawY.set(0);
    };

    window.addEventListener("pointermove", onMove, { passive: true });
    document.addEventListener("pointerleave", onLeave);

    return () => {
      window.removeEventListener("pointermove", onMove);
      document.removeEventListener("pointerleave", onLeave);
    };
  }, [active, enabled, rawX, rawY]);

  return { x, y };
}

export interface StackSpreadItem {
  /** Inline art for the card face. Preferred — no network request, scales cleanly. */
  node?: ReactNode;
  /** Remote image, used when `node` is absent. */
  src?: string;
  alt?: string;
}

export interface StackSpreadTarget {
  x: number;
  y: number;
  rotate: number;
  scale?: number;
  w: number;
  h: number;
}

export interface StackSpreadCard {
  item: StackSpreadItem;
  target: StackSpreadTarget;
  /** final x/y (vw/vh) for tablet + mobile; falls back to `target` */
  targetSm?: { x: number; y: number };
  /** angle while clustered */
  stackRotate?: number;
  /** offset while clustered (vw/vh) */
  stackOffset?: { x: number; y: number };
  /** paint order, higher on top */
  z?: number;
}

function Card({
  card,
  progress,
  reduce,
  clusterRotation,
  scaleMul,
  isSmall,
  colX,
  fixedCard,
  stackScale,
  cardRadius,
  pointer,
  depth,
}: {
  card: StackSpreadCard;
  progress: MotionValue<number>;
  reduce: boolean | null;
  clusterRotation: boolean;
  scaleMul: number | null;
  isSmall: boolean;
  colX: number | null;
  fixedCard: { w: number; h: number } | null;
  stackScale: number;
  cardRadius: number;
  pointer: { x: MotionValue<number>; y: MotionValue<number> };
  depth: number;
}) {
  const { item, target } = card;

  const flat = reduce === true;
  const stackRotate = flat ? 0 : clusterRotation ? (card.stackRotate ?? 0) : 0;
  const stackOffset = card.stackOffset ?? { x: 0, y: 0 };
  const restScale = scaleMul ?? target.scale ?? 1;

  // final resting spot: column grid on small screens, scatter on desktop
  const sm = isSmall && card.targetSm ? card.targetSm : null;
  const endX = sm ? (colX != null ? Math.sign(sm.x) * colX : sm.x) : target.x;
  const endY = sm ? sm.y : target.y;
  const endRotate = flat || isSmall ? 0 : target.rotate;

  // -50% keeps card centred on its anchor
  const translate = useTransform([progress, pointer.x, pointer.y], ([p, px, py]: number[]) => {
    const tx = stackOffset.x + (endX - stackOffset.x) * p;
    const ty = stackOffset.y + (endY - stackOffset.y) * p;
    const drift = depth * p;
    const dx = tx - px * PARALLAX_X * drift;
    const dy = ty - py * PARALLAX_Y * drift;
    return `calc(-50% + ${dx}vw) calc(-50% + ${dy}vh)`;
  });
  const rotate = useTransform(progress, [0, 1], [stackRotate, endRotate]);
  const scale = useTransform(progress, [0, 1], [stackScale, restScale]);

  return (
    <motion.div
      className="absolute left-1/2 top-1/2 will-change-transform"
      style={{
        width: `${fixedCard ? fixedCard.w : target.w}vw`,
        height: `${fixedCard ? fixedCard.h : target.h}vh`,
        zIndex: card.z ?? 1,
        translate,
        rotate,
        scale,
      }}
    >
      <CardFace item={item} cardRadius={cardRadius} />
    </motion.div>
  );
}

function CardFace({ item, cardRadius }: { item: StackSpreadItem; cardRadius: number }) {
  return (
    <div
      className="relative h-full w-full overflow-hidden shadow-[0_18px_40px_-24px_rgba(0,0,0,0.9)] max-md:rounded-[4vw]"
      style={{ borderRadius: `${cardRadius}px` }}
      role="img"
      aria-label={item.alt ?? ""}
    >
      {item.node ? (
        <div className="absolute inset-0">{item.node}</div>
      ) : (
        <img
          src={item.src}
          alt={item.alt ?? ""}
          draggable={false}
          className="absolute inset-0 h-full w-full object-cover"
        />
      )}
    </div>
  );
}

interface StackSpreadStageProps extends StackSpreadProps {
  cards: StackSpreadCard[];
}

function StackSpreadStage({
  cards,
  scrollLength = 350,
  bgColor = "var(--color-surface)",
  clusterRotation = true,
  stackScale = 0.82,
  cardRadius = 8,
  textColor = "var(--color-ink)",
  textFadeStart = 0.3,
  showScrollHint = true,
  headline = "",
  headlineMuted = "",
  headlineTail = "",
  subtitle = "",
  children,
  centerpiece,
}: StackSpreadStageProps) {
  const wrapRef = useRef<HTMLDivElement>(null);
  const reduce = useReducedMotion();
  const { scale: scaleMul, small: isSmall, colX, card: fixedCard } = useResponsive();

  const { scrollYProgress } = useScroll({
    target: wrapRef,
    offset: ["start start", "end end"],
  });

  // hold, scatter, then settle
  const progress = useTransform(scrollYProgress, [0, SCATTER_START, SCATTER_END, 1], [0, 0, 1, 1]);

  const [spread, setSpread] = useState(false);
  useMotionValueEvent(progress, "change", (p) => {
    setSpread((was) => (was ? p > 0.985 : p >= 0.999));
  });
  const parallaxEnabled = reduce !== true && !isSmall;
  const pointer = usePointerParallax(spread, parallaxEnabled);

  const noScale = reduce === true;
  const copyOpacity = useTransform(progress, [textFadeStart, textFadeStart + 0.35], [0, 1]);
  const copyScale = useTransform(progress, [textFadeStart, 0.9], [0.85, 1]);
  const hintOpacity = useTransform(progress, [0, SCATTER_START], [1, 0]);

  return (
    <section ref={wrapRef} className="relative w-full" style={{ height: `${scrollLength}vh`, backgroundColor: bgColor }}>
      <div className="sticky top-0 h-screen w-full overflow-hidden">
        {/* centre: either a single graphic, or the headline block */}
        {centerpiece ? (
          <motion.div
            className="pointer-events-none absolute inset-0 z-[5] flex items-center justify-center"
            style={{ opacity: copyOpacity, scale: noScale ? 1 : copyScale }}
          >
            <div className="pointer-events-auto">{centerpiece}</div>
          </motion.div>
        ) : (
        <motion.div
          className="pointer-events-none absolute inset-0 z-[5] flex flex-col items-center justify-center px-6 text-center max-md:px-8"
          style={{ opacity: copyOpacity, scale: noScale ? 1 : copyScale }}
        >
          <h1
            className="font-display mx-auto w-full max-w-[18ch] text-balance text-[clamp(2.25rem,6vw,5.5rem)] leading-[0.95]! font-extrabold tracking-tight"
            style={{ color: textColor }}
          >
            {headline}
            <span className="opacity-60">{headlineMuted}</span>
            {headlineTail}
          </h1>
          <p
            className="mt-5 w-full max-w-[44ch] text-[clamp(0.95rem,1.3vw,1.2rem)] leading-relaxed tracking-tight"
            style={{ color: textColor, opacity: 0.6 }}
          >
            {subtitle}
          </p>
          {children ? <div className="pointer-events-auto mt-[2vw] max-md:mt-6">{children}</div> : null}
        </motion.div>
        )}

        {/* scattering cards */}
        <div className="absolute inset-0 z-10">
          {cards.map((card, i) => (
            <Card
              key={i}
              card={card}
              progress={progress}
              reduce={reduce}
              clusterRotation={clusterRotation}
              scaleMul={scaleMul}
              isSmall={isSmall}
              colX={colX}
              fixedCard={fixedCard}
              stackScale={stackScale}
              cardRadius={cardRadius}
              pointer={pointer}
              depth={parallaxEnabled ? parallaxDepth(i, cards.length) : 0}
            />
          ))}
        </div>

        {/* scroll hint */}
        {showScrollHint && (
          <motion.div
            className="pointer-events-none absolute inset-x-0 bottom-8 z-20 flex flex-col items-center gap-1.5 text-[clamp(0.65rem,0.8vw,0.8rem)] font-medium tracking-[0.2em]"
            style={{ color: textColor, opacity: hintOpacity }}
          >
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth={2}
              strokeLinecap="round"
              strokeLinejoin="round"
              className="animate-bounce max-md:h-[4vw] max-md:w-[4vw]"
              aria-hidden="true"
            >
              <path d="m6 9 6 6 6-6" />
            </svg>
          </motion.div>
        )}
      </div>
    </section>
  );
}

export interface StackSpreadProps {
  /** scatter scroll distance, in vh */
  scrollLength?: number;
  bgColor?: string;
  /** fan the clustered stack (default) or start flat */
  clusterRotation?: boolean;
  /** scale of the cards while clustered, before the scatter */
  stackScale?: number;
  /** corner radius on each card, in px (desktop only — mobile keeps its responsive radius) */
  cardRadius?: number;
  /** color of the centre headline and subtitle */
  textColor?: string;
  /** scroll progress (0-1) where the centre text starts fading in */
  textFadeStart?: number;
  /** show the scroll hint at the bottom until the scatter begins */
  showScrollHint?: boolean;
  headline?: string;
  headlineMuted?: string;
  headlineTail?: string;
  subtitle?: string;
  /** call to action rendered under the subtitle once the copy has faded in */
  children?: ReactNode;
  /** a single graphic in the middle instead of the headline, subtitle and call to action */
  centerpiece?: ReactNode;
}

export default function StackSpread(props: StackSpreadProps) {
  return <StackSpreadStage cards={CARDS} {...props} />;
}
