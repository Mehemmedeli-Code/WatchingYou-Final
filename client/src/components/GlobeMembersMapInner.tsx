import { useEffect, useMemo, useState } from "react";
import { ArrowLeft } from "lucide-react";
import { VenueMap, MapMarker, useVenueMap } from "@/components/ui/venue-map";
import { flagUrl, countryName, WORLD_CITIES, cityKey } from "@/lib/worldCities";
import { lang, t } from "@/lib/i18n";
import type { GlobeMapCity } from "@/components/GlobeMembersMap";

/**
 * The members on a real globe.
 *
 * World view: a flag on every country, with a count where people have joined. A click on a
 * flag flies the camera to that country and shows only its cities — each with two members'
 * names and how many more there are ("398+", capped at "999+"). A city opens its full list
 * beside the map, exactly as the city chips below do.
 */

interface CountryPoint {
  code: string;
  lng: number;
  lat: number;
  bounds: [[number, number], [number, number]];
}

// Every country the city list knows, pinned at its first city (the capital), and framed by
// all of its listed cities when zooming in.
const COUNTRIES: CountryPoint[] = (() => {
  const byCode = new Map<string, typeof WORLD_CITIES>();
  for (const city of WORLD_CITIES) {
    if (!byCode.has(city.country)) byCode.set(city.country, []);
    byCode.get(city.country)!.push(city);
  }
  return [...byCode.entries()].map(([code, cities]) => {
    const lngs = cities.map((c) => c.lon), lats = cities.map((c) => c.lat);
    // A single-city country still gets a frame of about 3° around it.
    const pad = cities.length === 1 ? 1.5 : 0.6;
    return {
      code,
      lng: cities[0].lon,
      lat: cities[0].lat,
      bounds: [[Math.min(...lngs) - pad, Math.min(...lats) - pad], [Math.max(...lngs) + pad, Math.max(...lats) + pad]],
    };
  });
})();

/** "398+" for 400 members, and never more than "999+". Nothing when the two names are all. */
export function moreThanTwo(total: number): string | null {
  const rest = total - 2;
  return rest > 0 ? `${Math.min(rest, 999)}+` : null;
}

const countryOf = (city: GlobeMapCity) =>
  city.countryCode?.toUpperCase()
  ?? WORLD_CITIES.find((c) => cityKey(c.name) === cityKey(city.city))?.country
  ?? null;

/** Moves the camera: into a country's frame, or back out to the whole Earth. */
function Camera({ country, home }: { country: CountryPoint | null; home: { center: [number, number]; zoom: number } }) {
  const map = useVenueMap();
  useEffect(() => {
    if (!map) return;
    if (country) map.fitBounds(country.bounds, { padding: 60, maxZoom: 6.5, duration: 1600 });
    else map.flyTo({ center: home.center, zoom: home.zoom, duration: 1400 });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [map, country]);
  return null;
}

/** A card's place against its city's dot: top-left corner relative to the dot's centre,
 *  and where its leader line meets it (null when the card sits right at the dot). */
export interface Callout { dx: number; dy: number; lead: { x: number; y: number } | null }

type Box = { x0: number; y0: number; x1: number; y1: number };
const CARD_W = 132;
const area = (a: Box, b: Box) =>
  Math.max(0, Math.min(a.x1, b.x1) - Math.max(a.x0, b.x0)) * Math.max(0, Math.min(a.y1, b.y1) - Math.max(a.y0, b.y0));
// Up first, then the sides, then down; the diagonals after.
const DIRECTIONS: [number, number][] = [[0, -1], [1, 0], [-1, 0], [0, 1], [1, -1], [-1, -1], [1, 1], [-1, 1]];
const DISTANCES = [8, 36, 70, 110];

/**
 * Lays the cards out like a map's callouts: each next to its dot if there is room, otherwise
 * pulled out along a short leader line to the nearest free spot — biggest cities first, so
 * they keep the natural place just above. The dot itself never moves.
 */
export function planCallouts(points: { x: number; y: number }[], heights: number[], width: number, height: number): Callout[] {
  const dots: Box[] = points.map((p) => ({ x0: p.x - 6, x1: p.x + 6, y0: p.y - 6, y1: p.y + 6 }));
  const taken: Box[] = [];
  return points.map((p, i) => {
    const h = heights[i];
    let best: { box: Box; anchor: { x: number; y: number }; d: number } | null = null, bestCost = Infinity;
    search: for (const d of DISTANCES) {
      for (const [ux, uy] of DIRECTIONS) {
        const step = ux !== 0 && uy !== 0 ? d / Math.SQRT2 : d;
        const anchor = { x: p.x + ux * step, y: p.y + uy * step };
        const x0 = ux < 0 ? anchor.x - CARD_W : ux > 0 ? anchor.x : anchor.x - CARD_W / 2;
        const y0 = uy < 0 ? anchor.y - h : uy > 0 ? anchor.y : anchor.y - h / 2;
        const box = { x0, y0, x1: x0 + CARD_W, y1: y0 + h };
        const offMap = CARD_W * h - area(box, { x0: 0, y0: 0, x1: width, y1: height });
        const cost = offMap + [...taken, ...dots.filter((_, j) => j !== i)].reduce((sum, other) => sum + area(box, other), 0);
        if (cost < bestCost) { best = { box, anchor, d }; bestCost = cost; }
        if (cost === 0) break search;
      }
    }
    taken.push(best!.box);
    return { dx: best!.box.x0 - p.x, dy: best!.box.y0 - p.y, lead: best!.d > 8 ? { x: best!.anchor.x - p.x, y: best!.anchor.y - p.y } : null };
  });
}

/** Re-plans the callouts in screen pixels whenever the camera stops: what collides depends on the zoom. */
function CardSides({ cities, onChange }: { cities: GlobeMapCity[]; onChange: (callouts: Map<GlobeMapCity, Callout>) => void }) {
  const map = useVenueMap();
  useEffect(() => {
    if (!map) return;
    const plan = () => {
      const canvas = map.getCanvas();
      const callouts = planCallouts(
        cities.map((c) => map.project([c.longitude, c.latitude])),
        cities.map((c) => 24 + 15 * Math.min(2, c.faces.length) + (moreThanTwo(c.memberCount) ? 15 : 0)),
        canvas.clientWidth, canvas.clientHeight);
      onChange(new Map(cities.map((c, i) => [c, callouts[i]])));
    };
    plan();
    map.on("moveend", plan);
    return () => { map.off("moveend", plan); };
  }, [map, cities, onChange]);
  return null;
}

function GlobeMembersMap({
  cities,
  selectedCity,
  onSelect,
  onCountryChange,
}: {
  cities: GlobeMapCity[];
  selectedCity?: string | null;
  onSelect: (city: GlobeMapCity) => void;
  /** Told whenever a country is opened on the map (or the map goes back to the whole Earth). */
  onCountryChange?: (code: string | null) => void;
}) {
  const [countryCode, setCountryCode] = useState<string | null>(null);
  useEffect(() => { onCountryChange?.(countryCode); }, [countryCode, onCountryChange]);
  const placed = useMemo(() => cities.filter((c) => c.latitude !== 0 || c.longitude !== 0), [cities]);

  // Members per country, for the badges on the flags.
  const membersByCountry = useMemo(() => {
    const counts = new Map<string, number>();
    for (const city of placed) {
      const code = countryOf(city);
      if (code) counts.set(code, (counts.get(code) ?? 0) + city.memberCount);
    }
    return counts;
  }, [placed]);

  const home = useMemo(() => ({
    center: (placed.length > 0
      ? [placed.reduce((s, c) => s + c.longitude, 0) / placed.length, placed.reduce((s, c) => s + c.latitude, 0) / placed.length]
      : [49.87, 40.41]) as [number, number],
    // The whole Earth in view: further out on a narrow screen.
    zoom: window.innerWidth < 640 ? 0.7 : 1.4,
  }), [placed]);

  const country = countryCode ? COUNTRIES.find((c) => c.code === countryCode) ?? null : null;
  const citiesHere = useMemo(() => country
    ? placed.filter((c) => countryOf(c) === country.code).sort((a, b) => b.memberCount - a.memberCount)
    : [], [placed, country]);

  // Every city keeps its dot exactly where it is; only its card moves (pushing whole cards
  // down used to drop Sumqayit and Shamakhi next to Lankaran). Which side it opens on is
  // worked out in screen pixels, after every camera move — see CardSides.
  const [callouts, setCallouts] = useState<Map<GlobeMapCity, Callout>>(new Map());

  return (
    <div className="relative h-[420px] overflow-hidden rounded-xl border border-line bg-[#05080a] sm:h-[520px]">
      <VenueMap center={home.center} zoom={home.zoom} globe spin={!country} clickToActivate>
        <Camera country={country} home={home} />
        <CardSides cities={citiesHere} onChange={setCallouts} />

        {/* World: a flag on every country. */}
        {!country && COUNTRIES.map((point) => {
          const members = membersByCountry.get(point.code) ?? 0;
          return (
            <MapMarker key={point.code} longitude={point.lng} latitude={point.lat} hideWhenBehind onClick={() => setCountryCode(point.code)}>
              <button
                type="button"
                title={countryName(point.code, lang)}
                aria-label={countryName(point.code, lang)}
                className={
                  "relative block rounded-[3px] shadow-[0_1px_4px_rgba(0,0,0,0.7)] transition-transform hover:scale-150 " +
                  (members > 0 ? "ring-2 ring-accent" : "opacity-80")
                }
              >
                <img src={flagUrl(point.code)} alt="" className="block h-3 w-4 rounded-[2px] object-cover" draggable={false} />
                {members > 0 ? (
                  <span className="absolute -right-2.5 -top-2.5 min-w-4 rounded-full bg-accent px-1 text-center text-[9px] font-bold leading-4 text-surface">
                    {members > 999 ? "999+" : members}
                  </span>
                ) : null}
              </button>
            </MapMarker>
          );
        })}

        {/* One country: its cities, two names each and how many more. */}
        {country && citiesHere.map((city) => {
          const active = city.city === selectedCity;
          const more = moreThanTwo(city.memberCount);
          return (
            <MapMarker key={`${city.city}-${city.latitude}`} longitude={city.longitude} latitude={city.latitude}
                       hideWhenBehind onClick={() => onSelect(city)}>
              {/* The dot is the city itself (its centre is 5,5 in this box); the card hangs off it,
                  on a leader line when it had to move away to find room. */}
              <span className="relative block h-2.5 w-2.5 rounded-full border-2 border-surface bg-accent shadow" aria-hidden />
              {(() => { const lead = callouts.get(city)?.lead; return lead ? (
                <span aria-hidden className="pointer-events-none absolute left-[5px] top-[5px] h-px origin-left bg-accent/80"
                  style={{ width: Math.hypot(lead.x, lead.y), transform: `rotate(${Math.atan2(lead.y, lead.x)}rad)` }} />
              ) : null; })()}
              <button
                type="button"
                aria-pressed={active}
                style={{ left: 5 + (callouts.get(city)?.dx ?? -66), top: 5 + (callouts.get(city)?.dy ?? -60) }}
                className={
                  "absolute w-[132px] rounded-lg border px-2.5 py-1.5 text-left shadow-lg transition-colors " +
                  (active ? "z-10 border-accent bg-accent text-surface" : "border-line bg-surface/95 text-ink hover:border-accent")
                }
              >
                <span className="flex items-center gap-1.5 text-xs font-semibold">
                  <img src={flagUrl(country.code)} alt="" className="h-3 w-4 rounded-[2px] object-cover" />
                  <span className="truncate">{city.city}</span>
                </span>
                {city.faces.slice(0, 2).map((face) => (
                  <span key={face.userId} className="mt-0.5 block truncate text-[11px] leading-tight">{face.displayName}</span>
                ))}
                {more ? (
                  <span className={"mt-0.5 block text-[11px] font-semibold " + (active ? "text-surface" : "text-accent")}>{more}</span>
                ) : null}
              </button>
            </MapMarker>
          );
        })}
      </VenueMap>

      {country ? (
        <div className="absolute left-3 top-3 z-20 flex max-w-[calc(100%-5rem)] items-center gap-2">
          <button
            type="button"
            onClick={() => setCountryCode(null)}
            className="flex items-center gap-1.5 rounded-full border border-line bg-surface/95 px-3 py-1.5 text-xs font-medium text-ink shadow hover:border-accent"
          >
            <ArrowLeft size={14} aria-hidden /> {t("globe.wholeWorld", "Whole world")}
          </button>
          <span className="flex items-center gap-1.5 truncate rounded-full bg-surface/95 px-3 py-1.5 text-xs text-ink shadow">
            <img src={flagUrl(country.code)} alt="" className="h-3 w-4 rounded-[2px] object-cover" />
            {countryName(country.code, lang)}
          </span>
        </div>
      ) : null}

      {country && citiesHere.length === 0 ? (
        <p className="absolute inset-x-0 bottom-10 z-20 mx-auto w-fit rounded-full bg-surface/95 px-4 py-2 text-xs text-ink-mute shadow">
          {t("globe.nobodyHere", "Nobody from this country has joined yet.")}
        </p>
      ) : null}
    </div>
  );
}

export default GlobeMembersMap;
