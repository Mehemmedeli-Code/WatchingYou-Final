import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { AnimatePresence, motion } from "motion/react";
import { ArrowLeft, AtSign, Camera, Check, Lock, Trash2, UserRound, Users, X } from "lucide-react";
import { Empty, Notice } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Textarea } from "@/components/ui/input";
import { useAuth } from "@/components/useAuth";
import { auth, get, post, put, del, postForm, ApiError, type UserProfile } from "@/lib/api";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import { FollowList, PersonRow, flip, toggleFollow, type PersonCard, type PeoplePage } from "@/components/FollowList";

const BIO_MAX = 150;

/** Edit profile: picture, @username, name and bio; the private switch; follow requests. */
export default function ProfileEditPage() {
  const { user, isSignedIn } = useAuth();
  const [fullName, setFullName] = useState(user?.fullName ?? "");
  const [username, setUsername] = useState(user?.username ?? "");
  const [bio, setBio] = useState(user?.bio ?? "");
  const [counts, setCounts] = useState<{ followers: number; following: number; requests: number } | null>(null);
  // Follow requests waiting for an answer; an accepted one stays in the list as its card, so
  // its "Follow back" button is right there.
  const [requests, setRequests] = useState<{ person: PersonCard; accepted: boolean }[]>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const [openList, setOpenList] = useState<"followers" | "following" | null>(null);
  // A picture that has been chosen but not saved: shown in the confirm sheet until the
  // person says yes (upload) or no (nothing changes).
  const [pending, setPending] = useState<{ file: File; url: string } | null>(null);

  useEffect(() => {
    if (!pending) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") discard(); };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [pending]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    setFullName(user?.fullName ?? "");
    setUsername(user?.username ?? "");
    setBio(user?.bio ?? "");
  }, [user?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const loadCounts = () => void get<{ followers: number; following: number; requests: number }>("/api/people/me").then(setCounts).catch(() => null);
  useEffect(() => {
    if (!isSignedIn) return;
    loadCounts();
    void get<PeoplePage>("/api/people/requests").then((r) => setRequests(r.items.map((person) => ({ person, accepted: false })))).catch(() => null);
  }, [isSignedIn]); // eslint-disable-line react-hooks/exhaustive-deps

  if (!isSignedIn || !user) {
    return (
      <section className="mx-auto max-w-3xl px-4 py-16">
        <Empty title={t("profile.signedOut", "Sign in to edit your profile.")} hint=""
          action={<a href="/account?returnUrl=%2Fprofile%2Fedit"><Button>{t("nav.signIn")}</Button></a>} />
      </section>
    );
  }

  // The header picture is a plain <img>: point it at the new file so it changes at once.
  function showInHeader(profile: UserProfile) {
    auth.setUser(profile);
    const header = document.querySelector<HTMLImageElement>(".rr-avatar img");
    if (header) header.src = profile.avatarUrl || "/icons/icon.svg";
  }

  async function run(work: () => Promise<UserProfile>, done: string) {
    setBusy(true);
    setMessage(null);
    try {
      showInHeader(await work());
      setMessage({ tone: "ok", text: done });
    } catch (e) {
      setMessage({ tone: "error", text: e instanceof ApiError ? e.message : t("error.generic", "Something went wrong.") });
    } finally {
      setBusy(false);
    }
  }

  // Choosing a file only previews it; nothing is uploaded until the sheet is confirmed.
  function choose(file: File | undefined) {
    if (!file) return;
    if (file.size > 2 * 1024 * 1024) {
      setMessage({ tone: "error", text: t("profile.pictureTooBig", "The picture must be 2 MB or smaller.") });
      return;
    }
    setPending({ file, url: URL.createObjectURL(file) });
  }

  function discard() {
    setPending((current) => { if (current) URL.revokeObjectURL(current.url); return null; });
  }

  function savePicture() {
    if (!pending) return;
    const form = new FormData();
    form.append("file", pending.file);
    discard();
    void run(() => postForm<UserProfile>("/api/auth/avatar", form), t("profile.pictureSaved", "Picture updated."));
  }

  async function answer(person: PersonCard, accept: boolean) {
    try {
      if (accept) {
        const card = await post<PersonCard>(`/api/people/requests/${person.id}/accept`);
        setRequests((list) => list.map((r) => r.person.id === person.id ? { person: card, accepted: true } : r));
      } else {
        await del(`/api/people/requests/${person.id}`);
        setRequests((list) => list.filter((r) => r.person.id !== person.id));
      }
      loadCounts();
    } catch (e) {
      setMessage({ tone: "error", text: e instanceof ApiError ? e.message : t("error.generic", "Something went wrong.") });
    }
  }

  // Saved on its own, from what is stored, so half-typed name changes are not sent with it.
  function setPrivate(isPrivate: boolean) {
    void run(
      () => put<UserProfile>("/api/auth/profile", { fullName: user!.fullName, phoneNumber: user!.phoneNumber ?? null, username: user!.username ?? null, isPrivate }),
      isPrivate ? t("profile.nowPrivate", "Your account is private.") : t("profile.nowPublic", "Your account is public."),
    ).then(loadCounts);
  }

  const handle = username.trim().replace(/^@/, "").toLowerCase();
  const handleOk = /^[a-z0-9._]{3,30}$/.test(handle);
  const waiting = requests.filter((r) => !r.accepted).length;

  return (
    <section className="mx-auto w-full max-w-3xl px-4 pb-16 pt-8">
      <a href="/profile" className="inline-flex items-center gap-1.5 text-sm text-ink-mute hover:text-accent">
        <ArrowLeft size={15} aria-hidden /> {t("profile.back", "Back to profile")}
      </a>
      <h1 className="mt-3 font-display text-3xl text-ink">{t("profile.edit", "Edit profile")}</h1>

      {/* Picture strip, as on Instagram: who you are on the left, the change button on the right. */}
      <div className="mt-6 flex flex-wrap items-center justify-between gap-4 rounded-2xl border border-line bg-gradient-to-r from-accent/10 via-surface-raised to-surface-raised p-5">
        <div className="flex min-w-0 items-center gap-4">
          <div className="shrink-0 rounded-full bg-gradient-to-tr from-accent-dim via-accent to-accent-bright p-[3px]">
            <img src={user.avatarUrl || "/icons/icon.svg"} alt="" className="h-16 w-16 rounded-full border-[3px] border-surface bg-surface object-cover" />
          </div>
          <div className="min-w-0">
            <p className="truncate font-semibold text-ink">{user.username ? `@${user.username}` : user.fullName}</p>
            <p className="truncate text-sm text-ink-mute">{user.fullName}</p>
            {counts ? (
              <p className="mt-1 flex gap-3 text-xs text-ink-mute">
                <button className="hover:text-accent" onClick={() => setOpenList("followers")}><strong className="text-ink">{counts.followers}</strong> {t("people.followers", "followers")}</button>
                <button className="hover:text-accent" onClick={() => setOpenList("following")}><strong className="text-ink">{counts.following}</strong> {t("profile.following", "following")}</button>
              </p>
            ) : null}
          </div>
        </div>
        <input ref={fileInput} type="file" accept="image/jpeg,image/png,image/webp" className="hidden"
          onChange={(e) => { choose(e.target.files?.[0]); e.target.value = ""; }} />
        <div className="flex flex-wrap gap-2">
          <Button size="sm" disabled={busy} onClick={() => fileInput.current?.click()}>
            <Camera size={14} aria-hidden /> {t("profile.changePicture", "Change picture")}
          </Button>
          {user.avatarUrl ? (
            <Button size="sm" variant="outline" disabled={busy}
              onClick={() => void run(() => del<UserProfile>("/api/auth/avatar"), t("profile.pictureRemoved", "Picture removed."))}>
              <Trash2 size={14} aria-hidden /> {t("profile.removePicture", "Remove")}
            </Button>
          ) : null}
        </div>
      </div>
      <p className="mt-2 px-1 text-xs text-ink-mute">{t("profile.pictureHint", "JPEG, PNG or WebP, up to 2 MB.")}</p>

      <form
        className="mt-6 space-y-5 rounded-2xl border border-line bg-surface-raised p-6"
        onSubmit={(e) => {
          e.preventDefault();
          void run(
            () => put<UserProfile>("/api/auth/profile", { fullName: fullName.trim(), phoneNumber: user.phoneNumber ?? null, username: handle, bio: bio.trim() }),
            t("profile.saved", "Profile saved."),
          );
        }}
      >
        <Row icon={<AtSign size={16} />} label={t("profile.username", "Username")}
          hint={handleOk || !handle ? t("profile.usernameHint", "3-30 characters: a-z, 0-9, dot, underscore.") : t("profile.usernameBad", "Only a-z, 0-9, dot and underscore, 3-30 characters.")}
          bad={!!handle && !handleOk}>
          <Input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" maxLength={31} />
        </Row>
        <Row icon={<UserRound size={16} />} label={t("profile.fullName", "Name")}>
          <Input value={fullName} onChange={(e) => setFullName(e.target.value)} autoComplete="name" maxLength={150} />
        </Row>
        <Row icon={<span className="text-sm font-semibold">Aa</span>} label={t("profile.bio", "Bio")}
          hint={`${bio.length} / ${BIO_MAX}`}>
          <Textarea value={bio} onChange={(e) => setBio(e.target.value.slice(0, BIO_MAX))} rows={3} maxLength={BIO_MAX}
            placeholder={t("profile.bioPlaceholder", "A line or two about you and your films")} className="resize-none" />
        </Row>
        {message ? <Notice tone={message.tone}>{message.text}</Notice> : null}
        <div className="flex flex-wrap justify-end gap-2 border-t border-line pt-5">
          <a href={`/people?q=${encodeURIComponent(handle)}`}><Button variant="outline"><Users size={15} aria-hidden /> {t("profile.findPeople", "Find people")}</Button></a>
          <Button type="submit" disabled={busy || !handleOk || !fullName.trim()}><Check size={15} aria-hidden /> {t("profile.save", "Save")}</Button>
        </div>
      </form>

      {/* Private switch. */}
      <div className="mt-6 flex items-center justify-between gap-4 rounded-2xl border border-line bg-surface-raised p-6">
        <div className="flex items-start gap-3">
          <span className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-accent/15 text-accent"><Lock size={16} aria-hidden /></span>
          <span>
            <span className="block font-medium text-ink">{t("profile.private", "Private account")}</span>
            <span className="block text-sm text-ink-mute">{t("profile.privateHint", "New followers need your approval, and only they see your films and lists.")}</span>
          </span>
        </div>
        <button role="switch" aria-checked={!!user.isPrivate} aria-label={t("profile.private", "Private account")} disabled={busy}
          onClick={() => setPrivate(!user.isPrivate)}
          className={cn("relative h-7 w-12 shrink-0 rounded-full transition-colors disabled:opacity-50", user.isPrivate ? "bg-accent" : "bg-line")}>
          <span className={cn("absolute top-1 h-5 w-5 rounded-full bg-white shadow transition-all", user.isPrivate ? "left-6" : "left-1")} />
        </button>
      </div>

      {/* Follow requests. */}
      <div className="mt-6 rounded-2xl border border-line bg-surface-raised p-6">
        <p className="flex items-center gap-2 font-display text-lg text-ink">
          {t("profile.requests", "Follow requests")}
          {waiting ? <span className="rounded-full bg-bad px-2 py-0.5 text-xs font-semibold text-white">{waiting}</span> : null}
        </p>
        {requests.length === 0 ? <p className="mt-2 text-sm text-ink-mute">{t("profile.noRequests", "No requests right now.")}</p> : null}
        <div className="mt-4 space-y-3">
          {requests.map(({ person, accepted }) => accepted ? (
            <PersonRow key={person.id} person={person}
              onToggle={() => void toggleFollow(person, (next) =>
                setRequests((list) => list.map((r) => ({ ...r, person: flip(r.person, person.id, next) }))))} />
          ) : (
            <div key={person.id} className="flex items-center justify-between gap-3">
              <a href={person.username ? `/u/${person.username}` : undefined} className="flex min-w-0 items-center gap-3">
                <img src={person.avatarUrl || "/icons/icon.svg"} alt="" className="h-11 w-11 shrink-0 rounded-full border border-line object-cover" />
                <span className="min-w-0">
                  <span className="block truncate text-sm font-medium text-ink">{person.fullName}</span>
                  <span className="block truncate text-xs text-ink-mute">{person.username ? `@${person.username}` : ""}</span>
                </span>
              </a>
              <span className="flex shrink-0 gap-2">
                <Button size="sm" onClick={() => void answer(person, true)}>{t("profile.accept", "Accept")}</Button>
                <Button size="sm" variant="outline" onClick={() => void answer(person, false)}>{t("profile.decline", "Decline")}</Button>
              </span>
            </div>
          ))}
        </div>
      </div>

      {openList ? <FollowList userId={user.id} kind={openList} onClose={() => setOpenList(null)} onChanged={loadCounts} /> : null}

      {createPortal(
        <AnimatePresence>
          {pending ? (
            <motion.div
              key="backdrop"
              className="fixed inset-0 z-[95] flex items-end justify-center bg-black/70"
              initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
              onClick={discard}
            >
              <motion.div
                role="dialog"
                aria-modal="true"
                aria-label={t("profile.confirmPicture", "Use this picture?")}
                className="w-full max-w-md rounded-t-2xl border border-b-0 border-line bg-surface-raised px-6 pb-8 pt-6 text-center"
                initial={{ y: "100%" }} animate={{ y: 0 }} exit={{ y: "100%" }}
                transition={{ type: "spring", stiffness: 380, damping: 34 }}
                onClick={(e) => e.stopPropagation()}
              >
                <img src={pending.url} alt="" className="mx-auto h-40 w-40 rounded-full border-2 border-accent object-cover" />
                <p className="mt-4 font-display text-lg text-ink">{t("profile.confirmPicture", "Use this picture?")}</p>
                <div className="mt-5 grid grid-cols-2 gap-3">
                  <Button variant="outline" onClick={discard}><X size={15} aria-hidden /> {t("profile.no", "No")}</Button>
                  <Button disabled={busy} onClick={savePicture}><Check size={15} aria-hidden /> {t("profile.save", "Save")}</Button>
                </div>
              </motion.div>
            </motion.div>
          ) : null}
        </AnimatePresence>,
        document.body,
      )}
    </section>
  );
}

/** One form row: an icon chip and label on the left, the field on the right (stacked on phones). */
function Row({ icon, label, hint, bad, children }: { icon: React.ReactNode; label: string; hint?: string; bad?: boolean; children: React.ReactNode }) {
  return (
    <label className="grid gap-2 md:grid-cols-[160px_1fr] md:items-start md:gap-6">
      <span className="flex items-center gap-2 pt-2 text-sm font-medium text-ink">
        <span className="flex h-7 w-7 items-center justify-center rounded-full bg-accent/15 text-accent">{icon}</span>
        {label}
      </span>
      <span className="space-y-1.5">
        {children}
        {hint ? <span className={cn("block text-xs", bad ? "text-bad" : "text-ink-mute")}>{hint}</span> : null}
      </span>
    </label>
  );
}
