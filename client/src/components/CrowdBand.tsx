import { Suspense, lazy } from "react";
import { t } from "@/lib/i18n";

// gsap and the canvas only ever appear on this page, so they load with it and nowhere else.
const CrowdCanvas = lazy(() => import("@/components/ui/crowd-canvas"));

/** The Open Peeps sprite sheet: 15 figures across, 7 down. */
const PEEPS = "https://cdn.21st.dev/assets/localized/abdb8990a7bef8c2f5af3e45f0a3c969c4b0603fba8be92e81347de4ea4e1ed7.png";

/**
 * A crowd walking under the header of the globe page, with the invitation sliding across it.
 *
 * Purely decorative: `pointer-events: none` throughout and `aria-hidden`, so clicks pass
 * through to nothing and a screen reader is not read a line of moving text on a loop. The
 * sentence it carries is also the page's point, so the page's own heading still says it.
 *
 * Full width on purpose. It sits outside the page's centred column, directly under the header,
 * and on the smallest screens it steps out past `main`'s side padding to reach both edges.
 */
export function CrowdBand() {
  const line = t("globe.meet");

  return (
    <div
      aria-hidden
      className="pointer-events-none relative h-[260px] w-auto select-none overflow-hidden border-b border-line max-[520px]:-mx-[0.9rem] sm:h-[340px]"
    >
      <Suspense fallback={null}>
        {/* The artwork is black line on white. Inverted, it becomes pale linework on this
            site's dark ground — the same people, drawn in the site's own light. */}
        <CrowdCanvas src={PEEPS} rows={15} cols={7} className="absolute inset-0 h-full w-full invert opacity-90" />
      </Suspense>

      {/* Two copies laid end to end, so the loop has no seam when the first slides off. */}
      <div className="rr-crowd-marquee absolute inset-x-0 top-[18%] flex whitespace-nowrap">
        {[0, 1].map((copy) => (
          <span key={copy} className="flex shrink-0">
            {Array.from({ length: 4 }, (_, i) => (
              <span
                key={i}
                className="px-8 font-display text-4xl font-bold tracking-tight text-accent drop-shadow-[0_2px_12px_rgba(0,0,0,0.85)] sm:text-6xl"
              >
                {line}
                <span className="px-8 text-ink-mute/40">✦</span>
              </span>
            ))}
          </span>
        ))}
      </div>

      {/* Fades the edges, so the crowd walks in out of the dark rather than from a hard line. */}
      <div className="absolute inset-0 bg-[linear-gradient(90deg,var(--color-surface)_0%,transparent_12%,transparent_88%,var(--color-surface)_100%)]" />
    </div>
  );
}

export default CrowdBand;
