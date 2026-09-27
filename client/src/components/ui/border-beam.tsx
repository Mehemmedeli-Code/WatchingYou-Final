import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * A travelling beam along the bottom edge of an input, in the site's accent green.
 *
 * Written rather than installed. The `border-beam` package on npm does the same effect well,
 * but its palette is four fixed variants — colorful, mono, ocean, sunset — with no green, no
 * colour prop and no CSS variables to override. Tinting it with a filter would have coloured
 * the input's own text along with the beam. Forty lines of CSS buys exact tokens, a dependency
 * we do not carry, and a beam that follows the palette if it ever changes again.
 *
 * `prefers-reduced-motion` stops the travel and leaves a still underline: the affordance
 * survives, the movement does not.
 */
export function BorderBeam({
  children,
  className,
  active = true,
  radius = "rounded-md",
}: {
  children: ReactNode;
  className?: string;
  /** Set false to hold the beam still — useful while a field is disabled. */
  active?: boolean;
  /** Match the wrapped control's own corner radius, or the rim sits proud of it. */
  radius?: string;
}) {
  return (
    <div className={cn("rr-beam relative", radius, className)} data-active={active ? "1" : "0"}>
      {children}
      {/* Purely decorative and never in the way of a click. */}
      <span className="rr-beam-line" aria-hidden />
      <span className="rr-beam-glow" aria-hidden />
    </div>
  );
}

export default BorderBeam;
