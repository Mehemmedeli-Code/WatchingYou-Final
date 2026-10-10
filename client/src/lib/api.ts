/**
 * One place that knows how to talk to the .NET host. Keeps the access token in memory and
 * transparently refreshes once on a 401.
 *
 * The refresh token: on the website it lives in an HttpOnly cookie the server sets (page
 * scripts never see it; this file only remembers whose session the tab holds). The phone app
 * keeps it in storage as before, since it calls the API from another origin.
 */
import { apiUrl, isApp } from "@/lib/platform";
import { lang, t } from "@/lib/i18n";
import { translateServerMessage } from "@/lib/serverMessages";

export interface UserProfile {
  id: string;
  fullName: string;
  email: string;
  phoneNumber?: string | null;
  isEmailConfirmed: boolean;
  isPhoneConfirmed: boolean;
  roles: string[];
  username?: string | null;
  avatarUrl?: string | null;
  isPrivate?: boolean;
  bio?: string | null;
}

/** Registration hands back a destination, not a session — the code has to be confirmed first. */
export interface RegistrationResponse {
  email: string;
  verificationSent: boolean;
  message: string;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  user: UserProfile;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly fieldErrors?: Record<string, string[]>,
    readonly code?: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

const REFRESH_KEY = "rr.refresh";

/**
 * Per tab, not per browser. localStorage is shared across every tab on the origin, so two
 * accounts open at once fought over one key — and testing a support chat means having exactly
 * that: the customer in one window, the desk in another. sessionStorage gives each tab its own,
 * and localStorage is still read once at startup so an ordinary single-tab visit stays signed
 * in across a restart.
 */
const OWNER_KEY = "rr.refresh.user";

/** The website keeps no token of its own: the server holds it in a cookie. */
const cookieMode = () => !isApp();

const tokenStore = {
  read(): string | null {
    if (cookieMode()) return sessionStorage.getItem(OWNER_KEY) ?? localStorage.getItem(OWNER_KEY);
    const mine = sessionStorage.getItem(REFRESH_KEY);
    const shared = localStorage.getItem(REFRESH_KEY);
    // Same account in two tabs: the shared copy is the newest. Each tab preferring its own
    // copy meant one of them eventually sent an already-rotated token, which the server
    // treats as theft and answers by signing the account out everywhere.
    const owner = sessionStorage.getItem(OWNER_KEY);
    if (mine && shared && owner && owner === localStorage.getItem(OWNER_KEY)) return shared;
    return mine ?? shared;
  },
  write(token: string, userId: string) {
    if (cookieMode()) {
      // Earlier versions kept the token here; it is not needed (or valid) any more.
      sessionStorage.removeItem(REFRESH_KEY);
      localStorage.removeItem(REFRESH_KEY);
    } else {
      sessionStorage.setItem(REFRESH_KEY, token);
      localStorage.setItem(REFRESH_KEY, token);
    }
    sessionStorage.setItem(OWNER_KEY, userId);
    localStorage.setItem(OWNER_KEY, userId);
  },
  clear() {
    sessionStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(REFRESH_KEY);
    sessionStorage.removeItem(OWNER_KEY);
    localStorage.removeItem(OWNER_KEY);
  },
};

let accessToken: string | null = null;
let currentUser: UserProfile | null = null;
const listeners = new Set<(user: UserProfile | null) => void>();

/** Seconds until a JWT's exp claim, or 0 when it cannot be read. */
function secondsLeft(token: string): number {
  try {
    const payload = token.split(".")[1];
    const json = JSON.parse(atob(payload.replace(/-/g, "+").replace(/_/g, "/"))) as { exp?: number };
    return json.exp ? json.exp - Date.now() / 1000 : 0;
  } catch {
    return 0;
  }
}

function announce() {
  for (const listener of listeners) listener(currentUser);
}

/** A browser-storage key that belongs to the signed-in account, so a half-finished booking,
 *  a promo code or offline tickets never show up for the next person signed in on this browser. */
export const accountKey = (key: string) => `${key}.${currentUser?.id ?? "guest"}`;

export const auth = {
  /** For the real-time connection, which cannot send headers on a WebSocket upgrade. */
  get accessToken() {
    return accessToken;
  },
  /** A token good for at least another minute, refreshing first when it is not. A socket
   *  reconnecting an hour after the page loaded would otherwise present an expired one. */
  async ensureToken(): Promise<string | null> {
    if (!accessToken || secondsLeft(accessToken) < 60) await tryRefresh();
    return accessToken;
  },
  get user() {
    return currentUser;
  },
  /** What sign-out sends so the server can end this session: the token (phone app) or the
   *  account whose cookie to revoke (website). */
  logoutBody() {
    return cookieMode() ? { userId: tokenStore.read() } : { refreshToken: tokenStore.read() };
  },
  get isSignedIn() {
    return currentUser !== null;
  },
  /** A sign-in is saved on this device, whether or not it could be confirmed just now. With
   *  no signal the session cannot be refreshed, but the person has not signed out — the app
   *  should say "offline", not "sign in". */
  hasSavedSession() {
    return tokenStore.read() !== null;
  },
  /** After the profile page saves: the new name, handle or picture everywhere at once. */
  setUser(user: UserProfile) {
    currentUser = user;
    announce();
  },
  isAdmin() {
    return currentUser?.roles.includes("Admin") ?? false;
  },
  isSecurity() {
    return (currentUser?.roles.includes("Security") || currentUser?.roles.includes("Admin")) ?? false;
  },
  subscribe(listener: (user: UserProfile | null) => void) {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
  /** Replaces the cached profile after an edit, so the UI reflects it without re-logging in. */
  patchUser(user: UserProfile) {
    currentUser = user;
    announce();
  },
  apply(response: AuthResponse) {
    accessToken = response.accessToken;
    currentUser = response.user;
    tokenStore.write(response.refreshToken, response.user.id);
    announce();
  },
  clear() {
    accessToken = null;
    currentUser = null;
    tokenStore.clear();
    announce();
  },
};

async function parseError(response: Response): Promise<ApiError> {
  let message = `Request failed (${response.status}).`;
  let fieldErrors: Record<string, string[]> | undefined;
  let code: string | undefined;

  try {
    const body = await response.json();
    message = body.title ?? body.message ?? message;
    fieldErrors = body.errors ?? undefined;
    code = body.code ?? undefined;
  } catch {
    /* a non-JSON body is fine; the status carries enough meaning */
  }

  // The server speaks English; the customer reads the page's language.
  message = translateServerMessage(message, lang);
  if (fieldErrors) {
    fieldErrors = Object.fromEntries(Object.entries(fieldErrors)
      .map(([field, list]) => [field, list.map((text) => translateServerMessage(text, lang))]));
  }

  return new ApiError(message, response.status, fieldErrors, code);
}

/**
 * A read that could not reach the server at all (a dropped connection, the site restarting)
 * is tried twice more before the page is told. Only reads: repeating a GET changes nothing,
 * while repeating a payment or a booking could do it twice.
 */
async function fetchWithRetry(url: string, init: RequestInit): Promise<Response> {
  const method = (init.method ?? "GET").toUpperCase();
  const attempts = method === "GET" ? 3 : 1;
  for (let attempt = 1; ; attempt++) {
    try {
      return await fetch(url, init);
    } catch (err) {
      if (attempt >= attempts) throw err;
      await new Promise((resolve) => setTimeout(resolve, 700 * attempt));
    }
  }
}

async function send(path: string, init: RequestInit, retry: boolean): Promise<Response> {
  const headers = new Headers(init.headers);
  if (accessToken) headers.set("Authorization", `Bearer ${accessToken}`);
  // The page's language, not the browser's: the server writes the PDF ticket in it, and the
  // phone app has no language cookie to go by.
  headers.set("Accept-Language", lang);
  if (cookieMode()) headers.set("X-Refresh-Mode", "cookie");
  if (init.body && !(init.body instanceof FormData)) headers.set("Content-Type", "application/json");

  const response = await fetchWithRetry(apiUrl(path), { ...init, headers, credentials: "include" });

  if (response.status === 401 && retry && (await tryRefresh())) {
    return send(path, init, false);
  }
  return response;
}

let refreshing: Promise<boolean> | null = null;

/**
 * Refresh tokens rotate, and the server treats a token used twice as stolen: it revokes every
 * session the account has. Two tabs opening at once both read the same token from
 * localStorage and both refreshed with it — the second looked like theft, and the user was
 * signed out everywhere. So refreshes are serialised: one at a time within a tab (a shared
 * promise) and across tabs (a Web Lock). The token is read only once the lock is held, so the
 * second tab picks up the one the first has just written.
 */
function tryRefresh(): Promise<boolean> {
  refreshing ??= withRefreshLock(refreshOnce).finally(() => { refreshing = null; });
  return refreshing;
}

function withRefreshLock<T>(work: () => Promise<T>): Promise<T> {
  // Web Locks are in every current browser; without them a single tab still works as before.
  if (!("locks" in navigator)) return work();
  return navigator.locks.request("rr.refresh", work) as Promise<T>;
}

async function refreshOnce(): Promise<boolean> {
  const saved = tokenStore.read();
  const response = saved
    ? await fetch(apiUrl("/api/auth/refresh"), {
        method: "POST",
        headers: { "Content-Type": "application/json", ...(cookieMode() ? { "X-Refresh-Mode": "cookie" } : {}) },
        body: JSON.stringify(cookieMode() ? { userId: saved } : { refreshToken: saved }),
        credentials: "include",
      }).catch(() => null)
    : null;

  if (response?.ok) {
    auth.apply((await response.json()) as AuthResponse);
    return true;
  }
  if (await sessionFromCookie()) return true;

  auth.clear();
  return false;
}

/** The page was served to a signed-in visitor (the header shows their picture) but this tab
 *  has no working refresh token: exchange the session cookie for one rather than showing
 *  "sign in" under a header that says otherwise. */
async function sessionFromCookie(): Promise<boolean> {
  if (document.body?.dataset.signedIn !== "true") return false;
  const response = await fetch(apiUrl("/api/auth/session"), {
    method: "POST", credentials: "include", headers: cookieMode() ? { "X-Refresh-Mode": "cookie" } : {},
  }).catch(() => null);
  if (!response?.ok) return false;
  auth.apply((await response.json()) as AuthResponse);
  return true;
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await send(path, init, true);
  if (!response.ok) throw await parseError(response);
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export const get = <T,>(path: string) => api<T>(path);
export const post = <T,>(path: string, body?: unknown) =>
  api<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) });
export const put = <T,>(path: string, body?: unknown) =>
  api<T>(path, { method: "PUT", body: body === undefined ? undefined : JSON.stringify(body) });
export const patch = <T,>(path: string, body?: unknown) =>
  api<T>(path, { method: "PATCH", body: body === undefined ? undefined : JSON.stringify(body) });
export const del = <T,>(path: string) => api<T>(path, { method: "DELETE" });

export const postForm = <T,>(path: string, form: FormData) =>
  api<T>(path, { method: "POST", body: form });

export interface UploadProgress { loaded: number; total: number }

/**
 * A form upload that reports how far it has got. fetch cannot report upload progress, and a
 * large film with no progress looks frozen, so this one goes through XMLHttpRequest. Signs in
 * again once on a 401, like every other call.
 */
export async function uploadForm<T>(path: string, form: FormData, onProgress: (p: UploadProgress) => void, signal?: AbortSignal): Promise<T> {
  const attempt = (token: string | null) => new Promise<{ status: number; text: string }>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", apiUrl(path));
    xhr.withCredentials = true;
    if (token) xhr.setRequestHeader("Authorization", `Bearer ${token}`);
    xhr.setRequestHeader("Accept-Language", lang);
    xhr.upload.onprogress = (e) => { if (e.lengthComputable) onProgress({ loaded: e.loaded, total: e.total }); };
    xhr.onload = () => resolve({ status: xhr.status, text: xhr.responseText });
    xhr.onerror = () => reject(new ApiError(t("studio.connectionDropped", "The connection dropped during the upload. Check your internet and try again."), 0));
    xhr.onabort = () => reject(new ApiError(t("studio.cancelled", "Upload cancelled."), 0, undefined, "aborted"));
    signal?.addEventListener("abort", () => xhr.abort());
    xhr.send(form);
  });

  let result = await attempt(await auth.ensureToken());
  if (result.status === 401 && (await tryRefresh())) result = await attempt(accessToken);
  if (result.status < 200 || result.status >= 300) {
    throw await parseError(new Response(result.text || null, { status: result.status, headers: { "Content-Type": "application/json" } }));
  }
  return (result.text ? JSON.parse(result.text) : undefined) as T;
}

/** Downloads an authenticated file (a PDF ticket, a CSV export) and hands it to the browser
 *  as a save. A plain link cannot carry the bearer token, so the bytes are fetched first. */
export async function download(path: string, fallbackName: string): Promise<void> {
  const response = await send(path, { method: "GET" }, true);
  if (!response.ok) throw await parseError(response);

  const disposition = response.headers.get("Content-Disposition") ?? "";
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const name = match ? decodeURIComponent(match[1]) : fallbackName;

  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** Restores a session on page load. Every Razor page is a fresh document, so this runs often. */
export async function restoreSession(): Promise<UserProfile | null> {
  if (!tokenStore.read() && document.body?.dataset.signedIn !== "true") return null;
  if (!(await tryRefresh())) return null;
  return currentUser;
}

export function query(params: Record<string, string | number | boolean | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === "") continue;
    search.set(key, String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}
