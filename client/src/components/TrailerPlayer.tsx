import { useEffect, useRef } from "react";
import { createPortal } from "react-dom";
import { motion } from "motion/react";
import { X } from "lucide-react";
import { embedFor } from "@/components/MovieDialog";
import { formatRuntime } from "@/lib/format";
import { apiUrl } from "@/lib/platform";
import { t } from "@/lib/i18n";

export interface TrailerFilm {
  title: string;
  genre: string;
  releaseYear: number;
  durationMinutes: number;
  trailerUrl: string;
}

/**
 * The Watch button's screen: the film's trailer, full screen over the catalogue like a player
 * page of its own. The catalogue stays underneath, exactly where it was — the ✕ in the corner
 * (or Esc) closes the player and the reader carries on scrolling from the same card.
 *
 * A trailer stored with the site streams from our /api/trailers; a studio's official trailer
 * plays in YouTube's embedded player (its no-cookie domain, and without "related videos"
 * from other channels at the end).
 */
export function TrailerPlayer({ film, onClose }: { film: TrailerFilm; onClose: () => void }) {
  // Kept in a ref, so a parent re-rendering with a new arrow function does not tear down and
  // rebuild the listeners below while the trailer plays.
  const close = useRef(onClose);
  close.current = onClose;

  useEffect(() => {
    // Captured on the window, and stopped there: opened from the film dialog, Esc used to
    // reach the dialog's own listener too and close both at once.
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      e.stopPropagation();
      close.current();
    };
    window.addEventListener("keydown", onKey, true);
    // The page behind stays put while the player is open, and the button that opened it gets
    // the focus back afterwards.
    const overflow = document.body.style.overflow;
    const opener = document.activeElement as HTMLElement | null;
    document.body.style.overflow = "hidden";
    return () => {
      window.removeEventListener("keydown", onKey, true);
      document.body.style.overflow = overflow;
      opener?.focus?.({ preventScroll: true });
    };
  }, []);

  const source = embedFor(film.trailerUrl);
  const iframeSrc = source.kind === "iframe"
    ? source.src.replace("https://www.youtube.com/embed/", "https://www.youtube-nocookie.com/embed/")
      + (source.src.includes("?") ? "&" : "?") + "autoplay=1&rel=0&playsinline=1"
    : null;

  return createPortal(
    <motion.div
      role="dialog"
      aria-modal="true"
      aria-label={film.title}
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      className="fixed inset-0 z-[100] flex flex-col bg-black text-white"
      // React passes events up through the portal to whoever rendered the player. Opened from
      // the film dialog, a click here (the ✕ included) was also a click on the dialog's
      // backdrop, which closed the dialog as well.
      onClick={(e) => e.stopPropagation()}
      onKeyDown={(e) => e.stopPropagation()}
    >
      <header
        className="flex items-start gap-3 px-4 pb-3 sm:px-8"
        style={{ paddingTop: "calc(env(safe-area-inset-top) + 14px)" }}
      >
        <div className="min-w-0 flex-1">
          <p className="text-[11px] font-semibold uppercase tracking-[0.2em] text-accent">{t("movie.trailer", "Trailer")}</p>
          <h2 className="truncate font-display text-xl font-bold sm:text-2xl">{film.title}</h2>
          <p className="text-xs text-white/60">
            {film.genre} · {film.releaseYear} · {formatRuntime(film.durationMinutes)}
          </p>
        </div>
        {/* Large and high-contrast, so it is found at once on top of a dark trailer and is an
            easy target for a thumb. */}
        <button
          type="button"
          onClick={() => close.current()}
          aria-label={t("common.close", "Close")}
          autoFocus
          className="relative z-10 flex h-12 shrink-0 items-center gap-2 rounded-full bg-white px-3 text-black shadow-lg transition hover:bg-white/85 active:scale-95 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent sm:px-4"
        >
          <X size={24} strokeWidth={2.5} aria-hidden />
          <span className="hidden text-sm font-semibold sm:inline">{t("common.close", "Close")}</span>
        </button>
      </header>

      <div className="flex flex-1 items-center justify-center px-0 pb-6 sm:px-8">
        <div className="aspect-video w-full max-w-6xl overflow-hidden bg-black sm:rounded-xl">
          {iframeSrc ? (
            <iframe
              src={iframeSrc}
              title={film.title}
              className="h-full w-full"
              allow="autoplay; fullscreen; encrypted-media; picture-in-picture"
              allowFullScreen
            />
          ) : (
            <video src={apiUrl(source.src)} className="h-full w-full" controls autoPlay playsInline />
          )}
        </div>
      </div>
    </motion.div>,
    document.body,
  );
}
