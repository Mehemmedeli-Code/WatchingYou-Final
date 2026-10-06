import { useEffect, useId, useState } from "react";
import { motion, useMotionValue, useReducedMotion, useSpring, useTransform } from "motion/react";

/**
 * The brand's eye, large, in the middle of the home page's scatter. It replaces a headline
 * the scattered posters kept covering: the eye says "WatchingYou" without a word, and it
 * cannot be half-hidden the way a line of text could.
 *
 * It watches: the pupil follows the pointer, and every few seconds the eye blinks. Both stop
 * under reduced motion. The whole eye is a link down to the catalogue.
 */
export function HeroEye({ href = "#catalogue", label }: { href?: string; label: string }) {
  const id = useId().replace(/:/g, "");
  const reduce = useReducedMotion();

  // Pointer position as -1..1 across the window. A stiff, light spring: the gaze snaps to the
  // pointer almost at once, with just enough give that it does not look mechanical.
  const rawX = useMotionValue(0);
  const rawY = useMotionValue(0);
  const x = useSpring(rawX, { stiffness: 520, damping: 32, mass: 0.2 });
  const y = useSpring(rawY, { stiffness: 520, damping: 32, mass: 0.2 });
  const irisX = useTransform(x, (v) => v * 9);
  const irisY = useTransform(y, (v) => v * 5);

  useEffect(() => {
    if (reduce) return;
    const move = (e: PointerEvent) => {
      rawX.set((e.clientX / window.innerWidth) * 2 - 1);
      rawY.set((e.clientY / window.innerHeight) * 2 - 1);
    };
    window.addEventListener("pointermove", move, { passive: true });
    return () => window.removeEventListener("pointermove", move);
  }, [reduce, rawX, rawY]);

  // A blink every four to seven seconds — irregular, the way a real one is.
  const [blink, setBlink] = useState(false);
  useEffect(() => {
    if (reduce) return;
    let timer: number;
    const schedule = () => {
      timer = window.setTimeout(() => {
        setBlink(true);
        window.setTimeout(() => setBlink(false), 140);
        schedule();
      }, 4000 + Math.random() * 3000);
    };
    schedule();
    return () => window.clearTimeout(timer);
  }, [reduce]);

  const lid = "M4 44C4 44 26 8 64 8s60 36 60 36-22 36-60 36S4 44 4 44Z";

  return (
    <a
      href={href}
      aria-label={label}
      className="group block rounded-full focus-visible:outline-4 focus-visible:outline-offset-8 focus-visible:outline-accent"
      style={{ width: "min(34vh, 26vw)", minWidth: 170 }}
    >
      <svg viewBox="0 0 128 88" className="h-auto w-full overflow-visible drop-shadow-[0_0_40px_rgba(0,230,118,0.35)] transition-transform duration-500 group-hover:scale-[1.04]" aria-hidden="true">
        <defs>
          <radialGradient id={`${id}-iris`} cx="42%" cy="38%" r="62%">
            <stop offset="0%" stopColor="#9BFFC6" />
            <stop offset="55%" stopColor="#00E676" />
            <stop offset="100%" stopColor="#0B7A3F" />
          </radialGradient>
          <radialGradient id={`${id}-white`} cx="50%" cy="45%" r="60%">
            <stop offset="0%" stopColor="#0E1A13" />
            <stop offset="100%" stopColor="#040806" />
          </radialGradient>
          <filter id={`${id}-glow`} x="-30%" y="-60%" width="160%" height="220%">
            <feGaussianBlur stdDeviation="2.4" result="blur" />
            <feMerge><feMergeNode in="blur" /><feMergeNode in="SourceGraphic" /></feMerge>
          </filter>
          <clipPath id={`${id}-lid`}><path d={lid} /></clipPath>
        </defs>

        {/* The eye closes by squashing vertically about its centre line. */}
        <motion.g
          style={{ originX: "64px", originY: "44px" }}
          animate={{ scaleY: blink ? 0.06 : 1 }}
          transition={{ duration: 0.07, ease: "easeInOut" }}
        >
          <g filter={`url(#${id}-glow)`}>
            <path d={lid} fill={`url(#${id}-white)`} stroke="#00E676" strokeWidth="4" strokeLinejoin="round" />
            <g clipPath={`url(#${id}-lid)`}>
              <motion.g style={{ x: irisX, y: irisY }}>
                <circle cx="64" cy="44" r="27" fill={`url(#${id}-iris)`} />
                {/* Fine rays in the iris, so it reads as an eye close up and not a green disc. */}
                {Array.from({ length: 24 }, (_, i) => {
                  const a = (i / 24) * Math.PI * 2;
                  return (
                    <line key={i} x1={64 + Math.cos(a) * 15} y1={44 + Math.sin(a) * 15}
                      x2={64 + Math.cos(a) * 25} y2={44 + Math.sin(a) * 25}
                      stroke="#0B7A3F" strokeWidth="0.7" opacity="0.55" />
                  );
                })}
                <circle cx="64" cy="44" r="27" fill="none" stroke="#063D20" strokeWidth="1.5" />
                <circle cx="66" cy="45" r="12.5" fill="#030A06" />
                <circle cx="56.5" cy="34.5" r="5" fill="#EAFFF2" />
                <circle cx="71" cy="51" r="2" fill="#EAFFF2" opacity="0.7" />
              </motion.g>
            </g>
          </g>
        </motion.g>
      </svg>
    </a>
  );
}
