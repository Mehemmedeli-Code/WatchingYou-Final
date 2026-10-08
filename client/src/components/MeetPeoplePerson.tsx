/// <reference types="vite/client" />
import { t } from "@/lib/i18n";

/**
 * Two 3D-rendered friends (Blender / Pixar style) beside the globe: he leans in and points at
 * it in a black WatchingYou sweater with the green eye on the chest; she has a bucket of
 * popcorn. The page's invitation is in a speech bubble above them.
 *
 * The picture ships with the site (public/img), cut out on a transparent background, so it
 * sits on either theme. Decorative: the sentence is also the page's heading, so it is hidden
 * from screen readers.
 */
export function MeetPeoplePerson({ className }: { className?: string }) {
  const line = t("globe.meetPeople", "Meet the people!");
  return (
    <div className={"relative select-none pt-16 " + (className ?? "")} aria-hidden>
      {/* The speech bubble, above the pair, its tail pointing down towards them. */}
      <div className="absolute right-4 top-0 z-10 w-max max-w-[230px] rounded-2xl border-[3px] border-black bg-white px-4 py-2 text-center font-display text-sm font-bold leading-snug text-black shadow-[3px_3px_0_#000] sm:text-base">
        {line}
        <span className="absolute -bottom-[14px] left-10 h-0 w-0 border-x-[10px] border-t-[13px] border-x-transparent border-t-black" />
        <span className="absolute -bottom-[9px] left-[43px] h-0 w-0 border-x-[7px] border-t-[10px] border-x-transparent border-t-white" />
      </div>
      <img
        src={`${import.meta.env.BASE_URL}img/meet-people.webp`}
        alt=""
        width={757}
        height={720}
        loading="lazy"
        draggable={false}
        // A faint green glow keeps the black sweater from melting into the dark card.
        className="h-[260px] w-auto drop-shadow-[0_0_22px_rgba(34,224,122,0.2)] sm:h-[300px]"
      />
    </div>
  );
}
