import { useCallback, useEffect, useState } from "react";
import { Clapperboard, Eye, Globe2, Grid3x3, Lock, MessageCircle, Settings, Sparkles, Upload, UserRound } from "lucide-react";
import { Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Modal } from "@/components/ui/modal";
import { useAuth } from "@/components/useAuth";
import { FollowButton, FollowList, toggleFollow, type PersonCard } from "@/components/FollowList";
import { DirectChat } from "@/components/DirectChat";
import { ApiError, get, put, query } from "@/lib/api";
import { t, formatWhen } from "@/lib/i18n";
import { cn } from "@/lib/utils";

interface ProfileView {
  id: string;
  username?: string | null;
  fullName: string;
  avatarUrl?: string | null;
  bio?: string | null;
  followers: number;
  following: number;
  isPrivate: boolean;
  isMe: boolean;
  isFollowing: boolean;
  isRequested: boolean;
  followsMe: boolean;
  isOnline: boolean;
  canView: boolean;
  canMessage: boolean;
}

interface ProfileFilm {
  id: string;
  title: string;
  synopsis: string;
  origin: "AiGenerated" | "HandCrafted";
  visibility: "Private" | "Public";
  status: string;
  viewCount: number;
  submittedAtUtc: string;
  streamUrl: string;
}

type Origin = "all" | "human" | "ai";

/** Whose profile: /u/{username} names it, /profile is your own. */
function handleFromPath(): string | null {
  const match = /^\/u\/([^/?#]+)/.exec(window.location.pathname) ?? /^#\/u\/([^/?#]+)/.exec(window.location.hash);
  return match ? decodeURIComponent(match[1]) : null;
}

/**
 * A profile, laid out the way people know from Instagram: picture, handle, counts and bio on
 * top; below, a grid of the member's approved films instead of photos, with a handmade / AI
 * switch on the right. On a private account the owner also decides, film by film, whether it
 * is out in its gallery for everyone or kept for followers.
 */
export default function ProfilePage() {
  const { user, isSignedIn } = useAuth();
  const handle = handleFromPath() ?? user?.username ?? null;
  const [profile, setProfile] = useState<ProfileView | null>(null);
  const [missing, setMissing] = useState(false);
  const [origin, setOrigin] = useState<Origin>("all");
  const [films, setFilms] = useState<ProfileFilm[] | null>(null);
  const [filmCount, setFilmCount] = useState(0);
  const [playing, setPlaying] = useState<ProfileFilm | null>(null);
  const [openList, setOpenList] = useState<"followers" | "following" | null>(null);
  const [chat, setChat] = useState(false);

  const loadProfile = useCallback(async () => {
    if (!handle) return;
    try {
      setProfile(await get<ProfileView>(`/api/people/u/${encodeURIComponent(handle)}`));
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) setMissing(true);
    }
  }, [handle]);

  useEffect(() => { if (isSignedIn) void loadProfile(); }, [isSignedIn, loadProfile]);

  useEffect(() => {
    if (!profile?.canView) return;
    setFilms(null);
    void get<{ items: ProfileFilm[]; publicCount: number }>(`/api/shorts/by/${profile.id}` + query({ origin: origin === "all" ? undefined : origin }))
      .then((r) => { setFilms(r.items); setFilmCount(r.publicCount); })
      .catch(() => setFilms([]));
  }, [profile?.id, profile?.canView, origin]);

  // A private account's film: out in its gallery for everyone, or kept for followers. Shown
  // at once, put back if the server says no.
  async function setListed(film: ProfileFilm, listed: boolean) {
    const visibility: ProfileFilm["visibility"] = listed ? "Public" : "Private";
    const apply = (value: ProfileFilm["visibility"]) =>
      setFilms((list) => list && list.map((f) => f.id === film.id ? { ...f, visibility: value } : f));
    apply(visibility);
    try { await put(`/api/shorts/${film.id}/visibility`, { id: film.id, visibility }); }
    catch { apply(film.visibility); }
  }

  if (!isSignedIn) {
    return (
      <section className="mx-auto max-w-4xl px-4 py-16">
        <Empty title={t("profile.signedOut", "Sign in to see profiles.")} hint=""
          action={<a href={`/account?returnUrl=${encodeURIComponent(window.location.pathname)}`}><Button>{t("nav.signIn")}</Button></a>} />
      </section>
    );
  }
  if (missing) {
    return (
      <section className="mx-auto max-w-4xl px-4 py-16">
        <Empty title={t("profile.notFound", "This profile does not exist.")} hint={t("profile.notFoundHint", "The link may be wrong, or the account was removed.")}
          action={<a href="/people"><Button variant="outline">{t("profile.findPeople", "Find people")}</Button></a>} />
      </section>
    );
  }
  if (!profile) return <section className="mx-auto max-w-4xl px-4 py-16"><Spinner label={t("common.loading")} /></section>;

  const asCard: PersonCard = {
    id: profile.id, username: profile.username, fullName: profile.fullName, avatarUrl: profile.avatarUrl,
    followers: profile.followers, isFollowing: profile.isFollowing, isMe: profile.isMe, isOnline: profile.isOnline,
    isRequested: profile.isRequested, isPrivate: profile.isPrivate, followsMe: profile.followsMe,
  };

  const follow = () => void toggleFollow(asCard, (next) => setProfile((p) => p && {
    ...p, ...next,
    followers: p.followers + (next.isFollowing ? 1 : 0) - (p.isFollowing ? 1 : 0),
    // Following a private account you were not let into yet changes nothing else on screen;
    // unfollowing one hides its grid again.
    canView: p.isMe || !p.isPrivate || next.isFollowing,
  }));

  return (
    <section className="mx-auto w-full max-w-4xl px-4 pb-16 pt-8 md:pt-12">
      {/* Header: picture on the left, everything else to its right. */}
      <header className="grid grid-cols-[auto_1fr] items-center gap-5 md:gap-12 md:px-8">
        <div className="relative">
          <div className="rounded-full bg-gradient-to-tr from-accent-dim via-accent to-accent-bright p-[3px]">
            <img src={profile.avatarUrl || "/icons/icon.svg"} alt=""
              className="h-20 w-20 rounded-full border-4 border-surface bg-surface object-cover md:h-36 md:w-36" />
          </div>
          {profile.isOnline ? (
            <span className="absolute bottom-1 right-1 h-4 w-4 rounded-full border-[3px] border-surface bg-good md:bottom-3 md:right-3 md:h-5 md:w-5"
              title={t("people.online", "Online")} />
          ) : null}
        </div>

        <div className="min-w-0 space-y-4">
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="flex items-center gap-2 truncate font-display text-xl text-ink md:text-2xl">
              {profile.username ? profile.username : profile.fullName}
              {profile.isPrivate ? <Lock size={16} className="text-ink-mute" aria-label={t("profile.private", "Private account")} /> : null}
            </h1>
            {profile.isMe ? (
              <>
                <a href="/profile/edit"><Button size="sm" variant="outline"><Settings size={14} aria-hidden /> {t("profile.edit", "Edit profile")}</Button></a>
                <a href="/studio"><Button size="sm" variant="outline"><Upload size={14} aria-hidden /> {t("profile.upload", "Upload a film")}</Button></a>
              </>
            ) : (
              <>
                <FollowButton person={asCard} onToggle={follow} />
                {profile.canMessage ? (
                  <Button size="sm" variant="outline" onClick={() => setChat(true)}>
                    <MessageCircle size={14} aria-hidden /> {t("profile.message", "Message")}
                  </Button>
                ) : null}
              </>
            )}
          </div>

          <div className="flex gap-6 text-sm text-ink md:gap-10 md:text-base">
            <p><strong>{profile.canView ? filmCount : "–"}</strong> {t("profile.films", "films")}</p>
            <button className="text-left hover:text-accent disabled:hover:text-ink" disabled={!profile.canView} onClick={() => setOpenList("followers")}>
              <strong>{profile.followers}</strong> {t("people.followers", "followers")}
            </button>
            <button className="text-left hover:text-accent disabled:hover:text-ink" disabled={!profile.canView} onClick={() => setOpenList("following")}>
              <strong>{profile.following}</strong> {t("profile.following", "following")}
            </button>
          </div>

          <div className="space-y-1">
            <p className="font-semibold text-ink">{profile.fullName}</p>
            {profile.bio ? <p className="whitespace-pre-line text-sm text-ink-mute">{profile.bio}</p> : null}
            {!profile.isMe && profile.followsMe ? <p className="text-xs text-accent">{t("profile.followsYou", "Follows you")}</p> : null}
          </div>
        </div>
      </header>

      {/* The grid's heading, with the handmade / AI switch on the right. */}
      <div className="mt-10 flex flex-wrap items-center justify-between gap-3 border-t border-line">
        <p className="-mt-px inline-flex items-center gap-2 border-t-2 border-accent py-3 text-xs font-semibold uppercase tracking-widest text-ink">
          <Grid3x3 size={15} aria-hidden /> {t("profile.filmsTab", "Films")}
        </p>
        {profile.canView ? (
          <div className="mt-2 inline-flex rounded-full border border-line bg-surface-raised p-1 text-xs" role="radiogroup" aria-label={t("profile.category", "Category")}>
            {([["all", t("profile.all", "All"), null], ["human", t("profile.handmade", "Handmade"), <UserRound size={13} />], ["ai", t("profile.ai", "Made with AI"), <Sparkles size={13} />]] as const).map(([key, label, icon]) => (
              <button key={key} role="radio" aria-checked={origin === key} onClick={() => setOrigin(key)}
                className={cn("inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 transition-colors",
                  origin === key ? "bg-accent text-surface" : "text-ink-mute hover:text-ink")}>
                {icon}{label}
              </button>
            ))}
          </div>
        ) : null}
      </div>

      <div className="mt-6">
        {!profile.canView ? (
          <EmptyGrid icon={<Lock size={30} />} title={t("profile.lockedTitle", "This account is private")}
            hint={t("profile.lockedHint", "Follow it to see its films and lists.")} />
        ) : films === null ? (
          <Spinner label={t("common.loading")} />
        ) : films.length === 0 ? (
          profile.isMe ? (
            <EmptyGrid icon={<Clapperboard size={30} />}
              title={t("profile.shareTitle", "Share your films")}
              hint={t("profile.shareHint", "Films you upload in Studio appear here once an admin approves them.")}
              action={<a href="/studio" className="text-sm font-medium text-accent hover:underline">{t("profile.shareFirst", "Upload your first film")}</a>} />
          ) : (
            <EmptyGrid icon={<Clapperboard size={30} />} title={t("profile.noFilms", "No films yet")} hint="" />
          )
        ) : (
          <div className="grid grid-cols-3 gap-1 md:gap-3">
            {films.map((film) => (
              <FilmTile key={film.id} film={film} onOpen={() => setPlaying(film)}
                onListed={profile.isMe && profile.isPrivate ? (listed) => void setListed(film, listed) : undefined} />
            ))}
          </div>
        )}
      </div>

      {playing ? (
        <Modal onClose={() => setPlaying(null)} label={playing.title} width="max-w-3xl">
          <div className="overflow-hidden rounded-xl border border-line bg-surface-raised">
            <video src={playing.streamUrl} controls autoPlay playsInline className="aspect-video w-full bg-black" />
            <div className="space-y-2 p-5">
              <div className="flex flex-wrap items-center gap-2">
                <h2 className="font-display text-lg text-ink">{playing.title}</h2>
                <OriginBadge origin={playing.origin} />
              </div>
              {playing.synopsis ? <p className="text-sm text-ink-mute">{playing.synopsis}</p> : null}
              <p className="flex items-center gap-1.5 text-xs text-ink-mute">
                <Eye size={13} aria-hidden /> {playing.viewCount} · {formatWhen(playing.submittedAtUtc, { day: "numeric", month: "short", year: "numeric" })}
              </p>
            </div>
          </div>
        </Modal>
      ) : null}

      {openList ? <FollowList userId={profile.id} kind={openList} onClose={() => setOpenList(null)} onChanged={loadProfile} /> : null}
      {chat ? <DirectChat userId={profile.id} onClose={() => setChat(false)} /> : null}
    </section>
  );
}

function OriginBadge({ origin }: { origin: ProfileFilm["origin"] }) {
  return origin === "AiGenerated" ? (
    <span className="inline-flex items-center gap-1 rounded-full bg-accent/15 px-2 py-0.5 text-[11px] font-medium text-accent"><Sparkles size={11} aria-hidden /> {t("profile.ai", "Made with AI")}</span>
  ) : (
    <span className="inline-flex items-center gap-1 rounded-full bg-ink/10 px-2 py-0.5 text-[11px] font-medium text-ink"><UserRound size={11} aria-hidden /> {t("profile.handmade", "Handmade")}</span>
  );
}

/**
 * One square: the film's own frame as the picture, its views and title on hover. For the owner
 * of a private account it also carries the switch between "in the gallery for everyone" and
 * "followers only".
 */
function FilmTile({ film, onOpen, onListed }: { film: ProfileFilm; onOpen: () => void; onListed?: (listed: boolean) => void }) {
  const listed = film.visibility === "Public";
  return (
    <div className="relative">
      <button onClick={onOpen} className="group relative block aspect-square w-full overflow-hidden rounded-md bg-surface-raised focus-visible:outline-2 focus-visible:outline-accent md:rounded-lg"
        aria-label={film.title}>
        <video src={`${film.streamUrl}#t=6`} muted playsInline preload="metadata" className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-105" />
        <span className="absolute right-2 top-2 rounded-full bg-black/60 p-1.5 text-white" title={film.origin === "AiGenerated" ? t("profile.ai", "Made with AI") : t("profile.handmade", "Handmade")}>
          {film.origin === "AiGenerated" ? <Sparkles size={13} aria-hidden /> : <UserRound size={13} aria-hidden />}
        </span>
        {onListed && !listed ? (
          <span className="absolute left-2 top-2 inline-flex items-center gap-1 rounded-full bg-black/60 px-2 py-0.5 text-[10px] font-semibold text-white">
            <Lock size={10} aria-hidden /> {t("profile.followersOnly", "Followers only")}
          </span>
        ) : null}
        <span className="absolute inset-0 flex flex-col items-center justify-center gap-1 bg-black/55 p-2 text-center text-white opacity-0 transition-opacity group-hover:opacity-100">
          <span className="line-clamp-2 text-sm font-semibold">{film.title}</span>
          <span className="flex items-center gap-1 text-xs"><Eye size={13} aria-hidden /> {film.viewCount}</span>
        </span>
      </button>
      {onListed ? (
        <button
          onClick={() => onListed(!listed)}
          title={listed ? t("profile.listedHint", "Everyone sees it in the gallery; your profile stays for followers.") : t("profile.followersOnlyHint", "Only your followers see it, on your profile.")}
          className={cn("absolute bottom-2 left-1/2 inline-flex -translate-x-1/2 items-center gap-1.5 whitespace-nowrap rounded-full px-3 py-1.5 text-xs font-semibold shadow-lg transition-colors",
            listed ? "bg-black/70 text-white hover:bg-black/85" : "bg-accent text-surface hover:bg-accent-bright")}
        >
          {listed ? <Lock size={13} aria-hidden /> : <Globe2 size={13} aria-hidden />}
          {listed ? t("profile.makeFollowersOnly", "Followers only") : t("profile.showInGallery", "Show in the gallery")}
        </button>
      ) : null}
    </div>
  );
}

function EmptyGrid({ icon, title, hint, action }: { icon: React.ReactNode; title: string; hint: string; action?: React.ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-3 py-16 text-center">
      <span className="flex h-20 w-20 items-center justify-center rounded-full border-2 border-ink text-ink">{icon}</span>
      <p className="font-display text-2xl text-ink">{title}</p>
      {hint ? <p className="max-w-sm text-sm text-ink-mute">{hint}</p> : null}
      {action}
    </div>
  );
}
