import { useCallback, useEffect, useRef, useState } from "react";
import { Ban, Inbox, Send, ShieldOff, X } from "lucide-react";
import { Spinner } from "@/components/Shell";
import { Button } from "@/components/ui/button";
import { DirectChat } from "@/components/DirectChat";
import { get, put } from "@/lib/api";
import { formatWhen, t, lang } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { flagUrl, countryName } from "@/lib/worldCities";

interface MessagePerson {
  userId: string;
  name: string;
  avatarUrl?: string | null;
  city?: string | null;
  countryCode?: string | null;
  preview?: string | null;
  lastWasMine: boolean;
  count: number;
  atUtc: string;
  /** Their messages not seen yet (Received only). */
  unread: number;
}

interface MessageOverview {
  sent: MessagePerson[];
  received: MessagePerson[];
  blockedByMe: MessagePerson[];
  blockedMe: MessagePerson[];
}

type Tab = "sent" | "received" | "blockedByMe" | "blockedMe";

/**
 * The message box behind the header's message button: who wrote to you, whom you wrote to,
 * whom you blocked and who blocked you, and the conversation itself one tap away. Updates by
 * itself when a message arrives. Writing to someone from the globe still opens the same chat.
 */
export function MessageBox({ onClose, onChanged }: { onClose: () => void; onChanged?: () => void }) {
  const [data, setData] = useState<MessageOverview | null>(null);
  const [tab, setTab] = useState<Tab>("received");
  const [chatWith, setChatWith] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  // Held in a ref so a caller passing a fresh function each render cannot loop the loader.
  const changed = useRef(onChanged);
  changed.current = onChanged;

  const load = useCallback(async () => {
    setData(await get<MessageOverview>("/api/messages/overview").catch(() => ({
      sent: [], received: [], blockedByMe: [], blockedMe: [],
    })));
    changed.current?.();
  }, []);

  useEffect(() => { void load(); }, [load]);
  useRealtime("dm", () => void load());

  async function unblock(userId: string) {
    setBusy(userId);
    try {
      await put(`/api/messages/${userId}/block`, { otherUserId: userId, blocked: false });
      await load();
    } finally {
      setBusy(null);
    }
  }

  const tabs: { id: Tab; label: string; icon: typeof Send }[] = [
    { id: "received", label: t("gm.received", "Wrote to you"), icon: Inbox },
    { id: "sent", label: t("gm.sent", "You wrote to"), icon: Send },
    { id: "blockedByMe", label: t("gm.blockedByMe", "You blocked"), icon: Ban },
    { id: "blockedMe", label: t("gm.blockedMe", "Blocked you"), icon: ShieldOff },
  ];

  const rows = data ? data[tab] : [];

  return (
    <div className="rounded-2xl border border-line bg-surface-raised p-5 shadow-2xl sm:p-6">
      <div className="flex items-center justify-between gap-3">
        <p className="font-display text-lg text-ink">{t("gm.title", "Messages")}</p>
        <button type="button" onClick={onClose} aria-label={t("common.close", "Close")}
          className="flex h-8 w-8 items-center justify-center rounded-full text-ink-mute transition hover:bg-surface hover:text-ink">
          <X size={18} aria-hidden />
        </button>
      </div>

      <div role="tablist" className="mt-3 grid grid-cols-2 gap-2">
        {tabs.map(({ id, label, icon: Icon }) => {
          const count = data ? data[id].length : 0;
          const active = tab === id;
          return (
            <button
              key={id}
              role="tab"
              aria-selected={active}
              onClick={() => setTab(id)}
              className={`flex items-center justify-center gap-2 rounded-lg border px-3 py-2 text-sm transition-colors ${
                active ? "border-accent bg-accent-dim text-surface" : "border-line text-ink hover:border-accent-dim"
              }`}
            >
              <Icon size={15} aria-hidden />
              <span className="leading-tight">{label}</span>
              <span className={`text-xs ${active ? "" : "text-ink-mute"}`}>{count}</span>
            </button>
          );
        })}
      </div>

      <div className="mt-4 max-h-[60vh] overflow-y-auto">
        {data === null ? (
          <Spinner label={t("common.loading")} />
        ) : rows.length === 0 ? (
          <p className="py-6 text-center text-sm text-ink-mute">{t(`gm.empty.${tab}`, "Nothing here yet.")}</p>
        ) : (
          <ul className="space-y-2">
            {rows.map((person) => {
              const blockTab = tab === "blockedByMe" || tab === "blockedMe";
              return (
                <li key={person.userId} className="flex items-center gap-3 rounded-lg border border-line p-3">
                  <img src={person.avatarUrl || "/icons/avatar.svg"} alt="" className="h-10 w-10 shrink-0 rounded-full object-cover" />

                  <div className="min-w-0 flex-1">
                    <p className="flex items-center gap-2 text-sm text-ink">
                      <span className={`truncate ${person.unread > 0 ? "font-semibold" : "font-medium"}`}>{person.name}</span>
                      {person.countryCode ? (
                        <img
                          src={flagUrl(person.countryCode)}
                          alt=""
                          title={countryName(person.countryCode, lang)}
                          className="h-3.5 w-[18px] shrink-0 rounded-[2px] object-cover"
                        />
                      ) : null}
                      {person.city ? <span className="truncate text-xs text-ink-mute">{person.city}</span> : null}
                    </p>
                    <p className={`mt-0.5 truncate text-xs ${person.unread > 0 ? "text-ink" : "text-ink-mute"}`}>
                      {blockTab
                        ? `${tab === "blockedByMe" ? t("gm.blockedOn", "Blocked") : t("gm.blockedYouOn", "Blocked you")} · ${formatWhen(person.atUtc)}`
                        : `${person.preview ?? ""}`}
                    </p>
                    {!blockTab ? (
                      <p className="mt-0.5 text-[11px] text-ink-mute">
                        {person.count} {t("gm.messages", "messages")} · {formatWhen(person.atUtc)}
                      </p>
                    ) : null}
                  </div>

                  {person.unread > 0 && tab === "received" ? (
                    <span className="min-w-5 rounded-full bg-bad px-1.5 text-center text-[11px] font-bold leading-5 text-white">{person.unread}</span>
                  ) : null}

                  {tab === "blockedByMe" ? (
                    <Button size="sm" variant="outline" disabled={busy === person.userId} onClick={() => unblock(person.userId)}>
                      {t("gm.unblock", "Unblock")}
                    </Button>
                  ) : tab === "blockedMe" ? null : (
                    <Button size="sm" onClick={() => setChatWith(person.userId)}>
                      {t("gm.open", "Open")}
                    </Button>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </div>

      {chatWith ? (
        <DirectChat userId={chatWith} onClose={() => { setChatWith(null); void load(); }} onChanged={() => void load()} />
      ) : null}
    </div>
  );
}
