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
export const formatDateTime = (iso: string) =>
  formatDateIn(new Date(iso), { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", timeZone: TIME_ZONE });

export function formatRuntime(minutes: number) {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return hours > 0 ? `${hours}h ${rest}m` : `${rest}m`;
}
