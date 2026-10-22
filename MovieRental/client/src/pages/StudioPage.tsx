import { useCallback, useEffect, useRef, useState } from "react";
import { Eye, EyeOff, MessageSquare, Sparkles, Upload } from "lucide-react";
import { Section, Panel, Notice, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Textarea, Field, Select } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { VideoPlayer } from "@/components/VideoPlayer";
import { useAuth } from "@/components/useAuth";
import { get, post, put, postForm, ApiError } from "@/lib/api";
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
        <div className="grid gap-6 lg:grid-cols-[1.3fr_1fr]">
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
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  async function upload() {
    if (!file || !title) return;
    setBusy(true);
    setError(null);

    const form = new FormData();
    form.append("title", title);
    form.append("synopsis", synopsis);
    form.append("origin", origin);
    form.append("visibility", visibility);
    form.append("file", file);

    try {
      await postForm("/api/shorts", form);
      setTitle(""); setSynopsis(""); setFile(null);
      if (inputRef.current) inputRef.current.value = "";
      onDone("Submitted. Security reviews it first, then an admin decides — within three days.");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "The upload did not finish.");
    } finally {
      setBusy(false);
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

        <Field label={t("studio.visibility")}>
          <Select value={visibility} onChange={(e) => setVisibility(e.target.value as ShortFilmVisibility)}>
            <option value="Private">{t("common.private")}</option>
            <option value="Public">{t("common.public")}</option>
          </Select>
        </Field>

        <Field label={t("field.videoFile")}>
          <input
            ref={inputRef}
            type="file"
            accept="video/mp4,video/quicktime,video/webm,video/x-matroska"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            className="block w-full text-sm text-ink-mute file:mr-3 file:rounded-full file:border-0 file:bg-accent file:px-4 file:py-2 file:text-sm file:font-medium file:text-surface"
          />
        </Field>

        {file ? <p className="text-xs text-ink-mute">{file.name} · {megabytes(file.size)}</p> : null}
        {error ? <Notice tone="error">{error}</Notice> : null}

        <Button disabled={!file || !title || busy} onClick={upload}>
          <Upload size={15} aria-hidden />
          {busy ? t("common.loading") : t("common.send")}
        </Button>
      </div>
    </Panel>
  );
}

function SubmissionCard({ entry, onChanged }: { entry: ShortFilmDetail; onChanged: () => void }) {
  const { film, report, comments } = entry;
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
      <div className="grid gap-6 lg:grid-cols-[1.1fr_1fr]">
        <div>
          <VideoPlayer src={film.streamUrl} title={film.title} />
          <h3 className="mt-4 font-display text-xl text-ink">{film.title}</h3>
          <p className="mt-1 text-xs text-ink-mute">
            {film.originalFileName} · {megabytes(film.sizeBytes)} · {formatWhen(film.submittedAtUtc)}
          </p>

          <div className="mt-3 flex flex-wrap items-center gap-2">
            <Badge tone={statusTone(film.status)}>{t(`status.${lower(film.status)}`, film.status)}</Badge>
            <Badge>{film.origin === "AiGenerated" ? t("studio.originAi") : t("studio.originHuman")}</Badge>
            <Badge tone={isPublic ? "good" : undefined}>{isPublic ? t("common.public") : t("common.private")}</Badge>
            {film.status === "Approved" ? <Badge>{film.viewCount} {t("common.views")}</Badge> : null}
            {film.status !== "Approved" && film.status !== "Rejected" ? (
              <span className="text-xs text-ink-mute">{film.hoursLeft} {t("studio.hoursLeft")}</span>
            ) : null}
          </div>

          <Button className="mt-4" size="sm" variant="outline" disabled={busy} onClick={toggleVisibility}>
            {isPublic ? <EyeOff size={14} aria-hidden /> : <Eye size={14} aria-hidden />}
            {isPublic ? t("studio.makePrivate") : t("studio.makePublic")}
          </Button>
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
                <p className="text-xs text-ink-mute">{c.authorName} · {c.authorRole} · {formatWhen(c.createdAtUtc)}</p>
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
