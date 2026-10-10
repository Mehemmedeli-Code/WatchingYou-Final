import type { ReactNode } from "react";
import { MoonStarIcon, SunIcon } from "lucide-react";
import { cn } from "@/lib/utils";
import { t } from "@/lib/i18n";
import { useTheme, type Theme } from "@/lib/theme";

/**
 * Adapted from the theme-switcher-1 component.
 *
 * Two options, light and dark — the "follow the system" option was left out on request. The
 * sliding ring was the original's `motion` layoutId animation; with two fixed slots a CSS
 * transform does the same slide, and keeps `motion` out of app.js, which every page parses.
 * Zinc greys became the site's own tokens, so the control repaints with the theme it controls.
 * `next-themes` is replaced by lib/theme.ts: see the note there for why.
 */

const OPTIONS: { value: Theme; icon: ReactNode }[] = [
  { value: "light", icon: <SunIcon aria-hidden /> },
  { value: "dark", icon: <MoonStarIcon aria-hidden /> },
];

export function ThemeSwitcher() {
  const [theme, setTheme] = useTheme();
  const index = Math.max(0, OPTIONS.findIndex((option) => option.value === theme));

  return (
    <div
      role="radiogroup"
      aria-label={t("theme.label")}
      className="relative inline-flex items-center overflow-hidden rounded-full bg-surface-raised ring-1 ring-line ring-inset"
    >
      <span
        aria-hidden
        className="pointer-events-none absolute left-0 top-0 size-8 rounded-full border border-accent-dim transition-transform duration-500 ease-[cubic-bezier(.34,1.4,.64,1)] motion-reduce:transition-none"
        style={{ transform: `translateX(${index * 100}%)` }}
      />
      {OPTIONS.map((option) => {
        const active = theme === option.value;
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={active}
            aria-label={t(`theme.${option.value}`)}
            onClick={() => setTheme(option.value)}
            className={cn(
              "relative flex size-8 items-center justify-center rounded-full transition-colors [&_svg]:size-4",
              active ? "text-ink" : "text-ink-mute hover:text-ink",
            )}
          >
            {option.icon}
          </button>
        );
      })}
    </div>
  );
}

export default ThemeSwitcher;
