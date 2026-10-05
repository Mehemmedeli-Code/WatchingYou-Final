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
import { useRealtime, useRealtimeLive, useTypingIndicator, useTypingSender } from "@/lib/realtime";

/**
 * The customer's side: one window they keep coming back to, the way a bank's chat works.
 *
 * Replies arrive over the SignalR connection the moment an agent sends them. Polling stays
 * underneath as a safety net — every four seconds while the socket is down, every thirty
 * while it is up — so a dropped connection costs a few seconds, never a message.
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

    const latest = await get<ChatThread | null>("/api/help/chat").catch(() => undefined);

    // undefined means the request failed; null means there is genuinely no conversation yet.
    // Only the second should clear what is on screen — a dropped poll must not wipe a
    // conversation the customer is reading.
    if (latest !== undefined) setThread(latest);
  }, []);

  const live = useRealtimeLive();
  const [typing, showTyping] = useTypingIndicator();
  const sendTyping = useTypingSender("HelpTyping", null);

  useRealtime("help", () => void load());
  useRealtime<{ fromDesk: boolean; name: string }>("helpTyping", (e) => {
    if (e.fromDesk) showTyping(e.name || t("help.desk"));
  });

  useEffect(() => {
    if (!isSignedIn) return;
    void load();
    const timer = setInterval(() => void load(), live ? 30000 : 4000);

    // Coming back to the tab should show the answer immediately rather than after the next tick.
    const onFocus = () => void load();
    window.addEventListener("focus", onFocus);

    return () => {
      clearInterval(timer);
      window.removeEventListener("focus", onFocus);
    };
  }, [isSignedIn, load, live]);

  async function send(text: string) {
    sending.current = true;
    setPending(true);
    setError(null);
    try {
      setThread(await post<ChatThread>("/api/help/chat", { body: text }));
    } catch (err) {
      // A 409 here means two writes crossed, not that the customer did anything wrong. Retry
      // once rather than telling them to reload — they should never have to.
      if (err instanceof ApiError && err.status === 409) {
        try {
          setThread(await post<ChatThread>("/api/help/chat", { body: text }));
          return;
        } catch {
          /* fall through to the message below */
        }
      }
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
          live={live}
          otherTyping={typing}
          onTyping={sendTyping}
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
