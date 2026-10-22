/**
 * One place that knows how to talk to the .NET host. Keeps the access token in memory
 * and the refresh token in localStorage, then transparently refreshes once on a 401.
 * Storing the short-lived access token outside localStorage limits what an XSS bug reaches.
 */

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
let accessToken: string | null = null;
let currentUser: UserProfile | null = null;
const listeners = new Set<(user: UserProfile | null) => void>();

function announce() {
  for (const listener of listeners) listener(currentUser);
}

export const auth = {
  get user() {
    return currentUser;
  },
  get isSignedIn() {
    return currentUser !== null;
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
  apply(response: AuthResponse) {
    accessToken = response.accessToken;
    currentUser = response.user;
    localStorage.setItem(REFRESH_KEY, response.refreshToken);
    announce();
  },
  clear() {
    accessToken = null;
    currentUser = null;
    localStorage.removeItem(REFRESH_KEY);
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

  return new ApiError(message, response.status, fieldErrors, code);
}

async function send(path: string, init: RequestInit, retry: boolean): Promise<Response> {
  const headers = new Headers(init.headers);
  if (accessToken) headers.set("Authorization", `Bearer ${accessToken}`);
  if (init.body && !(init.body instanceof FormData)) headers.set("Content-Type", "application/json");

  const response = await fetch(path, { ...init, headers, credentials: "include" });

  if (response.status === 401 && retry && (await tryRefresh())) {
    return send(path, init, false);
  }
  return response;
}

async function tryRefresh(): Promise<boolean> {
  const refreshToken = localStorage.getItem(REFRESH_KEY);
  if (!refreshToken) return false;

  const response = await fetch("/api/auth/refresh", {
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

/** Restores a session on page load. Every Razor page is a fresh document, so this runs often. */
export async function restoreSession(): Promise<UserProfile | null> {
  if (!localStorage.getItem(REFRESH_KEY)) return null;
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
