// Exercises the whole API against a running site (Development, seeded accounts):
//   node tools/api-check.mjs [https://localhost:7139]
//
// 1. Sweep: every operation in the OpenAPI document, as anonymous and as the role that owns
//    it, with real ids where they exist and an empty body. Nothing may answer 5xx.
// 2. Flows: the things people actually do, end to end, each step must succeed.
// Writes real rows into the dev database (a rental, a booking + refund, a message, ...).
import { readFileSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { execFileSync } from "node:child_process";
import { logins } from "./test-logins.mjs";   // this machine's test passwords, outside the repository
import { staffPath } from "./staff-paths.mjs";   // the staff screens' addresses

process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0"; // the local dev certificate
const BASE = process.argv[2] ?? "https://localhost:7140";
// The real site's database holds real people's messages and bookings; the check's test rows
// showed up there once. Run it through tools/api-check.ps1, which uses a throwaway database.
if (new URL(BASE).port === "7139" && !process.argv.includes("--yes-the-real-site")) {
  console.error("Refusing to run against the real site (7139). Use: powershell -File tools\\api-check.ps1");
  process.exit(2);
}
// Where the Console mail sender writes; the card checkout's code is read from it.
const LOG = process.argv[3] ?? new URL("../logs/site-output.log", import.meta.url);
const failures = [];
let checks = 0;

// Staff screens and APIs also want the staff PIN (StaffGate). The seeded staff share the
// development PIN; the secret path comes from the host's user secrets.
const STAFF_PIN = process.env.STAFF_PIN ?? logins.staffPin;
const STAFF_UNLOCK = staffPath("unlock");
const staffCookies = new Map();   // access token -> its wy.staff cookie

async function call(method, path, { token, body, cookie, headers: extra } = {}) {
  const headers = { ...extra };
  if (token) headers.Authorization = `Bearer ${token}`;
  const jar = [cookie, staffCookies.get(token)].filter(Boolean).join("; ");
  if (jar) headers.Cookie = jar;
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const res = await fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), redirect: "manual" });
  const text = await res.text();
  let json = null;
  try { json = JSON.parse(text); } catch { /* not JSON */ }
  return { status: res.status, json, text, headers: res.headers };
}

function expect(label, ok, detail = "") {
  checks++;
  if (!ok) failures.push(`${label} ${detail}`.trim());
  console.log(`${ok ? "ok  " : "FAIL"} ${label}${ok ? "" : "  " + detail}`);
}

async function login(email, password) {
  const r = await call("POST", "/api/auth/login", { body: { email, password } });
  if (r.status !== 200) throw new Error(`login ${email}: ${r.status} ${r.text}`);
  const session = { token: r.json.accessToken, refresh: r.json.refreshToken, user: r.json.user, cookie: r.headers.get("set-cookie")?.split(";")[0] };
  if (r.json.user.roles.some((role) => ["SuperAdmin", "Admin", "Security", "Cashier", "Moderator"].includes(role))) await unlockStaff(session);
  return session;
}

async function unlockStaff(session) {
  const page = await call("GET", STAFF_UNLOCK, { token: session.token });
  const form = /name="__RequestVerificationToken" type="hidden" value="([^"]+)"/.exec(page.text)?.[1];
  const res = await fetch(BASE + STAFF_UNLOCK, {
    method: "POST", redirect: "manual",
    headers: { Authorization: `Bearer ${session.token}`, "Content-Type": "application/x-www-form-urlencoded",
               Cookie: page.headers.getSetCookie().map((c) => c.split(";")[0]).join("; ") },
    body: new URLSearchParams({ pin: STAFF_PIN, returnUrl: "", __RequestVerificationToken: form ?? "" }),
  });
  const staff = res.headers.getSetCookie().map((c) => c.split(";")[0]).find((c) => c.startsWith("wy.staff="));
  if (!staff) throw new Error(`staff PIN refused for ${session.user.email}: ${res.status}`);
  staffCookies.set(session.token, staff);
}

const admin = await login("admin@reelandrow.test", logins.admin);
const security = await login("security@reelandrow.test", logins.security);
const customer = await login("customer@reelandrow.test", logins.customer);
const cashier = await login("kassa@reelandrow.test", logins.kassa);

// ---------------------------------------------------------------- real ids for path params
const movies = (await call("GET", "/api/movies?pageSize=50")).json.items;
const screenings = (await call("GET", "/api/screenings")).json;
const venues = (await call("GET", "/api/venues")).json;
const users = (await call("GET", "/api/admin/users", { token: admin.token })).json;
const userList = users.items ?? users;
const movie = movies[0];
const ids = {
  query: { "/api/globe/members": "?city=Baku", "/api/globe/online": "?country=AZ", "/api/globe/online/members": "?country=AZ",
           "/api/people/username-available": "?username=sweep_check" },
  movie: movie.id, movieId: movie.id, slug: movie.slug,
  screening: screenings[0].id, hall: venues[0].halls[0].id,
  user: customer.user.id, userId: security.user.id,
};

function fill(path) {
  return path.replace(/\{(\w+)\}/g, (_, name) => {
    if (name === "slug") return ids.slug;
    if (name === "paymentId") return randomUUID();
    if (name === "userId") return ids.userId;
    if (name === "movieId") return ids.movieId;
    if (path.startsWith("/api/movies/") || path.startsWith("/api/admin/movies/")) return ids.movie;
    if (path.startsWith("/api/screenings/")) return ids.screening;
    if (path.startsWith("/api/halls/")) return ids.hall;
    return randomUUID(); // unknown or destructive: a fresh id must give 404, never 500
  });
}

function ownerOf(path) {
  if (path.startsWith("/api/backoffice")) return cashier;
  if (path.startsWith("/api/security") || path.startsWith("/api/reports") || path.startsWith("/api/help/inbox")) return security;
  if (path.startsWith("/api/admin")) return admin;
  return customer;
}

// ---------------------------------------------------------------- 1. sweep
const doc = (await call("GET", "/swagger/v1/swagger.json", { token: admin.token, cookie: admin.cookie })).json;
expect("openapi document", !!doc?.paths, "admin could not read /swagger/v1/swagger.json");

// Side effects nobody wants from an empty-body sweep: re-seeding, shifts, sessions, accounts.
const skip = new Set(["POST /api/admin/movies/seed", "POST /api/backoffice/shift/open", "POST /api/backoffice/shift/close",
  "POST /api/auth/logout", "POST /api/auth/account/delete", "POST /api/auth/register", "POST /api/auth/login",
  "POST /api/auth/verification/send", "POST /api/auth/password/forgot", "POST /api/auth/password/reset",
  "POST /api/auth/verification/confirm", "POST /api/auth/refresh",
  // SearchingYou is exercised as a scenario of its own; three more writes here spent the minute's
  // write allowance the follow-request checks after the sweep need.
  "POST /api/searching", "POST /api/searching/{id}/join", "DELETE /api/searching/{id}/join", "DELETE /api/searching/{id}"]);

for (const [path, ops] of Object.entries(doc.paths)) {
  for (const method of Object.keys(ops)) {
    const key = `${method.toUpperCase()} ${path}`;
    if (skip.has(key)) continue;
    const url = fill(path) + (ids.query[path] ?? "");
    // Mutations never touch a real record in the sweep: their ids are always fresh.
    const target = method === "get" ? url : path.replace(/\{\w+\}/g, () => randomUUID());
    const anon = await call(method.toUpperCase(), target, method === "get" ? {} : { body: {} });
    expect(`sweep anon  ${key}`, anon.status < 500, `-> ${anon.status} ${anon.text.slice(0, 160)}`);
    const owner = ownerOf(path);
    let authed = await call(method.toUpperCase(), target, method === "get" ? { token: owner.token } : { token: owner.token, body: {} });
    // Manager-only routes inside a staff area (reports, shift history, PRO list): admin owns them.
    if (authed.status === 403) authed = await call(method.toUpperCase(), target, method === "get" ? { token: admin.token } : { token: admin.token, body: {} });
    expect(`sweep owner ${key}`, authed.status < 500, `-> ${authed.status} ${authed.text.slice(0, 160)}`);
    if (method === "get" && !path.includes("{"))
      // 302 counts: /api/avatars/me answers with a redirect to the picture by design.
      expect(`get 2xx     ${key}`, authed.status >= 200 && authed.status < 400, `-> ${authed.status} ${authed.text.slice(0, 160)}`);
  }
}

// ---------------------------------------------------------------- 2. flows
const c = customer.token;
const ok = (r) => r.status >= 200 && r.status < 300;

// Catalogue and rentals
let r = await call("GET", `/api/movies/${movie.id}`); expect("movie detail", ok(r), `${r.status}`);
// The cast, with photos served by the site itself
const famous = (await call("GET", "/api/movies?search=Shawshank")).json?.items?.[0];
r = famous ? await call("GET", `/api/movies/${famous.id}`) : { status: 0, json: null };
const star = r.json?.cast?.find((a) => a.photoUrl);
expect("a film's detail lists its cast", ok(r) && r.json.cast?.length > 0 && !!star, `${r.status} ${JSON.stringify(r.json?.cast)?.slice(0, 100)}`);
if (star) { const photo = await call("GET", star.photoUrl); expect("an actor's photo is served", photo.status === 200, `${photo.status}`); }
r = await call("GET", "/api/cast/..%2F..%2Fappsettings.json"); expect("the photo endpoint gives out no other file", r.status === 404, `${r.status}`);
r = await call("PUT", `/api/watchlist/${movie.id}`, { token: c }); expect("watchlist add", ok(r), `${r.status} ${r.text}`);
r = await call("GET", "/api/watchlist/ids", { token: c }); expect("watchlist contains it", ok(r) && JSON.stringify(r.json).includes(movie.id), r.text.slice(0, 120));
r = await call("DELETE", `/api/watchlist/${movie.id}`, { token: c }); expect("watchlist remove", ok(r), `${r.status}`);
const rentable = movies.find((m) => m.availableCopies > 0) ?? movie;
r = await call("POST", "/api/rentals", { token: c, body: { movieId: rentable.id, days: 3, card: { number: "4242424242424242", expiryMonth: 12, expiryYear: 2030, cvc: "123", holderName: "Api Check" } } });
expect("rent a movie (or already rented)", ok(r) || r.status === 409, `${r.status} ${r.text.slice(0, 160)}`);
r = await call("GET", "/api/rentals/mine", { token: c }); expect("my rentals", ok(r), `${r.status}`);
r = await call("POST", `/api/movies/${movie.id}/reviews`, { token: c, body: { movieId: movie.id, stars: 5, comment: "API check review" } });
expect("review (needs a rental, so 400/409 allowed)", r.status < 500, `${r.status} ${r.text.slice(0, 160)}`);

// A run that stopped half-way leaves an open checkout; one per show is allowed, so clear them.
for (const p of (await call("GET", "/api/bookings/pending", { token: c })).json ?? [])
  await call("POST", `/api/bookings/${p.paymentId}/cancel`, { token: c });

// Cinema: seat map -> quote -> card checkout -> code from the log -> confirm -> ticket pdf -> refund
// Three days out: refunds close 48 hours before the start (RefundPolicy.DefaultWindowHours).
const show = screenings.find((s) => s.capacity - s.seatsTaken > 2 && new Date(s.startsAtUtc) > new Date(Date.now() + 72 * 3600000));
r = await call("GET", `/api/screenings/${show.id}/seats`); expect("seat map (anonymous)", ok(r), `${r.status}`);
const free = r.json.seats.filter((s) => !s.isTaken).slice(-1)[0];
r = await call("POST", `/api/screenings/${show.id}/quote`, { token: c, body: { seats: 1, promoCode: "WELCOME10", usePoints: false } });
expect("price quote with promo", ok(r) && r.json.promoApplied, `${r.status} ${r.text.slice(0, 160)}`);
r = await call("POST", `/api/screenings/${show.id}/checkout`, {
  token: c,
  body: { screeningId: show.id, seats: [{ row: free.row, number: free.number }], card: { number: "4242424242424242", expiryMonth: 12, expiryYear: 2030, cvc: "123", holderName: "Api Check" }, promoCode: null, usePoints: false },
});
expect("card checkout holds the seat", ok(r), `${r.status} ${r.text.slice(0, 200)}`);
if (ok(r)) {
  const { paymentId, reference } = r.json;
  // The Console mail sender logs the e-mail; the code sits in <strong> right after the reference.
  const log = readFileSync(LOG, "utf8");
  const mail = log.slice(log.lastIndexOf(reference));
  const code = mail.match(/<strong>(\d{6})<\/strong>/)?.[1];
  expect("confirmation code found in the dev mail log", !!code, "no code after " + reference);
  r = await call("GET", `/api/screenings/${show.id}/seats`, { token: c });
  expect("held seat shows as taken", r.json.seats.some((s) => s.row === free.row && s.number === free.number && s.isTaken), "");
  r = await call("POST", `/api/screenings/${show.id}/checkout`, { token: c, body: { screeningId: show.id, seats: [{ row: 1, number: 1 }], card: { number: "4242424242424242", expiryMonth: 12, expiryYear: 2030, cvc: "123", holderName: "Api Check" }, usePoints: false } });
  expect("second open checkout on same show refused", r.status === 409, `${r.status}`);
  r = await call("POST", `/api/bookings/${paymentId}/confirm`, { token: c, body: { paymentId, code: "000000" === code ? "111111" : "000000" } });
  expect("wrong code rejected", r.status === 400, `${r.status}`);
  r = await call("POST", `/api/bookings/${paymentId}/confirm`, { token: c, body: { paymentId, code } });
  expect("right code confirms the booking", ok(r), `${r.status} ${r.text.slice(0, 160)}`);
  const [again1, again2] = await Promise.all([
    call("POST", `/api/bookings/${paymentId}/confirm`, { token: c, body: { paymentId, code } }),
    call("POST", `/api/bookings/${paymentId}/confirm`, { token: c, body: { paymentId, code } }),
  ]);
  expect("repeat confirms are harmless", ok(again1) && ok(again2), `${again1.status}/${again2.status}`);
  r = await call("GET", `/api/bookings/${paymentId}/ticket.pdf`, { token: c });
  expect("ticket PDF", ok(r) && r.headers.get("content-type")?.includes("pdf"), `${r.status}`);
  r = await call("GET", `/api/bookings/${paymentId}/ticket.pdf`, { token: security.token });
  expect("someone else's ticket PDF is hidden", r.status === 404, `${r.status}`);
  r = await call("GET", `/api/bookings/${paymentId}/refund-terms`, { token: c }); expect("refund terms", ok(r), `${r.status}`);
  const [ref1, ref2] = await Promise.all([
    call("POST", `/api/bookings/${paymentId}/refund`, { token: c, body: { paymentId, reason: "api check" } }),
    call("POST", `/api/bookings/${paymentId}/refund`, { token: c, body: { paymentId, reason: "api check" } }),
  ]);
  expect("parallel refunds: exactly one succeeds", [ref1, ref2].filter(ok).length === 1, `${ref1.status}/${ref2.status} ${ref1.text.slice(0, 160)}`);
}
r = await call("GET", "/api/loyalty", { token: c }); expect("loyalty", ok(r), `${r.status}`);

// Messages between two people on the globe
await call("PUT", "/api/globe/presence", { token: security.token, body: { shareOnGlobe: true, city: "Baku", countryCode: "AZ", latitude: 40.4, longitude: 49.86 } });
r = await call("POST", `/api/messages/${security.user.id}`, { token: c, body: { otherUserId: security.user.id, body: "Salam from the API check" } });
expect("send a direct message", ok(r), `${r.status} ${r.text.slice(0, 160)}`);
r = await call("GET", `/api/messages/${customer.user.id}`, { token: security.token });
expect("recipient reads it", ok(r) && r.text.includes("Salam from the API check"), `${r.status}`);
r = await call("GET", "/api/messages/overview", { token: security.token }); expect("message overview", ok(r), `${r.status}`);

// Help desk
r = await call("POST", "/api/help/chat", { token: c, body: { body: "API check: help please" } }); expect("customer writes to help", ok(r), `${r.status} ${r.text.slice(0, 120)}`);
r = await call("GET", "/api/help/inbox", { token: security.token }); expect("desk inbox", ok(r), `${r.status}`);
const convo = (r.json?.items ?? r.json ?? []).find?.((x) => JSON.stringify(x).includes(customer.user.email) || JSON.stringify(x).includes(customer.user.fullName));
if (convo) {
  r = await call("POST", `/api/help/inbox/${convo.id}`, { token: security.token, body: { conversationId: convo.id, body: "Desk reply from API check" } });
  expect("desk replies", ok(r), `${r.status} ${r.text.slice(0, 120)}`);
  r = await call("GET", "/api/help/chat", { token: c }); expect("customer sees the reply", ok(r) && r.text.includes("Desk reply from API check"), `${r.status}`);
} else expect("customer's conversation in the inbox", false, "not found");

// Admin: schedule a screening, edit it, cancel it
const startsAt = new Date(Date.now() + 9 * 86400000).toISOString();
r = await call("POST", "/api/admin/screenings", { token: admin.token, body: { movieId: movie.id, hallId: ids.hall, startsAtUtc: startsAt, seatPrice: 9, audioLanguage: "az" } });
expect("admin creates a screening", ok(r), `${r.status} ${r.text.slice(0, 160)}`);
if (ok(r)) {
  const sid = r.json.id;
  r = await call("PUT", `/api/admin/screenings/${sid}`, { token: admin.token, body: { id: sid, movieId: movie.id, hallId: ids.hall, startsAtUtc: startsAt, seatPrice: 11, audioLanguage: "en", subtitleLanguage: "az" } });
  expect("admin edits it", ok(r) && r.json.seatPrice === 11, `${r.status} ${r.text.slice(0, 160)}`);
  r = await call("POST", `/api/admin/screenings/${sid}/cancel`, { token: admin.token, body: { id: sid, reason: "API check" } });
  expect("admin cancels it", ok(r), `${r.status} ${r.text.slice(0, 160)}`);
}
r = await call("GET", "/api/admin/screenings", { token: customer.token }); expect("customer cannot use admin API", r.status === 403, `${r.status}`);

// Back office: shift, box office sale, bar sale, close
await call("POST", "/api/backoffice/shift/close", { token: cashier.token, body: { countedCash: 0, note: "api check reset" } });
r = await call("POST", "/api/backoffice/shift/open", { token: cashier.token, body: { openingFloat: 100 } }); expect("cashier opens a shift", ok(r), `${r.status} ${r.text.slice(0, 160)}`);
const boxShows = screenings.filter((s) => s.id !== show.id && new Date(s.startsAtUtc) > new Date(Date.now() + 3600000));
const types = (await call("GET", "/api/backoffice/ticket-types", { token: cashier.token })).json;
const boxShow = (boxShows.items ?? boxShows).find?.((s) => s.capacity - (s.seatsTaken ?? s.sold ?? 0) > 1) ?? (boxShows.items ?? boxShows)[0];
if (boxShow) {
  const map = (await call("GET", `/api/screenings/${boxShow.id ?? boxShow.screeningId}/seats`)).json;
  const seat = map.seats.filter((s) => !s.isTaken)[0];
  r = await call("POST", "/api/backoffice/box-office/sell", { token: cashier.token, body: { screeningId: map.screeningId, seats: [{ row: seat.row, number: seat.number, ticketTypeId: (types.items ?? types)[0].id }], tender: "Cash", cashReceived: 50 } });
  expect("box office sells a ticket for cash", ok(r), `${r.status} ${r.text.slice(0, 200)}`);
} else expect("box office has a show to sell", false, "none listed");
const bar = (await call("GET", "/api/backoffice/bar/items", { token: cashier.token })).json;
const item = (bar.items ?? bar).find((i) => i.stock > 2);
r = await call("POST", "/api/backoffice/bar/sell", { token: cashier.token, body: { lines: [{ itemId: item.id, quantity: 1 }], tender: "Card" } });
expect("bar sale", ok(r), `${r.status} ${r.text.slice(0, 160)}`);
r = await call("POST", "/api/backoffice/shift/close", { token: cashier.token, body: { countedCash: 100, note: "api check" } });
expect("cashier closes the shift", ok(r), `${r.status} ${r.text.slice(0, 160)}`);

// Following, and the list behind the count
await call("POST", `/api/people/${security.user.id}/follow`, { token: c });
r = await call("GET", `/api/people/${security.user.id}/followers`, { token: security.token });
expect("followers list shows the follower", ok(r) && r.json.items.some((p) => p.id === customer.user.id), `${r.status} ${r.text.slice(0, 120)}`);
r = await call("GET", `/api/people/${customer.user.id}/following`, { token: c });
expect("following list shows the followee", ok(r) && r.json.items.some((p) => p.id === security.user.id && p.isFollowing), `${r.status}`);
r = await call("DELETE", `/api/people/${security.user.id}/follow`, { token: c }); expect("unfollow", ok(r), `${r.status}`);

// Private account: following asks, the owner accepts or declines
const sec = security.user;
r = await call("PUT", "/api/auth/profile", { token: security.token, body: { fullName: sec.fullName, isPrivate: true } });
expect("account goes private", ok(r) && r.json.isPrivate === true, `${r.status} ${r.text.slice(0, 120)}`);
r = await call("POST", `/api/people/${sec.id}/follow`, { token: c });
expect("following a private account sends a request", ok(r) && r.json.status === "requested", `${r.status} ${r.text}`);
r = await call("GET", `/api/people/${sec.id}/followers`, { token: c });
expect("a private account's lists are hidden from a requester", r.status === 403, `${r.status}`);
r = await call("GET", "/api/people/me", { token: security.token });
expect("owner sees one request waiting", ok(r) && r.json.requests >= 1, r.text);
r = await call("GET", "/api/people/requests", { token: security.token });
expect("request listed for the owner", ok(r) && r.json.items.some((p) => p.id === customer.user.id), `${r.status}`);
r = await call("POST", `/api/people/requests/${customer.user.id}/accept`, { token: security.token });
expect("accept answers with a follow-back card", ok(r) && r.json.followsMe === true && r.json.isFollowing === false, r.text.slice(0, 160));
r = await call("GET", `/api/people/${sec.id}/followers`, { token: c });
expect("accepted follower may see the lists", ok(r) && r.json.items.some((p) => p.id === customer.user.id), `${r.status}`);
await call("DELETE", `/api/people/${sec.id}/follow`, { token: c });
await call("POST", `/api/people/${sec.id}/follow`, { token: c });
r = await call("DELETE", `/api/people/requests/${customer.user.id}`, { token: security.token });
expect("decline removes the request", ok(r), `${r.status}`);
r = await call("GET", "/api/people/requests", { token: security.token });
expect("declined request is gone", ok(r) && !r.json.items.some((p) => p.id === customer.user.id), r.text.slice(0, 120));
await call("PUT", "/api/auth/profile", { token: security.token, body: { fullName: sec.fullName, isPrivate: false } });

// People search from the first letter; a @username belongs to one person, whatever the case
r = await call("GET", "/api/people?q=a", { token: c });
expect("people search answers from one letter", ok(r) && Array.isArray(r.json.items), `${r.status}`);
r = await call("GET", "/api/people?q=%20", { token: c });
expect("a blank people search lists nobody", ok(r) && r.json.items.length === 0, r.text.slice(0, 80));
const handle = "check_" + Date.now().toString(36);
r = await call("GET", `/api/people/username-available?username=${handle}`, { token: c });
expect("an unused username is free", ok(r) && r.json.available === true, r.text);
r = await call("PUT", "/api/auth/profile", { token: c, body: { fullName: customer.user.fullName, username: handle } });
expect("customer takes it", ok(r) && r.json.username === handle, `${r.status} ${r.text.slice(0, 120)}`);
r = await call("GET", `/api/people/username-available?username=${handle.toUpperCase()}`, { token: security.token });
expect("then it is taken for everyone else, in any case", ok(r) && r.json.available === false, r.text);
r = await call("PUT", "/api/auth/profile", { token: security.token, body: { fullName: sec.fullName, username: handle.toUpperCase() } });
expect("saving someone's username is a 409 that says so", r.status === 409 && r.json?.message === "That username is taken.", `${r.status} ${r.text.slice(0, 120)}`);
r = await call("GET", "/api/people/username-available?username=a", { token: c });
expect("a malformed username is a 400", r.status === 400 && !!r.json?.message, `${r.status}`);

// The picture: a flag, then removed (the grey figure stays, and removing again changes nothing)
r = await call("PUT", "/api/auth/avatar/flag/AZ", { token: c });
expect("a flag becomes the picture", ok(r) && r.json.avatarUrl === "/app/flags/az.svg", `${r.status} ${r.text.slice(0, 120)}`);
r = await call("PUT", "/api/auth/avatar/flag/zz", { token: c });
expect("a country without a flag is a 400", r.status === 400 && !!r.json?.message, `${r.status}`);
r = await call("PUT", "/api/auth/avatar/flag/..%2F..%2Fweb.config", { token: c });
expect("a path for a country is refused", r.status === 400 || r.status === 404, `${r.status}`);
r = await call("DELETE", "/api/auth/avatar", { token: c });
expect("removing leaves the grey figure, not the eye", ok(r) && r.json.avatarUrl === "/icons/avatar-blank.svg", `${r.status} ${r.text.slice(0, 120)}`);
r = await call("DELETE", "/api/auth/avatar", { token: c });
expect("removing again keeps the grey figure", ok(r) && r.json.avatarUrl === "/icons/avatar-blank.svg", `${r.status}`);

// A short film's views: every play counts, fetching the file does not
const upload = new FormData();
upload.append("file", new Blob([Uint8Array.from([0, 0, 0, 0x18, ...Buffer.from("ftypisom"), ...new Array(52).fill(0)])], { type: "video/mp4" }), "check.mp4");
upload.append("title", "API check film"); upload.append("origin", "HandCrafted"); upload.append("visibility", "Public");
// The customer has written a lot by now: a write that meets the per-minute limit waits as long
// as Retry-After says, once.
const patient = async (send) => {
  let res = await send();
  if (res.status === 429) { await new Promise((done) => setTimeout(done, (Number(res.headers.get("retry-after")) || 60) * 1000)); res = await send(); }
  return res;
};
const uploaded = await patient(() => fetch(BASE + "/api/shorts", { method: "POST", headers: { Authorization: `Bearer ${c}` }, body: upload }));
const shortFilm = uploaded.ok ? await uploaded.json() : null;
expect("upload a short film", !!shortFilm?.id, `${uploaded.status}`);
if (shortFilm) {
  const play = (token) => patient(() => call("POST", `/api/shorts/${shortFilm.id}/view`, { token, body: {} }));
  r = await play(cashier.token);
  expect("a film still in review has no views to count (404)", r.status === 404, `${r.status}`);
  await call("POST", `/api/security/shorts/${shortFilm.id}/claim`, { token: security.token });
  r = await call("POST", `/api/security/shorts/${shortFilm.id}/report`, { token: security.token, body: { watchedInFull: true, summary: "api check",
    checks: ["Copyright", "SexualContent", "GraphicViolence", "MinorsWithoutConsent", "HateOrExtremism", "PersonalData", "FileIntegrity", "OriginDeclaration"].map((check) => ({ check, outcome: "Pass", note: null })) } });
  expect("security files the report", ok(r), `${r.status} ${r.text.slice(0, 160)}`);
  r = await call("PUT", `/api/admin/shorts/${shortFilm.id}/decision`, { token: admin.token, body: { approve: true, note: "api check" } });
  expect("admin approves the film", ok(r), `${r.status} ${r.text.slice(0, 160)}`);
  const views = [];
  for (const token of [cashier.token, cashier.token, admin.token]) views.push((await play(token)).json?.views);
  expect("every play is a view, the same person's second one too", views.join() === "1,2,3", views.join());
  await (await fetch(BASE + `/api/shorts/${shortFilm.id}/stream`, { headers: { Range: "bytes=0-" } })).arrayBuffer();
  r = await call("GET", "/api/gallery/human");
  const card = r.json?.find((x) => x.film.id === shortFilm.id);
  expect("fetching the file is not a view; the gallery shows 3", card?.film.viewCount === 3, `${card?.film.viewCount}`);
}

// A tab whose refresh token is gone but whose page cookie is good gets a session back
r = await call("POST", "/api/auth/session", { cookie: customer.cookie });
expect("session from the page cookie", ok(r) && r.json.user.id === customer.user.id, `${r.status}`);
r = await call("POST", "/api/auth/session");
expect("no cookie, no session", r.status === 401, `${r.status}`);

// Session: refresh rotates, logout revokes
r = await call("POST", "/api/auth/refresh", { body: { refreshToken: customer.refresh } }); expect("refresh token rotates", ok(r), `${r.status}`);
const rotated = r.json?.refreshToken;
r = await call("POST", "/api/auth/logout", { body: { refreshToken: rotated } }); expect("logout", ok(r), `${r.status}`);
r = await call("POST", "/api/auth/refresh", { body: { refreshToken: rotated } }); expect("logged-out token is dead", r.status === 401, `${r.status}`);

// Edge cases: answers that were a 500, or wrong, when a value sat at the edge or two requests
// raced. The made-up client address keeps the per-address sign-up limit out of the way.
const elsewhere = () => ({ "X-Forwarded-For": `10.77.${(Math.random() * 255) | 0}.${(Math.random() * 255) | 0}` });

// Sign-in and the reset form say plainly when an e-mail has no account; codes go to registered ones only
const nobody = { email: "nobody-here@reelandrow.test", password: "Whatever123" };
r = await call("POST", "/api/auth/login", { body: nobody, headers: elsewhere() });
expect("sign-in, unknown e-mail: 404 account_not_found", r.status === 404 && r.json?.code === "account_not_found", `${r.status} ${r.text.slice(0, 120)}`);
r = await call("POST", "/api/auth/login", { body: { email: customer.user.email, password: "Wrong123456" }, headers: elsewhere() });
expect("sign-in, wrong password: 401 invalid_credentials", r.status === 401 && r.json?.code === "invalid_credentials", `${r.status} ${r.text.slice(0, 120)}`);
r = await call("POST", "/api/auth/password/forgot", { body: { email: nobody.email }, headers: elsewhere() });
expect("reset, unknown e-mail: 404, no code", r.status === 404 && r.json?.code === "account_not_found", `${r.status} ${r.text.slice(0, 120)}`);
r = await call("POST", "/api/auth/password/forgot", { body: { email: customer.user.email }, headers: elsewhere() });
expect("reset, registered e-mail: 204, code sent", r.status === 204, `${r.status} ${r.text.slice(0, 120)}`);
r = await call("POST", "/api/auth/password/forgot", { body: { email: customer.user.email }, headers: elsewhere() });
expect("reset asked again within a minute: 409", r.status === 409, `${r.status} ${r.text.slice(0, 120)}`);
for (const q of ["from=0001-01-01&to=9999-12-31", "from=9999-12-31&to=9999-12-31"]) {
  r = await call("GET", `/api/backoffice/reports/sales?${q}`, { token: admin.token });
  expect(`sales report for ${q} falls back, not a 500`, r.status === 200, `${r.status} ${r.text.slice(0, 120)}`);
}
r = await call("GET", "/api/backoffice/box-office/screenings?date=0001-01-01", { token: cashier.token });
expect("box office for year 1 falls back to today", r.status === 200, `${r.status}`);
r = await call("GET", "/api/on-display?From=9999-12-31");
expect("on display from year 9999: 400 naming From", r.status === 400 && r.json?.errors?.From, r.text.slice(0, 160));
r = await call("GET", "/api/movies?Page=abc");
expect("a page that is not a number: 400 naming Page", r.status === 400 && r.json?.code === "validation" && r.json.errors?.Page, r.text.slice(0, 160));
r = await call("POST", `/api/screenings/${show.id}/quote`, { token: c, body: { seats: 1000, usePoints: false } });
expect("a quote for 1000 seats: 400, not the price of 8", r.status === 400 && r.json?.errors?.Seats, r.text.slice(0, 160));
r = await call("POST", "/api/admin/screenings", { token: admin.token, body: { movieId: movie.id, hallId: ids.hall, startsAtUtc: new Date(Date.now() - 86400000).toISOString(), seatPrice: 9, audioLanguage: "az" } });
expect("a screening in the past: 400", r.status === 400, `${r.status} ${r.text.slice(0, 160)}`);
r = await call("POST", "/api/admin/screenings", { token: admin.token, body: { movieId: movie.id, hallId: ids.hall, startsAtUtc: "9999-12-31T20:00:00Z", seatPrice: 9, audioLanguage: "az" } });
expect("a screening in year 9999: 400", r.status === 400, `${r.status} ${r.text.slice(0, 160)}`);
const slot = new Date(Date.now() + 11 * 86400000);
r = await call("POST", "/api/admin/screenings", { token: admin.token, body: { movieId: movie.id, hallId: ids.hall, startsAtUtc: slot.toISOString(), seatPrice: 9, audioLanguage: "az" } });
expect("a screening in a free slot", ok(r), `${r.status} ${r.text.slice(0, 160)}`);
if (ok(r)) {
  const clash = await call("POST", "/api/admin/screenings", { token: admin.token, body: { movieId: movie.id, hallId: ids.hall, startsAtUtc: new Date(+slot + 3600000).toISOString(), seatPrice: 9, audioLanguage: "az" } });
  expect("a second one an hour later in the same hall: 409", clash.status === 409 && clash.json?.message, `${clash.status} ${clash.text.slice(0, 160)}`);
  await call("DELETE", `/api/admin/screenings/${r.json.id}`, { token: admin.token });
}
// Ten saves of one slot at once (a double click, two admins): the check and the insert are one
// step under the hall's lock, so one show is created and the other nine are told the hall is busy.
const burstSlot = new Date(Date.now() + 13 * 86400000).toISOString();
const burst = await Promise.all(Array.from({ length: 10 }, () =>
  call("POST", "/api/admin/screenings", { token: admin.token, body: { movieId: movie.id, hallId: ids.hall, startsAtUtc: burstSlot, seatPrice: 9, audioLanguage: "az" } })));
expect("ten saves of one slot at once: one screening, nine 409",
  burst.filter(ok).length === 1 && burst.filter((x) => x.status === 409).length === 9, burst.map((x) => x.status).join(","));
for (const won of burst.filter(ok)) await call("DELETE", `/api/admin/screenings/${won.json.id}`, { token: admin.token });
await call("POST", "/api/backoffice/shift/close", { token: cashier.token, body: { countedCash: 0, note: "api check reset" } });
await call("POST", "/api/backoffice/shift/open", { token: cashier.token, body: { openingFloat: 0 } });
const closes = await Promise.all([1, 2, 3].map(() => call("POST", "/api/backoffice/shift/close", { token: cashier.token, body: { countedCash: 0 } })));
expect("three closes of one shift at once: one Z report, the others 404",
  closes.filter(ok).length === 1 && closes.filter((x) => x.status === 404).length === 2, closes.map((x) => x.status).join(","));
const twice = `api-check-twice-${Date.now().toString(36)}@example.test`;
const signUps = await Promise.all([1, 2, 3].map(() => call("POST", "/api/auth/register", { headers: elsewhere(), body: { fullName: "Api Check", email: twice, password: "Passw0rdTest", gender: "male" } })));
expect("one e-mail registered three times at once: one account, the others 409 saying so",
  signUps.filter(ok).length === 1 && signUps.filter((x) => x.status === 409 && x.json?.message === "That e-mail is already registered.").length === 2,
  signUps.map((x) => `${x.status} ${x.json?.message ?? ""}`).join(" | "));

console.log(`\n${checks - failures.length}/${checks} checks passed.`);
if (failures.length) { console.log("\nFailures:\n  " + failures.join("\n  ")); process.exit(1); }
