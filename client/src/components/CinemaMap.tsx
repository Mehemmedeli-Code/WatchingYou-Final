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
  /** /img/venues/<name>.jpg when that file exists (see VenuePhotos on the server). */
  photoUrl?: string | null;
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
    <div className="mb-4 flex items-center gap-3 rounded-xl border border-accent/40 bg-surface-raised p-2 pr-4">
      {venue.photoUrl ? (
        <img src={venue.photoUrl} alt={venue.name} className="h-16 w-24 shrink-0 rounded-lg object-cover sm:h-20 sm:w-32" />
      ) : null}
      <p className="min-w-0 text-sm text-accent">
        {t("cinema.venue")}: <span className="font-semibold">{venue.name}</span>
        {venue.address ? <span className="block text-xs text-ink-mute">{venue.address}</span> : null}
      </p>
    </div>
  );
}
