/**
 * Translations arrive inline with the document, put there by _Layout.cshtml, so the first
 * paint is already in the right language. There is nothing to fetch and nothing to await.
 *
 * Razor and React read the same JSON files on the server, so a key can never be translated
 * on one side and missing on the other.
 */
interface RrGlobals {
  lang: string;
  t: Record<string, string>;
}

const globals: RrGlobals =
  (window as unknown as { __RR__?: RrGlobals }).__RR__ ?? { lang: "az", t: {} };

export const lang = globals.lang;

/**
 * `fallback` is what shows when a key has not been translated yet. Passing the English
 * sentence keeps the page readable while a locale is still being filled in.
 */
export function t(key: string, fallback?: string): string {
  return globals.t[key] ?? fallback ?? key;
}

/** Formats a date in the visitor's language rather than the browser's. */
export function formatWhen(iso: string, options?: Intl.DateTimeFormatOptions): string {
  return new Intl.DateTimeFormat(lang, options ?? {
    day: "numeric", month: "short", hour: "2-digit", minute: "2-digit",
  }).format(new Date(iso));
}

export function formatDay(iso: string): string {
  return new Intl.DateTimeFormat(lang, { weekday: "short", day: "numeric", month: "short" })
    .format(new Date(iso));
}

const LANGUAGE_NAMES: Record<string, string> = {
  az: "Azərbaycan", en: "English", ru: "Русский", tr: "Türkçe",
};

export const languageName = (code: string) => LANGUAGE_NAMES[code] ?? code.toUpperCase();
