import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Globe2, MapPin, Users, X } from "lucide-react";
import { Section, Panel, Notice, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Field } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { useAuth } from "@/components/useAuth";
import { get, put, query } from "@/lib/api";
import { t, lang } from "@/lib/i18n";
import { WORLD_CITIES, flagUrl, countryName, cityKey, type WorldCity } from "@/lib/worldCities";
import { TasteCompare } from "@/components/TasteCompare";
import { ErrorBoundary } from "@/components/ErrorBoundary";
import { DirectChat } from "@/components/DirectChat";
import { GlobeMessages } from "@/components/GlobeMessages";
import { GlobeMembersMap } from "@/components/GlobeMembersMap";
import { CrowdBand } from "@/components/CrowdBand";
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
  const [messagesKey, setMessagesKey] = useState(0);

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
      <>
      <CrowdBand />
      <Section title={t("globe.title")} lede={t("globe.lede")}>
        <Empty
          title={t("globe.signedOut")}
          hint=""
          action={<a href="/account"><Button>{t("nav.signIn")}</Button></a>}
        />
      </Section>
      </>
    );
  }

  return (
    <>
    <CrowdBand />
    <Section title={t("globe.title")} lede={t("globe.lede")}>
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_380px]">
        <div className="rounded-xl border border-line bg-surface-raised p-6">
          {/* The real Earth: the members, pinned to the cities they chose. The decorative
              globe and the city list below stay as they were. */}
          <GlobeMembersMap cities={cities ?? []} selectedCity={open?.city} onSelect={openCity} />

          {/* The decorative globe. */}
          <div className="flex justify-center py-6">
            <ErrorBoundary label="Globe" fallback={null}>
              <Globe size={250} className="flex shrink-0 justify-center" />
            </ErrorBoundary>
          </div>

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
                    <Flag country={city.countryCode ?? findCity(city.city)?.country} />
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
                    <Flag country={open.countryCode ?? findCity(open.city)?.country} />
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
                <Globe2 size={18} aria-hidden />
                {t("globe.title")}
              </p>
              <p className="mt-1 text-sm text-ink-mute">{t("globe.lede")}</p>
            </Panel>
          )}

          <PresencePanel onSaved={loadCities} />
        </div>
      </div>

      {/* Remounted after a chat opened from the globe closes, so a new "sent" shows at once. */}
      <GlobeMessages key={messagesKey} />

      {compareWith ? (
        <TasteCompare member={compareWith} onClose={() => setCompareWith(null)} />
      ) : null}

      {writeTo ? (
        <DirectChat userId={writeTo.userId} onClose={() => { setWriteTo(null); setMessagesKey((k) => k + 1); }} />
      ) : null}
    </Section>
    </>
  );
}


/** A country's flag as it is — its own colours, no tint, no backdrop. */
function Flag({ country, className = "h-3.5 w-[18px]" }: { country?: string | null; className?: string }) {
  if (!country) return null;
  return (
    <img
      src={flagUrl(country)}
      alt=""
      title={countryName(country, lang)}
      loading="lazy"
      className={`${className} shrink-0 rounded-[2px] object-cover`}
    />
  );
}

/** The listed city a stored name (and, when known, country) refers to. */
function findCity(name: string, country?: string | null): WorldCity | undefined {
  const key = cityKey(name);
  return WORLD_CITIES.find((c) => cityKey(c.name) === key && (!country || c.country === country))
    ?? WORLD_CITIES.find((c) => cityKey(c.name) === key);
}

/** Opting in, and out. Out is one click and it clears the stored city, not just the flag. */
function PresencePanel({ onSaved }: { onSaved: () => void }) {
  const { user } = useAuth();
  const [chosen, setChosen] = useState<WorldCity | null>(null);
  const [avatar, setAvatar] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    get<{ shareOnGlobe: boolean; city?: string | null; countryCode?: string | null; avatarUrl?: string | null }>("/api/auth/me")
      .then((me) => {
        setVisible(me.shareOnGlobe);
        setChosen(me.city ? findCity(me.city, me.countryCode) ?? null : null);
        setAvatar(me.avatarUrl ?? "");
      })
      .catch(() => null);
  }, [user?.id]);

  async function save(share: boolean) {
    setBusy(true);
    setMessage(null);
    try {
      // The browser is never asked for a position: the city is picked from the list, and the
      // list carries its coordinates.
      await put("/api/globe/presence", {
        shareOnGlobe: share,
        city: share ? chosen?.name ?? null : null,
        countryCode: share ? chosen?.country ?? null : null,
        latitude: share ? chosen?.lat ?? null : null,
        longitude: share ? chosen?.lon ?? null : null,
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
        <Field label={t("globe.city")} hint={`${WORLD_CITIES.length} ${t("globe.citiesKnown", "cities")}`}>
          <CityPicker value={chosen} onChange={setChosen} />
        </Field>

        <Field label={t("globe.avatar")}>
          <Input value={avatar} onChange={(e) => setAvatar(e.target.value)} placeholder="https://…" />
        </Field>

        {message ? <Notice tone="ok">{message}</Notice> : null}

        <div className="flex gap-2">
          <Button size="sm" disabled={busy || !chosen} onClick={() => save(true)}>
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

/** Type a city or a country; pick from the list. Every row shows the country's flag. */
function CityPicker({ value, onChange }: { value: WorldCity | null; onChange: (city: WorldCity | null) => void }) {
  const [text, setText] = useState(value?.name ?? "");
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const box = useRef<HTMLDivElement>(null);

  useEffect(() => { setText(value?.name ?? ""); }, [value]);

  useEffect(() => {
    const close = (e: MouseEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, []);

  // Country names in the page's language, computed once, so "Türkiyə" finds Istanbul.
  const countries = useMemo(() => {
    const names = new Map<string, string>();
    for (const c of WORLD_CITIES) if (!names.has(c.country)) names.set(c.country, countryName(c.country, lang));
    return names;
  }, []);

  // Every city, by country name in the page language, then by city — the whole list, no cap.
  const everything = useMemo(
    () => [...WORLD_CITIES].sort((a, b) =>
      (countries.get(a.country) ?? a.country).localeCompare(countries.get(b.country) ?? b.country, lang)
      || a.name.localeCompare(b.name, lang)),
    [countries],
  );

  const matches = useMemo(() => {
    const key = cityKey(text);
    if (!key || (value && value.name === text)) return everything;
    const starts: WorldCity[] = [];
    const inside: WorldCity[] = [];
    for (const c of WORLD_CITIES) {
      const name = cityKey(c.name);
      const country = cityKey(countries.get(c.country) ?? "");
      if (name.startsWith(key)) starts.push(c);
      else if (name.includes(key) || country.startsWith(key) || c.country.toLowerCase() === key) inside.push(c);
    }
    return [...starts, ...inside];
  }, [text, value, countries, everything]);
  const grouped = matches === everything;

  function pick(city: WorldCity) {
    onChange(city);
    setText(city.name);
    setOpen(false);
  }

  return (
    <div ref={box} className="relative">
      <div className="relative">
        {value && value.name === text ? (
          <span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2">
            <Flag country={value.country} className="h-4 w-[22px]" />
          </span>
        ) : null}
        <Input
          value={text}
          role="combobox"
          aria-expanded={open}
          aria-autocomplete="list"
          placeholder="Baku"
          className={value && value.name === text ? "pl-11" : undefined}
          onFocus={() => setOpen(true)}
          onChange={(e) => {
            setText(e.target.value);
            setActive(0);
            setOpen(true);
            if (value && e.target.value !== value.name) onChange(null);
          }}
          onKeyDown={(e) => {
            if (e.key === "ArrowDown") { e.preventDefault(); setOpen(true); setActive((i) => Math.min(i + 1, matches.length - 1)); }
            else if (e.key === "ArrowUp") { e.preventDefault(); setActive((i) => Math.max(i - 1, 0)); }
            else if (e.key === "Enter" && open && matches[active]) { e.preventDefault(); pick(matches[active]); }
            else if (e.key === "Escape") setOpen(false);
          }}
        />
      </div>

      {open && matches.length > 0 ? (
        <ul
          role="listbox"
          className="absolute z-30 mt-1 max-h-72 w-full overflow-y-auto rounded-lg border border-line bg-surface-raised py-1 shadow-lg"
        >
          {matches.map((city, i) => (
            <li key={`${city.country}-${city.name}`} role="option" aria-selected={i === active}>
              {grouped && (i === 0 || matches[i - 1].country !== city.country) ? (
                <p className="sticky top-0 z-10 flex items-center gap-2 bg-surface-raised px-3 pb-1 pt-2 text-[11px] uppercase tracking-wider text-ink-mute">
                  <Flag country={city.country} className="h-3 w-4" />
                  {countries.get(city.country)}
                </p>
              ) : null}
              <button
                type="button"
                onMouseDown={(e) => e.preventDefault()}
                onMouseEnter={() => setActive(i)}
                onClick={() => pick(city)}
                className={`flex w-full items-center gap-2.5 px-3 py-2 text-left text-sm ${
                  i === active ? "bg-surface text-ink" : "text-ink"
                }`}
              >
                <Flag country={city.country} className="h-4 w-[22px]" />
                <span className="truncate">{city.name}</span>
                <span className="ml-auto truncate text-xs text-ink-mute">{countries.get(city.country)}</span>
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
