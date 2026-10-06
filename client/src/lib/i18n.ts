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

// Some browser engines ship without Azerbaijani calendar data and print a month as "M09".
// Where that happens the names below are used instead; everything else — the time zone, the
// numbers, the other three languages — still comes from Intl.
const AZ_MONTHS_SHORT = ["yan", "fev", "mar", "apr", "may", "iyn", "iyl", "avq", "sen", "okt", "noy", "dek"];
const AZ_MONTHS_LONG = ["yanvar", "fevral", "mart", "aprel", "may", "iyun", "iyul", "avqust", "sentyabr", "oktyabr", "noyabr", "dekabr"];
const AZ_WEEKDAYS_SHORT = ["B.", "B.e.", "Ç.a.", "Ç.", "C.a.", "C.", "Ş."];
const AZ_WEEKDAYS_LONG = ["bazar", "bazar ertəsi", "çərşənbə axşamı", "çərşənbə", "cümə axşamı", "cümə", "şənbə"];

const azHasCalendar = (() => {
  try { return !/M\d/.test(new Intl.DateTimeFormat("az", { month: "short" }).format(new Date(2026, 8, 1))); }
  catch { return false; }
})();

/** Intl.DateTimeFormat in the interface language, with the Azerbaijani gap filled. */
export function formatDateIn(date: Date, options: Intl.DateTimeFormatOptions): string {
  if (lang !== "az" || azHasCalendar || (!options.month && !options.weekday)) {
    return new Intl.DateTimeFormat(lang, options).format(date);
  }

  // Take the numbers from Intl (so the time zone is honoured) and put the names in by hand.
  const parts = new Intl.DateTimeFormat("en-GB", {
    ...options,
    month: options.month ? "numeric" : undefined,
    weekday: options.weekday ? "short" : undefined,
    hour12: false,
  }).formatToParts(date);
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value;

  const long = options.month === "long";
  const month = part("month");
  const monthName = month ? (long ? AZ_MONTHS_LONG : AZ_MONTHS_SHORT)[Number(month) - 1] : undefined;
  const weekdayIndex = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"].indexOf(part("weekday") ?? "");
  const weekday = weekdayIndex >= 0 ? (options.weekday === "long" ? AZ_WEEKDAYS_LONG : AZ_WEEKDAYS_SHORT)[weekdayIndex] : undefined;

  const datePart = [weekday, part("day"), monthName, part("year")].filter(Boolean).join(" ");
  const timePart = part("hour") && part("minute") ? `${part("hour")}:${part("minute")}` : "";
  return [datePart, timePart].filter(Boolean).join(", ");
}

/** Formats a date in the visitor's language rather than the browser's. */
export function formatWhen(iso: string, options?: Intl.DateTimeFormatOptions): string {
  return formatDateIn(new Date(iso), options ?? {
    day: "numeric", month: "short", hour: "2-digit", minute: "2-digit",
  });
}

export function formatDay(iso: string): string {
  return formatDateIn(new Date(iso), { weekday: "short", day: "numeric", month: "short" });
}

const LANGUAGE_NAMES: Record<string, string> = {
  az: "Azərbaycan", en: "English", ru: "Русский", tr: "Türkçe",
};

export const languageName = (code: string) => LANGUAGE_NAMES[code] ?? code.toUpperCase();
