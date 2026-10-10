import { useEffect, useState } from "react";
import { createPortal } from "react-dom";
import { UserCheck, UserPlus, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Spinner } from "@/components/Shell";
import { get, post, del, query } from "@/lib/api";
import { t } from "@/lib/i18n";

export interface PersonCard {
  id: string;
  username?: string | null;
  fullName: string;
  avatarUrl?: string | null;
  followers: number;
  isFollowing: boolean;
  isMe: boolean;
  isOnline: boolean;
  /** Asked to follow this private account; waiting for an answer. */
  isRequested?: boolean;
  isPrivate?: boolean;
  /** They follow the viewer: the button says "Follow back". */
  followsMe?: boolean;
}

export interface PeoplePage {
  items: PersonCard[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}

type Relation = Pick<PersonCard, "isFollowing" | "isRequested">;

/**
 * Follow, unfollow, or take back a request. Shown at once and put back if the server says no;
 * following a private account turns into "Requested" once the server says so.
 */
export async function toggleFollow(person: PersonCard, update: (next: Relation) => void) {
  const before: Relation = { isFollowing: person.isFollowing, isRequested: !!person.isRequested };
  const stop = person.isFollowing || person.isRequested;
  update(stop ? { isFollowing: false, isRequested: false } : { isFollowing: !person.isPrivate, isRequested: !!person.isPrivate });
  try {
    if (stop) await del(`/api/people/${person.id}/follow`);
    else {
      const result = await post<{ status: "following" | "requested" }>(`/api/people/${person.id}/follow`);
      update({ isFollowing: result.status === "following", isRequested: result.status === "requested" });
    }
  } catch {
    update(before);
  }
}

/** Applies a new relation to one card in a list, keeping its follower count in step. */
export const flip = (p: PersonCard, id: string, next: Relation): PersonCard =>
  p.id !== id ? p : { ...p, ...next, followers: p.followers + (next.isFollowing ? 1 : 0) - (p.isFollowing ? 1 : 0) };

/** The Follow button for one person, in whichever of its four states applies. */
export function FollowButton({ person, onToggle }: { person: PersonCard; onToggle: () => void }) {
  const [label, active] = person.isFollowing ? [t("people.following", "Following"), true]
    : person.isRequested ? [t("people.requested", "Requested"), true]
    : person.followsMe ? [t("people.followBack", "Follow back"), false]
    : [t("people.follow", "Follow"), false];
  return (
    <Button size="sm" variant={active ? "outline" : "solid"} onClick={onToggle}>
      {active ? <UserCheck size={14} aria-hidden /> : <UserPlus size={14} aria-hidden />}
      {label}
    </Button>
  );
}

/** One person in a list: picture with an online dot, name, @handle, and a Follow button. */
export function PersonRow({ person, onToggle }: { person: PersonCard; onToggle: () => void }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <a href={person.username ? `/u/${encodeURIComponent(person.username)}` : undefined} className="group flex min-w-0 items-center gap-3">
        <span className="relative shrink-0">
          <img src={person.avatarUrl || "/icons/icon.svg"} alt="" className="h-11 w-11 rounded-full border border-line object-cover" />
          {person.isOnline ? <span className="absolute bottom-0 right-0 h-3 w-3 rounded-full border-2 border-surface bg-good" title={t("people.online", "Online")} /> : null}
        </span>
        <span className="min-w-0">
          <span className="block truncate text-sm font-medium text-ink group-hover:text-accent">{person.fullName}</span>
          <span className="block truncate text-xs text-ink-mute">{person.username ? `@${person.username}` : ""}</span>
        </span>
      </a>
      {person.isMe ? <Badge>{t("globe.you")}</Badge> : <FollowButton person={person} onToggle={onToggle} />}
    </div>
  );
}

/**
 * The list behind a follower / following count, like Instagram's: newest first, thirty at a
 * time, with a Follow button on every row.
 */
export function FollowList({ userId, kind, onClose, onChanged }: {
  userId: string;
  kind: "followers" | "following";
  onClose: () => void;
  /** Called when the viewer follows or unfollows someone from inside the list. */
  onChanged?: () => void;
}) {
  const [list, setList] = useState<PeoplePage | null>(null);
  const [loading, setLoading] = useState(false);
  const [hidden, setHidden] = useState(false);   // a private account that has not accepted the viewer

  async function load(page: number) {
    setLoading(true);
    const next = await get<PeoplePage>(`/api/people/${userId}/${kind}` + query({ page })).catch(() => null);
    if (!next && page === 1) { setHidden(true); setList({ items: [], page: 1, pageSize: 30, hasMore: false }); }
    if (next) setList((current) => page > 1 && current ? { ...next, items: [...current.items, ...next.items] } : next);
    setLoading(false);
  }

  useEffect(() => { void load(1); }, [userId, kind]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  const title = kind === "followers" ? t("people.followersTitle", "Followers") : t("people.followingTitle", "Following");

  return createPortal(
    <div className="fixed inset-0 z-[90] flex items-center justify-center bg-black/70 p-4" onClick={onClose}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="flex max-h-[80vh] w-full max-w-md flex-col rounded-xl border border-line bg-surface-raised"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-line px-5 py-3">
          <p className="font-display text-lg text-ink">{title}</p>
          <button onClick={onClose} aria-label={t("common.close", "Close")} className="text-ink-mute hover:text-ink">
            <X size={18} aria-hidden />
          </button>
        </div>
        <div className="flex-1 space-y-3 overflow-y-auto px-5 py-4">
          {list === null ? <Spinner label={t("common.loading")} /> : null}
          {hidden ? (
            <p className="py-6 text-center text-sm text-ink-mute">{t("people.privateLists", "This account is private. Follow it to see who it follows.")}</p>
          ) : null}
          {list && list.items.length === 0 && !hidden ? (
            <p className="py-6 text-center text-sm text-ink-mute">
              {kind === "followers" ? t("people.noFollowers", "No followers yet.") : t("people.noFollowing", "Not following anyone yet.")}
            </p>
          ) : null}
          {list?.items.map((person) => (
            <PersonRow
              key={person.id}
              person={person}
              onToggle={() => void toggleFollow(person, (next) => {
                setList((l) => l && { ...l, items: l.items.map((p) => flip(p, person.id, next)) });
                onChanged?.();
              })}
            />
          ))}
          {list?.hasMore ? (
            <Button className="w-full" size="sm" variant="outline" disabled={loading} onClick={() => void load(list.page + 1)}>
              {loading ? t("common.loading") : t("globe.loadMore")}
            </Button>
          ) : null}
        </div>
      </div>
    </div>,
    document.body,
  );
}
