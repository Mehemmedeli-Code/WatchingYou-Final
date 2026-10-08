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
/// <reference types="vite/client" />
import { Map as MapLibreMap, Marker, NavigationControl, setWorkerUrl } from "maplibre-gl";
import "maplibre-gl/dist/maplibre-gl.css";
// MapLibre draws in a web worker that it loads from a file next to its own module. Bundled,
// there is no such file — the worker request came back 404 and the map sat on its loading
// dots, then gave up. Vite builds the worker (with the code it imports) into one file of our
// own, and MapLibre is told where it is.
import mapWorkerUrl from "maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url";

setWorkerUrl(mapWorkerUrl);
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

/** The map itself, for children that move the camera (fly to a country, back out again). */
export function useVenueMap(): MapLibreMap | null {
  return useMapContext().map;
}

const DARK_STYLE = "https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json";
const LIGHT_STYLE = "https://basemaps.cartocdn.com/gl/positron-gl-style/style.json";
const styleFor = (theme: "light" | "dark") => (theme === "light" ? LIGHT_STYLE : DARK_STYLE);

export function VenueMap({
  center,
  zoom = 11,
  globe = false,
  spin = false,
  clickToActivate = false,
  className,
  children,
}: {
  center: [number, number];
  zoom?: number;
  /** Draw the Earth as a globe instead of a flat sheet (the members' page). */
  globe?: boolean;
  /** Turn the globe slowly until the visitor touches it. */
  spin?: boolean;
  /**
   * The map ignores the scroll wheel and drags until it is clicked. Scrolling the page past a
   * map that grabs the wheel and spins the Earth is what annoyed people. Once clicked, it turns
   * and zooms (two fingers on a trackpad or a screen, together); it locks again when the
   * pointer leaves it or the visitor taps elsewhere.
   */
  clickToActivate?: boolean;
  className?: string;
  children?: ReactNode;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [map, setMap] = useState<MapLibreMap | null>(null);
  const [ready, setReady] = useState(false);
  const [active, setActive] = useState(!clickToActivate);
  const spinAllowed = useRef(spin);
  spinAllowed.current = spin;

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

    let loaded = false;
    const onLoad = () => { loaded = true; setReady(true); };
    // The basemap comes from a third party. If the style itself cannot be fetched — a blocked
    // CDN, no network — say so rather than sit on loading dots. Once the map has loaded, a
    // single tile that fails (a hiccup, one missing square) is not a broken map: it is logged
    // and the rest keeps working.
    const onError = (event: unknown) => {
      console.error("[Map] style or tiles failed:", event);
      if (!loaded) setFailed(true);
    };

    instance.on("load", onLoad);
    instance.on("error", onError);

    // The globe is a property of the style, so it is set again after every style load — a
    // theme switch swaps the style and would otherwise flatten the Earth.
    const applyGlobe = () => instance.setProjection({ type: "globe" });
    if (globe) instance.on("style.load", applyGlobe);

    // A slow turn — about one revolution every two and a half minutes — until the visitor
    // takes over; then it stays where they left it. It also pauses while the page asks
    // (spin={false}), e.g. while a country is being looked at.
    let spinning = globe && spin;
    let spinFrame = 0;
    const turn = () => {
      if (!spinning) return;
      if (spinAllowed.current && !instance.isMoving()) {
        const centre = instance.getCenter();
        instance.setCenter([centre.lng + 0.04, centre.lat]);
      }
      spinFrame = requestAnimationFrame(turn);
    };
    const stopSpin = () => { spinning = false; cancelAnimationFrame(spinFrame); };
    if (spinning) {
      instance.once("load", () => { spinFrame = requestAnimationFrame(turn); });
      instance.on("mousedown", stopSpin);
      instance.on("touchstart", stopSpin);
      instance.on("wheel", stopSpin);
    }

    setMap(instance);

    // Nor should it spin indefinitely when the request simply never answers.
    const giveUp = setTimeout(() => setReady((current) => (current ? current : (setFailed(true), false))), 12000);

    return () => {
      clearTimeout(giveUp);
      stopSpin();
      instance.off("style.load", applyGlobe);
      instance.off("mousedown", stopSpin);
      instance.off("touchstart", stopSpin);
      instance.off("wheel", stopSpin);
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

  // Click to use: every way of moving the map is off until the visitor clicks it, and off
  // again when they leave it.
  useEffect(() => {
    if (!map || !clickToActivate) return;
    const handlers = [map.scrollZoom, map.dragPan, map.dragRotate, map.touchZoomRotate, map.doubleClickZoom, map.keyboard];
    if (active) {
      handlers.forEach((h) => h.enable());
      // Two fingers turn and zoom together; tilting is not needed on a globe.
      map.touchZoomRotate.enableRotation();
    } else {
      handlers.forEach((h) => h.disable());
    }
  }, [map, active, clickToActivate]);

  useEffect(() => {
    if (!clickToActivate) return;
    const box = containerRef.current;
    if (!box) return;
    const activate = () => setActive(true);
    const leave = (event: PointerEvent) => { if (event.pointerType === "mouse") setActive(false); };
    const outside = (event: PointerEvent) => { if (!box.contains(event.target as Node)) setActive(false); };
    box.addEventListener("pointerdown", activate);
    box.addEventListener("pointerleave", leave);
    document.addEventListener("pointerdown", outside);
    return () => {
      box.removeEventListener("pointerdown", activate);
      box.removeEventListener("pointerleave", leave);
      document.removeEventListener("pointerdown", outside);
    };
  }, [clickToActivate]);

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
        {clickToActivate && ready && !failed && !active ? (
          <div className="pointer-events-none absolute inset-x-0 bottom-12 z-10 flex justify-center">
            <span className="rounded-full bg-surface/90 px-3 py-1.5 text-xs text-ink-mute shadow">
              {t("map.clickToUse", "Click the globe to turn and zoom it")}
            </span>
          </div>
        ) : null}
      </div>
    </MapContext.Provider>
  );
}

/** Renders React into a MapLibre marker element, so a pin can be any markup we like. */
export function MapMarker({
  longitude,
  latitude,
  offset,
  hideWhenBehind = false,
  onClick,
  children,
}: {
  longitude: number;
  latitude: number;
  /** Pixels to shift the marker by, so neighbours that would overlap can fan out. */
  offset?: [number, number];
  /** On a globe: hide the pin while it is on the far side of the Earth. */
  hideWhenBehind?: boolean;
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

  const [dx, dy] = offset ?? [0, 0];
  useEffect(() => {
    marker.setOffset([dx, dy]);
  }, [marker, dx, dy]);

  // On a globe, a pin on the far side would otherwise show through the Earth and still take
  // clicks. Hidden (and unclickable) until the globe turns it to the front.
  useEffect(() => {
    if (!map || !hideWhenBehind) return;
    const element = marker.getElement();
    const toRad = Math.PI / 180;
    const update = () => {
      const centre = map.getCenter();
      const cosAngle =
        Math.sin(centre.lat * toRad) * Math.sin(latitude * toRad) +
        Math.cos(centre.lat * toRad) * Math.cos(latitude * toRad) * Math.cos((longitude - centre.lng) * toRad);
      // A little short of the horizon, so pins do not flicker right on the edge.
      const visible = cosAngle > 0.12;
      element.style.visibility = visible ? "" : "hidden";
      element.style.pointerEvents = visible ? "" : "none";
    };
    update();
    map.on("move", update);
    return () => { map.off("move", update); };
  }, [map, marker, hideWhenBehind, longitude, latitude]);

  return createPortal(children, marker.getElement());
}
