// Helpers for the end-to-end API test: HTTP calls, SQL checks against the real database,
// reading e-mailed codes from the server log, and a results table.
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";

process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0"; // local dev certificate

export const BASE = "https://localhost:7139";
const LOG = "C:/wy/apitest/server.log";

export const results = [];
let section = "";
export const setSection = (name) => { section = name; console.log(`\n=== ${name}`); };

/** One HTTP call. Returns status, parsed JSON (when JSON), raw text and headers. */
export async function call(method, path, { token, body, raw, form, headers = {} } = {}) {
  const h = { ...headers };
  if (token) h.Authorization = `Bearer ${token}`;
  let payload;
  if (form) payload = form;
  else if (raw !== undefined) { payload = raw; h["Content-Type"] = "application/json"; }
  else if (body !== undefined) { payload = JSON.stringify(body); h["Content-Type"] = "application/json"; }
  let res = await fetch(BASE + path, { method, headers: h, body: payload, redirect: "manual" });
  // The sign-in endpoints are rate-limited per address (brute-force protection). A test run
  // signs in far more often than a person, so it waits the time the server asks and retries —
  // the limit itself is tested separately at the end.
  for (let tries = 0; res.status === 429 && tries < 3 && !globalThis.NO_RETRY_429; tries++) {
    const wait = Number(res.headers.get("retry-after") || 30);
    console.log(`     (rate limited on ${path}; waiting ${wait}s as the server asks)`);
    await new Promise((r) => setTimeout(r, (wait + 1) * 1000));
    res = await fetch(BASE + path, { method, headers: h, body: payload, redirect: "manual" });
  }
  const type = res.headers.get("content-type") || "";
  let text = "", json = null, bytes = 0;
  if (type.includes("json") || type.includes("text") || type === "") {
    text = await res.text(); bytes = text.length;
    if (type.includes("json") && text) { try { json = JSON.parse(text); } catch { /* keep text */ } }
  } else { bytes = (await res.arrayBuffer()).byteLength; }
  return { status: res.status, json, text, type, bytes, headers: res.headers };
}

/** Records one expectation. `expect` is a status or list of acceptable statuses. */
export function check(name, method, path, got, expect, extra = "") {
  const ok = Array.isArray(expect) ? expect.includes(got.status) : got.status === expect;
  const detail = ok ? extra : `${extra} ${got.text ? got.text.slice(0, 160) : ""}`.trim();
  results.push({ section, name, method, path, status: got.status, expect: [].concat(expect).join("/"), ok, detail });
  console.log(`${ok ? "PASS" : "FAIL"} ${String(got.status).padEnd(3)} ${method.padEnd(6)} ${path}  — ${name}${detail ? "  | " + detail : ""}`);
  return ok;
}

/** A database assertion, recorded like an HTTP one. */
export function assertDb(name, condition, detail = "") {
  results.push({ section, name, method: "SQL", path: "", status: "", expect: "", ok: !!condition, detail });
  console.log(`${condition ? "PASS" : "FAIL"} SQL    ${name}${detail ? "  | " + detail : ""}`);
  return !!condition;
}

/** Runs a SELECT against MovieRentalDB and returns rows as objects. */
export function sql(query) {
  query = query.replace(/(?<![\w\[])identity\./g, "[identity].");   // "identity" is a reserved word
  // One nvarchar(max) value holding the whole result as JSON; -y 0 prints it untruncated
  // under a header line and a dashes line, which are dropped.
  const out = execFileSync("sqlcmd", [
    "-S", "(localdb)\\MSSQLLocalDB", "-d", "MovieRentalDB", "-E", "-b", "-y", "0", "-f", "65001",
    "-Q", `SET NOCOUNT ON; SELECT ISNULL((${query} FOR JSON PATH, INCLUDE_NULL_VALUES), '[]') AS j`,
  ], { encoding: "utf8" });
  const lines = out.split(/\r?\n/);
  const start = lines.findIndex((l) => /^-+\s*$/.test(l));
  const text = lines.slice(start + 1).join("").trim();
  return text ? JSON.parse(text) : [];
}
export const one = (query) => sql(query)[0] ?? null;
export const q = (value) => `'${String(value).replace(/'/g, "''")}'`;

/** The newest six-digit code e-mailed (to the console log) to this address. */
export async function codeFor(email, since = 0) {
  for (let i = 0; i < 20; i++) {
    const log = readFileSync(LOG, "utf8").slice(since);
    const blocks = log.split(/E-MAIL to /).filter((b) => b.toLowerCase().startsWith(email.toLowerCase()));
    const last = blocks.at(-1);
    const code = last?.match(/(?<!\d)\d{6}(?!\d)/)?.[0];
    if (code) return code;
    await new Promise((r) => setTimeout(r, 300));
  }
  return null;
}
export const logSize = () => readFileSync(LOG, "utf8").length;

export async function login(email, password) {
  const r = await call("POST", "/api/auth/login", { body: { email, password } });
  return r.json ? { token: r.json.accessToken, refresh: r.json.refreshToken, user: r.json.user, status: r.status } : { status: r.status };
}

export const RANDOM_ID = "11111111-2222-3333-4444-555555555555";
export const later = (hours) => new Date(Date.now() + hours * 3600_000).toISOString();
