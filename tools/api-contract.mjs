// Contract check for the whole API, against a running site (Development, seeded accounts):
//   node tools/api-contract.mjs [https://localhost:7140] [site log] [path regex]
//
// Every operation in the OpenAPI document is called with real ids and with broken input: an
// empty body, null, each field null / of the wrong type / empty / far too long, ids that name
// nothing, query values that are not numbers or dates; as a guest, a customer, an admin without
// the staff PIN, and the role it is for. Each answer must be
//   - below 500,
//   - one of the codes the document lists for that operation,
//   - for an error, a body that says what went wrong ({ code, message } or a validation problem),
//   - for a 200, shaped the way the document says.
// It writes junk rows (films, screenings, bar items named "fuzz"), so it runs on the throwaway
// site of tools/api-check.ps1, never on the real one.
import { randomUUID } from "node:crypto";
import { readFileSync } from "node:fs";
import { execFileSync } from "node:child_process";

process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0"; // the local dev certificate
const BASE = process.argv[2] ?? "https://localhost:7140";
if (new URL(BASE).port === "7139") {
  console.error("Refusing to run against the real site (7139). Use: powershell -File tools\\api-check.ps1");
  process.exit(2);
}
const LOG = process.argv[3] ?? new URL("../logs/site-output.log", import.meta.url);
const only = process.argv[4] ? new RegExp(process.argv[4]) : null;   // e.g. "^/api/(auth|admin/users)"
const STAFF_PIN = process.env.STAFF_PIN ?? "580417";
const KEY = "/" + (process.env.STAFF_KEY ?? execFileSync("dotnet",
  ["user-secrets", "list", "--project", new URL("../src/MovieRental.Host", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1")],
  { encoding: "utf8" }).match(/Staff:PathKey = (\S+)/)?.[1]);

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
// A made-up client address per call (the site trusts loopback as a proxy), so the per-address
// limits on sign-in and codes do not stop the sweep half-way.
const X = () => ({ "X-Forwarded-For": `10.${(Math.random() * 255) | 0}.${(Math.random() * 255) | 0}.${(Math.random() * 255) | 0}` });
async function call(method, path, { token, body, cookie, headers: extra, raw } = {}) {
  const headers = { ...(extra ?? {}) };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (cookie) headers.Cookie = cookie;
  let payload = raw;
  if (raw === undefined && body !== undefined) { headers["Content-Type"] ??= "application/json"; payload = JSON.stringify(body); }
  const res = await fetch(BASE + path, { method, headers, body: payload, redirect: "manual", signal: AbortSignal.timeout(30000) });
  const text = await res.text();
  let json = null;
  try { json = JSON.parse(text); } catch { /* not JSON */ }
  return { status: res.status, json, text, headers: res.headers };
}
async function login(email, password) {
  const r = await call("POST", "/api/auth/login", { body: { email, password }, headers: X() });
  if (r.status !== 200) throw new Error(`login ${email}: ${r.status} ${r.text}`);
  return { token: r.json.accessToken, user: r.json.user };
}
async function registerAndConfirm(email, password = "Passw0rdTest") {
  let r = await call("POST", "/api/auth/register", { body: { fullName: "Contract Check", email, password }, headers: X() });
  if (r.status !== 200) throw new Error(`register ${r.status} ${r.text}`);
  await sleep(300);
  const log = readFileSync(LOG, "utf8");
  const code = [...log.slice(log.lastIndexOf(email)).matchAll(/<strong>(\d{6})<\/strong>/g)].at(0)?.[1];
  r = await call("POST", "/api/auth/verification/confirm", { body: { email, channel: "Email", code }, headers: X() });
  if (r.status !== 204) throw new Error(`confirm ${r.status} ${r.text}`);
  return login(email, password);
}
const ck = (r) => (r.headers.getSetCookie?.() ?? []);
async function unlock(u) {
  const page = await call("GET", KEY + "/unlock", { token: u.token, headers: X() });
  const form = /name="__RequestVerificationToken" type="hidden" value="([^"]+)"/.exec(page.text)?.[1];
  const r = await call("POST", KEY + "/unlock", { token: u.token, cookie: ck(page).map((c) => c.split(";")[0]).join("; "),
    headers: { ...X(), "Content-Type": "application/x-www-form-urlencoded" },
    raw: new URLSearchParams({ pin: STAFF_PIN, returnUrl: "", __RequestVerificationToken: form ?? "" }).toString() });
  u.staff = ck(r).find((c) => c.startsWith("wy.staff="))?.split(";")[0];
  if (!u.staff) throw new Error(`staff PIN refused for ${u.user.email}: ${r.status}`);
  return u;
}

const admin = await unlock(await login("admin@reelandrow.test", "Admin1234"));
const security = await unlock(await login("security@reelandrow.test", "Security1234"));
const customer = await login("customer@reelandrow.test", "Customer1234");

const stamp = Date.now().toString(36);
const fan = await registerAndConfirm(`contract-${stamp}@example.test`);
const target = await registerAndConfirm(`contract-target-${stamp}@example.test`);   // the admin user endpoints act on this one
const doomed = await registerAndConfirm(`contract-doomed-${stamp}@example.test`);   // account/delete runs as this one
const doc = (await call("GET", "/swagger/v1/swagger.json", { token: admin.token, cookie: admin.staff, headers: X() })).json;

// ---------------------------------------------------------------- real ids
const as = (u, extra = {}) => ({ token: u?.token, cookie: u?.staff, headers: X(), ...extra });
const get = async (u, path) => (await call("GET", path, as(u))).json;
const items = (j) => (Array.isArray(j) ? j : j?.items ?? []);
const movies = items(await get(fan, "/api/movies?pageSize=50"));
const screenings = items(await get(fan, "/api/screenings"));
const future = screenings.filter((s) => new Date(s.startsAtUtc) > new Date(Date.now() + 3 * 86400000));
const venues = items(await get(fan, "/api/venues"));
const bookings = items(await get(customer, "/api/bookings/mine"));
const rentals = items(await get(customer, "/api/rentals/mine"));
// A conversation of the fan's own, so message ids are theirs to edit.
await call("POST", `/api/messages/${security.user.id}`, as(fan, { body: { otherUserId: security.user.id, body: "fuzz hello" } }));
const thread = await get(fan, `/api/messages/${security.user.id}`);
const firstOf = (j, ...keys) => { for (const k of keys) { const v = items(j)[0]?.[k]; if (v) return v; } return undefined; };

// Records the sweep needs an id of, made for it: a confirmed booking of the fan's own, a film of
// theirs in each review queue, a report about them.
async function upload(u, title) {
  const form = new FormData();
  const bytes = new Uint8Array(64); bytes.set([0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6f, 0x6d]);
  form.append("file", new Blob([bytes], { type: "video/mp4" }), "fuzz.mp4");
  form.append("title", title); form.append("origin", "HandCrafted");
  const r = await call("POST", "/api/shorts", { token: u.token, headers: X(), raw: form });
  if (r.status !== 200) console.log("upload:", r.status, r.text.slice(0, 200));
  return r.json?.id;
}
const myShort = await upload(fan, "Fuzz pending");
const reviewed = await upload(fan, "Fuzz reviewed");
await call("POST", `/api/security/shorts/${reviewed}/claim`, as(security));
const checks = doc.components.schemas.SecurityCheck.enum.map((check) => ({ check, outcome: "Pass", note: null }));
let r0 = await call("POST", `/api/security/shorts/${reviewed}/report`, as(security, { body: { watchedInFull: true, summary: "fuzz", checks } }));
if (r0.status !== 204) console.log("security report:", r0.status, r0.text.slice(0, 200));
r0 = await call("POST", `/api/messages/${fan.user.id}/report`, as(target, { body: { reason: "fuzz" } }));
if (r0.status !== 204) console.log("dm report:", r0.status, r0.text.slice(0, 200));
async function book() {
  for (const show of future) {
    const map = await get(fan, `/api/screenings/${show.id}/seats`);
    const seat = map?.seats?.filter((s) => !s.isTaken).at(-1);
    if (!seat) continue;
    const card = { number: "4242424242424242", expiryMonth: 12, expiryYear: 2030, cvc: "123", holderName: "Fuzz" };
    const r = await call("POST", `/api/screenings/${show.id}/checkout`, as(fan, { body: { seats: [{ row: seat.row, number: seat.number }], card } }));
    if (r.status !== 200) { console.log("checkout:", r.status, r.text.slice(0, 160)); continue; }
    await sleep(300);
    const log = readFileSync(LOG, "utf8");
    const code = log.slice(log.lastIndexOf(r.json.reference)).match(/<strong>(\d{6})<\/strong>/)?.[1];
    const c = await call("POST", `/api/bookings/${r.json.paymentId}/confirm`, as(fan, { body: { code } }));
    if (c.status === 200) return r.json.paymentId;
    console.log("confirm:", c.status, c.text.slice(0, 160));
  }
}
const booking = await book();

const ids = {
  movie: movies[0]?.id, slug: movies[0]?.slug, screening: future[0]?.id ?? screenings[0]?.id, hall: venues[0]?.halls?.[0]?.id,
  booking: booking ?? bookings[0]?.paymentId, rental: rentals[0]?.id, message: items(thread?.messages ?? thread)[0]?.id,
  short: items(await get(fan, "/api/gallery/ai"))[0]?.film?.id, myShort,
  queue: reviewed,
  check: myShort,
  inbox: firstOf(await get(security, "/api/help/inbox"), "id"),
  report: firstOf(await get(security, "/api/reports"), "id"),
  promo: firstOf(await get(admin, "/api/admin/promos"), "id"),
  banner: firstOf(await get(admin, "/api/admin/banners"), "id"),
  ticketType: firstOf(await get(admin, "/api/backoffice/ticket-types"), "id"),
  barItem: firstOf(await get(admin, "/api/backoffice/bar/items"), "id"),
  deal: firstOf(await get(admin, "/api/backoffice/deals"), "id"),
};
console.log("ids:", Object.entries(ids).map(([k, v]) => `${k}=${v ? "yes" : "NONE"}`).join(" "));

function idFor(path, name) {
  const n = name.toLowerCase();
  if (n === "slug") return ids.slug;
  if (n === "file") return "0".repeat(32) + "-" + "0".repeat(32) + ".png";
  if (n === "username") return security.user.username ?? "security";
  if (/^(userid|otheruserid|followerid)$/.test(n)) return path.startsWith("/api/admin/users") ? target.user.id : security.user.id;
  if (n === "movieid") return ids.movie;
  if (n === "screeningid") return ids.screening;
  if (n === "hallid") return ids.hall;
  if (n === "paymentid") return ids.booking;
  if (n === "messageid") return ids.message;
  if (n === "itemid") return ids.barItem;
  if (n === "tickettypeid") return ids.ticketType;
  if (n === "conversationid") return ids.inbox;
  // "id": by where it sits
  const rules = [[/^\/api\/(admin\/)?movies\//, ids.movie], [/^\/api\/(admin\/)?screenings\//, ids.screening], [/^\/api\/halls\//, ids.hall],
    [/^\/api\/rentals\//, ids.rental], [/^\/api\/admin\/shorts\//, ids.queue], [/^\/api\/security\/shorts\//, ids.check], [/^\/api\/shorts\/\{id\}\/(visibility|comments)/, ids.myShort], [/^\/api\/shorts\//, ids.short],
    [/^\/api\/help\/inbox\//, ids.inbox], [/^\/api\/reports\//, ids.report], [/^\/api\/admin\/promos\//, ids.promo], [/^\/api\/admin\/banners\//, ids.banner],
    [/ticket-types\//, ids.ticketType], [/bar\/items\//, ids.barItem], [/deals\//, ids.deal], [/^\/api\/admin\/users\//, target.user.id],
    [/^\/api\/people\//, security.user.id]];
  for (const [re, v] of rules) if (re.test(path)) return v;
  return undefined;
}
const fill = (path, real = true) => path.replace(/\{(\w+)\}/g, (_, name) => (real ? idFor(path, name) : undefined) ?? randomUUID());

// ---------------------------------------------------------------- example bodies from the schema
const deref = (s) => (s?.$ref ? doc.components.schemas[s.$ref.split("/").pop()] : s) ?? {};
const typeOf = (s) => { s = deref(s); if (s.oneOf) return typeOf(s.oneOf.find((x) => deref(x).type !== "null")); return Array.isArray(s.type) ? s.type.find((t) => t !== "null") : s.type ?? (s.properties ? "object" : s.enum ? "string" : undefined); };
function example(schema, name, path, depth = 0) {
  let s = deref(schema);
  if (s.oneOf) s = deref(s.oneOf.find((x) => deref(x).type !== "null") ?? s.oneOf[0]);
  if (s.enum) return s.enum[0];
  const n = (name ?? "").toLowerCase();
  switch (typeOf(s)) {
    case "object": return depth > 4 ? {} : Object.fromEntries(Object.entries(s.properties ?? {}).map(([k, v]) => [k, example(v, k, path, depth + 1)]));
    case "array": return [example(s.items, name, path, depth + 1)];
    case "integer": case "number":
      return /month/.test(n) ? 12 : /year/.test(n) ? 2030 : /stars|rating/.test(n) ? 5 : /row|number|seat/.test(n) ? 1 : 1;
    case "boolean": return false;
    case "string":
      if (s.format === "uuid") return idFor(path, name) ?? randomUUID();
      if (s.format === "date-time") return new Date(Date.now() + 20 * 86400000).toISOString();
      if (s.format === "date") return new Date(Date.now() + 86400000).toISOString().slice(0, 10);
      if (/email/.test(n)) return fan.user.email;
      if (/password/.test(n)) return "WrongPass1";
      if (/^code$/.test(n)) return "123456";
      if (/pin/.test(n)) return "1234";
      if (/phone/.test(n)) return "+994501234567";
      if (/countrycode/.test(n)) return "AZ";
      if (/city/.test(n)) return "Baku";
      if (/language/.test(n)) return "az";
      if (/cvc/.test(n)) return "123";
      if (/^number$/.test(n)) return "4242424242424242";
      if (/url/.test(n)) return "https://example.com/x.jpg";
      return "fuzz";
    default: return null;
  }
}
const wrongType = (s) => ({ object: "x", array: "x", string: 12345, integer: "abc", number: "abc", boolean: "maybe" })[typeOf(s)] ?? 7;

function bodyVariants(op, path) {
  const schema = op.requestBody?.content?.["application/json"]?.schema;
  if (!schema) return [];
  const base = example(schema, null, path);
  const out = [["{}", {}], ["null", null], ["[]", []], ["example", base]];
  if (base && typeof base === "object" && !Array.isArray(base)) {
    const props = deref(schema).properties ?? {};
    for (const [k, v] of Object.entries(props)) {
      out.push([`${k}=null`, { ...base, [k]: null }], [`${k}=wrong type`, { ...base, [k]: wrongType(v) }]);
      const t = typeOf(v);
      if (t === "string" && deref(v).format !== "uuid" && !deref(v).enum) out.push([`${k}=""`, { ...base, [k]: "" }], [`${k}=long`, { ...base, [k]: "y".repeat(5000) }]);
      if (t === "string" && deref(v).format === "uuid") out.push([`${k}=unknown id`, { ...base, [k]: randomUUID() }], [`${k}=empty id`, { ...base, [k]: "00000000-0000-0000-0000-000000000000" }]);
      if (t === "string" && deref(v).enum) out.push([`${k}=not a value`, { ...base, [k]: "Nope" }]);
      if (t === "integer" || t === "number") out.push([`${k}=-1`, { ...base, [k]: -1 }], [`${k}=huge`, { ...base, [k]: 1e15 }], [`${k}=0`, { ...base, [k]: 0 }]);
      if (t === "array") out.push([`${k}=[]`, { ...base, [k]: [] }], [`${k}=[null]`, { ...base, [k]: [null] }]);
    }
  }
  return out;
}
function queryVariants(op) {
  const out = [];
  for (const p of (op.parameters ?? []).filter((x) => x.in === "query")) {
    const t = typeOf(p.schema);
    for (const v of t === "string" && deref(p.schema).format !== "date-time" && deref(p.schema).format !== "date" ? ["", "y".repeat(3000)] : ["abc", "-1", "99999999999", ""])
      out.push([`?${p.name}=${v.slice(0, 12)}`, `${p.name}=${encodeURIComponent(v)}`]);
  }
  return out;
}

// ---------------------------------------------------------------- the checks
const problems = new Map();   // one line per operation and kind of problem
const counts = { calls: 0, ops: 0 };
function problem(key, kind, detail) { const k = `${key} | ${kind}`; if (!problems.has(k)) problems.set(k, detail); }
function judge(key, op, variant, r) {
  counts.calls++;
  const documented = Object.keys(op.responses ?? {});
  const where = `[${variant}] -> ${r.status} ${r.text.slice(0, 160)}`;
  if (r.status >= 500) problem(key, "5xx", where);
  else if (!documented.includes(String(r.status))) problem(key, `undocumented ${r.status}`, where);
  if (r.status >= 400) {
    const j = r.json;
    if (!j || !((j.code && j.message) || (j.title && j.status))) problem(key, `error ${r.status} without a body`, where);
  }
  if (r.status === 200 && op.responses?.["200"]?.content?.["application/json"]?.schema) {
    const s = deref(op.responses["200"].content["application/json"].schema);
    const t = typeOf(s);
    if (t === "array" && !Array.isArray(r.json)) problem(key, "200 is not the documented array", where);
    const nullSaid = r.json === null && /`null`/.test(op.description ?? "");   // e.g. no help conversation yet
    if (t === "object" && !nullSaid && (r.json === null || typeof r.json !== "object" || Array.isArray(r.json))) problem(key, "200 is not the documented object", where);
    if (t === "object" && r.json && s.required) for (const field of s.required) if (!(field in r.json)) problem(key, `200 lacks "${field}"`, where);
  }
}
async function send(method, url, who, body) {
  for (let attempt = 0; ; attempt++) {
    const r = await call(method, url, body === undefined ? as(who) : as(who, { raw: JSON.stringify(body), headers: { ...X(), "Content-Type": "application/json" } }));
    if (r.status !== 429 || attempt > 3) return r;
    await sleep((Number(r.headers.get("retry-after")) || 20) * 1000 + 500);
  }
}

const staffOnly = (p) => /^\/api\/(admin|backoffice|security|reports)(\/|$)/.test(p) || p.startsWith("/api/help/inbox") || p === "/api/pro/subscribers";
const skip = new Set(["POST /api/admin/movies/seed", "POST /api/auth/avatar", "POST /api/shorts", "PUT /api/admin/users/{id}/staff-pin"]);

for (const [path, ops] of Object.entries(doc.paths)) {
  if (only && !only.test(path)) continue;
  for (const [m, op] of Object.entries(ops)) {
    const method = m.toUpperCase(), key = `${method} ${path}`;
    if (!op?.responses || skip.has(key)) continue;
    counts.ops++;
    const who = key === "POST /api/auth/account/delete" ? doomed : staffOnly(path) ? admin : fan;

    // who may call it: a guest, and for staff calls a customer and an admin without the PIN
    judge(key, op, "guest", await send(method, fill(path, false), null, method === "GET" || method === "DELETE" ? undefined : {}));
    if (staffOnly(path)) {
      judge(key, op, "customer", await send(method, fill(path, false), fan, method === "GET" || method === "DELETE" ? undefined : {}));
      judge(key, op, "admin without PIN", await send(method, fill(path, false), { token: admin.token }, method === "GET" || method === "DELETE" ? undefined : {}));
    }
    // ids that are unknown, and not ids at all
    if (path.includes("{")) {
      judge(key, op, "unknown id", await send(method, fill(path, false), who, method === "GET" || method === "DELETE" ? undefined : example(op.requestBody?.content?.["application/json"]?.schema, null, path) ?? undefined));
      judge(key, op, "not an id", await send(method, path.replace(/\{\w+\}/g, "not-an-id"), who, method === "GET" || method === "DELETE" ? undefined : {}));
    }
    if (method === "DELETE") continue;   // real deletes are left to the flows
    const url = fill(path);
    if (method === "GET") {
      judge(key, op, "real", await send(method, url, who));
      for (const [label, q] of queryVariants(op)) judge(key, op, label, await send(method, `${url}?${q}`, who));
    } else {
      if (!op.requestBody) judge(key, op, "no body", await send(method, url, who));
      for (const [label, body] of bodyVariants(op, path)) judge(key, op, label, await send(method, url, who, body));
    }
  }
}

for (const [k, v] of problems) console.log(`FAIL ${k}\n      ${v}`);
console.log(`\n${counts.ops} operations, ${counts.calls} calls, ${problems.size} problems.`);
if (problems.size) process.exit(1);
