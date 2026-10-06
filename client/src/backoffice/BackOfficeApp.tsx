import { useCallback, useEffect, useState, type ReactNode } from "react";
import {
  BarChart3, CalendarDays, Clapperboard, Coffee, ExternalLink, Handshake, History, LayoutDashboard,
  LogOut, Menu as MenuIcon, Package, ShieldCheck, Tags, Ticket, Wallet, X,
} from "lucide-react";
import { auth, get, post } from "@/lib/api";
import { lang, t } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import { useAuth } from "@/components/useAuth";
import { ThemeSwitcher } from "@/components/ui/theme-switcher";
import { azn, time } from "./shared";
import { DashboardView } from "./DashboardView";
import { BoxOfficeView } from "./BoxOfficeView";
import { BarView } from "./BarView";
import { ShiftHistoryView, ShiftView, type ShiftSummary } from "./ShiftView";
import { DistributorsView, MenuView, PricingView, ScheduleView } from "./SetupViews";
import { ReportsView } from "./ReportsView";

type Route =
  | "dashboard" | "box-office" | "bar" | "shift"
  | "schedule" | "pricing" | "menu" | "distributors" | "reports" | "shifts";

interface NavEntry { route: Route; label: string; icon: ReactNode; adminOnly?: boolean }

const TILL: NavEntry[] = [
  { route: "box-office", label: t("bo.boxOfficeTitle", "Box office"), icon: <Ticket size={17} /> },
  { route: "bar", label: t("bo.barTitle", "Bar"), icon: <Coffee size={17} /> },
  { route: "shift", label: t("bo.shiftTitle", "Cash shift"), icon: <Wallet size={17} /> },
];

const MANAGE: NavEntry[] = [
  { route: "dashboard", label: t("bo.dashboard", "Dashboard"), icon: <LayoutDashboard size={17} />, adminOnly: true },
  { route: "schedule", label: t("bo.scheduleTitle", "Schedule"), icon: <CalendarDays size={17} />, adminOnly: true },
  { route: "pricing", label: t("bo.pricingTitle", "Prices & tariffs"), icon: <Tags size={17} />, adminOnly: true },
  { route: "menu", label: t("bo.menuTitle", "Bar menu & stock"), icon: <Package size={17} />, adminOnly: true },
  { route: "distributors", label: t("bo.distributorsNav", "Distributors"), icon: <Handshake size={17} />, adminOnly: true },
  { route: "reports", label: t("bo.reportsTitle", "Reports"), icon: <BarChart3 size={17} />, adminOnly: true },
  { route: "shifts", label: t("bo.shiftHistory", "Shift history"), icon: <History size={17} />, adminOnly: true },
];

const ALL_ROUTES = [...TILL, ...MANAGE].map((e) => e.route);

function readRoute(): Route | null {
  const hash = window.location.hash.replace(/^#\/?/, "");
  return (ALL_ROUTES as string[]).includes(hash) ? (hash as Route) : null;
}

/**
 * The back office — the cinema's management system, the way R-Keeper is a restaurant's.
 *
 * Two kinds of people use it. A cashier sees the tills: box office, bar, and their own cash
 * shift. The manager (Admin) sees those plus programming, prices, the bar menu, distributor
 * settlement and reports. The server enforces the same split on every endpoint; the sidebar
 * only avoids offering what would be refused.
 *
 * Routing is by hash, inside one Razor page, so the browser's back button works between
 * screens without a client router arguing with the server about URLs.
 */
export default function BackOfficeApp() {
  const { user, isAdmin } = useAuth();
  const isCashier = user?.roles.includes("Cashier") ?? false;
  const allowed = isAdmin || isCashier;

  const home: Route = isAdmin ? "dashboard" : "box-office";
  const [route, setRoute] = useState<Route>(() => readRoute() ?? home);
  const [shift, setShift] = useState<ShiftSummary | null>(null);
  const [menuOpen, setMenuOpen] = useState(false);

  useEffect(() => {
    const onHash = () => { setRoute(readRoute() ?? home); setMenuOpen(false); };
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, [home]);

  const refreshShift = useCallback(async () => {
    if (!allowed) return;
    setShift((await get<ShiftSummary | undefined>("/api/backoffice/shift").catch(() => undefined)) ?? null);
  }, [allowed]);

  useEffect(() => { void refreshShift(); }, [refreshShift]);

  const go = (next: string) => { window.location.hash = `#/${next}`; };

  if (!allowed) {
    // The page itself is guarded server-side; this only covers a session that ended while
    // the page was open.
    return (
      <div className="grid min-h-screen place-items-center p-6 text-center">
        <div className="space-y-3">
          <ShieldCheck className="mx-auto text-ink-mute" size={32} />
          <p className="font-display text-xl text-ink">{t("bo.staffOnly", "Staff only")}</p>
          <a className="text-sm text-accent hover:underline" href="/account?returnUrl=%2Fbackoffice">{t("nav.signIn", "Sign in")}</a>
        </div>
      </div>
    );
  }

  // A cashier who types a manager route into the address bar lands on the till instead of an
  // error screen full of 403s.
  const effective: Route = !isAdmin && MANAGE.some((m) => m.route === route) ? home : route;

  const view = (() => {
    switch (effective) {
      case "dashboard": return <DashboardView go={go} />;
      case "box-office": return <BoxOfficeView shift={shift} onSold={() => void refreshShift()} />;
      case "bar": return <BarView shift={shift} onSold={() => void refreshShift()} />;
      case "shift": return <ShiftView shift={shift} onChange={setShift} />;
      case "schedule": return <ScheduleView />;
      case "pricing": return <PricingView />;
      case "menu": return <MenuView />;
      case "distributors": return <DistributorsView />;
      case "reports": return <ReportsView />;
      case "shifts": return <ShiftHistoryView />;
    }
  })();

  async function signOut() {
    await post("/api/auth/logout", { refreshToken: localStorage.getItem("rr.refresh") }).catch(() => null);
    auth.clear();
    window.location.href = "/account";
  }

  const sidebar = (
    <nav className="flex h-full flex-col gap-6 overflow-y-auto px-3 py-4" aria-label={t("nav.backoffice", "Back office")}>
      <a href="/" className="flex items-center gap-2.5 px-2" title={t("bo.toSite", "Public site")}>
        <span className="grid h-8 w-8 place-items-center rounded-lg bg-accent text-surface"><Clapperboard size={18} /></span>
        <span className="leading-tight">
          <span className="block font-display text-sm font-semibold text-ink">WatchingYou</span>
          <span className="block text-[11px] uppercase tracking-wider text-ink-mute">{t("nav.backoffice", "Back office")}</span>
        </span>
      </a>

      <NavGroup title={t("bo.groupTill", "Tills")} entries={TILL} active={effective} />
      {isAdmin ? <NavGroup title={t("bo.groupManage", "Management")} entries={MANAGE} active={effective} /> : null}

      <div className="mt-auto space-y-1 border-t border-line pt-4 text-sm">
        {isAdmin ? (
          <>
            <a href="/admin" className="flex items-center gap-2.5 rounded-md px-2 py-1.5 text-ink-mute hover:bg-line/40 hover:text-ink">
              <ExternalLink size={15} />{t("bo.contentAdmin", "Catalogue & users")}
            </a>
            <a href="/swagger" className="flex items-center gap-2.5 rounded-md px-2 py-1.5 text-ink-mute hover:bg-line/40 hover:text-ink">
              <ExternalLink size={15} />{t("nav.api", "API reference")}
            </a>
          </>
        ) : null}
        <a href="/" className="flex items-center gap-2.5 rounded-md px-2 py-1.5 text-ink-mute hover:bg-line/40 hover:text-ink">
          <ExternalLink size={15} />{t("bo.toSite", "Public site")}
        </a>
      </div>
    </nav>
  );

  return (
    <div className="min-h-screen bg-surface text-ink lg:grid lg:grid-cols-[248px_1fr]" style={{ fontFamily: "var(--font-sans)" }}>
      <aside className="sticky top-0 hidden h-screen border-r border-line bg-surface-raised lg:block">{sidebar}</aside>

      {menuOpen ? (
        <div className="fixed inset-0 z-40 lg:hidden">
          <button type="button" aria-label={t("bo.closeMenu", "Close menu")} className="absolute inset-0 bg-black/50" onClick={() => setMenuOpen(false)} />
          <aside className="absolute inset-y-0 left-0 w-72 max-w-[85vw] border-r border-line bg-surface-raised">{sidebar}</aside>
        </div>
      ) : null}

      <div className="min-w-0">
        <header className="sticky top-0 z-30 flex h-14 items-center gap-3 border-b border-line bg-surface/90 px-4 backdrop-blur sm:px-6">
          <button type="button" className="grid h-9 w-9 place-items-center rounded-md border border-line lg:hidden"
            aria-label={t("bo.openMenu", "Open menu")} onClick={() => setMenuOpen(true)}>
            {menuOpen ? <X size={18} /> : <MenuIcon size={18} />}
          </button>

          <button type="button" onClick={() => go("shift")}
            className={cn("flex items-center gap-2 rounded-full border px-3 py-1 text-xs",
              shift ? "border-good/60 text-good" : "border-line text-ink-mute")}>
            <span className={cn("h-2 w-2 rounded-full", shift ? "bg-good" : "bg-ink-mute/50")} aria-hidden />
            {shift
              ? <>{t("bo.shiftOpen", "Shift open")} · {time(shift.openedAtUtc)} · <span className="tabular-nums">{azn(shift.salesTotal)}</span></>
              : t("bo.shiftClosed", "No open shift")}
          </button>

          <div className="ml-auto flex items-center gap-3">
            <div className="hidden gap-1 text-xs sm:flex">
              {["az", "en", "ru", "tr"].map((code) => (
                <a key={code} href={`/lang/${code}?returnUrl=${encodeURIComponent("/backoffice" + window.location.hash)}`}
                  className={cn("rounded px-1.5 py-0.5 uppercase", code === lang ? "bg-line text-ink" : "text-ink-mute hover:text-ink")}>
                  {code}
                </a>
              ))}
            </div>
            <ThemeSwitcher />
            <span className="hidden text-right text-xs leading-tight md:block">
              <span className="block text-ink">{user?.fullName}</span>
              <span className="block text-ink-mute">{isAdmin ? t("bo.roleManager", "Manager") : t("bo.roleCashier", "Cashier")}</span>
            </span>
            <button type="button" onClick={() => void signOut()} aria-label={t("bo.signOut", "Sign out")} title={t("bo.signOut", "Sign out")}
              className="grid h-9 w-9 place-items-center rounded-md border border-line text-ink-mute hover:border-bad hover:text-bad">
              <LogOut size={16} />
            </button>
          </div>
        </header>

        <main>{view}</main>
      </div>
    </div>
  );
}

function NavGroup({ title, entries, active }: { title: string; entries: NavEntry[]; active: Route }) {
  return (
    <div>
      <p className="mb-1.5 px-2 text-[11px] font-medium uppercase tracking-wider text-ink-mute">{title}</p>
      <ul className="space-y-0.5">
        {entries.map((entry) => (
          <li key={entry.route}>
            <a href={`#/${entry.route}`} aria-current={active === entry.route ? "page" : undefined}
              className={cn("flex items-center gap-2.5 rounded-md px-2 py-2 text-sm transition-colors",
                active === entry.route ? "bg-accent/12 font-medium text-accent" : "text-ink-mute hover:bg-line/40 hover:text-ink")}>
              {entry.icon}{entry.label}
            </a>
          </li>
        ))}
      </ul>
    </div>
  );
}
