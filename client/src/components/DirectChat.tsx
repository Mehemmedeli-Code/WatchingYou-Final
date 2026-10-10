import { useCallback, useEffect, useRef, useState } from "react";
import { Modal } from "@/components/ui/modal";
import { Ban, Flag, X } from "lucide-react";
import { Panel, Notice } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { ChatMessages, type ChatMessage } from "@/components/ui/chat-messages";
import { get, post, put, ApiError } from "@/lib/api";
import { t } from "@/lib/i18n";
import { openDirectChats, useRealtime, useRealtimeLive, useTypingIndicator, useTypingSender } from "@/lib/realtime";
import { askText } from "@/lib/dialog";

interface DirectLine {
  id: string;
  mine: boolean;
  senderName: string;
  body: string;
  createdAtUtc: string;
}

export interface DirectConversation {
  otherUserId: string;
  otherName: string;
  otherAvatarUrl?: string | null;
  otherCity?: string | null;
  blockedByMe: boolean;
  /** Blocked either way, or the other person left the globe. The reason is not distinguished. */
  unreachable: boolean;
  messages: DirectLine[];
  /** More messages exist above the newest page. */
  hasOlder?: boolean;
}

/**
 * A conversation with another member.
 *
 * Block and report sit in the header rather than behind a menu: the moment somebody needs
 * them is not the moment to make them go looking. Reporting also blocks, because nobody
 * should keep receiving from a person they have just reported while the desk catches up.
 */
export function DirectChat({
  userId,
  onClose,
  onChanged,
}: {
  userId: string;
  onClose: () => void;
  onChanged?: () => void;
}) {
  const [thread, setThread] = useState<DirectConversation | null>(null);
  // Pages above the newest one, loaded on request. The poll only ever brings the newest page.
  const [older, setOlder] = useState<DirectLine[]>([]);
  const [olderMore, setOlderMore] = useState<boolean | null>(null);
  const [loadingOlder, setLoadingOlder] = useState(false);

  async function loadOlder() {
    const first = older[0] ?? thread?.messages[0];
    if (!first) return;
    setLoadingOlder(true);
    const page = await get<DirectConversation>(`/api/messages/${userId}?before=${encodeURIComponent(first.createdAtUtc)}`).catch(() => null);
    setLoadingOlder(false);
    if (!page) return;
    setOlder((list) => [...page.messages, ...list]);
    setOlderMore(page.hasOlder ?? false);
  }
  const [pending, setPending] = useState(false);
  const [message, setMessage] = useState<{ tone: "ok" | "error"; text: string } | null>(null);
  const sending = useRef(false);

  const load = useCallback(async () => {
    if (sending.current) return;
    const latest = await get<DirectConversation>(`/api/messages/${userId}`).catch(() => undefined);
    if (latest !== undefined) setThread(latest);
  }, [userId]);

  useEffect(() => {
    openDirectChats.add(userId);
    return () => { openDirectChats.delete(userId); };
  }, [userId]);

  const live = useRealtimeLive();
  const [typing, showTyping] = useTypingIndicator();
  const sendTyping = useTypingSender("Typing", userId);

  // A new message from this person reloads the thread at once; the poll stays as a safety
  // net, just much slower while the socket is up.
  useRealtime<{ fromUserId: string }>("dm", (e) => { if (e.fromUserId === userId) void load(); });
  useRealtime<{ fromUserId: string; fromName: string }>("typing", (e) => {
    if (e.fromUserId === userId) showTyping(e.fromName || thread?.otherName || "");
  });

  useEffect(() => {
    void load();
    const timer = setInterval(() => void load(), live ? 30000 : 5000);
    return () => clearInterval(timer);
  }, [load, live]);

  async function send(text: string) {
    sending.current = true;
    setPending(true);
    setMessage(null);
    try {
      setThread(await post<DirectConversation>(`/api/messages/${userId}`, { body: text }));
      onChanged?.();
    } catch (err) {
      setMessage({
        tone: "error",
        text: err instanceof ApiError && err.status === 409 ? t("dm.blocked") : t("error.action"),
      });
    } finally {
      sending.current = false;
      setPending(false);
    }
  }

  async function toggleBlock() {
    if (!thread) return;
    await put(`/api/messages/${userId}/block`, { blocked: !thread.blockedByMe }).catch(() => null);
    await load();
    onChanged?.();
  }

  async function report() {
    if (!thread) return;
    const last = [...thread.messages].reverse().find((m) => !m.mine);
    const reason = await askText(t("dm.reportReason"));
    if (reason === null) return;

    await post(`/api/messages/${userId}/report`, { quote: last?.body ?? "(no message)", reason })
      .catch(() => null);
    setMessage({ tone: "ok", text: t("dm.reported") });
    await load();
    onChanged?.();
  }

  const newest = thread?.messages ?? [];
  const shown = [...older.filter((o) => !newest.some((n) => n.id === o.id)), ...newest];
  const canLoadOlder = olderMore ?? thread?.hasOlder ?? false;
  const bubbles: ChatMessage[] = shown.map((line) => ({
    id: line.id,
    sender: line.mine ? "user" : "desk",
    content: line.body,
    authorName: line.senderName,
    at: line.createdAtUtc,
  }));

  return (
    <Modal onClose={onClose} label={thread?.otherName} width="max-w-xl" layer={94}>
        <Panel>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="flex items-center gap-3">
              {thread?.otherAvatarUrl ? (
                <img src={thread.otherAvatarUrl} alt="" className="h-10 w-10 rounded-full object-cover" />
              ) : (
                <span className="flex h-10 w-10 items-center justify-center rounded-full bg-surface text-ink-mute">
                  {(thread?.otherName ?? "?").slice(0, 1).toUpperCase()}
                </span>
              )}
              <div>
                <p className="font-display text-lg text-ink">{thread?.otherName ?? "…"}</p>
                {thread?.otherCity ? <p className="text-xs text-ink-mute">{thread.otherCity}</p> : null}
              </div>
            </div>

            <div className="flex items-center gap-2">
              <Button size="sm" variant="outline" onClick={toggleBlock}>
                <Ban size={14} aria-hidden />
                {thread?.blockedByMe ? t("dm.unblock") : t("dm.block")}
              </Button>
              <Button size="sm" variant="outline" onClick={report}>
                <Flag size={14} aria-hidden />
                {t("dm.report")}
              </Button>
              <button onClick={onClose} aria-label={t("common.cancel")} className="text-ink-mute hover:text-ink">
                <X size={18} aria-hidden />
              </button>
            </div>
          </div>

          {message ? <div className="mt-3"><Notice tone={message.tone}>{message.text}</Notice></div> : null}

          {thread?.blockedByMe ? (
            <div className="mt-3"><Notice tone="info">{t("dm.blockedByMe")}</Notice></div>
          ) : thread?.unreachable ? (
            <div className="mt-3"><Notice tone="info">{t("dm.blocked")}</Notice></div>
          ) : null}

          {canLoadOlder ? (
            <div className="mt-3 text-center">
              <Button size="sm" variant="outline" disabled={loadingOlder} onClick={loadOlder}>
                {t("dm.older", "Earlier messages")}
              </Button>
            </div>
          ) : null}

          <div className="mt-4">
            <ChatMessages
              className="h-[420px] w-full"
              messages={bubbles}
              pending={pending}
              onSend={send}
              onRefresh={load}
              title={thread?.otherName ?? ""}
              subtitle={t("dm.safety")}
              placeholder={t("dm.placeholder")}
              live={live}
              otherTyping={typing}
              onTyping={thread?.unreachable || thread?.blockedByMe ? undefined : sendTyping}
            />
          </div>
        </Panel>
    </Modal>
  );
}
