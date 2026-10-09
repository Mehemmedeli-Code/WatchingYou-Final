import type { ReactNode } from "react";
import { ChevronRight, Clapperboard, Film, Globe2, Hand, LifeBuoy, Sparkles } from "lucide-react";
import { t } from "@/lib/i18n";

/** The website's pages the app also carries, beyond its three main tabs. */
export const DISCOVER_ROUTES = ["catalogue", "on-display", "ai-catalog", "human-craft", "globe", "help"] as const;
export type DiscoverRoute = (typeof DISCOVER_ROUTES)[number];

const ITEMS: { route: DiscoverRoute; icon: ReactNode; label: () => string; hint: () => string }[] = [
  { route: "catalogue", icon: <Film size={22} />, label: () => t("nav.catalogue", "Catalogue"), hint: () => t("app.catalogueHint", "Every film: search, browse, save for later") },
  { route: "on-display", icon: <Clapperboard size={22} />, label: () => t("nav.onDisplay", "Movies on Display"), hint: () => t("app.onDisplayHint", "What is on at the cinemas today") },
  { route: "ai-catalog", icon: <Sparkles size={22} />, label: () => t("nav.aiCatalog", "AI Catalog"), hint: () => t("app.aiHint", "Short films made with AI") },
  { route: "human-craft", icon: <Hand size={22} />, label: () => t("nav.humanCraft", "Human Craft"), hint: () => t("app.craftHint", "Short films made by hand") },
  { route: "globe", icon: <Globe2 size={22} />, label: () => t("nav.globe", "Globe"), hint: () => t("app.globeHint", "See who is watching around the world") },
  { route: "help", icon: <LifeBuoy size={22} />, label: () => t("nav.help", "Help"), hint: () => t("app.helpHint", "Got a question? Chat with us") },
];

export const discoverLabel = (route: DiscoverRoute) => ITEMS.find((item) => item.route === route)!.label();

/**
 * The Discover tab: one large card per page, sized for a thumb. The catalogue is here to browse;
 * renting and Watching PRO are not — digital content sold inside an app must go through the
 * stores' own billing.
 */
export function DiscoverTab({ onOpen }: { onOpen: (route: DiscoverRoute) => void }) {
  return (
    <div className="px-4 pt-6">
      <h1 className="font-display text-3xl font-bold tracking-tight text-ink">{t("app.more", "Discover")}</h1>
      <p className="mt-1 text-sm text-ink-mute">{t("app.moreLede", "Everything on the website, made for your phone.")}</p>

      <ul className="mt-6 grid gap-3">
        {ITEMS.map((item) => (
          <li key={item.route}>
            <button
              type="button"
              onClick={() => onOpen(item.route)}
              className="flex w-full items-center gap-4 rounded-2xl border border-line bg-surface-raised p-4 text-left transition active:scale-[0.99] hover:border-accent"
            >
              <span className="grid h-12 w-12 shrink-0 place-items-center rounded-xl bg-accent/12 text-accent">{item.icon}</span>
              <span className="min-w-0 flex-1">
                <span className="block font-display text-base font-semibold text-ink">{item.label()}</span>
                <span className="mt-0.5 block text-sm text-ink-mute">{item.hint()}</span>
              </span>
              <ChevronRight size={20} className="shrink-0 text-ink-mute" aria-hidden />
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
