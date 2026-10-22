import { useCallback, useEffect, useState } from "react";
import { Ticket } from "lucide-react";
import { Section, Notice, Spinner, Empty } from "@/components/Shell";
import { TicketCard, type TicketResponse } from "@/components/BookingFlow";
import { useAuth } from "@/components/useAuth";
import { auth, get } from "@/lib/api";
import { loadOfflineTickets, saveOfflineTickets } from "@/lib/offlineTickets";
import { onAppResume } from "@/lib/platform";
import { t } from "@/lib/i18n";
import { syncTicketReminders } from "./reminders";

/**
 * The phone's ticket wallet. Tickets are read from the server when there is signal and kept
 * on the device, so the QR codes open at the cinema door with none — the reason most people
 * install a cinema app at all.
 */
export function TicketsTab({ onSignIn }: { onSignIn: () => void }) {
  const { isSignedIn } = useAuth();
  const [tickets, setTickets] = useState<TicketResponse[] | null>(null);
  const [offlineSince, setOfflineSince] = useState<string | null>(null);

  const load = useCallback(async () => {
    const fallback = () => {
      const saved = loadOfflineTickets();
      setTickets(saved?.tickets ?? []);
      setOfflineSince(saved ? saved.savedAt : null);
    };

    if (!isSignedIn || !navigator.onLine) { fallback(); return; }
    try {
      const mine = await get<TicketResponse[]>("/api/bookings/mine");
      saveOfflineTickets(mine);
      void syncTicketReminders(mine);
      setTickets(mine);
      setOfflineSince(null);
    } catch {
      fallback();
    }
  }, [isSignedIn]);

  useEffect(() => { void load(); }, [load]);
  useEffect(() => onAppResume(() => { void load(); }), [load]);

  // Signed in on this device, but the session could not be confirmed — no signal. Not a
  // reason to ask for the password again.
  const offlineSession = !isSignedIn && auth.hasSavedSession();

  if (tickets === null) return <Section title={t("app.tickets", "My tickets")}><Spinner label={t("common.loading", "Loading")} /></Section>;

  return (
    <Section title={t("app.tickets", "My tickets")} lede={t("app.ticketsLede", "Show the QR code at the door. It works without internet.")}>
      {offlineSince ? <div className="mb-4"><Notice tone="info">{t("offline.tickets", "You are offline. These are the tickets saved on this device.")}</Notice></div> : null}

      {tickets.length === 0 ? (
        <Empty
          title={t("app.noTickets", "No tickets yet")}
          hint={isSignedIn
            ? t("app.noTicketsHint", "Pick a film on the Films tab and book a seat.")
            : offlineSession
              ? t("app.offlineSession", "No internet. Your tickets will update as soon as you are back online.")
              : t("app.signInForTickets", "Sign in to see the tickets you bought.")}
          action={isSignedIn || offlineSession ? undefined : (
            <button type="button" onClick={onSignIn}
              className="inline-flex h-11 items-center gap-2 rounded-full bg-accent px-6 text-sm font-semibold text-surface">
              <Ticket size={16} />{t("nav.signIn", "Sign in")}
            </button>
          )}
        />
      ) : (
        <div className="space-y-4">
          {tickets.map((ticket) => (
            <TicketCard key={ticket.paymentId} ticket={ticket} onRefunded={() => void load()} />
          ))}
        </div>
      )}
    </Section>
  );
}
