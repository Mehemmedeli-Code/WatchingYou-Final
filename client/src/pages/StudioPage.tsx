import { useCallback, useEffect, useRef, useState } from "react";
import { Eye, EyeOff, Film, MessageSquare, Sparkles, Trash2, Upload, X } from "lucide-react";
import { Section, Panel, Notice, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Textarea, Field, Select } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { VideoPlayer } from "@/components/VideoPlayer";
import { useAuth } from "@/components/useAuth";
import { askConfirm } from "@/lib/dialog";
import { get, post, put, del, uploadForm, ApiError } from "@/lib/api";
import { t, formatWhen } from "@/lib/i18n";
import {
  statusTone, megabytes,
  type ShortFilmDetail, type ShortFilmOrigin, type ShortFilmVisibility,
} from "@/lib/shorts";

const AI_STUDIO_URL = "https://higgsfield.ai/";
const lower = (value: string) => value.charAt(0).toLowerCase() + value.slice(1);

export default function StudioPage() {
  const { isSignedIn } = useAuth();
  const [mine, setMine] = useState<ShortFilmDetail[] | null>(null);
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);

  const load = useCallback(async () => {
    if (!isSignedIn) return;
    setMine(await get<ShortFilmDetail[]>("/api/shorts/mine").catch(() => []));
  }, [isSignedIn]);

  useEffect(() => { void load(); }, [load]);

  return (
    <>
      <Section title={t("studio.title")} lede={t("studio.lede")}>
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1.3fr_1fr]">
          {isSignedIn
            ? <UploadForm onDone={(text) => { setMessage({ tone: "ok", text }); void load(); }} />
            : <Panel><Notice tone="info">{t("common.signInFirst")}</Notice></Panel>}

          <Panel className="flex flex-col justify-between">
            <div>
              <h3 className="font-display text-xl text-ink">{t("studio.originAi")}</h3>
              <p className="mt-1 text-sm text-ink-mute">{t("studio.originAttest")}</p>
            </div>
            <a href={AI_STUDIO_URL} target="_blank" rel="noreferrer noopener" className="mt-5 inline-flex">
              <Button variant="outline"><Sparkles size={15} aria-hidden />{AI_STUDIO_URL.replace("https://", "")}</Button>
            </a>
          </Panel>
        </div>
        {message ? <div className="mt-4"><Notice tone={message.tone}>{message.text}</Notice></div> : null}
      </Section>

      {isSignedIn ? (
        <Section title={t("studio.mine")}>
          {mine === null ? <Spinner label={t("common.loading")} /> : null}
          {mine?.length === 0 ? <Empty title={t("gallery.empty")} hint={t("studio.lede")} /> : null}

          <div className="space-y-5">
            {mine?.map((entry) => (
              <SubmissionCard key={entry.film.id} entry={entry} onChanged={load} />
            ))}
          </div>
        </Section>
      ) : null}
    </>
  );
}

function UploadForm({ onDone }: { onDone: (message: string) => void }) {
  const [title, setTitle] = useState("");
  const [synopsis, setSynopsis] = useState("");
  const [origin, setOrigin] = useState<ShortFilmOrigin>("HandCrafted");
  const [visibility, setVisibility] = useState<ShortFilmVisibility>("Private");
  // Only a private account chooses: on a public one every approved film is in its gallery.
  const privateAccount = !!useAuth().user?.isPrivate;
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<{ loaded: number; total: number; started: number } | null>(null);
  const cancel = useRef<AbortController | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  async function upload() {
    if (!file || !title) return;
    setBusy(true);
    setError(null);
    const started = Date.now();
    setProgress({ loaded: 0, total: file.size, started });
    cancel.current = new AbortController();

    const form = new FormData();
    form.append("title", title);
    form.append("synopsis", synopsis);
    form.append("origin", origin);
    form.append("visibility", visibility);
    form.append("file", file);

    try {
      await uploadForm("/api/shorts", form, (p) => setProgress({ ...p, started }), cancel.current.signal);
      setTitle(""); setSynopsis(""); setFile(null);
      if (inputRef.current) inputRef.current.value = "";
      onDone(t("studio.submitted", "Submitted. Security reviews it first, then an admin decides — within three days."));
    } catch (err) {
      if (!(err instanceof ApiError && err.code === "aborted"))
        setError(err instanceof ApiError ? err.message : t("studio.uploadFailed", "The upload did not finish. Try again."));
    } finally {
      setBusy(false);
      setProgress(null);
      cancel.current = null;
    }
  }

  return (
    <Panel>
      <h3 className="font-display text-xl text-ink">{t("studio.upload")}</h3>
      <div className="mt-5 space-y-4">
        <Field label={t("admin.col.title")}>
          <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder={t("field.titleHint")} />
        </Field>

        <Field label={t("field.synopsis")}>
          <Textarea value={synopsis} onChange={(e) => setSynopsis(e.target.value)} />
        </Field>

        <Field label={t("studio.origin")} hint={t("studio.originAttest")}>
          <Select value={origin} onChange={(e) => setOrigin(e.target.value as ShortFilmOrigin)}>
            <option value="HandCrafted">{t("studio.originHuman")}</option>
            <option value="AiGenerated">{t("studio.originAi")}</option>
          </Select>
        </Field>

        {privateAccount ? (
          <Field label={t("studio.visibility")} hint={t("studio.visibilityHint", "Your account is private: choose whether the approved film is also in its gallery for everyone, or for your followers only.")}>
            <Select value={visibility} onChange={(e) => setVisibility(e.target.value as ShortFilmVisibility)}>
              <option value="Private">{t("profile.followersOnly", "Followers only")}</option>
              <option value="Public">{t("profile.showInGallery", "Show in the gallery")}</option>
            </Select>
          </Field>
        ) : null}

        <Field label={t("field.videoFile")}>
          {/* The browser labels its own file button ("Choose File", "No file chosen") in the
              browser's language, whatever the page is set to; so it stays hidden and this one
              speaks the page's language. */}
          <input
            ref={inputRef}
            type="file"
            accept="video/mp4,video/quicktime,video/webm,video/x-matroska"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            className="sr-only"
            tabIndex={-1}
          />
          <span className="flex flex-wrap items-center gap-3">
            <Button type="button" size="sm" disabled={busy} onClick={() => inputRef.current?.click()}>
              <Film size={14} aria-hidden /> {t("studio.chooseFile", "Choose a video")}
            </Button>
            <span className="min-w-0 truncate text-sm text-ink-mute">{file ? file.name : t("studio.noFile", "No video chosen yet")}</span>
          </span>
        </Field>

        {file ? <p className="text-xs text-ink-mute">{file.name} · {megabytes(file.size)}</p> : null}
        {progress ? <UploadBar {...progress} onCancel={() => cancel.current?.abort()} /> : null}
        {error ? <Notice tone="error">{error}</Notice> : null}

        <Button disabled={!file || !title || busy} onClick={upload}>
          <Upload size={15} aria-hidden />
          {busy ? t("studio.uploading", "Uploading…") : t("common.send")}
        </Button>
      </div>
    </Panel>
  );
}

/** How far the upload is, how fast it goes, and roughly how long is left. */
function UploadBar({ loaded, total, started, onCancel }: { loaded: number; total: number; started: number; onCancel: () => void }) {
  const percent = total ? Math.min(100, Math.round((loaded / total) * 100)) : 0;
  const seconds = (Date.now() - started) / 1000;
  const speed = seconds > 0.5 ? loaded / seconds : 0;          // bytes per second
  const left = speed > 0 ? (total - loaded) / speed : null;    // seconds
  const done = loaded >= total;
  const eta = left === null ? t("studio.estimating", "estimating…")
    : left < 60 ? t("studio.secondsLeft", "{n} s left").replace("{n}", String(Math.max(1, Math.ceil(left))))
    : t("studio.minutesLeft", "about {n} min left").replace("{n}", String(Math.ceil(left / 60)));
  return (
    <div className="space-y-2 rounded-xl border border-line bg-surface p-3" role="status" aria-live="polite">
      <div className="flex items-center justify-between gap-3 text-sm">
        <span className="font-medium text-ink">{done ? t("studio.processing", "Saving on the server…") : `${percent}%`}</span>
        {!done ? (
          <button onClick={onCancel} className="inline-flex items-center gap-1 text-xs text-ink-mute hover:text-bad">
            <X size={13} aria-hidden /> {t("common.cancel", "Cancel")}
          </button>
        ) : null}
      </div>
      <div className="h-2 overflow-hidden rounded-full bg-line" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}>
        <div className={`h-full rounded-full bg-accent transition-[width] duration-300 ${done ? "animate-pulse" : ""}`} style={{ width: `${percent}%` }} />
      </div>
      <p className="flex flex-wrap justify-between gap-2 text-xs text-ink-mute">
        <span>{megabytes(loaded)} / {megabytes(total)}{speed ? ` · ${megabytes(speed)}/s` : ""}</span>
        {!done ? <span>{eta}</span> : null}
      </p>
    </div>
  );
}

function SubmissionCard({ entry, onChanged }: { entry: ShortFilmDetail; onChanged: () => void }) {
  const { film, report, comments } = entry;
  const privateAccount = !!useAuth().user?.isPrivate;
  const [draft, setDraft] = useState("");
  const [busy, setBusy] = useState(false);

  const isPublic = film.visibility === "Public";

  async function toggleVisibility() {
    setBusy(true);
    await put(`/api/shorts/${film.id}/visibility`, { visibility: isPublic ? "Private" : "Public" })
      .catch(() => null);
    setBusy(false);
    onChanged();
  }

  // Takes back a submission that has not been approved: the wrong file, a change of mind.
  async function withdraw() {
    if (!(await askConfirm(t("studio.withdrawConfirm", "Withdraw this film? It is removed from review and deleted."), { danger: true, confirmLabel: t("studio.withdrawYes", "Yes, withdraw it"), cancelLabel: t("profile.no", "No") }))) return;
    setBusy(true);
    await del(`/api/shorts/${film.id}`).catch(() => null);
    setBusy(false);
    onChanged();
  }

  async function comment() {
    if (!draft.trim()) return;
    setBusy(true);
    await post(`/api/shorts/${film.id}/comments`, { body: draft }).catch(() => null);
    setDraft("");
    setBusy(false);
    onChanged();
  }

  return (
    <Panel>
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1.1fr_1fr]">
        <div>
          <VideoPlayer src={film.streamUrl} title={film.title} />
          <h3 className="mt-4 font-display text-xl text-ink">{film.title}</h3>
          <p className="mt-1 text-xs text-ink-mute">
            {film.originalFileName} · {megabytes(film.sizeBytes)} · {formatWhen(film.submittedAtUtc)}
          </p>

          <div className="mt-3 flex flex-wrap items-center gap-2">
            <Badge tone={statusTone(film.status)}>{t(`status.${lower(film.status)}`, film.status)}</Badge>
            <Badge>{film.origin === "AiGenerated" ? t("studio.originAi") : t("studio.originHuman")}</Badge>
            {privateAccount ? <Badge tone={isPublic ? "good" : undefined}>{isPublic ? t("profile.inGallery", "In the gallery") : t("profile.followersOnly", "Followers only")}</Badge> : null}
            {film.status === "Approved" ? <Badge>{film.viewCount} {t("common.views")}</Badge> : null}
            {film.status !== "Approved" && film.status !== "Rejected" ? (
              <span className="text-xs text-ink-mute">{film.hoursLeft} {t("studio.hoursLeft")}</span>
            ) : null}
          </div>

          {film.status !== "Approved" ? (
            <Button className="mr-2 mt-4" size="sm" variant="danger" disabled={busy} onClick={withdraw}>
              <Trash2 size={14} aria-hidden /> {t("studio.withdraw", "Withdraw submission")}
            </Button>
          ) : null}
          {privateAccount ? (
            <Button className="mt-4" size="sm" variant="outline" disabled={busy} onClick={toggleVisibility}>
              {isPublic ? <EyeOff size={14} aria-hidden /> : <Eye size={14} aria-hidden />}
              {isPublic ? t("profile.makeFollowersOnly", "Followers only") : t("profile.showInGallery", "Show in the gallery")}
            </Button>
          ) : null}
        </div>

        <div>
          <h4 className="font-display text-lg text-ink">{t("studio.report")}</h4>
          {report ? (
            <div className="mt-2 space-y-2">
              <p className="text-xs text-ink-mute">
                {report.reviewerName} · {formatWhen(report.completedAtUtc)}
                {report.watchedInFull ? ` · ${t("studio.watchedInFull")}` : null}
              </p>
              {report.summary ? <p className="text-sm text-ink">{report.summary}</p> : null}

              <ul className="space-y-1">
                {report.checks.map((check) => (
                  <li key={check.check} className="flex items-start justify-between gap-3 text-sm">
                    <span className="text-ink-mute">{t(`check.${check.check}`, check.check)}</span>
                    <Badge tone={check.outcome === "Fail" ? "bad" : check.outcome === "Pass" ? "good" : undefined}>
                      {check.outcome === "Pass" ? t("security.pass")
                        : check.outcome === "Fail" ? t("security.fail") : t("security.na")}
                    </Badge>
                  </li>
                ))}
              </ul>
            </div>
          ) : (
            <p className="mt-2 text-sm text-ink-mute">{t("studio.noReport")}</p>
          )}

          {film.reviewerNote ? (
            <div className="mt-4">
              <Notice tone={film.status === "Rejected" ? "error" : "ok"}>{film.reviewerNote}</Notice>
            </div>
          ) : null}

          <h4 className="mt-6 font-display text-lg text-ink">{t("studio.thread")}</h4>
          <div className="mt-2 space-y-2">
            {comments.map((c) => (
              <div key={c.id} className="rounded-lg border border-line p-3">
                <p className="text-xs text-ink-mute">{c.authorName} · {t(`role.${c.authorRole}`, c.authorRole)} · {formatWhen(c.createdAtUtc)}</p>
                <p className="mt-1 text-sm text-ink">{c.body}</p>
              </div>
            ))}
          </div>

          <div className="mt-3 flex gap-2">
            <Input
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              placeholder={t("studio.writeComment")}
              onKeyDown={(e) => { if (e.key === "Enter") void comment(); }}
            />
            <Button size="sm" disabled={busy || !draft.trim()} onClick={comment}>
              <MessageSquare size={14} aria-hidden />
              {t("common.send")}
            </Button>
          </div>
        </div>
      </div>
    </Panel>
  );
}
