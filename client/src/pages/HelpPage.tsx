import { useCallback, useEffect, useRef, useState } from "react";
import { ShieldCheck } from "lucide-react";
import { Section, Notice, Empty } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { ChatMessages, type ChatMessage } from "@/components/ui/chat-messages";
import { useAuth } from "@/components/useAuth";
import { get, post, ApiError } from "@/lib/api";
import { t } from "@/lib/i18n";
import type { ChatThread } from "@/lib/help";
import { toBubbles } from "@/lib/help";

/**
 * The customer's side: one window they keep coming back to, the way a bank's chat works.
 *
 * New replies arrive by polling every five seconds. A socket would be tidier, but polling a
 * single small thread is cheap, survives a dropped connection without reconnect logic, and
 * needs nothing added to the host — and nobody notices five seconds in a conversation with a
 * human on the other end.
 */
export default function HelpPage() {
  const { isSignedIn, user } = useAuth();
  const [thread, setThread] = useState<ChatThread | null>(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const sending = useRef(false);

  const load = useCallback(async () => {
    // Skipped mid-send, or the poll would overwrite the message being added.
    if (sending.current) return;
    setThread(await get<ChatThread | null>("/api/help/chat").catch(() => null));
  }, []);

  useEffect(() => {
    if (!isSignedIn) return;
    void load();
    const timer = setInterval(() => void load(), 5000);
    return () => clearInterval(timer);
  }, [isSignedIn, load]);

  async function send(text: string) {
    sending.current = true;
    setPending(true);
    setError(null);
    try {
      setThread(await post<ChatThread>("/api/help/chat", { body: text }));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("error.action"));
    } finally {
      sending.current = false;
      setPending(false);
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

  const greeting: ChatMessage = {
    id: "greeting",
    sender: "desk",
    content: t("help.greeting"),
    authorName: t("help.desk"),
  };

  const messages = thread ? [greeting, ...toBubbles(thread)] : [greeting];

  return (
    <Section title={t("help.title")} lede={t("help.lede")}>
      {error ? <div className="mb-4"><Notice tone="error">{error}</Notice></div> : null}

      <div className="max-w-2xl">
        <ChatMessages
          className="h-[520px] w-full"
          messages={messages}
          pending={pending}
          onSend={send}
          onRefresh={load}
          title={t("help.title")}
          subtitle={user?.email ?? ""}
          placeholder={t("help.placeholder")}
        />

        {/* Said plainly, because the worry is reasonable: people are used to being asked for
            things a support desk has no business knowing. */}
        <div className="mt-3">
          <Notice tone="info">
            <span className="flex items-center gap-1.5">
              <ShieldCheck size={13} aria-hidden />
              {t("help.noPassword")}
            </span>
          </Notice>
        </div>
      </div>
    </Section>
  );
}
