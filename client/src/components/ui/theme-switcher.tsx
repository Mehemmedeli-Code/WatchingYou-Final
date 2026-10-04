import type { ReactNode } from "react";
import { MoonStarIcon, SunIcon } from "lucide-react";
import { motion } from "motion/react";
import { cn } from "@/lib/utils";
import { t } from "@/lib/i18n";
import { useTheme, type Theme } from "@/lib/theme";

/**
 * Adapted from the theme-switcher-1 component.
 *
 * Two options, light and dark — the "follow the system" option was left out on request. The
 * sliding ring is the original's shared `layoutId` animation; zinc greys became the site's own
 * tokens, so the control repaints with the theme it controls. `next-themes` is replaced by
 * lib/theme.ts: see the note there for why.
 */

const OPTIONS: { value: Theme; icon: ReactNode }[] = [
  { value: "light", icon: <SunIcon aria-hidden /> },
  { value: "dark", icon: <MoonStarIcon aria-hidden /> },
];

export function ThemeSwitcher() {
  const [theme, setTheme] = useTheme();

  return (
    <div
      role="radiogroup"
      aria-label={t("theme.label")}
      className="inline-flex items-center overflow-hidden rounded-full bg-surface-raised ring-1 ring-line ring-inset"
    >
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
            {active ? (
              <motion.span
                layoutId="theme-option"
                transition={{ type: "spring", bounce: 0.3, duration: 0.6 }}
                className="absolute inset-0 rounded-full border border-accent-dim"
              />
            ) : null}
          </button>
        );
      })}
    </div>
  );
}

export default ThemeSwitcher;
