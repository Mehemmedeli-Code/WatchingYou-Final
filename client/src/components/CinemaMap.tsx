import { Suspense, lazy } from "react";
import { ErrorBoundary } from "@/components/ErrorBoundary";
import { t } from "@/lib/i18n";

export interface MappedVenue {
  id: string;
  name: string;
  city: string;
  address?: string | null;
  latitude: number;
  longitude: number;
  halls: { id: string; name: string }[];
}

/**
 * MapLibre and its stylesheet are about 200 KB gzipped, and only one page shows a map. Loading
 * it lazily keeps it out of the bundle every other page pays for — the same reasoning as the
 * 3D hall preview.
 */
const CinemaMapInner = lazy(() => import("@/components/CinemaMapInner"));

export function CinemaMap(props: {
  venues: MappedVenue[];
  selectedId?: string | null;
  onSelect?: (venueId: string | null) => void;
  city: string;
  onCity: (city: string) => void;
}) {
  if (props.venues.length === 0) return null;

  return (
    <ErrorBoundary
      label="Map"
      // A map that cannot start is a missing convenience, not a broken page: the listing
      // below it works perfectly well without one.
      fallback={
        <div className="mb-6 flex h-40 items-center justify-center rounded-xl border border-line bg-surface-raised px-6 text-center text-sm text-ink-mute">
          {t("map.unavailable")}
        </div>
      }
    >
      <Suspense
        fallback={<div className="mb-6 h-72 animate-pulse rounded-xl border border-line bg-surface-raised sm:h-96" />}
      >
        <CinemaMapInner {...props} />
      </Suspense>
    </ErrorBoundary>
  );
}

export function VenueFilterHint({ venue }: { venue?: MappedVenue | null }) {
  if (!venue) return null;
  return (
    <p className="mb-3 text-sm text-accent">
      {t("cinema.venue")}: {venue.name}
      {venue.address ? ` · ${venue.address}` : ""}
    </p>
  );
}
