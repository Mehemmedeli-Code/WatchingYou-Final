import { useCallback, useEffect, useMemo, useState } from "react";
import { Clock, Languages, MapPin, Ticket, Users } from "lucide-react";
import { Section, Panel, Empty, Notice, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { ApiError, del, get, post } from "@/lib/api";
import { CITIES, formatDateTime } from "@/lib/format";
import { lang, languageName, t } from "@/lib/i18n";
import { flagUrl } from "@/lib/worldCities";
import { cn } from "@/lib/utils";

type Partner = "female" | "male" | "mixed";

interface Show {
  screeningId: string;
  movieTitle: string;
  posterUrl?: string | null;
  venue: string;
  city: string;
  hall: string;
  startsAtUtc: string;
  audioLanguage: string;
  subtitleLanguage?: string | null;
}
interface MyTicket extends Show { seats: number; searching: boolean }
/** /api/screenings, the public list: what is on and how many seats are left. */
interface Screening {
  id: string; movieTitle: string; posterUrl?: string | null; venueName: string; city: string; hall: string;
  startsAtUtc: string; audioLanguage: string; subtitleLanguage?: string | null; capacity: number; seatsTaken: number;
}
interface Search extends Show {
  id: string;
  ownerName: string;
  ownerUsername?: string | null;
  mine: boolean;
  joined: boolean;
  partner: Partner;
  peopleWanted: number;
  peopleJoined: number;
  hasTickets: boolean;
  found: boolean;
  cancelled: boolean;
  seatsLeft: number;
  /** Who joined: sent to the owner only. */
  members: { name: string; username?: string | null }[];
}

const PARTNER_ENUM: Record<Partner, string> = { female: "Female", male: "Male", mixed: "Mixed" };
type Pick = { show: Show; most: number };

/**
 * SearchingYou: company for a film. Four questions — what company; tickets bought or to buy; which
 * film; how many — and the search joins the list below. Bought, it can bring one person fewer than
 * the tickets held; to buy, it lasts while the screening has a seat for each of the group, and is
 * called off when it no longer does.
 */
export default function SearchingPage() {
  const [step, setStep] = useState(1);
  const [partner, setPartner] = useState<Partner | null>(null);
  const [bought, setBought] = useState<boolean | null>(null);
  const [pick, setPick] = useState<Pick | null>(null);
  const [people, setPeople] = useState(1);
  const [tickets, setTickets] = useState<MyTicket[] | null>(null);
  const [shows, setShows] = useState<Screening[] | null>(null);
  const [city, setCity] = useState(CITIES[0].city);
  const [query, setQuery] = useState("");
  const [searches, setSearches] = useState<Search[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);

  const load = useCallback(async () => {
    setSearches(await get<Search[]>("/api/searching").catch(() => []));
  }, []);
  useEffect(() => { void load(); }, [load]);
  useEffect(() => {
    if (step !== 3) return;
    if (bought && tickets === null) void get<MyTicket[]>("/api/searching/tickets").then(setTickets).catch(() => setTickets([]));
    if (bought === false && shows === null) void get<Screening[]>("/api/screenings").then(setShows).catch(() => setShows([]));
  }, [step, bought, tickets, shows]);

  // Screenings to buy for: in the chosen city, not started, with a seat for the owner and one more.
  const choices = useMemo(() => {
    const now = Date.now();
    const q = query.trim().toLowerCase();
    return (shows ?? [])
      .filter((s) => s.city === city && new Date(s.startsAtUtc).getTime() > now && s.capacity - s.seatsTaken >= 2
        && (!q || s.movieTitle.toLowerCase().includes(q)))
      .sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc))
      .slice(0, 40);
  }, [shows, city, query]);

  const fail = (err: unknown) =>
    setMessage({ tone: "error", text: err instanceof ApiError && err.status < 500 ? err.message : t("error.action") });

  function reset() {
    setStep(1); setPartner(null); setBought(null); setPick(null); setPeople(1); setTickets(null); setShows(null); setQuery("");
  }

  async function start() {
    if (!partner || bought === null || !pick) return;
    setBusy(true); setMessage(null);
    try {
      await post("/api/searching", { screeningId: pick.show.screeningId, partner: PARTNER_ENUM[partner], people, hasTickets: bought });
      setMessage({ tone: "ok", text: t("searching.started") });
      reset();
      await load();
    } catch (err) { fail(err); } finally { setBusy(false); }
  }

  async function act(search: Search, action: "join" | "leave" | "close") {
    setBusy(true); setMessage(null);
    try {
      if (action === "join") await post(`/api/searching/${search.id}/join`);
      else if (action === "leave") await del(`/api/searching/${search.id}/join`);
      else await del(`/api/searching/${search.id}`);
      await load();
    } catch (err) { fail(err); } finally { setBusy(false); }
  }

  const calledOff = searches?.filter((s) => s.cancelled && (s.mine || s.joined)) ?? [];

  return (
    <Section title={t("searching.title")} lede={t("searching.lede")}>
      {/* The meet-ups that fell through, for the owner and everyone who joined. */}
      {calledOff.map((s) => (
        <div key={s.id} className="mb-3">
          <Notice tone="error">{t("searching.cancelled")} — {s.movieTitle}, {formatDateTime(s.startsAtUtc, s.city)}</Notice>
        </div>
      ))}

      <Panel className="mb-8">
        <p className="mb-1 text-xs font-semibold uppercase tracking-wider text-accent">{t("searching.step")} {step} / 4</p>

        {step === 1 ? (
          <>
            <h3 className="mb-5 font-display text-2xl text-ink">{t("searching.qPartner")}</h3>
            <div className="grid max-w-xl grid-cols-3 gap-3 sm:gap-5">
              {(["female", "male", "mixed"] as const).map((kind) => (
                <button
                  key={kind}
                  type="button"
                  onClick={() => { setPartner(kind); setStep(2); }}
                  aria-pressed={partner === kind}
                  className={cn(
                    "group overflow-hidden rounded-2xl border-2 text-left transition hover:-translate-y-0.5",
                    partner === kind ? "border-accent" : "border-transparent hover:border-accent/60",
                  )}
                >
                  <PartnerPicture kind={kind} />
                  <span className="block bg-surface-raised px-3 py-2 text-center text-sm font-semibold text-ink">{t(`searching.${kind}`)}</span>
                </button>
              ))}
            </div>
          </>
        ) : null}

        {step === 2 ? (
          <>
            <h3 className="mb-5 font-display text-2xl text-ink">{t("searching.qBought")}</h3>
            <div className="grid max-w-xl gap-3 sm:grid-cols-2">
              {[true, false].map((value) => (
                <button
                  key={String(value)}
                  type="button"
                  onClick={() => { setBought(value); setPick(null); setPeople(1); setStep(3); }}
                  aria-pressed={bought === value}
                  className={cn("rounded-2xl border-2 bg-surface p-4 text-left transition hover:border-accent/60",
                    bought === value ? "border-accent" : "border-line")}
                >
                  <Ticket size={20} className="mb-2 text-accent" aria-hidden />
                  <span className="block font-semibold text-ink">{t(value ? "searching.bought" : "searching.willBuy")}</span>
                  <span className="mt-1 block text-xs text-ink-mute">{t(value ? "searching.boughtHint" : "searching.willBuyHint")}</span>
                </button>
              ))}
            </div>
            <div className="mt-6"><Button variant="ghost" onClick={() => setStep(1)}>{t("searching.back")}</Button></div>
          </>
        ) : null}

        {step === 3 && bought ? (
          <>
            <h3 className="mb-5 font-display text-2xl text-ink">{t("searching.qTicket")}</h3>
            {tickets === null ? <Spinner label={t("common.loading")} /> : null}
            {tickets?.length === 0 ? (
              <Empty title={t("searching.noTickets")} hint="" action={<a href="/on-display"><Button>{t("searching.buy")}</Button></a>} />
            ) : null}
            <div className="grid gap-3 md:grid-cols-2">
              {tickets?.map((ticket) => {
                const most = Math.min(10, ticket.seats - 1);
                return (
                  <ChoiceCard key={ticket.screeningId} show={ticket} chosen={pick?.show.screeningId === ticket.screeningId}
                    disabled={ticket.searching || most < 1}
                    onPick={() => { setPick({ show: ticket, most }); setPeople(1); setStep(4); }}
                    note={ticket.searching ? t("searching.already") : most < 1 ? t("searching.needTwo") : null}>
                    {ticket.seats} {t("searching.seats")}
                  </ChoiceCard>
                );
              })}
            </div>
            <div className="mt-6"><Button variant="ghost" onClick={() => setStep(2)}>{t("searching.back")}</Button></div>
          </>
        ) : null}

        {step === 3 && bought === false ? (
          <>
            <h3 className="mb-5 font-display text-2xl text-ink">{t("searching.qShow")}</h3>
            <div className="mb-4 flex flex-wrap gap-2">
              {CITIES.map((c) => (
                <Button key={c.city} size="sm" variant={city === c.city ? "solid" : "outline"} onClick={() => setCity(c.city)}>
                  <img src={flagUrl(c.country)} alt="" className="h-3 w-4 rounded-sm object-cover" />
                  {t(`city.${c.city}`, c.city)}
                </Button>
              ))}
              <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t("searching.findFilm")} aria-label={t("searching.findFilm")}
                className="h-8 min-w-0 flex-1 rounded-lg border border-line bg-surface px-3 text-sm text-ink placeholder:text-ink-mute focus:border-accent focus:outline-none" />
            </div>
            {shows === null ? <Spinner label={t("common.loading")} /> : null}
            {shows && choices.length === 0 ? <Empty title={t("searching.noShows")} hint="" /> : null}
            <div className="grid gap-3 md:grid-cols-2">
              {choices.map((s) => {
                const show: Show = { screeningId: s.id, movieTitle: s.movieTitle, posterUrl: s.posterUrl, venue: s.venueName, city: s.city, hall: s.hall,
                  startsAtUtc: s.startsAtUtc, audioLanguage: s.audioLanguage, subtitleLanguage: s.subtitleLanguage };
                const free = s.capacity - s.seatsTaken;
                return (
                  <ChoiceCard key={s.id} show={show} chosen={pick?.show.screeningId === s.id}
                    onPick={() => { setPick({ show, most: Math.min(10, free - 1) }); setPeople(1); setStep(4); }}>
                    {free} {t("searching.seatsLeft")}
                  </ChoiceCard>
                );
              })}
            </div>
            <div className="mt-6"><Button variant="ghost" onClick={() => setStep(2)}>{t("searching.back")}</Button></div>
          </>
        ) : null}

        {step === 4 && pick ? (
          <>
            <h3 className="mb-2 font-display text-2xl text-ink">{t("searching.qPeople")}</h3>
            <p className="mb-5 text-sm text-ink-mute">
              {pick.show.movieTitle} · {bought ? t("searching.mostBought") : t("searching.mostToBuy")}: {pick.most}
            </p>
            <div className="flex flex-wrap gap-2">
              {Array.from({ length: pick.most }, (_, i) => i + 1).map((n) => (
                <Button key={n} variant={people === n ? "solid" : "outline"} onClick={() => setPeople(n)} className="w-12" aria-pressed={people === n}>
                  {n}
                </Button>
              ))}
            </div>
            <div className="mt-6 flex gap-2">
              <Button variant="ghost" onClick={() => setStep(3)}>{t("searching.back")}</Button>
              <Button disabled={busy} onClick={start}>{t("searching.start")}</Button>
            </div>
          </>
        ) : null}
      </Panel>

      {message ? <div className="mb-6"><Notice tone={message.tone}>{message.text}</Notice></div> : null}

      <h2 className="mb-4 font-display text-2xl text-ink">{t("searching.active")}</h2>
      {searches === null ? <Spinner label={t("common.loading")} /> : null}
      {searches?.length === 0 ? <Empty title={t("searching.none")} hint={t("searching.noneHint")} /> : null}
      <div className="grid gap-4 md:grid-cols-2">
        {searches?.map((search) => (
          <Panel key={search.id} className={cn("flex flex-col gap-4", search.cancelled && "opacity-80")}>
            <div className="flex items-center gap-3">
              <div className="h-12 w-12 shrink-0 overflow-hidden rounded-xl"><PartnerPicture kind={search.partner} /></div>
              <p className="min-w-0 text-sm text-ink">
                {search.ownerUsername
                  ? <a href={`/u/${search.ownerUsername}`} className="font-semibold hover:text-accent">{search.ownerName}</a>
                  : <span className="font-semibold">{search.ownerName}</span>}
                {" "}{t("searching.looking")}{" "}
                <span className="font-semibold text-accent">{search.peopleWanted} {t("searching.people")} · {t(`searching.${search.partner}`)}</span>
              </p>
              {search.cancelled ? <Badge tone="bad" className="ml-auto shrink-0">{t("searching.off")}</Badge>
                : search.found ? <Badge tone="good" className="ml-auto shrink-0">{t("searching.found")}</Badge>
                : search.mine ? <Badge tone="good" className="ml-auto shrink-0">{t("searching.mine")}</Badge> : null}
            </div>
            <div className="flex min-w-0 gap-3"><ShowFacts show={search} /></div>
            {search.cancelled ? <Notice tone="error">{t("searching.cancelled")}</Notice> : null}
            <p className="text-xs text-ink-mute">
              {search.hasTickets ? t("searching.ticketsBought") : `${t("searching.ticketsToBuy")} · ${search.seatsLeft} ${t("searching.seatsLeft")}`}
            </p>
            {search.mine && search.members.length > 0 ? (
              <p className="text-xs text-ink-mute">
                {t("searching.members")}:{" "}
                {search.members.map((m, i) => (
                  <span key={i}>
                    {i > 0 ? ", " : ""}
                    {m.username ? <a href={`/u/${m.username}`} className="font-semibold text-ink hover:text-accent">{m.name}</a> : <span className="font-semibold text-ink">{m.name}</span>}
                  </span>
                ))}
              </p>
            ) : null}
            <div className="mt-auto flex flex-wrap items-center gap-2">
              <span className="mr-auto flex items-center gap-1.5 text-xs text-ink-mute">
                <Users size={13} aria-hidden /> {search.peopleJoined}/{search.peopleWanted}
                {search.found || search.cancelled ? "" : ` · ${search.peopleWanted - search.peopleJoined} ${t("searching.places")}`}
              </span>
              {search.cancelled ? (search.joined ? <Button size="sm" variant="outline" disabled={busy} onClick={() => act(search, "leave")}>{t("searching.leave")}</Button> : null) : search.mine ? (
                <>
                  {/* The owner buys the tickets for the whole group. */}
                  {search.hasTickets ? null : <a href={`/cinema?screening=${search.screeningId}`}><Button size="sm" variant="outline">{t("searching.buyGroup")}</Button></a>}
                  {search.found ? null : <Button size="sm" variant="danger" disabled={busy} onClick={() => act(search, "close")}>{t("searching.close")}</Button>}
                </>
              ) : search.joined ? (
                <>
                  {/* Joining buys nothing: the owner buys for the group. Leaving frees the place again. */}
                  <Badge tone="good">{t("searching.joined")}</Badge>
                  <Button size="sm" variant="outline" disabled={busy} onClick={() => act(search, "leave")}>{t("searching.leave")}</Button>
                </>
              ) : (
                <Button size="sm" disabled={busy} onClick={() => act(search, "join")}>{t("searching.join")}</Button>
              )}
            </div>
          </Panel>
        ))}
      </div>
    </Section>
  );
}

function ChoiceCard({ show, chosen, disabled, onPick, note, children }: {
  show: Show; chosen: boolean; disabled?: boolean; onPick: () => void; note?: string | null; children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onPick}
      aria-pressed={chosen}
      className={cn(
        "flex flex-col gap-2 rounded-xl border-2 bg-surface p-3 text-left transition disabled:cursor-not-allowed disabled:opacity-50",
        chosen ? "border-accent" : "border-line hover:border-accent/60",
      )}
    >
      <span className="flex min-w-0 gap-3"><ShowFacts show={show} /></span>
      <span className="flex flex-wrap gap-x-3 text-xs text-ink-mute">
        {children}
        {note ? <span className="text-accent">{note}</span> : null}
      </span>
    </button>
  );
}

/** The picture for a kind of company: a girl on pink, a boy on blue, or both side by side. */
function PartnerPicture({ kind }: { kind: Partner }) {
  // The pictures are grey on grey; multiplied onto the colour, the colour shows through them.
  // Multiplied onto the colour. male.jpg has its face darkened (an ffmpeg curve): its face was nearly the grey of its background and vanished into the blue.
  const img = (src: string) => <img src={src} alt="" className="h-full w-full object-cover mix-blend-multiply" draggable={false} />;
  if (kind === "mixed") {
    return (
      // Each half shows its whole figure, head to shoulders, with a light line between them.
      <span className="grid aspect-square grid-cols-2 gap-px overflow-hidden bg-white/70">
        <span className="overflow-hidden bg-pink-300">{img("/img/searching/female.jpg")}</span>
        <span className="overflow-hidden bg-sky-300">{img("/img/searching/male.jpg")}</span>
      </span>
    );
  }
  return (
    <span className={cn("block aspect-square", kind === "female" ? "bg-pink-300" : "bg-sky-300")}>
      {img(kind === "female" ? "/img/searching/female.jpg" : "/img/searching/male.jpg")}
    </span>
  );
}

/** Where, when and in which language: the film, its country and city, cinema and hall, the time there. */
function ShowFacts({ show }: { show: Show }) {
  const country = CITIES.find((c) => c.city === show.city)?.country;
  const countryName = country ? new Intl.DisplayNames([lang], { type: "region" }).of(country.toUpperCase()) : null;
  return (
    <>
      {show.posterUrl
        ? <img src={show.posterUrl} alt="" className="h-24 w-16 shrink-0 rounded-md object-cover" loading="lazy" />
        : <span className="h-24 w-16 shrink-0 rounded-md bg-line" />}
      <span className="min-w-0 space-y-1 text-xs text-ink-mute">
        <span className="block truncate font-display text-base text-ink">{show.movieTitle}</span>
        <span className="flex items-center gap-1.5">
          {country ? <img src={flagUrl(country)} alt="" className="h-3 w-4 rounded-sm object-cover" /> : <MapPin size={12} aria-hidden />}
          {countryName ? `${countryName} · ` : ""}{t(`city.${show.city}`, show.city)} · {show.venue} · {show.hall}
        </span>
        <span className="flex items-center gap-1.5"><Clock size={12} aria-hidden /> {formatDateTime(show.startsAtUtc, show.city)}</span>
        <span className="flex items-center gap-1.5">
          <Languages size={12} aria-hidden /> {languageName(show.audioLanguage)}
          {show.subtitleLanguage ? ` · ${t("searching.subs")}: ${languageName(show.subtitleLanguage)}` : ""}
        </span>
      </span>
    </>
  );
}
