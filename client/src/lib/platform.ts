/**
 * The few places where the website and the phone app behave differently, kept in one file.
 *
 * On the website every page is served by the .NET host, so API calls are same-origin paths
 * ("/api/...") and moving between screens is a page load. In the Capacitor app the screens are
 * bundled into the app itself: API calls go to the real server by full URL, and moving between
 * screens stays inside the app. The mobile entry (mobile/main.tsx) announces itself by setting
 * window.__WY_APP__ before anything else loads; without it, every function here does exactly
 * what the website always did.
 */
interface AppGlobals {
  /** e.g. https://api.watchingyou.az — no trailing slash. */
  apiBase: string;
  /** Switches the app to one of its tabs. */
  navigate: (route: "films" | "tickets" | "account") => void;
}

const app = (): AppGlobals | undefined => (window as unknown as { __WY_APP__?: AppGlobals }).__WY_APP__;

export const isApp = () => app() !== undefined;

/** A path on the API, as the fetch in this environment must spell it. */
export function apiUrl(path: string): string {
  const current = app();
  return current && path.startsWith("/") ? current.apiBase + path : path;
}

/** Sends someone to sign in, and — on the website — back to where they were afterwards. */
export function goToSignIn(returnTo: string) {
  const current = app();
  if (current) current.navigate("account");
  else window.location.href = `/account?returnUrl=${encodeURIComponent(returnTo)}`;
}

/** Where to land once signed in or out. The website reloads (its header is server-rendered);
 *  the app just changes tab, since the shared auth store already told every screen. */
export function afterSignIn(websiteTarget: string) {
  const current = app();
  if (current) current.navigate("films");
  else window.location.href = websiteTarget;
}

export function afterSignOut() {
  const current = app();
  if (current) current.navigate("account");
  else window.location.href = "/";
}

/**
 * Opens a page that is not ours — Stripe's checkout. On the website the browser simply goes
 * there. In the app it opens in the system's in-app browser (SFSafariViewController, Chrome
 * Custom Tabs), which is what the stores expect for payment pages; the app hears when it is
 * closed and re-checks the booking.
 */
export async function openExternal(url: string) {
  if (!app()) {
    window.location.href = url;
    return;
  }
  const { Browser } = await import("@capacitor/browser");
  await Browser.open({ url, presentationStyle: "popover" });
}

/** Opens one of the server's own pages (the privacy policy): a normal link on the website,
 *  the in-app browser in the app, since the app does not contain those pages. */
export function openServerPage(path: string) {
  const current = app();
  if (current) void openExternal(current.apiBase + path);
  else window.location.href = path;
}

/** Fired when the app comes back to the foreground or the in-app browser is closed. */
export const RESUME_EVENT = "wy:resume";

export function onAppResume(callback: () => void): () => void {
  window.addEventListener(RESUME_EVENT, callback);
  return () => window.removeEventListener(RESUME_EVENT, callback);
}
