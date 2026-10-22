import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { AnimatePresence, motion } from "motion/react";
import { ArrowLeft, Check, Flag, Image as ImageIcon, Search, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { auth, del, postForm, put, ApiError, type UserProfile } from "@/lib/api";
import { lang, t } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import { WORLD_CITIES, countryName, flagUrl } from "@/lib/worldCities";

/** What a picture address shows: the eye when there is none, the grey figure after "remove". */
export const BLANK_AVATAR = "/icons/avatar-blank.svg";
export const avatarSrc = (url?: string | null) => url || "/icons/avatar.svg";

const COUNTRIES = [...new Set(WORLD_CITIES.map((c) => c.country))];

/**
 * Change my picture, the way Instagram's "+" does it: one from the gallery, a country's flag,
 * or remove it. Removing leaves the grey figure, which cannot itself be removed.
 */
export function AvatarSheet({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone?: (message: string, ok: boolean) => void }) {
  const [step, setStep] = useState<"menu" | "flags">("menu");
  const [pending, setPending] = useState<{ file: File; url: string } | null>(null);
  const [filter, setFilter] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const user = auth.user;

  const countries = useMemo(() => COUNTRIES
    .map((code) => ({ code, name: countryName(code, lang) }))
    .sort((a, b) => (a.code === "AZ" ? -1 : b.code === "AZ" ? 1 : a.name.localeCompare(b.name, lang))), []);
  const shown = filter.trim() ? countries.filter((c) => c.name.toLowerCase().includes(filter.trim().toLowerCase())) : countries;

  function close() {
    setPending((current) => { if (current) URL.revokeObjectURL(current.url); return null; });
    setStep("menu"); setFilter(""); setError(null);
    onClose();
  }

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") close(); };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  async function run(work: () => Promise<UserProfile>, done: string) {
    setBusy(true); setError(null);
    try {
      const profile = await work();
      auth.setUser(profile);
      // The site's header picture is plain HTML outside React.
      const header = document.querySelector<HTMLImageElement>(".rr-avatar img");
      if (header) header.src = avatarSrc(profile.avatarUrl);
      onDone?.(done, true);
      close();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t("error.generic", "Something went wrong."));
    } finally { setBusy(false); }
  }

  function choose(file: File | undefined) {
    if (!file) return;
    if (file.size > 2 * 1024 * 1024) { setError(t("profile.pictureTooBig", "The picture must be 2 MB or smaller.")); return; }
    setPending({ file, url: URL.createObjectURL(file) });
  }

  function upload() {
    if (!pending) return;
    const form = new FormData();
    form.append("file", pending.file);
    void run(() => postForm<UserProfile>("/api/auth/avatar", form), t("profile.pictureSaved", "Picture updated."));
  }

  const option = "flex w-full items-center gap-3 rounded-xl px-4 py-3.5 text-left text-ink hover:bg-line/40 disabled:opacity-50";

  return createPortal(
    <AnimatePresence>
      {open ? (
        <motion.div key="backdrop" className="fixed inset-0 z-[95] flex items-end justify-center bg-black/70"
          initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} onClick={close}>
          <motion.div role="dialog" aria-modal="true" aria-label={t("profile.changePicture", "Change picture")}
            className="flex max-h-[85vh] w-full max-w-md flex-col rounded-t-2xl border border-b-0 border-line bg-surface-raised px-5 pb-[max(2rem,env(safe-area-inset-bottom))] pt-5"
            initial={{ y: "100%" }} animate={{ y: 0 }} exit={{ y: "100%" }}
            transition={{ type: "spring", stiffness: 380, damping: 34 }} onClick={(e) => e.stopPropagation()}>
            <input ref={fileInput} type="file" accept="image/jpeg,image/png,image/webp" className="hidden"
              onChange={(e) => { choose(e.target.files?.[0]); e.target.value = ""; }} />

            {pending ? (
              <div className="text-center">
                <img src={pending.url} alt="" className="mx-auto h-40 w-40 rounded-full border-2 border-accent object-cover" />
                <p className="mt-4 font-display text-lg text-ink">{t("profile.confirmPicture", "Use this picture?")}</p>
                <div className="mt-5 grid grid-cols-2 gap-3">
                  <Button variant="outline" onClick={() => setPending(null)}><X size={15} aria-hidden /> {t("profile.no", "No")}</Button>
                  <Button disabled={busy} onClick={upload}><Check size={15} aria-hidden /> {t("profile.save", "Save")}</Button>
                </div>
              </div>
            ) : step === "flags" ? (
              <>
                <div className="flex items-center gap-2">
                  <button type="button" className="grid h-9 w-9 place-items-center rounded-full hover:bg-line/40" aria-label={t("profile.flagBack", "Back")} onClick={() => setStep("menu")}>
                    <ArrowLeft size={18} />
                  </button>
                  <p className="font-display text-lg text-ink">{t("profile.flagTitle", "Pick a flag")}</p>
                </div>
                <div className="relative mt-3">
                  <Search size={15} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-mute" aria-hidden />
                  <Input value={filter} onChange={(e) => setFilter(e.target.value)} placeholder={t("profile.flagSearch", "Search a country")} className="pl-9" />
                </div>
                <div className="mt-3 grid min-h-0 grid-cols-4 gap-2 overflow-y-auto pb-1 sm:grid-cols-5">
                  {shown.map((c) => (
                    <button key={c.code} type="button" disabled={busy} title={c.name}
                      onClick={() => void run(() => put<UserProfile>(`/api/auth/avatar/flag/${c.code.toLowerCase()}`), t("profile.pictureSaved", "Picture updated."))}
                      className="flex flex-col items-center gap-1 rounded-xl p-2 hover:bg-line/40">
                      <img src={flagUrl(c.code)} alt="" loading="lazy" className="h-14 w-14 rounded-full border border-line object-cover" />
                      <span className="w-full truncate text-center text-[11px] text-ink-mute">{c.name}</span>
                    </button>
                  ))}
                </div>
              </>
            ) : (
              <>
                <img src={avatarSrc(user?.avatarUrl)} alt="" className="mx-auto h-20 w-20 rounded-full border border-line object-cover" />
                <p className="mb-2 mt-3 text-center font-display text-lg text-ink">{t("profile.changePicture", "Change picture")}</p>
                <button type="button" className={option} disabled={busy} onClick={() => fileInput.current?.click()}>
                  <ImageIcon size={18} className="text-accent" aria-hidden /> {t("profile.fromGallery", "Choose from gallery")}
                </button>
                <button type="button" className={option} disabled={busy} onClick={() => setStep("flags")}>
                  <Flag size={18} className="text-accent" aria-hidden /> {t("profile.flagAvatar", "Country flag")}
                </button>
                {user?.avatarUrl !== BLANK_AVATAR ? (
                  <button type="button" className={cn(option, "text-bad")} disabled={busy}
                    onClick={() => void run(() => del<UserProfile>("/api/auth/avatar"), t("profile.pictureRemoved", "Picture removed."))}>
                    <Trash2 size={18} aria-hidden /> {t("profile.removePicture", "Remove")}
                  </button>
                ) : null}
                <p className="mt-2 px-4 text-xs text-ink-mute">{t("profile.pictureHint", "JPEG, PNG or WebP, up to 2 MB.")}</p>
              </>
            )}
            {error ? <p className="mt-3 text-center text-sm text-bad" role="alert">{error}</p> : null}
          </motion.div>
        </motion.div>
      ) : null}
    </AnimatePresence>,
    document.body,
  );
}
