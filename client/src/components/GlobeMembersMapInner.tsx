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
  const placed = cities.filter((c) => c.latitude !== 0 || c.longitude !== 0);

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
  const citiesHere = country
    ? placed.filter((c) => countryOf(c) === country.code).sort((a, b) => b.memberCount - a.memberCount)
    : [];

  // Neighbouring cities (Baku and Sumqayit are 30 km apart) would draw their cards on top of
  // each other. Each card that lands near one already placed steps down below it instead.
  const offsets = useMemo(() => {
    const placedSoFar: GlobeMapCity[] = [];
    const result = new Map<GlobeMapCity, [number, number]>();
    for (const city of citiesHere) {
      const crowd = placedSoFar.filter((other) =>
        Math.abs(other.latitude - city.latitude) < 1.2 && Math.abs(other.longitude - city.longitude) < 2.5).length;
      result.set(city, [0, crowd * 78]);
      placedSoFar.push(city);
    }
    return result;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [countryCode, cities]);

  return (
    <div className="relative h-[420px] overflow-hidden rounded-xl border border-line bg-[#05080a] sm:h-[520px]">
      <VenueMap center={home.center} zoom={home.zoom} globe spin={!country} clickToActivate>
        <Camera country={country} home={home} />

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
                       offset={offsets.get(city)} hideWhenBehind onClick={() => onSelect(city)}>
              <button
                type="button"
                aria-pressed={active}
                className={
                  "min-w-[120px] -translate-y-1/2 rounded-lg border px-2.5 py-1.5 text-left shadow-lg transition-colors " +
                  (active ? "border-accent bg-accent text-surface" : "border-line bg-surface/95 text-ink hover:border-accent")
                }
              >
                <span className="flex items-center gap-1.5 text-xs font-semibold">
                  <img src={flagUrl(country.code)} alt="" className="h-3 w-4 rounded-[2px] object-cover" />
                  {city.city}
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
