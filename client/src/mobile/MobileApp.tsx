import { useEffect, useState, type ReactNode } from "react";
import { Clapperboard, ScanLine, Ticket, UserRound } from "lucide-react";
import { Capacitor } from "@capacitor/core";
import { cn } from "@/lib/utils";
import { t } from "@/lib/i18n";
import { RESUME_EVENT, onAppResume } from "@/lib/platform";
import { auth, get, restoreSession } from "@/lib/api";
import type { TicketResponse } from "@/components/BookingFlow";
import { clearTicketReminders, listenForReminderTaps, syncTicketReminders } from "./reminders";
import { ThemeSwitcher } from "@/components/ui/theme-switcher";
import { ErrorBoundary } from "@/components/ErrorBoundary";
import CinemaPage from "@/pages/CinemaPage";
import AccountPage from "@/pages/AccountPage";
import { TicketsTab } from "./TicketsTab";
import { DoorTab } from "./DoorTab";
import { useAuth } from "@/components/useAuth";
import { LANGUAGES, appLanguage, setAppLanguage } from "./boot";

type Route = "films" | "tickets" | "door" | "account";

const readRoute = (): Route => {
  const hash = window.location.hash.replace(/^#\/?/, "");
  return hash === "tickets" || hash === "account" || hash === "door" ? hash : "films";
};

/**
 * The phone app: three tabs over the same screens the website uses.
 *
 * Films is the cinema page — schedule, seat map, checkout. Tickets is the wallet, with the QR
 * codes saved for offline use. Account is sign-in, registration and the profile. Film rental
 * is deliberately absent: digital content sold inside an app must go through Apple's and
 * Google's own billing, while cinema tickets — a service used in the real world — may be paid
 * by card, so the app sells only tickets.
 */
export default function MobileApp() {
  const [route, setRoute] = useState<Route>(readRoute);
  // Door staff (Security) and admins get a fourth tab for scanning tickets at the entrance;
  // the check-in API enforces the same rule, so hiding it is presentation, not security.
  const { isSecurity, isSignedIn } = useAuth();
  // Nobody signed in, and no saved sign-in waiting for a signal to be confirmed: the app is
  // just its sign-in screen until they are in. A saved session counts as in — offline at the
  // cinema door, the tickets tab must still open.
  const signedOut = !isSignedIn && !auth.hasSavedSession();
  const current: Route = signedOut ? "account" : route === "door" && !isSecurity ? "films" : route;

  useEffect(() => {
    const onHash = () => { setRoute(readRoute()); window.scrollTo(0, 0); };
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  // Native wiring. Everything here is skipped in a browser, where the same bundle can be
  // opened for testing.
  useEffect(() => {
    if (!Capacitor.isNativePlatform()) return;
    const cleanups: (() => void)[] = [];
    const resume = () => window.dispatchEvent(new Event(RESUME_EVENT));

    void (async () => {
      const { App } = await import("@capacitor/app");
      const { Browser } = await import("@capacitor/browser");
      const { StatusBar, Style } = await import("@capacitor/status-bar");
      const { SplashScreen } = await import("@capacitor/splash-screen");

      const onResume = await App.addListener("resume", resume);
      const onBrowserClosed = await Browser.addListener("browserFinished", resume);
      // Android's back button walks back to Films first, and only then leaves the app.
      const onBack = await App.addListener("backButton", () => {
        if (readRoute() !== "films") window.location.hash = "#/films";
        else void App.exitApp();
      });
      cleanups.push(() => void onResume.remove(), () => void onBrowserClosed.remove(), () => void onBack.remove());

      const light = document.documentElement.dataset.theme === "light";
      await StatusBar.setStyle({ style: light ? Style.Light : Style.Dark }).catch(() => undefined);
      await SplashScreen.hide().catch(() => undefined);
    })();

    return () => cleanups.forEach((fn) => fn());
  }, []);

  // Reminders: kept in step with the tickets at start-up and whenever the app comes back,
  // wiped on sign-out, and a tap on one opens the ticket wallet.
  useEffect(() => {
    const sync = () => {
      // Opened with no signal: the saved sign-in could not be confirmed then. Confirm it now,
      // so nobody is asked to sign in again just because they started the app underground.
      if (!auth.isSignedIn && auth.hasSavedSession()) {
        void restoreSession()
          .then(() => { if (auth.isSignedIn) window.dispatchEvent(new Event(RESUME_EVENT)); })
          .catch(() => undefined);
        return;
      }
      if (!auth.isSignedIn) return;
      void get<TicketResponse[]>("/api/bookings/mine").then(syncTicketReminders).catch(() => undefined);
    };
    sync();
    const stopResume = onAppResume(sync);
    window.addEventListener("online", sync);
    const stopAuth = auth.subscribe((user) => { if (user) sync(); else void clearTicketReminders(); });
    let stopTaps = () => {};
    void listenForReminderTaps((next) => { window.location.hash = `#/${next}`; }).then((stop) => { stopTaps = stop; });
    return () => { stopResume(); stopAuth(); stopTaps(); window.removeEventListener("online", sync); };
  }, []);

  const go = (next: Route) => { window.location.hash = `#/${next}`; };

  return (
    <div className="flex min-h-screen flex-col bg-surface text-ink" style={{ fontFamily: "var(--font-sans)" }}>
      <header
        className="sticky top-0 z-30 flex items-center gap-3 border-b border-line bg-surface/90 px-4 pb-2.5 backdrop-blur"
        style={{ paddingTop: "calc(env(safe-area-inset-top) + 10px)" }}
      >
        <EyeMark />
        <span className="font-display text-lg font-bold tracking-tight text-accent">WatchingYou</span>
        <div className="ml-auto flex items-center gap-2">
          <select
            aria-label="Language"
            value={appLanguage}
            onChange={(e) => setAppLanguage(e.target.value as (typeof LANGUAGES)[number])}
            className="h-8 rounded-full border border-line bg-surface-raised px-2 text-xs uppercase text-ink"
          >
            {LANGUAGES.map((code) => <option key={code} value={code}>{code.toUpperCase()}</option>)}
          </select>
          <ThemeSwitcher />
        </div>
      </header>

      <main className={cn("flex-1", signedOut ? "pb-8" : "pb-24")}>
        <ErrorBoundary label={current}>
          {current === "films" ? <CinemaPage hideTickets /> : null}
          {current === "tickets" ? <TicketsTab onSignIn={() => go("account")} /> : null}
          {current === "door" ? <DoorTab /> : null}
          {current === "account" ? <AccountPage /> : null}
        </ErrorBoundary>
      </main>

      {signedOut ? null : (
      <nav
        aria-label="WatchingYou"
        className={cn("fixed inset-x-0 bottom-0 z-30 grid border-t border-line bg-surface-raised/95 backdrop-blur", isSecurity ? "grid-cols-4" : "grid-cols-3")}
        style={{ paddingBottom: "env(safe-area-inset-bottom)" }}
      >
        <Tab active={current === "films"} onClick={() => go("films")} icon={<Clapperboard size={22} />} label={t("app.films", "Films")} />
        <Tab active={current === "tickets"} onClick={() => go("tickets")} icon={<Ticket size={22} />} label={t("app.tickets", "My tickets")} />
        {isSecurity ? <Tab active={current === "door"} onClick={() => go("door")} icon={<ScanLine size={22} />} label={t("door.title", "Door")} /> : null}
        <Tab active={current === "account"} onClick={() => go("account")} icon={<UserRound size={22} />} label={t("nav.account", "Account")} />
      </nav>
      )}
    </div>
  );
}

function Tab({ active, onClick, icon, label }: { active: boolean; onClick: () => void; icon: ReactNode; label: string }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-current={active ? "page" : undefined}
      className={cn("flex h-16 flex-col items-center justify-center gap-1 text-[11px] font-medium transition-colors",
        active ? "text-accent" : "text-ink-mute")}
    >
      {icon}
      {label}
    </button>
  );
}

/** The site's eye, small, for the header. */
function EyeMark() {
  return (
    <svg width="30" height="21" viewBox="0 0 64 44" aria-hidden="true">
      <path d="M2 22C2 22 14 4 32 4s30 18 30 18-12 18-30 18S2 22 2 22Z" fill="#050A07" stroke="#00E676" strokeWidth="2.6" strokeLinejoin="round" />
      <circle cx="32" cy="22" r="13" fill="#00E676" />
      <circle cx="34" cy="23" r="6.4" fill="#04120A" />
      <circle cx="28" cy="17" r="2.6" fill="#EAFFF2" />
    </svg>
  );
}
