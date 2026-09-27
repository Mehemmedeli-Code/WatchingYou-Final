import { useCallback, useEffect, useState } from "react";
import { LifeBuoy, ShieldCheck } from "lucide-react";
import { Section, Panel, Notice, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Input, Textarea, Field } from "@/components/ui/input";
import { useAuth } from "@/components/useAuth";
import { get, post, ApiError } from "@/lib/api";
import { t } from "@/lib/i18n";
import { HelpThreadCard, type HelpThreadItem } from "@/components/HelpThread";

export default function HelpPage() {
  const { isSignedIn, user } = useAuth();
  const [threads, setThreads] = useState<HelpThreadItem[] | null>(null);
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);

  const load = useCallback(async () => {
    if (!isSignedIn) { setThreads([]); return; }
    setThreads(await get<HelpThreadItem[]>("/api/help/mine").catch(() => []));
  }, [isSignedIn]);

  useEffect(() => { void load(); }, [load]);

  async function send() {
    setBusy(true);
    setMessage(null);
    try {
      await post("/api/help", { subject, body });
      setSubject("");
      setBody("");
      setMessage({ tone: "ok", text: t("help.sent") });
      await load();
    } catch (err) {
      setMessage({ tone: "error", text: err instanceof ApiError ? err.message : t("error.action") });
    } finally {
      setBusy(false);
    }
  }

  if (!isSignedIn) {
    return (
      <Section title={t("help.title")} lede={t("help.lede")}>
        <Empty
          title={t("common.signInFirst")}
          hint=""
          action={<a href="/account"><Button>{t("nav.signIn")}</Button></a>}
        />
      </Section>
    );
  }

  return (
    <Section title={t("help.title")} lede={t("help.lede")}>
      <div className="grid gap-6 lg:grid-cols-[1fr_1fr]">
        <Panel>
          <p className="flex items-center gap-2 font-display text-xl text-ink">
            <LifeBuoy size={18} aria-hidden />
            {t("help.title")}
          </p>

          {/* Shown, not asked for: the address comes from the session, so nobody can put
              somebody else's in the box. */}
          <p className="mt-2 text-xs text-ink-mute">{user?.email}</p>

          <div className="mt-4 space-y-3">
            <Field label={t("help.subject")}>
              <Input value={subject} onChange={(e) => setSubject(e.target.value)} maxLength={160} />
            </Field>
            <Field label={t("help.body")}>
              <Textarea value={body} onChange={(e) => setBody(e.target.value)} rows={6} maxLength={4000} />
            </Field>

            {message ? <Notice tone={message.tone}>{message.text}</Notice> : null}

            <Notice tone="info">
              <span className="flex items-center gap-1.5">
                <ShieldCheck size={13} aria-hidden />
                {t("help.noPassword")}
              </span>
            </Notice>

            <Button disabled={busy || !subject.trim() || !body.trim()} onClick={send}>
              {busy ? t("common.loading") : t("help.send")}
            </Button>
          </div>
        </Panel>

        <div>
          <p className="mb-3 font-display text-lg text-ink">{t("help.mine")}</p>
          {threads === null ? <Spinner label={t("common.loading")} /> : null}
          {threads?.length === 0 ? <Empty title={t("help.empty")} hint="" /> : null}

          <div className="space-y-3">
            {threads?.map((thread) => (
              <HelpThreadCard key={thread.id} thread={thread} onChanged={load} />
            ))}
          </div>
        </div>
      </div>
    </Section>
  );
}
