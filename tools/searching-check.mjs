// SearchingYou end to end, with the rules around it: tickets bought or to buy, joining and
// leaving, two people racing for the last place, the watcher calling a group off, the owner
// buying for the group (only that screening's paid seats count), and women-only / men-only
// searches with the gender asked at sign-up.
//
// Never against the real site: it books seats, shrinks a screening's room in SQL and changes
// accounts. tools/api-check.ps1 runs it on its throwaway copy, with the demo people seeded.
//
//   node tools/searching-check.mjs <base url> <log with the e-mailed codes> <database>
import { readFileSync, readdirSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";

process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";
const [BASE = "https://localhost:7140", LOG, DATABASE = "WatchingYouApiCheck"] = process.argv.slice(2);
if (!LOG) { console.error("usage: node tools/searching-check.mjs <base url> <log> <database>"); process.exit(2); }
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

// The site's LocalDB may belong to another logon session; its named pipe works from any.
const pipe = readdirSync("\\\\.\\pipe\\").find((p) => p.includes("LOCALDB"));
const SQL = (q) => execFileSync("sqlcmd", ["-S", pipe ? `np:\\\\.\\pipe\\${pipe}` : "(localdb)\\MSSQLLocalDB", "-d", DATABASE, "-I", "-W", "-h", "-1", "-Q", "SET NOCOUNT ON; " + q], { encoding: "utf8" }).trim();
const ip = () => `10.${Math.random() * 250 | 0}.${Math.random() * 250 | 0}.${Math.random() * 250 | 0}`;
const wait = (ms) => new Promise((res) => setTimeout(res, ms));

async function call(method, url, { token, body } = {}) {
  // Each call from its own address, so the write limits never stand in for a rule under test.
  const headers = { "X-Forwarded-For": ip() };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const r = await fetch(BASE + url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await r.text(); let json = null; try { json = JSON.parse(text); } catch {}
  return { status: r.status, json, text };
}

let fails = 0, n = 0;
const expect = (label, ok, detail = "") => { n++; if (!ok) fails++; console.log(`${ok ? "ok  " : "FAIL"} ${label}${ok ? "" : "  " + detail}`); };

const pw = readFileSync(path.join(repo, "tools/test-accounts.md"), "utf8").split("\n")
  .find((l) => l.includes("customer@reelandrow.test")).split("|").map((c) => c.trim()).filter(Boolean).pop();
const demoPw = /Password = "([^"]+)"/.exec(readFileSync(path.join(repo, "src/MovieRental.Host/Infrastructure/DemoPeople.cs"), "utf8"))[1];
const login = async (email, password) => (await call("POST", "/api/auth/login", { body: { email, password } })).json;
const demo = (u) => login(`${u}.wydemo@gmail.com`, demoPw);

// A test card checkout, confirmed with the code the Console mailer wrote to the log.
async function buy(token, showId, seats) {
  const r = await call("POST", `/api/screenings/${showId}/checkout`, { token, body: {
    screeningId: showId, seats: seats.map((s) => ({ row: s.row, number: s.number })),
    card: { number: "4242424242424242", expiryMonth: 12, expiryYear: 2030, cvc: "123", holderName: "Search Check" },
    promoCode: null, usePoints: false } });
  const { paymentId, reference } = r.json ?? {};
  if (!paymentId) return r.status;
  await wait(800);
  const log = readFileSync(LOG, "utf8");
  const code = log.slice(log.lastIndexOf(reference)).match(/<strong>(\d{6})<\/strong>/)?.[1];
  return (await call("POST", `/api/bookings/${paymentId}/confirm`, { token, body: { paymentId, code } })).status;
}
const freeSeats = async (showId, count) => (await call("GET", `/api/screenings/${showId}/seats`)).json.seats.filter((s) => !s.isTaken).slice(-count);

const me = await login("customer@reelandrow.test", pw);   // a test login with no gender yet
const [aysel, murad, elvin, nigar, leyla] = await Promise.all(["aysel.quliyeva", "murad.aliyev", "elvin.mammadli", "nigar.hasanova", "leyla.rzayeva"].map(demo));
if (!me?.accessToken || !aysel?.accessToken) { console.log("FAIL could not sign in (are the demo people seeded?)"); process.exit(1); }
const c0 = me.accessToken;

// Seven screenings, picked once, so one scenario shrinking a room never shifts another's.
const shows = (await call("GET", "/api/screenings")).json
  .filter((s) => s.capacity - s.seatsTaken > 6 && new Date(s.startsAtUtc) > new Date(Date.now() + 3 * 3600000));
if (shows.length < 7) { console.log(`FAIL only ${shows.length} screenings to use, 7 needed`); process.exit(1); }

// ---- Tickets bought: three tickets bring two people.
const show = shows[0];
expect("three tickets bought (test card)", await buy(c0, show.id, await freeSeats(show.id, 3)) === 200);
let r = await call("GET", "/api/searching/tickets", { token: c0 });
expect("the tickets are offered: 3 seats, city, cinema, hall, time, language", r.json?.some((x) => x.screeningId === show.id && x.seats === 3 && x.city && x.venue && x.hall && x.audioLanguage), r.text.slice(0, 200));
r = await call("POST", "/api/searching", { token: c0, body: { screeningId: show.id, partner: "Mixed", people: 3, hasTickets: true } });
expect("bought 3, asks for 3: 400 (one ticket is the owner's)", r.status === 400, `${r.status}`);
r = await call("POST", "/api/searching", { token: aysel.accessToken, body: { screeningId: show.id, partner: "Mixed", people: 1, hasTickets: true } });
expect("'bought' without a ticket: 400", r.status === 400, `${r.status}`);
r = await call("POST", "/api/searching", { token: c0, body: { screeningId: show.id, partner: "Mixed", people: 2, hasTickets: true } });
expect("bought 3, asks for 2: started", r.status === 200, `${r.status} ${r.text.slice(0, 120)}`);
const id = r.json;
r = await call("POST", "/api/searching", { token: c0, body: { screeningId: show.id, partner: "Mixed", people: 1, hasTickets: true } });
expect("a second open one for the same screening: 409", r.status === 409, `${r.status}`);
r = await call("GET", "/api/searching", { token: aysel.accessToken });
const listed = r.json?.find((x) => x.id === id);
expect("listed for others: 2 wanted, tickets bought, no e-mail", listed?.peopleWanted === 2 && listed.partner === "mixed" && listed.hasTickets && !r.text.includes("@"), r.text.slice(0, 200));
r = await call("POST", `/api/searching/${id}/join`, { token: c0 }); expect("joining your own: 409", r.status === 409, `${r.status}`);
r = await call("POST", `/api/searching/${id}/join`, { token: aysel.accessToken }); expect("first joins", r.status === 204, `${r.status} ${r.text.slice(0, 120)}`);
r = await call("POST", `/api/searching/${id}/join`, { token: aysel.accessToken }); expect("the same one twice: 409", r.status === 409, `${r.status}`);
const both = await Promise.all([murad, elvin].map((u) => call("POST", `/api/searching/${id}/join`, { token: u.accessToken })));
expect("two for the last place at once: one gets it, the other 404", both.filter((x) => x.status === 204).length === 1 && both.filter((x) => x.status === 404).length === 1, both.map((x) => x.status).join(","));
r = await call("GET", "/api/searching", { token: c0 });
const mine = r.json?.find((x) => x.id === id);
expect("full: the owner sees it found, with the two who joined", mine?.found === true && mine.members.length === 2, JSON.stringify(mine)?.slice(0, 200));
r = await call("GET", "/api/searching", { token: nigar.accessToken });
expect("an outsider no longer sees the full one", !r.json?.some((x) => x.id === id));
r = await call("DELETE", `/api/searching/${id}/join`, { token: aysel.accessToken }); expect("a member leaves the group", r.status === 204, `${r.status}`);
r = await call("DELETE", `/api/searching/${id}/join`, { token: aysel.accessToken }); expect("leaving twice: 404", r.status === 404, `${r.status}`);
r = await call("GET", "/api/searching", { token: nigar.accessToken });
const reopened = r.json?.find((x) => x.id === id);
expect("an outsider sees it open again, 1 of 2", reopened && !reopened.found && reopened.peopleJoined === 1, JSON.stringify(reopened)?.slice(0, 160));
r = await call("POST", `/api/searching/${id}/join`, { token: nigar.accessToken }); expect("someone new takes the free place", r.status === 204, `${r.status}`);

// ---- Tickets to buy: lives while the screening has a seat for the whole group.
const later = shows[1];
r = await call("POST", "/api/searching", { token: murad.accessToken, body: { screeningId: later.id, partner: "Male", people: 2, hasTickets: false } });
expect("to buy: started with no ticket", r.status === 200, `${r.status} ${r.text.slice(0, 120)}`);
const open = r.json;
r = await call("POST", `/api/searching/${open}/join`, { token: elvin.accessToken }); expect("someone joins it", r.status === 204, `${r.status}`);
// The room shrinks under them (a throwaway database): 2 seats left for a group of 3.
SQL(`UPDATE cinema.Screenings SET Rows = 1, SeatsPerRow = 2 + (SELECT COUNT(*) FROM cinema.SeatBookings WHERE ScreeningId = '${later.id}' AND IsDeleted = 0) WHERE Id = '${later.id}'`);
let off = null;
for (let i = 0; i < 20 && !off?.cancelled; i++) {
  await wait(3000);
  off = (await call("GET", "/api/searching", { token: murad.accessToken })).json?.find((x) => x.id === open);
}
expect("too few seats left: called off within the watcher's 30 s", off?.cancelled === true, JSON.stringify(off)?.slice(0, 160));
r = await call("GET", "/api/searching", { token: elvin.accessToken });
expect("the one who joined sees it called off too", r.json?.find((x) => x.id === open)?.cancelled === true);
r = await call("POST", `/api/searching/${open}/join`, { token: nigar.accessToken }); expect("joining a called-off one: 404", r.status === 404, `${r.status}`);
r = await call("POST", "/api/searching", { token: murad.accessToken, body: { screeningId: later.id, partner: "Male", people: 2, hasTickets: false } });
expect("to buy, and the seats are not there: 400", r.status === 400, `${r.status}`);
r = await call("DELETE", `/api/searching/${id}`, { token: aysel.accessToken }); expect("someone else cannot close it: 404", r.status === 404, `${r.status}`);
r = await call("GET", "/api/searching"); expect("signed out: 401", r.status === 401, `${r.status}`);

// ---- The owner buys for the group: only paid seats on its own screening count.
const [g1, g2] = [shows[2], shows[3]];
r = await call("POST", "/api/searching", { token: nigar.accessToken, body: { screeningId: g1.id, partner: "Female", people: 1, hasTickets: false } });
expect("to buy, 1 wanted: started", r.status === 200, `${r.status} ${r.text.slice(0, 120)}`);
const group = r.json;
const groupState = async () => (await call("GET", "/api/searching", { token: nigar.accessToken })).json?.find((x) => x.id === group);
expect("another film's tickets: bought", await buy(nigar.accessToken, g2.id, await freeSeats(g2.id, 2)) === 200);
expect("…do not count for the group", (await groupState())?.hasTickets === false);
// Seats held at the checkout, the code never entered: not bought.
const held = await call("POST", `/api/screenings/${g1.id}/checkout`, { token: nigar.accessToken, body: {
  screeningId: g1.id, seats: (await freeSeats(g1.id, 2)).map((s) => ({ row: s.row, number: s.number })),
  card: { number: "4242424242424242", expiryMonth: 12, expiryYear: 2030, cvc: "123", holderName: "Search Check" }, promoCode: null, usePoints: false } });
expect("two seats only held, not paid…", held.status === 200, `${held.status}`);
expect("…do not count either", (await groupState())?.hasTickets === false);
await call("POST", `/api/bookings/${held.json?.paymentId}/cancel`, { token: nigar.accessToken });
expect("one ticket of two on its screening: bought", await buy(nigar.accessToken, g1.id, await freeSeats(g1.id, 1)) === 200);
expect("…not enough yet", (await groupState())?.hasTickets === false);
expect("the second ticket on its screening: bought", await buy(nigar.accessToken, g1.id, await freeSeats(g1.id, 1)) === 200);
expect("…now the group's tickets are bought", (await groupState())?.hasTickets === true);
expect("written down at once, not at the next sweep", SQL(`SELECT HasTickets FROM cinema.PartnerRequests WHERE Id = '${group}'`) === "1");

// ---- Women-only and men-only searches.
r = await call("POST", "/api/searching", { token: aysel.accessToken, body: { screeningId: shows[4].id, partner: "Female", people: 2, hasTickets: false } });
expect("a woman starts a search for women", r.status === 200, `${r.status} ${r.text.slice(0, 120)}`);
const girls = r.json;
r = await call("POST", `/api/searching/${girls}/join`, { token: murad.accessToken });
expect("a man joining it: 400, women only", r.status === 400 && r.text.includes("women only"), `${r.status} ${r.text.slice(0, 120)}`);
r = await call("POST", `/api/searching/${girls}/join`, { token: c0 });
expect("no gender on the profile: 400, say it first", r.status === 400 && r.text.includes("profile"), `${r.status} ${r.text.slice(0, 120)}`);
r = await call("POST", `/api/searching/${girls}/join`, { token: leyla.accessToken });
expect("a woman joins it", r.status === 204, `${r.status} ${r.text.slice(0, 120)}`);
r = await call("POST", "/api/searching", { token: murad.accessToken, body: { screeningId: shows[5].id, partner: "Male", people: 1, hasTickets: false } });
const boys = r.json;
r = await call("POST", `/api/searching/${boys}/join`, { token: leyla.accessToken });
expect("a woman joining a search for men: 400, men only", r.status === 400 && r.text.includes("men only"), `${r.status} ${r.text.slice(0, 120)}`);
r = await call("POST", "/api/searching", { token: leyla.accessToken, body: { screeningId: shows[6].id, partner: "Mixed", people: 2, hasTickets: false } });
const mixed = r.json;
r = await call("POST", `/api/searching/${mixed}/join`, { token: murad.accessToken });
expect("anyone joins a mixed one", r.status === 204, `${r.status} ${r.text.slice(0, 120)}`);

// ---- Gender: asked at sign-up, chosen once on the profile, put right by an admin.
const mail = `searching-check-${Date.now().toString(36)}@example.test`;   // not a seeded test login
r = await call("POST", "/api/auth/register", { body: { fullName: "Gender Check", email: mail, password: "Passw0rdTest" } });
expect("registering without a gender: 400", r.status === 400, `${r.status} ${r.text.slice(0, 120)}`);
r = await call("POST", "/api/auth/register", { body: { fullName: "Gender Check", email: mail, password: "Passw0rdTest", gender: "other" } });
expect("an unknown gender: 400", r.status === 400, `${r.status}`);
r = await call("POST", "/api/auth/register", { body: { fullName: "Gender Check", email: mail, password: "Passw0rdTest", gender: "female" } });
expect("registering with one", r.status === 200, `${r.status} ${r.text.slice(0, 120)}`);
r = await call("PUT", "/api/auth/profile", { token: c0, body: { fullName: me.user.fullName, gender: "female" } });
expect("an older account chooses it on the profile", r.status === 200 && r.json?.gender === "female", `${r.status} ${r.text.slice(0, 120)}`);
r = await call("PUT", "/api/auth/profile", { token: c0, body: { fullName: me.user.fullName, gender: "male" } });
expect("and cannot switch it later: 400", r.status === 400, `${r.status} ${r.text.slice(0, 120)}`);
r = await call("PUT", "/api/auth/profile", { token: c0, body: { fullName: me.user.fullName } });
expect("saving the name alone keeps it", r.status === 200 && r.json?.gender === "female", `${r.status}`);
r = await call("PUT", `/api/admin/users/${me.user.id}/gender`, { token: c0, body: { gender: "male" } });
expect("a customer cannot use the admin's correction", r.status === 401 || r.status === 403, `${r.status}`);

console.log(`${n - fails}/${n} ok`);
process.exit(fails ? 1 : 0);
