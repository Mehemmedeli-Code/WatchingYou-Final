import { useState } from "react";
import { Mail, Send, ShieldCheck } from "lucide-react";
import { Panel } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { post } from "@/lib/api";
import { t, formatWhen } from "@/lib/i18n";

export interface HelpReplyItem {
  id: string;
  authorName: string;
  authorRole: string;
  body: string;
  createdAtUtc: string;
}

export interface HelpThreadItem {
  id: string;
  userEmail: string;
  userName: string;
  subject: string;
  body: string;
  status: "Open" | "Answered" | "Closed";
  createdAtUtc: string;
  replies: HelpReplyItem[];
}

const tone = (status: HelpThreadItem["status"]) =>
  status === "Open" ? "warn" : status === "Answered" ? "good" : undefined;

/**
 * One conversation, from either side.
 *
 * The desk sees the sender's name and e-mail and nothing else about the account — no roles,
 * no phone, and no password, which is not stored in readable form anywhere and would not be
 * shown here if it were. A support screen that displayed credentials would cost exactly the
 * trust a help desk runs on.
 */
export function HelpThreadCard({
  thread,
  onChanged,
  desk = false,
}: {
  thread: HelpThreadItem;
  onChanged: () => void;
  desk?: boolean;
}) {
  const [reply, setReply] = useState("");
  const [busy, setBusy] = useState(false);

  async function send() {
    if (!reply.trim()) return;
    setBusy(true);
    await post(`/api/help/${thread.id}/replies`, { body: reply }).catch(() => null);
    setReply("");
    setBusy(false);
    onChanged();
  }

  return (
    <Panel>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="font-display text-lg text-ink">{thread.subject}</p>
          <p className="mt-1 flex items-center gap-1.5 text-xs text-ink-mute">
            <Mail size={12} aria-hidden />
            {/* The address it came from, which is the only identifying detail the desk needs. */}
            {thread.userEmail}
            <span className="opacity-60">· {formatWhen(thread.createdAtUtc)}</span>
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Badge tone={tone(thread.status)}>{t(`help.status.${thread.status}`, thread.status)}</Badge>
          {desk && thread.status !== "Closed" ? (
            <Button
              size="sm"
              variant="outline"
              onClick={async () => {
                await post(`/api/help/inbox/${thread.id}/close`).catch(() => null);
                onChanged();
              }}
            >
              {t("help.close")}
            </Button>
          ) : null}
        </div>
      </div>

      <p className="mt-3 whitespace-pre-wrap text-sm text-ink">{thread.body}</p>

      {thread.replies.length > 0 ? (
        <div className="mt-4 space-y-3 border-t border-line pt-3">
          {thread.replies.map((item) => {
            const fromDesk = item.authorRole !== "Customer";
            return (
              <div key={item.id} className={fromDesk ? "rounded-lg bg-surface p-3" : "pl-3"}>
                <p className="flex items-center gap-1.5 text-xs text-ink-mute">
                  {fromDesk ? <ShieldCheck size={12} aria-hidden /> : null}
                  {item.authorName}
                  <Badge tone={fromDesk ? "good" : undefined}>{item.authorRole}</Badge>
                  <span className="opacity-60">{formatWhen(item.createdAtUtc)}</span>
                </p>
                <p className="mt-1 whitespace-pre-wrap text-sm text-ink">{item.body}</p>
              </div>
            );
          })}
        </div>
      ) : null}

      {thread.status !== "Closed" ? (
        <div className="mt-4 flex flex-col gap-2 sm:flex-row">
          <Textarea
            value={reply}
            onChange={(e) => setReply(e.target.value)}
            placeholder={t("help.reply")}
            className="flex-1"
          />
          <Button size="sm" disabled={busy || !reply.trim()} onClick={send}>
            <Send size={14} aria-hidden />
            {t("help.send")}
          </Button>
        </div>
      ) : null}
    </Panel>
  );
}
