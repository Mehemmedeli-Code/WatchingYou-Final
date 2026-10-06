import { Capacitor } from "@capacitor/core";
import type { TicketResponse } from "@/components/BookingFlow";
import { t } from "@/lib/i18n";

/**
 * "Your film starts in an hour" — scheduled on the phone itself.
 *
 * Local notifications need no server, no Firebase project and no push certificate: the app
 * hands the operating system a time and a message, and the phone delivers it even if the app
 * is closed. Every sync replaces the whole set, so a refunded ticket loses its reminder and a
 * new one gains one, and signing out clears them all.
 */
const LEAD_MINUTES = 60;

/** Notification ids must be 32-bit integers; a booking's id is a GUID, so it is hashed. */
function idFor(paymentId: string): number {
  let hash = 0;
  for (const ch of paymentId) hash = (Math.imul(hash, 31) + ch.charCodeAt(0)) | 0;
  return Math.abs(hash) || 1;
}

async function plugin() {
  if (!Capacitor.isNativePlatform()) return null;
  return (await import("@capacitor/local-notifications")).LocalNotifications;
}

export async function syncTicketReminders(tickets: TicketResponse[]): Promise<void> {
  const notifications = await plugin();
  if (!notifications) return;

  try {
    const pending = await notifications.getPending();
    if (pending.notifications.length) {
      await notifications.cancel({ notifications: pending.notifications.map((n) => ({ id: n.id })) });
    }

    const soonest = Date.now() + 2 * 60_000;
    const upcoming = tickets
      .map((ticket) => ({ ticket, at: new Date(ticket.startsAtUtc).getTime() - LEAD_MINUTES * 60_000 }))
      .filter(({ at }) => at > soonest);
    if (upcoming.length === 0) return;

    // Asked for only when there is something to remind about — a permission prompt on first
    // launch, before the person has a ticket, is the one most people say no to.
    let permission = await notifications.checkPermissions();
    if (permission.display === "prompt" || permission.display === "prompt-with-rationale") {
      permission = await notifications.requestPermissions();
    }
    if (permission.display !== "granted") return;

    await notifications.schedule({
      notifications: upcoming.map(({ ticket, at }) => ({
        id: idFor(ticket.paymentId),
        title: t("app.reminderTitle", "Your film starts in an hour"),
        body: `${ticket.movieTitle} · ${ticket.hall} · ${ticket.seats.map((s) => s.label).join(", ")}`,
        schedule: { at: new Date(at), allowWhileIdle: true },
        extra: { route: "tickets" },
      })),
    });
  } catch {
    // A reminder is a courtesy. If the OS refuses, the ticket itself is unaffected.
  }
}

export async function clearTicketReminders(): Promise<void> {
  const notifications = await plugin();
  if (!notifications) return;
  try {
    const pending = await notifications.getPending();
    if (pending.notifications.length) {
      await notifications.cancel({ notifications: pending.notifications.map((n) => ({ id: n.id })) });
    }
  } catch { /* nothing to clear */ }
}

/** Tapping a reminder opens the ticket wallet. */
export async function listenForReminderTaps(open: (route: "tickets") => void): Promise<() => void> {
  const notifications = await plugin();
  if (!notifications) return () => undefined;
  const handle = await notifications.addListener("localNotificationActionPerformed", () => open("tickets"));
  return () => void handle.remove();
}
