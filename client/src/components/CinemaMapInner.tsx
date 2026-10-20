import { useEffect, useRef, useState } from "react";
import { Check, ChevronDown, MapPin } from "lucide-react";
import { VenueMap, MapMarker } from "@/components/ui/venue-map";
import { CITIES } from "@/lib/format";
import { flagUrl } from "@/lib/worldCities";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";

import type { MappedVenue } from "@/components/CinemaMap";

/**
 * Where the cinemas are, one capital at a time. Clicking a pin filters the listing below to
 * that building, which is the only reason to put a map on a listings page at all — a map you
 * cannot act on is a picture. The view is fixed: the pins are what is clicked, and the flags
 * in the corner move it to another city.
 */
function CinemaMap({
  venues,
  selectedId,
  onSelect,
  city,
  onCity,
}: {
  venues: MappedVenue[];
  selectedId?: string | null;
  onSelect?: (venueId: string | null) => void;
  city: string;
  onCity: (city: string) => void;
}) {
  const here = CITIES.find((c) => c.city === city) ?? CITIES[0];
  const placed = venues.filter((v) => v.city === here.city && (v.latitude !== 0 || v.longitude !== 0));

  // Every cinema of the city in view, with its name: Baku's four are a few streets apart,
  // Ankara's twelve kilometres. The view is fixed, so it has to be right without the visitor
  // zooming — and on a phone as well as a desktop, so the map fits them at its real size.
  const points = placed.map((v) => [v.longitude, v.latitude] as [number, number]);

  return (
    <div className="mb-6">
      {/* The city picker sits above the map, not on it: nothing covers the streets or pins. */}
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <p className="flex items-center gap-2 text-sm text-ink">
          <MapPin size={15} className="text-accent" aria-hidden />
          <span className="font-medium">{t(`city.${here.city}`, here.city)}</span>
          <span className="text-ink-mute">· {placed.length} {t("map.cinemas", "cinemas")}</span>
        </p>
        <CityPicker city={here.city} onCity={onCity} />
      </div>

      <div className="h-72 overflow-hidden rounded-xl border border-line sm:h-96">
        <VenueMap center={here.centre} zoom={11} fit={points} locked>
          {placed.map((venue) => {
            const active = venue.id === selectedId;
            return (
              <MapMarker
                key={venue.id}
                longitude={venue.longitude}
                latitude={venue.latitude}
                onClick={() => onSelect?.(active ? null : venue.id)}
              >
                {/* The box is the pin alone, lifted by half: its tip lands on the cinema. The name
                    hangs below, outside the box. Lifting pin and name together put the point
                    under the name, and a cinema at the top of the map lost its pin over the edge. */}
                <div className="group relative -translate-y-1/2 cursor-pointer">
                  <MapPin
                    size={active ? 32 : 26}
                    aria-hidden
                    className="block drop-shadow-lg transition-all"
                    style={{ color: "#FF3B30", fill: active ? "#FF3B30" : "#7a1410" }}
                  />
                  <span
                    className={
                      "absolute left-1/2 top-full mt-0.5 -translate-x-1/2 whitespace-nowrap rounded px-1.5 py-0.5 text-[11px] font-medium shadow " +
                      (active ? "bg-accent text-surface" : "bg-surface/90 text-ink")
                    }
                  >
                    {venue.name}
                  </span>
                </div>
              </MapMarker>
            );
          })}
        </VenueMap>
      </div>
    </div>
  );
}

/** One button with the current city's flag; the others open beneath it. */
function CityPicker({ city, onCity }: { city: string; onCity: (city: string) => void }) {
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  const here = CITIES.find((c) => c.city === city) ?? CITIES[0];

  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => { if (!box.current?.contains(event.target as Node)) setOpen(false); };
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") setOpen(false); };
    document.addEventListener("pointerdown", outside);
    document.addEventListener("keydown", escape);
    return () => { document.removeEventListener("pointerdown", outside); document.removeEventListener("keydown", escape); };
  }, [open]);

  return (
    <div ref={box} className="relative">
      <button type="button" onClick={() => setOpen((o) => !o)} aria-haspopup="listbox" aria-expanded={open}
        aria-label={`${t("map.city", "City")}: ${t(`city.${here.city}`, here.city)}`}
        className="flex items-center gap-2 rounded-lg border border-line bg-surface-raised px-2.5 py-1.5 text-xs font-semibold text-ink transition hover:border-accent/60">
        <img src={flagUrl(here.country)} alt="" className="h-3.5 w-5 rounded-sm object-cover" />
        {here.label}
        <ChevronDown size={14} className={cn("text-ink-mute transition", open && "rotate-180")} aria-hidden />
      </button>
      {open ? (
        <ul role="listbox" aria-label={t("map.city", "City")}
          className="absolute right-0 top-full z-30 mt-1.5 min-w-40 overflow-hidden rounded-xl border border-line bg-surface-raised p-1 shadow-xl shadow-black/40">
          {CITIES.map((c) => (
            <li key={c.city}>
              <button type="button" role="option" aria-selected={c.city === here.city}
                onClick={() => { onCity(c.city); setOpen(false); }}
                className={cn("flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-left text-sm transition hover:bg-surface",
                  c.city === here.city ? "text-ink" : "text-ink-mute")}>
                <img src={flagUrl(c.country)} alt="" className="h-3.5 w-5 rounded-sm object-cover" />
                <span className="flex-1">{t(`city.${c.city}`, c.city)}</span>
                <span className="text-[11px] font-semibold text-ink-mute">{c.label}</span>
                {c.city === here.city ? <Check size={14} className="text-accent" aria-hidden /> : null}
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

export default CinemaMap;
