import { useCallback, useEffect, useRef, useState } from "react";
import { Mail } from "lucide-react";
import { Panel, Empty, Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { ChatMessages } from "@/components/ui/chat-messages";
import { get, post, query } from "@/lib/api";
import { t, formatWhen } from "@/lib/i18n";
import { toBubbles, type ChatThread, type InboxRow } from "@/lib/help";

/**
 * The Security desk's side: who is waiting on the left, the conversation on the right.
 *
 * Each row shows the sender's e-mail and nothing else about their account. That is the only
 * identifying detail an agent needs to answer a question, and it is all this screen is given
 * — no roles, no phone, no bookings, and no password, which is not stored in readable form
 * and would not be shown here if it were.
 */
export function HelpInbox() {
  const [rows, setRows] = useState<InboxRow[] | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
  const [thread, setThread] = useState<ChatThread | null>(null);
  const [includeClosed, setIncludeClosed] = useState(false);
  const [pending, setPending] = useState(false);
  const sending = useRef(false);

  const loadInbox = useCallback(async () => {
    setRows(await get<InboxRow[]>("/api/help/inbox" + query({ includeClosed })).catch(() => []));
  }, [includeClosed]);

  const loadThread = useCallback(async (id: string) => {
    if (sending.current) return;
    setThread(await get<ChatThread>(`/api/help/inbox/${id}`).catch(() => null));
  }, []);

  // The list and the open conversation both refresh on a timer, so an agent sees a new
  // message arrive without touching anything.
  useEffect(() => {
    void loadInbox();
    const timer = setInterval(() => void loadInbox(), 6000);
    return () => clearInterval(timer);
  }, [loadInbox]);

  useEffect(() => {
    if (!openId) return;
    void loadThread(openId);
    const timer = setInterval(() => void loadThread(openId), 5000);
    return () => clearInterval(timer);
  }, [openId, loadThread]);

  async function reply(text: string) {
    if (!openId) return;
    sending.current = true;
    setPending(true);
    try {
      setThread(await post<ChatThread>(`/api/help/inbox/${openId}`, { body: text }));
      await loadInbox();
    } finally {
      sending.current = false;
      setPending(false);
    }
  }

  return (
    <div className="grid gap-4 lg:grid-cols-[300px_1fr]">
      <div>
        <Button
          className="mb-3 w-full"
          size="sm"
          variant="outline"
          aria-pressed={includeClosed}
          onClick={() => setIncludeClosed((v) => !v)}
        >
          {t("help.showClosed")}
        </Button>

        {rows === null ? <Spinner label={t("common.loading")} /> : null}
        {rows?.length === 0 ? <Empty title={t("help.empty")} hint="" /> : null}

        <div className="space-y-2">
          {rows?.map((row) => (
            <button
              key={row.id}
              onClick={() => setOpenId(row.id)}
              aria-current={row.id === openId ? "true" : undefined}
              className={`w-full rounded-lg border p-3 text-left transition-colors ${
                row.id === openId
                  ? "border-accent bg-surface-raised"
                  : "border-line hover:border-accent-dim"
              }`}
            >
              <p className="flex items-center gap-1.5 truncate text-sm text-ink">
                <Mail size={12} aria-hidden />
                {row.userEmail}
              </p>
              <p className="mt-1 truncate text-xs text-ink-mute">{row.preview}</p>
              <p className="mt-1.5 flex flex-wrap items-center gap-1.5">
                <Badge tone={row.status === "Open" ? "warn" : row.status === "Answered" ? "good" : undefined}>
                  {t(`help.status.${row.status}`, row.status)}
                </Badge>
                {row.unread > 0 ? <Badge tone="bad">{row.unread} {t("help.waiting")}</Badge> : null}
                <span className="text-[11px] text-ink-mute">{formatWhen(row.lastMessageAtUtc)}</span>
              </p>
            </button>
          ))}
        </div>
      </div>

      {thread && openId ? (
        <div>
          <ChatMessages
            className="h-[520px] w-full"
            messages={toBubbles(thread, true)}
            pending={pending}
            onSend={reply}
            onRefresh={() => loadThread(openId)}
            title={thread.userName}
            subtitle={thread.userEmail}
            placeholder={t("help.placeholder")}
          />

          {thread.status !== "Closed" ? (
            <Button
              className="mt-3"
              size="sm"
              variant="outline"
              onClick={async () => {
                await post(`/api/help/inbox/${openId}/close`).catch(() => null);
                await loadInbox();
                await loadThread(openId);
              }}
            >
              {t("help.close")}
            </Button>
          ) : null}
        </div>
      ) : (
        <Panel className="flex items-center justify-center text-sm text-ink-mute">
          {t("help.pick")}
        </Panel>
      )}
    </div>
  );
}
