import { useEffect, useState } from "react";

/**
 * Light or dark, for the whole page at once.
 *
 * The theme lives as `data-theme` on <html>, because that is the one element the Razor header,
 * the footer and every React island all sit under. A small inline script in _Layout.cshtml sets
 * it before the first paint, so a light-theme visitor never sees a dark flash on load.
 *
 * Deliberately not `next-themes`. That library is a React context: this site mounts several
 * separate React roots, each would get its own provider, and they would disagree about the
 * theme. It is also shaped around Next.js server rendering, which this app does not use. One
 * attribute and one event do the job for every root and for the Razor markup too.
 */

export type Theme = "light" | "dark";

const KEY = "wy.theme";
const EVENT = "wy:theme";

export function getTheme(): Theme {
  return document.documentElement.dataset.theme === "light" ? "light" : "dark";
}

function apply(theme: Theme) {
  const root = document.documentElement;
  root.dataset.theme = theme;
  // Native controls — scrollbars, date pickers, autofill — follow this, not the CSS tokens.
  root.style.colorScheme = theme;
}

export function setTheme(theme: Theme) {
  apply(theme);
  try { localStorage.setItem(KEY, theme); } catch { /* private mode: the choice lasts this visit */ }
  window.dispatchEvent(new CustomEvent<Theme>(EVENT, { detail: theme }));
}

/** The current theme, re-rendering when it changes here or in another tab. */
export function useTheme(): [Theme, (theme: Theme) => void] {
  const [theme, set] = useState<Theme>(getTheme);

  useEffect(() => {
    const onChange = () => set(getTheme());

    // Another tab switched: follow it, so two open windows never disagree.
    const onStorage = (event: StorageEvent) => {
      if (event.key !== KEY) return;
      apply(event.newValue === "light" ? "light" : "dark");
      onChange();
    };

    window.addEventListener(EVENT, onChange);
    window.addEventListener("storage", onStorage);
    return () => {
      window.removeEventListener(EVENT, onChange);
      window.removeEventListener("storage", onStorage);
    };
  }, []);

  return [theme, setTheme];
}
