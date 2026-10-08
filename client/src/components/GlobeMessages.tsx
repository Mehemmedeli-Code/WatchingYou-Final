import { useCallback, useEffect, useState } from "react";
import { Ban, Inbox, Send, ShieldOff } from "lucide-react";
import { Panel, Spinner } from "@/components/Shell";
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
}

interface MessageOverview {
  sent: MessagePerson[];
  received: MessagePerson[];
  blockedByMe: MessagePerson[];
  blockedMe: MessagePerson[];
}

type Tab = "sent" | "received" | "blockedByMe" | "blockedMe";

/**
 * Under the globe: whom you wrote to, who wrote to you, whom you blocked and who blocked you.
 * Updates by itself when a message arrives.
 */
export function GlobeMessages() {
  const [data, setData] = useState<MessageOverview | null>(null);
  const [tab, setTab] = useState<Tab>("received");
  const [chatWith, setChatWith] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    setData(await get<MessageOverview>("/api/messages/overview").catch(() => ({
      sent: [], received: [], blockedByMe: [], blockedMe: [],
    })));
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
    <Panel className="mt-6">
      <p className="font-display text-lg text-ink">{t("gm.title", "Messages")}</p>

      <div role="tablist" className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-4">
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

      <div className="mt-4">
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
                  {person.avatarUrl ? (
                    <img src={person.avatarUrl} alt="" className="h-10 w-10 shrink-0 rounded-full object-cover" />
                  ) : (
                    <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-surface text-sm text-ink-mute">
                      {person.name.slice(0, 1).toUpperCase()}
                    </span>
                  )}

                  <div className="min-w-0 flex-1">
                    <p className="flex items-center gap-2 text-sm text-ink">
                      <span className="truncate font-medium">{person.name}</span>
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
                    <p className="mt-0.5 truncate text-xs text-ink-mute">
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
    </Panel>
  );
}
