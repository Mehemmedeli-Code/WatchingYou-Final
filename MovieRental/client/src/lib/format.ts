const money = new Intl.NumberFormat("en-GB", { style: "currency", currency: "USD" });
const shortDate = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric" });
const dateTime = new Intl.DateTimeFormat("en-GB", {
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
});

export const formatMoney = (value: number) => money.format(value);
export const formatDate = (iso: string) => shortDate.format(new Date(iso));
export const formatDateTime = (iso: string) => dateTime.format(new Date(iso));

export function formatRuntime(minutes: number) {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return hours > 0 ? `${hours}h ${rest}m` : `${rest}m`;
}

export function daysUntil(iso: string) {
  const diff = new Date(iso).getTime() - Date.now();
  return Math.ceil(diff / 86_400_000);
}
