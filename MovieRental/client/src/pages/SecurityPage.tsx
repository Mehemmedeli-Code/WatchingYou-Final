import { useCallback, useEffect, useState } from "react";
import { ShieldCheck } from "lucide-react";
import { Section, Panel, Notice, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Textarea, Field } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { VideoPlayer } from "@/components/VideoPlayer";
import { useAuth } from "@/components/useAuth";
import { get, post, ApiError } from "@/lib/api";
import { t } from "@/lib/i18n";
import {
  SECURITY_CHECKS, statusTone, megabytes,
  type ShortFilmDetail, type SecurityCheckName, type CheckOutcome,
} from "@/lib/shorts";

type Draft = Record<SecurityCheckName, { outcome: CheckOutcome; note: string }>;

const blankDraft = (): Draft =>
  Object.fromEntries(SECURITY_CHECKS.map((c) => [c, { outcome: "Pass" as CheckOutcome, note: "" }])) as Draft;

export default function SecurityPage() {
  const { isSecurity, isSignedIn } = useAuth();
  const [queue, setQueue] = useState<ShortFilmDetail[] | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
  const [draft, setDraft] = useState<Draft>(blankDraft);
  const [watched, setWatched] = useState(false);
  const [summary, setSummary] = useState("");
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    if (!isSecurity) return;
    setQueue(await get<ShortFilmDetail[]>("/api/security/shorts/queue").catch(() => []));
  }, [isSecurity]);

  useEffect(() => { void load(); }, [load]);

  function open(id: string) {
    setOpenId(id === openId ? null : id);
    setDraft(blankDraft());
    setWatched(false);
    setSummary("");
    setMessage(null);
  }

  async function claim(id: string) {
    await post(`/api/security/shorts/${id}/claim`).catch(() => null);
    await load();
  }

  async function file(id: string) {
    setBusy(true);
    setMessage(null);
    try {
      await post(`/api/security/shorts/${id}/report`, {
        watchedInFull: watched,
        summary,
        checks: SECURITY_CHECKS.map((check) => ({
          check,
          outcome: draft[check].outcome,
          note: draft[check].note || null,
        })),
      });
      setMessage({ tone: "ok", text: "Report filed. It is with the admin now." });
      setOpenId(null);
      await load();
    } catch (err) {
      setMessage({ tone: "error", text: err instanceof ApiError ? err.message : "The report was not accepted." });
    } finally {
      setBusy(false);
    }
  }

  if (!isSignedIn || !isSecurity) {
    return (
      <Section title={t("security.title")} lede={t("security.lede")}>
        <Empty
          title={t("common.signInFirst")}
          hint="security@reelandrow.test / Security1234"
          action={<a href="/account"><Button>{t("nav.signIn")}</Button></a>}
        />
      </Section>
    );
  }

  return (
    <Section title={t("security.title")} lede={t("security.lede")}>
      {queue === null ? <Spinner label={t("common.loading")} /> : null}
      {message ? <div className="mb-4"><Notice tone={message.tone}>{message.text}</Notice></div> : null}
      {queue?.length === 0 ? <Empty title={t("security.empty")} hint="" /> : null}

      <div className="space-y-4">
        {queue?.map(({ film }) => (
          <Panel key={film.id}>
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div className="max-w-[60ch]">
                <p className="font-display text-lg text-ink">{film.title}</p>
                <p className="mt-1 text-xs text-ink-mute">
                  {film.authorName} · {megabytes(film.sizeBytes)} · {film.hoursLeft}h
                </p>
                {film.synopsis ? <p className="mt-2 text-sm text-ink-mute">{film.synopsis}</p> : null}

                <div className="mt-2 flex flex-wrap gap-1.5">
                  <Badge tone={statusTone(film.status)}>{t(`status.${lower(film.status)}`, film.status)}</Badge>
                  <Badge>{film.origin === "AiGenerated" ? t("studio.originAi") : t("studio.originHuman")}</Badge>
                </div>
              </div>

              <div className="flex gap-2">
                {film.status === "Pending" ? (
                  <Button size="sm" variant="outline" onClick={() => claim(film.id)}>{t("security.claim")}</Button>
                ) : null}
                <Button size="sm" onClick={() => open(film.id)}>
                  <ShieldCheck size={14} aria-hidden />
                  {openId === film.id ? t("common.cancel") : t("security.file")}
                </Button>
              </div>
            </div>

            {openId === film.id ? (
              <div className="mt-5 border-t border-line pt-5">
                <VideoPlayer src={film.streamUrl} title={film.title} />

                <label className="mt-4 flex items-center gap-2 text-sm text-ink">
                  <input type="checkbox" checked={watched} onChange={(e) => setWatched(e.target.checked)} />
                  {t("security.confirmWatched")}
                </label>

                <div className="mt-4 space-y-2">
                  {SECURITY_CHECKS.map((check) => (
                    <div key={check} className="rounded-lg border border-line p-3">
                      <div className="flex flex-wrap items-center justify-between gap-3">
                        <p className="text-sm text-ink">{t(`check.${check}`, check)}</p>
                        <div className="flex gap-1">
                          {(["Pass", "Fail", "NotApplicable"] as CheckOutcome[]).map((outcome) => (
                            <Button
                              key={outcome}
                              size="sm"
                              variant={draft[check].outcome === outcome ? "solid" : "outline"}
                              onClick={() => setDraft({ ...draft, [check]: { ...draft[check], outcome } })}
                            >
                              {outcome === "Pass" ? t("security.pass")
                                : outcome === "Fail" ? t("security.fail") : t("security.na")}
                            </Button>
                          ))}
                        </div>
                      </div>

                      {draft[check].outcome === "Fail" ? (
                        <Textarea
                          className="mt-2"
                          value={draft[check].note}
                          placeholder={t("security.summary")}
                          onChange={(e) => setDraft({ ...draft, [check]: { ...draft[check], note: e.target.value } })}
                        />
                      ) : null}
                    </div>
                  ))}
                </div>

                <div className="mt-4">
                  <Field label={t("security.summary")}>
                    <Textarea value={summary} onChange={(e) => setSummary(e.target.value)} />
                  </Field>
                </div>

                <Button className="mt-4" disabled={!watched || busy} onClick={() => file(film.id)}>
                  {busy ? t("common.loading") : t("security.file")}
                </Button>
              </div>
            ) : null}
          </Panel>
        ))}
      </div>
    </Section>
  );
}

const lower = (value: string) => value.charAt(0).toLowerCase() + value.slice(1);
