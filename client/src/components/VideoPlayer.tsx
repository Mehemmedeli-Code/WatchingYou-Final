import { useRef } from "react";
import { cn } from "@/lib/utils";

/**
 * Plays a film from the authorised streaming endpoint. `preload="metadata"` keeps the page
 * light when several are listed: the browser fetches enough for a duration and a poster
 * frame, then waits for a real play before pulling the rest.
 *
 * `onWatch` is a view: it fires when the film starts, and again when it is played once more
 * after it ended. Pausing and resuming, or seeking, is still the same view.
 */
export function VideoPlayer({ src, title, onWatch, autoPlay, className }: {
  src: string; title: string; onWatch?: () => void; autoPlay?: boolean; className?: string;
}) {
  const counted = useRef(false);
  return (
    <video
      className={cn("w-full rounded-lg border border-line bg-black", className)}
      src={src}
      controls
      autoPlay={autoPlay}
      preload="metadata"
      playsInline
      aria-label={title}
      onPlay={() => { if (!counted.current) { counted.current = true; onWatch?.(); } }}
      onEnded={() => { counted.current = false; }}
    />
  );
}
