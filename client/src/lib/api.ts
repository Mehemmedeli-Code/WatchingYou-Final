/**
 * One place that knows how to talk to the .NET host. Keeps the access token in memory
 * and the refresh token in localStorage, then transparently refreshes once on a 401.
 * Storing the short-lived access token outside localStorage limits what an XSS bug reaches.
 */
import { apiUrl } from "@/lib/platform";
import { lang } from "@/lib/i18n";
import { translateServerMessage } from "@/lib/serverMessages";

export interface UserProfile {
  id: string;
  fullName: string;
  email: string;
  phoneNumber?: string | null;
  isEmailConfirmed: boolean;
  isPhoneConfirmed: boolean;
  roles: string[];
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
const tokenStore = {
  read(): string | null {
    return sessionStorage.getItem(REFRESH_KEY) ?? localStorage.getItem(REFRESH_KEY);
  },
  write(token: string) {
    sessionStorage.setItem(REFRESH_KEY, token);
    localStorage.setItem(REFRESH_KEY, token);
  },
  clear() {
    sessionStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(REFRESH_KEY);
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
  get isSignedIn() {
    return currentUser !== null;
  },
  /** A sign-in is saved on this device, whether or not it could be confirmed just now. With
   *  no signal the session cannot be refreshed, but the person has not signed out — the app
   *  should say "offline", not "sign in". */
  hasSavedSession() {
    return tokenStore.read() !== null;
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
    tokenStore.write(response.refreshToken);
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
  const refreshToken = tokenStore.read();
  if (!refreshToken) return false;

  const response = await fetch(apiUrl("/api/auth/refresh"), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refreshToken }),
    credentials: "include",
  });

  if (!response.ok) {
    auth.clear();
    return false;
  }

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
  if (!tokenStore.read()) return null;
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
