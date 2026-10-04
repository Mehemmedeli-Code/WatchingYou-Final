import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { createPortal } from "react-dom";
import { Map as MapLibreMap, Marker, NavigationControl } from "maplibre-gl";
import "maplibre-gl/dist/maplibre-gl.css";
import { cn } from "@/lib/utils";
import { t } from "@/lib/i18n";
import { getTheme, useTheme } from "@/lib/theme";

/**
 * A MapLibre map with React markers, adapted from the mapcn "marker label" component.
 *
 * What changed from the original, and why:
 *
 *  - `"use client"` removed — a Next.js marker with no meaning in a Vite build.
 *  - Its private `cn` helper dropped in favour of the project's own, so class merging behaves
 *    the same here as everywhere else.
 *  - Light/dark switching removed. This site has one theme, and the original carried a
 *    MutationObserver plus a media-query listener to follow a class that never changes.
 *  - shadcn tokens (`bg-background`, `text-foreground`, `bg-muted-foreground`) replaced with
 *    this project's role tokens; we never installed the shadcn base stylesheet, so those
 *    classes would have resolved to nothing.
 *  - The tile style is Carto's dark basemap, as in the original. It needs no API key, which
 *    is the reason to prefer it over Mapbox here.
 */

type MapContextValue = { map: MapLibreMap | null };
const MapContext = createContext<MapContextValue | null>(null);

function useMapContext() {
  const context = useContext(MapContext);
  if (!context) throw new Error("Map markers must live inside <VenueMap>.");
  return context;
}

const DARK_STYLE = "https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json";
const LIGHT_STYLE = "https://basemaps.cartocdn.com/gl/positron-gl-style/style.json";
const styleFor = (theme: "light" | "dark") => (theme === "light" ? LIGHT_STYLE : DARK_STYLE);

export function VenueMap({
  center,
  zoom = 11,
  className,
  children,
}: {
  center: [number, number];
  zoom?: number;
  className?: string;
  children?: ReactNode;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [map, setMap] = useState<MapLibreMap | null>(null);
  const [ready, setReady] = useState(false);

  // Built once. Re-creating a map on every render would tear down the GL context and lose
  // whatever the visitor had panned to.
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!containerRef.current) return;

    // Creating a map throws outright when WebGL is unavailable — an old driver, a locked-down
    // browser, a remote session. Catching it here keeps the failure local to the map.
    let instance: MapLibreMap;
    try {
      instance = new MapLibreMap({
        container: containerRef.current,
        style: styleFor(getTheme()),
        center,
        zoom,
        renderWorldCopies: false,
        attributionControl: { compact: true },
      });
    } catch (error) {
      console.error("[Map] WebGL unavailable:", error);
      setFailed(true);
      return;
    }

    instance.addControl(new NavigationControl({ showCompass: false }), "top-right");

    const onLoad = () => setReady(true);
    // The basemap tiles come from a third party. If that is unreachable — a blocked CDN, no
    // network — the old code sat on its loading dots for ever, which reads as "broken" with
    // no explanation. Say so instead.
    const onError = (event: unknown) => {
      console.error("[Map] style or tiles failed:", event);
      setFailed(true);
    };

    instance.on("load", onLoad);
    instance.on("error", onError);
    setMap(instance);

    // Nor should it spin indefinitely when the request simply never answers.
    const giveUp = setTimeout(() => setReady((current) => (current ? current : (setFailed(true), false))), 12000);

    return () => {
      clearTimeout(giveUp);
      instance.off("load", onLoad);
      instance.off("error", onError);
      instance.remove();
      setMap(null);
      setReady(false);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Swapping the style keeps the camera and the markers; only the tiles repaint. Only on a
  // real change: re-applying the style the map was just built with would reload every tile
  // while it is still on its first load.
  const [theme] = useTheme();
  const appliedTheme = useRef(getTheme());
  useEffect(() => {
    if (!map || theme === appliedTheme.current) return;
    appliedTheme.current = theme;
    map.setStyle(styleFor(theme));
  }, [map, theme]);

  const value = useMemo(() => ({ map }), [map]);

  return (
    <MapContext.Provider value={value}>
      <div ref={containerRef} className={cn("relative h-full w-full", className)}>
        {failed ? (
          <div className="absolute inset-0 z-10 flex items-center justify-center bg-surface px-6 text-center text-sm text-ink-mute">
            {t("map.unavailable")}
          </div>
        ) : null}
        {!ready && !failed ? (
          <div className="absolute inset-0 z-10 flex items-center justify-center bg-surface/60 backdrop-blur-sm">
            <div className="flex gap-1">
              <span className="size-1.5 animate-pulse rounded-full bg-ink-mute" />
              <span className="size-1.5 animate-pulse rounded-full bg-ink-mute [animation-delay:150ms]" />
              <span className="size-1.5 animate-pulse rounded-full bg-ink-mute [animation-delay:300ms]" />
            </div>
          </div>
        ) : null}
        {map ? children : null}
      </div>
    </MapContext.Provider>
  );
}

/** Renders React into a MapLibre marker element, so a pin can be any markup we like. */
export function MapMarker({
  longitude,
  latitude,
  onClick,
  children,
}: {
  longitude: number;
  latitude: number;
  onClick?: () => void;
  children: ReactNode;
}) {
  const { map } = useMapContext();
  const clickRef = useRef(onClick);
  clickRef.current = onClick;

  const marker = useMemo(() => {
    const element = document.createElement("div");
    element.addEventListener("click", () => clickRef.current?.());
    return new Marker({ element }).setLngLat([longitude, latitude]);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!map) return;
    marker.addTo(map);
    return () => {
      marker.remove();
    };
  }, [map, marker]);

  useEffect(() => {
    marker.setLngLat([longitude, latitude]);
  }, [marker, longitude, latitude]);

  return createPortal(children, marker.getElement());
}
