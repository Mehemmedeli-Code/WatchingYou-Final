import { useCallback, useEffect, useState } from "react";
import { MessageCircle } from "lucide-react";
import { Section, Empty, Spinner } from "@/components/Shell";
import { Badge } from "@/components/ui/badge";
import { DirectChat } from "@/components/DirectChat";
import { get } from "@/lib/api";
import { formatWhen, t } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";

interface DirectThreadRow {
  otherUserId: string;
  otherName: string;
  otherAvatarUrl?: string | null;
  preview: string;
  lastMessageAtUtc: string;
  unread: number;
}

/**
 * Every direct conversation in one list. The server has always answered GET /api/messages;
 * nothing on the site asked it, so a reply could only be found by reopening the sender's
 * card on the globe.
 */
export function MessagesInbox() {
  const [rows, setRows] = useState<DirectThreadRow[] | null>(null);
  const [open, setOpen] = useState<string | null>(null);

  const load = useCallback(async () => {
    setRows(await get<DirectThreadRow[]>("/api/messages").catch(() => []));
  }, []);

  useEffect(() => { void load(); }, [load]);
  useRealtime("dm", () => void load());

  return (
    <Section title={t("inbox.title", "Messages")} lede={t("inbox.lede", "Conversations with other members. New messages arrive live.")} className="scroll-mt-20">
      <div id="messages" />
      {open ? <DirectChat userId={open} onClose={() => { setOpen(null); void load(); }} onChanged={load} /> : null}

      {rows === null ? <Spinner /> : null}

      {rows && rows.length === 0 ? (
        <Empty
          title={t("inbox.emptyTitle", "No conversations yet")}
          hint={t("inbox.emptyHint", "Find people who share your taste on the globe and say hello.")}
          action={<a href="/globe" className="inline-flex h-9 items-center rounded-full border border-line px-4 text-sm text-ink hover:border-accent hover:text-accent">{t("nav.globe", "Globe")}</a>}
        />
      ) : null}

      {rows && rows.length > 0 ? (
        <ul className="divide-y divide-line overflow-hidden rounded-xl border border-line bg-surface-raised">
          {rows.map((row) => (
            <li key={row.otherUserId}>
              <button
                onClick={() => setOpen(row.otherUserId)}
                className="flex w-full items-center gap-3 px-4 py-3 text-left transition hover:bg-surface"
              >
                {row.otherAvatarUrl ? (
                  <img src={row.otherAvatarUrl} alt="" className="h-10 w-10 rounded-full object-cover" />
                ) : (
                  <span className="flex h-10 w-10 items-center justify-center rounded-full bg-surface text-ink-mute">
                    <MessageCircle size={16} aria-hidden />
                  </span>
                )}
                <span className="min-w-0 flex-1">
                  <span className="flex items-center justify-between gap-2">
                    <span className={row.unread > 0 ? "font-semibold text-ink" : "text-ink"}>{row.otherName}</span>
                    <span className="shrink-0 text-xs text-ink-mute">{formatWhen(row.lastMessageAtUtc)}</span>
                  </span>
                  <span className="mt-0.5 block truncate text-sm text-ink-mute">{row.preview}</span>
                </span>
                {row.unread > 0 ? <Badge tone="good">{row.unread}</Badge> : null}
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </Section>
  );
}
