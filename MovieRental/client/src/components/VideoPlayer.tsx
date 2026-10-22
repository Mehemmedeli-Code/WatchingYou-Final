/**
 * Plays a film from the authorised streaming endpoint. `preload="metadata"` keeps the page
 * light when several are listed: the browser fetches enough for a duration and a poster
 * frame, then waits for a real play before pulling the rest.
 */
export function VideoPlayer({ src, title }: { src: string; title: string }) {
  return (
    <video
      className="w-full rounded-lg border border-line bg-black"
      src={src}
      controls
      preload="metadata"
      playsInline
      aria-label={title}
    />
  );
}
