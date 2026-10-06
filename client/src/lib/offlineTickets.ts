import type { TicketResponse } from "@/components/BookingFlow";

/**
 * The customer's upcoming tickets, kept in this browser so the QR codes still open at the
 * cinema door with no signal. Only future screenings are kept, only the fields a ticket
 * shows, and the whole thing is wiped at sign-out.
 */
const KEY = "wy.tickets.offline";

export function saveOfflineTickets(tickets: TicketResponse[]) {
  try {
    const upcoming = tickets.filter((ticket) => new Date(ticket.startsAtUtc).getTime() > Date.now() - 6 * 3600_000);
    localStorage.setItem(KEY, JSON.stringify({ savedAt: new Date().toISOString(), tickets: upcoming }));
  } catch {
    /* storage full or blocked: offline tickets are a convenience, not a requirement */
  }
}

export function loadOfflineTickets(): { savedAt: string; tickets: TicketResponse[] } | null {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as { savedAt: string; tickets: TicketResponse[] };
    parsed.tickets = parsed.tickets.filter((ticket) => new Date(ticket.startsAtUtc).getTime() > Date.now() - 6 * 3600_000);
    return parsed;
  } catch {
    return null;
  }
}

export function clearOfflineTickets() {
  try { localStorage.removeItem(KEY); } catch { /* nothing to clear */ }
}
