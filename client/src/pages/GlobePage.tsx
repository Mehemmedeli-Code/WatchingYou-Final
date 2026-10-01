import { useCallback, useEffect, useState } from "react";
import { MapPin, Users, X } from "lucide-react";
import { Section, Panel, Notice, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Field } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { useAuth } from "@/components/useAuth";
import { get, put, query } from "@/lib/api";
import { t } from "@/lib/i18n";
import { TasteCompare } from "@/components/TasteCompare";
import { ErrorBoundary } from "@/components/ErrorBoundary";
import { DirectChat } from "@/components/DirectChat";
import { Globe } from "@/components/ui/globe";

/** What the server returns per city. Named here now that the 3D viewer is gone. */
export interface GlobeCityMarker {
  city: string;
  countryCode?: string | null;
  latitude: number;
  longitude: number;
  memberCount: number;
  faces: { userId: string; displayName: string; avatarUrl?: string | null }[];
}

interface GlobeMember {
  userId: string;
  displayName: string;
  avatarUrl?: string | null;
  joinedAtUtc: string;
}

interface GlobeMemberPage {
  city: string;
  total: number;
  page: number;
  pageSize: number;
  items: GlobeMember[];
}

export default function GlobePage() {
  const { isSignedIn, user } = useAuth();
  const [cities, setCities] = useState<GlobeCityMarker[] | null>(null);
  const [open, setOpen] = useState<GlobeCityMarker | null>(null);
  const [members, setMembers] = useState<GlobeMember[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loadingMore, setLoadingMore] = useState(false);
  const [compareWith, setCompareWith] = useState<GlobeMember | null>(null);
  const [writeTo, setWriteTo] = useState<GlobeMember | null>(null);

  const loadCities = useCallback(async () => {
    if (!isSignedIn) { setCities([]); return; }
    setCities(await get<GlobeCityMarker[]>("/api/globe/cities").catch(() => []));
  }, [isSignedIn]);

  useEffect(() => { void loadCities(); }, [loadCities]);

  const openCity = useCallback(async (marker: GlobeCityMarker) => {
    setOpen(marker);
    setCompareWith(null);
    setPage(1);
    const first = await get<GlobeMemberPage>("/api/globe/members" + query({ city: marker.city, page: 1 }))
      .catch(() => null);
    setMembers(first?.items ?? []);
    setTotal(first?.total ?? 0);
  }, []);

  async function loadMore() {
    if (!open || members.length >= total) return;
    setLoadingMore(true);
    const next = await get<GlobeMemberPage>("/api/globe/members" + query({ city: open.city, page: page + 1 }))
      .catch(() => null);
    if (next) {
      setMembers((current) => [...current, ...next.items]);
      setPage(next.page);
    }
    setLoadingMore(false);
  }

  if (!isSignedIn) {
    return (
      <Section title={t("globe.title")} lede={t("globe.lede")}>
        <Empty
          title={t("globe.signedOut")}
          hint=""
          action={<a href="/account"><Button>{t("nav.signIn")}</Button></a>}
        />
      </Section>
    );
  }

  return (
    <Section title={t("globe.title")} lede={t("globe.lede")}>
      <div className="grid gap-6 lg:grid-cols-[1fr_380px]">
        <div className="rounded-xl border border-line bg-surface-raised p-6">
          <ErrorBoundary label="Globe" fallback={null}>
            <Globe size={250} className="flex justify-center py-6" />
          </ErrorBoundary>

          {cities === null ? (
            <Spinner label={t("common.loading")} />
          ) : cities.length === 0 ? (
            <p className="py-6 text-center text-sm text-ink-mute">{t("globe.empty")}</p>
          ) : (
            // The globe is a disc with no projection behind it, so a pin would be decoration
            // pointing at nowhere. The cities are a list instead — same click, honest position.
            <div className="mt-4 flex flex-wrap justify-center gap-2">
              {cities.map((city) => {
                const active = city.city === open?.city;
                return (
                  <button
                    key={`${city.city}-${city.latitude}`}
                    onClick={() => openCity(city)}
                    aria-pressed={active}
                    className={`flex items-center gap-2 rounded-full border px-3 py-1.5 text-sm transition-colors ${
                      active ? "border-accent bg-accent-dim text-surface" : "border-line text-ink hover:border-accent-dim"
                    }`}
                  >
                    <span className="flex -space-x-1.5">
                      {city.faces.map((face) =>
                        face.avatarUrl ? (
                          <img key={face.userId} src={face.avatarUrl} alt="" className="h-5 w-5 rounded-full border border-surface object-cover" />
                        ) : (
                          <span key={face.userId} className="flex h-5 w-5 items-center justify-center rounded-full border border-surface bg-surface text-[10px] text-ink-mute">
                            {face.displayName.slice(0, 1).toUpperCase()}
                          </span>
                        ),
                      )}
                    </span>
                    {city.city}
                    <span className="text-xs text-ink-mute">{city.memberCount}</span>
                  </button>
                );
              })}
            </div>
          )}
        </div>

        {/* The panel the pin opens. Kept beside the globe rather than over it, so the map
            stays usable while you read. */}
        <div className="space-y-4">
          {open ? (
            <Panel className="max-h-[540px] overflow-y-auto">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="flex items-center gap-1.5 font-display text-xl text-ink">
                    <MapPin size={16} aria-hidden />
                    {open.city}
                  </p>
                  <p className="mt-1 flex items-center gap-1.5 text-xs text-ink-mute">
                    <Users size={13} aria-hidden />
                    {open.memberCount} {t("globe.members")}
                  </p>
                </div>
                <button onClick={() => setOpen(null)} aria-label={t("common.cancel")} className="text-ink-mute hover:text-ink">
                  <X size={18} aria-hidden />
                </button>
              </div>

              <div className="mt-4 space-y-2">
                {members.map((member) => (
                  <div key={member.userId} className="flex items-center justify-between gap-3 rounded-lg border border-line p-2">
                    <div className="flex min-w-0 items-center gap-2">
                      {member.avatarUrl ? (
                        <img src={member.avatarUrl} alt="" className="h-8 w-8 shrink-0 rounded-full object-cover" />
                      ) : (
                        <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-surface text-xs text-ink-mute">
                          {member.displayName.slice(0, 1).toUpperCase()}
                        </span>
                      )}
                      <span className="truncate text-sm text-ink">{member.displayName}</span>
                    </div>

                    {member.userId === user?.id ? (
                      <Badge>{t("globe.you")}</Badge>
                    ) : (
                      <div className="flex shrink-0 gap-1.5">
                        <Button size="sm" variant="outline" onClick={() => setCompareWith(member)}>
                          {t("globe.match")}
                        </Button>
                        <Button size="sm" onClick={() => setWriteTo(member)}>
                          {t("dm.message")}
                        </Button>
                      </div>
                    )}
                  </div>
                ))}

                {members.length < total ? (
                  <Button className="w-full" size="sm" variant="outline" disabled={loadingMore} onClick={loadMore}>
                    {loadingMore ? t("common.loading") : `${t("globe.loadMore")} (${total - members.length})`}
                  </Button>
                ) : null}
              </div>
            </Panel>
          ) : (
            <Panel>
              <p className="flex items-center gap-2 font-display text-lg text-ink">
                <Globe size={18} aria-hidden />
                {t("globe.title")}
              </p>
              <p className="mt-1 text-sm text-ink-mute">{t("globe.lede")}</p>
            </Panel>
          )}

          <PresencePanel onSaved={loadCities} />
        </div>
      </div>

      {compareWith ? (
        <TasteCompare member={compareWith} onClose={() => setCompareWith(null)} />
      ) : null}

      {writeTo ? (
        <DirectChat userId={writeTo.userId} onClose={() => setWriteTo(null)} />
      ) : null}
    </Section>
  );
}

/** Opting in, and out. Out is one click and it clears the stored city, not just the flag. */
function PresencePanel({ onSaved }: { onSaved: () => void }) {
  const { user } = useAuth();
  const [city, setCity] = useState("");
  const [avatar, setAvatar] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    get<{ shareOnGlobe: boolean; city?: string | null; avatarUrl?: string | null }>("/api/auth/me")
      .then((me) => {
        setVisible(me.shareOnGlobe);
        setCity(me.city ?? "");
        setAvatar(me.avatarUrl ?? "");
      })
      .catch(() => null);
  }, [user?.id]);

  async function save(share: boolean) {
    setBusy(true);
    setMessage(null);
    try {
      // The browser is never asked for a position. The city is typed, and its coordinates
      // are looked up from a small table of known places on the server side of the form.
      await put("/api/globe/presence", {
        shareOnGlobe: share,
        city: share ? city : null,
        countryCode: null,
        latitude: share ? CITY_POINTS[city.trim().toLowerCase()]?.[0] ?? null : null,
        longitude: share ? CITY_POINTS[city.trim().toLowerCase()]?.[1] ?? null : null,
        avatarUrl: avatar || null,
      });
      setVisible(share);
      setMessage(share ? t("globe.show") : t("globe.hide"));
      onSaved();
    } finally {
      setBusy(false);
    }
  }

  return (
    <Panel>
      <p className="font-display text-lg text-ink">{t("globe.joinTitle")}</p>
      <p className="mt-1 text-sm text-ink-mute">{t("globe.joinLede")}</p>

      <div className="mt-4 space-y-3">
        <Field label={t("globe.city")} hint={Object.keys(CITY_POINTS).length + " cities known"}>
          <Input
            value={city}
            onChange={(e) => setCity(e.target.value)}
            list="globe-cities"
            placeholder="Baku"
          />
        </Field>
        <datalist id="globe-cities">
          {Object.keys(CITY_POINTS).map((name) => (
            <option key={name} value={name.replace(/\b\w/g, (c) => c.toUpperCase())} />
          ))}
        </datalist>

        <Field label={t("globe.avatar")}>
          <Input value={avatar} onChange={(e) => setAvatar(e.target.value)} placeholder="https://…" />
        </Field>

        {message ? <Notice tone="ok">{message}</Notice> : null}

        <div className="flex gap-2">
          <Button size="sm" disabled={busy || !city.trim()} onClick={() => save(true)}>
            {t("globe.show")}
          </Button>
          {visible ? (
            <Button size="sm" variant="danger" disabled={busy} onClick={() => save(false)}>
              {t("globe.hide")}
            </Button>
          ) : null}
        </div>
      </div>
    </Panel>
  );
}

/**
 * A small gazetteer, so a typed city name becomes a pin without asking the browser where the
 * person is. Add rows as you need them; an unknown city simply gets no coordinates and no pin.
 */
const CITY_POINTS: Record<string, [number, number]> = {
  baku: [40.4093, 49.8671],
  ganja: [40.6828, 46.3606],
  sumqayit: [40.5855, 49.6317],
  istanbul: [41.0082, 28.9784],
  ankara: [39.9334, 32.8597],
  moscow: [55.7558, 37.6173],
  "saint petersburg": [59.9311, 30.3609],
  london: [51.5074, -0.1278],
  berlin: [52.52, 13.405],
  paris: [48.8566, 2.3522],
  madrid: [40.4168, -3.7038],
  rome: [41.9028, 12.4964],
  warsaw: [52.2297, 21.0122],
  kyiv: [50.4501, 30.5234],
  tbilisi: [41.7151, 44.8271],
  dubai: [25.2048, 55.2708],
  "new york": [40.7128, -74.006],
  toronto: [43.6532, -79.3832],
  tokyo: [35.6762, 139.6503],
  seoul: [37.5665, 126.978],
  delhi: [28.6139, 77.209],
  sydney: [-33.8688, 151.2093],
};
