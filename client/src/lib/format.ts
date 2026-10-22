import { formatDateIn, lang } from "@/lib/i18n";

// Prices are in manats — the cinema is in Baku and Stripe is configured for AZN. This used to
// format as US dollars, so every price on the site carried the wrong currency sign.
const number = new Intl.NumberFormat(lang, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// Dates follow the visitor's chosen language, and are shown on Baku time: a screening at
// 19:00 in Baku is at 19:00 whatever time zone the visitor's device happens to be set to.
const TIME_ZONE = "Asia/Baku";
/** Rentals and Watching PRO are priced in US dollars; cinema tickets stay in manat. These two
 *  follow RentalPricing on the server: three days for $0.50, a month of PRO for $5. */
export const RENTAL_PRICE = 0.5;
export const PRO_PRICE = 5;
/** Rentals and Watching PRO are priced in US dollars; cinema tickets stay in manat. */
export const formatUsd = (value: number) =>
  new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value);
export const formatMoney = (value: number) => `${number.format(value)} ₼`;
export const formatDate = (iso: string) =>
  formatDateIn(new Date(iso), { day: "numeric", month: "short", year: "numeric", timeZone: TIME_ZONE });
/** The capitals with cinemas: where the map's flags lead, and the clock their shows run on. */
export const CITIES = [
  { city: "Baku", country: "az", label: "AZE", zone: "Asia/Baku", centre: [49.865, 40.39] as [number, number] },
  { city: "Ankara", country: "tr", label: "TR", zone: "Europe/Istanbul", centre: [32.82, 39.91] as [number, number] },
  { city: "Moscow", country: "ru", label: "RUS", zone: "Europe/Moscow", centre: [37.59, 55.75] as [number, number] },
];

/** A show's time is told on its own city's clock: 12:00 in Ankara is 12:00 there. */
export const zoneFor = (city?: string | null) => CITIES.find((c) => c.city === city)?.zone ?? TIME_ZONE;

export const formatDateTime = (iso: string, city?: string | null) =>
  formatDateIn(new Date(iso), { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", timeZone: zoneFor(city) });

export function formatRuntime(minutes: number) {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return hours > 0 ? `${hours}h ${rest}m` : `${rest}m`;
}
