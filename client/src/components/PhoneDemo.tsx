import { useEffect, useRef, useState } from "react";
import { BatteryFull, Signal, Wifi } from "lucide-react";

const WIDE = "(min-width: 1024px)";

/**
 * A phone beside the poster ribbon, playing a recording of the site in somebody's hand:
 * scrolling the catalogue, turning the ribbon, picking seats, answering a message.
 *
 * Computers only. On a phone it would be a phone inside a phone, so below WIDE nothing renders
 * and the video is never requested. It plays only while on screen, and not at all for anyone
 * who asked their system for less motion (they get its first frame).
 *
 * The recording: tools/phone-demo (headless Edge on a throwaway copy of the site).
 */
export function PhoneDemo() {
  const [wide, setWide] = useState(() => window.matchMedia?.(WIDE).matches ?? false);
  const video = useRef<HTMLVideoElement>(null);

  useEffect(() => {
    const query = window.matchMedia?.(WIDE);
    if (!query) return;
    const update = () => setWide(query.matches);
    query.addEventListener("change", update);
    return () => query.removeEventListener("change", update);
  }, []);

  useEffect(() => {
    const element = video.current;
    if (!element || window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) return;
    element.muted = true;   // React does not always write the attribute; autoplay needs it
    const seen = new IntersectionObserver(([entry]) => {
      if (entry.isIntersecting) void element.play().catch(() => {});
      else element.pause();
    });
    seen.observe(element);
    return () => seen.disconnect();
  }, [wide]);

  if (!wide) return null;

  return (
    <div
      aria-hidden
      className="relative w-[236px] shrink-0 select-none rounded-[2.6rem] border border-white/10 bg-[#050605] p-[9px] shadow-[0_30px_80px_-24px_rgba(0,230,118,0.35),0_0_0_1px_rgba(0,0,0,0.6)]"
    >
      {/* Side buttons, for the silhouette. */}
      <span className="absolute -left-[3px] top-24 h-10 w-[3px] rounded-l bg-[#1a1d1a]" />
      <span className="absolute -left-[3px] top-36 h-14 w-[3px] rounded-l bg-[#1a1d1a]" />
      <span className="absolute -right-[3px] top-32 h-16 w-[3px] rounded-r bg-[#1a1d1a]" />

      <div className="overflow-hidden rounded-[2.05rem] bg-[#0a0c0a]">
        {/* The status bar sits above the page, as on a real phone, so the island covers nothing. */}
        <div className="relative flex h-7 items-center justify-between px-5 text-[10px] font-semibold text-white/90">
          <span>9:41</span>
          <span className="absolute left-1/2 top-1.5 h-[17px] w-[64px] -translate-x-1/2 rounded-full bg-black" />
          <span className="flex items-center gap-1">
            <Signal size={10} strokeWidth={2.6} />
            <Wifi size={10} strokeWidth={2.6} />
            <BatteryFull size={13} strokeWidth={2} />
          </span>
        </div>
        <video
          ref={video}
          src="/media/phone-demo.mp4"
          poster="/media/phone-demo.jpg"
          muted
          loop
          playsInline
          preload="auto"
          disablePictureInPicture
          className="block aspect-[390/844] w-full"
        />
      </div>
    </div>
  );
}

export default PhoneDemo;
