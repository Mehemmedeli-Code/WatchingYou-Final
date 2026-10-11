// Exercises the whole API against a running site (Development, seeded accounts):
//   node tools/api-check.mjs [https://localhost:7139]
//
// 1. Sweep: every operation in the OpenAPI document, as anonymous and as the role that owns
//    it, with real ids where they exist and an empty body. Nothing may answer 5xx.
// 2. Flows: the things people actually do, end to end, each step must succeed.
// Writes real rows into the dev database (a rental, a booking + refund, a message, ...).
import { readFileSync } from "node:fs";
import { randomUUID } from "node:crypto";

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

async function call(method, path, { token, body, cookie } = {}) {
  const headers = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  if (cookie) headers.Cookie = cookie;
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
  return { token: r.json.accessToken, refresh: r.json.refreshToken, user: r.json.user, cookie: r.headers.get("set-cookie")?.split(";")[0] };
}

const admin = await login("admin@reelandrow.test", "Admin1234");
const security = await login("security@reelandrow.test", "Security1234");
const customer = await login("customer@reelandrow.test", "Customer1234");
const cashier = await login("kassa@reelandrow.test", "Kassa1234");

// ---------------------------------------------------------------- real ids for path params
const movies = (await call("GET", "/api/movies?pageSize=50")).json.items;
const screenings = (await call("GET", "/api/screenings")).json;
const venues = (await call("GET", "/api/venues")).json;
const users = (await call("GET", "/api/admin/users", { token: admin.token })).json;
const userList = users.items ?? users;
const movie = movies[0];
const ids = {
  query: { "/api/globe/members": "?city=Baku", "/api/globe/online": "?country=AZ", "/api/globe/online/members": "?country=AZ" },
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
const doc = (await call("GET", "/swagger/v1/swagger.json", { cookie: admin.cookie })).json;
expect("openapi document", !!doc?.paths, "admin could not read /swagger/v1/swagger.json");

// Side effects nobody wants from an empty-body sweep: re-seeding, shifts, sessions, accounts.
const skip = new Set(["POST /api/admin/movies/seed", "POST /api/backoffice/shift/open", "POST /api/backoffice/shift/close",
  "POST /api/auth/logout", "POST /api/auth/account/delete", "POST /api/auth/register", "POST /api/auth/login",
  "POST /api/auth/verification/send", "POST /api/auth/password/forgot", "POST /api/auth/password/reset",
  "POST /api/auth/verification/confirm", "POST /api/auth/refresh"]);

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
r = await call("PUT", "/api/auth/profile", { token: security.token, body: { fullName: sec.fullName, phoneNumber: null, isPrivate: true } });
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
await call("PUT", "/api/auth/profile", { token: security.token, body: { fullName: sec.fullName, phoneNumber: null, isPrivate: false } });

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

console.log(`\n${checks - failures.length}/${checks} checks passed.`);
if (failures.length) { console.log("\nFailures:\n  " + failures.join("\n  ")); process.exit(1); }
