import { useState } from "react";
import { AnimatePresence, motion } from "motion/react";
import { LifeBuoy, MessageCircle, X } from "lucide-react";
import { DirectChat } from "@/components/DirectChat";
import { useAuth } from "@/components/useAuth";
import { openDirectChats, useRealtime } from "@/lib/realtime";
import { t } from "@/lib/i18n";

interface Nudge {
  id: number;
  kind: "dm" | "help";
  title: string;
  userId?: string;
}

/**
 * Site-wide "you have a new message" pop-up. Mounted on its own root, like the theme switcher,
 * so it works on every page — before this, a direct message only showed up if the recipient
 * happened to open that exact conversation.
 */
export function LiveNotifications() {
  const { isSignedIn } = useAuth();
  const [nudges, setNudges] = useState<Nudge[]>([]);
  const [chatWith, setChatWith] = useState<string | null>(null);
  const onHelpPage = document.getElementById("root")?.dataset.page === "help";

  function push(nudge: Omit<Nudge, "id">) {
    const id = Date.now() + Math.random();
    setNudges((list) => [...list.filter((n) => !(n.kind === nudge.kind && n.userId === nudge.userId)), { ...nudge, id }].slice(-3));
    setTimeout(() => setNudges((list) => list.filter((n) => n.id !== id)), 9000);
  }

  useRealtime<{ fromUserId: string; fromName: string }>("dm", (e) => {
    if (openDirectChats.has(e.fromUserId)) return;
    push({ kind: "dm", title: `${t("live.dmFrom", "New message from")} ${e.fromName}`, userId: e.fromUserId });
  });

  useRealtime("help", () => {
    if (onHelpPage) return;
    push({ kind: "help", title: t("live.helpReply", "The help desk replied to you") });
  });

  if (!isSignedIn) return null;

  return (
    <>
      {chatWith ? <DirectChat userId={chatWith} onClose={() => setChatWith(null)} /> : null}

      <div className="pointer-events-none fixed right-4 top-20 z-[85] flex w-[min(92vw,340px)] flex-col gap-2">
        <AnimatePresence>
          {nudges.map((nudge) => (
            <motion.div
              key={nudge.id}
              initial={{ opacity: 0, x: 24 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: 24 }}
              role="status"
              aria-live="polite"
              className="pointer-events-auto flex items-start gap-3 rounded-xl border border-accent-dim bg-surface-raised/95 p-3 text-sm text-ink shadow-lg backdrop-blur"
            >
              {nudge.kind === "dm"
                ? <MessageCircle size={18} className="mt-0.5 text-accent" aria-hidden />
                : <LifeBuoy size={18} className="mt-0.5 text-accent" aria-hidden />}
              <div className="flex-1">
                <p>{nudge.title}</p>
                <button
                  className="mt-1 text-xs font-medium text-accent hover:underline"
                  onClick={() => {
                    setNudges((list) => list.filter((n) => n.id !== nudge.id));
                    if (nudge.kind === "dm" && nudge.userId) setChatWith(nudge.userId);
                    else window.location.href = "/help";
                  }}
                >
                  {t("live.open", "Open")}
                </button>
              </div>
              <button
                onClick={() => setNudges((list) => list.filter((n) => n.id !== nudge.id))}
                aria-label={t("common.cancel")}
                className="text-ink-mute hover:text-ink"
              >
                <X size={14} aria-hidden />
              </button>
            </motion.div>
          ))}
        </AnimatePresence>
      </div>
    </>
  );
}
