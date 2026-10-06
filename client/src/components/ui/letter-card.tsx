import type { CSSProperties } from "react";

/**
 * One letter of WATCHING, as a frame of film: a neon letter glowing in the dark, sprocket
 * holes along the top and bottom edge. Eight of them scatter out of a stack on the home page
 * and settle around the eye, spelling the name.
 *
 * Built in HTML rather than a fixed SVG: the cards are sized in vw and vh, so their shape
 * changes with the screen, and an SVG would crop its own film edges on a wide one. Here the
 * bands always span the card and the letter is sized to the card (container query units).
 *
 * Fixed colours, not theme tokens — a neon sign looks the same in daylight and at night, and
 * the card must not wash out in the light theme.
 */
const sprockets: CSSProperties = {
  height: "12%",
  background:
    "radial-gradient(closest-side, #0E3A22 70%, transparent 74%) center / 16% 70% repeat-x, #020503",
};

export function LetterCard({ letter }: { letter: string }) {
  return (
    <div
      className="relative flex h-full w-full flex-col overflow-hidden"
      style={{ containerType: "size", background: "radial-gradient(ellipse at 50% 48%, #0B2A18 0%, #030705 75%)" }}
    >
      <div style={sprockets} />
      <div className="relative flex flex-1 items-center justify-center border-y border-[#00E676]/30">
        {/* faint scan lines, the texture of a projected frame */}
        <div
          className="pointer-events-none absolute inset-0"
          style={{ background: "repeating-linear-gradient(0deg, rgba(0,230,118,0.05) 0 1px, transparent 1px 4px)" }}
        />
        <span
          className="relative select-none font-extrabold leading-none"
          style={{
            fontFamily: "Poppins, Inter, system-ui, sans-serif",
            fontSize: "min(58cqh, 82cqw)",
            color: "#9BFFC6",
            textShadow: "0 0 6px #00E676, 0 0 18px #00E676, 0 0 42px rgba(0,230,118,0.55)",
          }}
        >
          {letter}
        </span>
      </div>
      <div style={sprockets} />
    </div>
  );
}
