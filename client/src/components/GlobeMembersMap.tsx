import { Suspense, lazy } from "react";
import { ErrorBoundary } from "@/components/ErrorBoundary";
import { t } from "@/lib/i18n";

export interface GlobeMapCity {
  city: string;
  countryCode?: string | null;
  latitude: number;
  longitude: number;
  memberCount: number;
  faces: { userId: string; displayName: string; avatarUrl?: string | null }[];
}

/** MapLibre is big and only two pages use it, so it loads with the page that shows it. */
const Inner = lazy(() => import("@/components/GlobeMembersMapInner"));

export function GlobeMembersMap(props: {
  cities: GlobeMapCity[];
  selectedCity?: string | null;
  onSelect: (city: GlobeMapCity) => void;
  onCountryChange?: (code: string | null) => void;
}) {
  return (
    <ErrorBoundary
      label="Globe map"
      // No map (no WebGL, tiles blocked): the city list below still does everything.
      fallback={
        <div className="flex h-40 items-center justify-center rounded-xl border border-line bg-surface-raised px-6 text-center text-sm text-ink-mute">
          {t("map.unavailable")}
        </div>
      }
    >
      <Suspense fallback={<div className="h-[420px] animate-pulse rounded-xl border border-line bg-surface-raised sm:h-[520px]" />}>
        <Inner {...props} />
      </Suspense>
    </ErrorBoundary>
  );
}
