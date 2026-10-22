// Watching PRO + the $0.50 / 3-day rental rules, checked through the API and in the database.
import { execFileSync } from "node:child_process";
import { call, check, assertDb, sql, one, q, login, results, setSection } from "./lib.mjs";

const days = (iso) => (new Date(iso) - Date.now()) / 86400000;
const exec = (text) => execFileSync("sqlcmd", ["-S", "(localdb)\\MSSQLLocalDB", "-d", "MovieRentalDB", "-E", "-b", "-I", "-Q", text], { encoding: "utf8" });
setSection("Watching PRO and rentals");

const c = await login("customer@reelandrow.test", "Customer1234");
const admin = await login("admin@reelandrow.test", "Admin1234");
const uid = c.user.id;

let r = await call("GET", "/api/pro");                 check("anonymous PRO status", "GET", "/api/pro", r, 401);
r = await call("GET", "/api/pro", { token: c.token }); check("PRO status", "GET", "/api/pro", r, 200);
assertDb("not PRO yet; prices $5 / $0.50 / 3 days",
  r.json.active === false && r.json.monthlyPrice === 5 && r.json.rentalPrice === 0.5 && r.json.rentalDays === 3);

const free = one(`SELECT TOP 1 m.Id FROM catalog.Movies m WHERE m.IsDeleted = 0 AND m.AvailableCopies > 0
  AND NOT EXISTS (SELECT 1 FROM rentals.Rentals x WHERE x.MovieId = m.Id AND x.UserId = ${q(uid)} AND x.ReturnedAtUtc IS NULL) ORDER BY m.Title`);
const id = free.Id;
exec(`UPDATE catalog.Movies SET VideoUrl = 'https://example.test/film.mp4' WHERE Id = '${id}'`);

r = await call("GET", `/api/movies/${id}`); check("public detail", "GET", "/api/movies/{id}", r, 200);
assertDb("public detail hides the video address, says it has one", r.json.videoUrl == null && r.json.hasVideo === true);
r = await call("GET", `/api/movies/${id}/watch`); check("watch needs sign-in", "GET", "/api/movies/{id}/watch", r, 401);
r = await call("GET", `/api/movies/${id}/watch`, { token: c.token }); check("watch without rental", "GET", "/api/movies/{id}/watch", r, 200);
assertDb("no rental, no PRO: not allowed", r.json.allowed === false && r.json.access === "none" && !r.json.videoUrl);
r = await call("GET", `/api/movies/${id}/watch`, { token: admin.token });
assertDb("admin may always watch", r.json.allowed === true && r.json.access === "admin" && r.json.videoUrl === "https://example.test/film.mp4");

r = await call("POST", "/api/rentals", { token: c.token, body: { movieId: id } }); check("rent (no days sent)", "POST", "/api/rentals", r, 200);
const rentalId = r.json.id;
assertDb("rent = $0.50 for 3 days", r.json.basePrice === 0.5 && Math.abs(days(r.json.dueAtUtc) - 3) < 0.01);
let row = one(`SELECT BasePrice, DailyPrice, DATEDIFF(minute, RentedAtUtc, DueAtUtc) AS mins FROM rentals.Rentals WHERE Id = ${q(rentalId)}`);
assertDb("DB row: 0.50, exactly 3 days", row.BasePrice === 0.5 && row.DailyPrice === 0.5 && row.mins === 4320, JSON.stringify(row));

r = await call("GET", `/api/movies/${id}/watch`, { token: c.token });
assertDb("renter may watch", r.json.allowed === true && r.json.access === "rental" && !!r.json.videoUrl);
r = await call("PUT", `/api/rentals/${rentalId}/extend`, { token: c.token, body: {} });
check("+3 days before the 3 are up is refused", "PUT", "/api/rentals/{id}/extend", r, 409);

exec(`UPDATE rentals.Rentals SET DueAtUtc = DATEADD(day, -2, SYSUTCDATETIME()) WHERE Id = '${rentalId}'`);
r = await call("GET", `/api/movies/${id}/watch`, { token: c.token });
assertDb("after 3 days: waiting for a decision, not playable", r.json.allowed === false && r.json.access === "awaitingDecision");
r = await call("GET", "/api/rentals/mine?status=overdue", { token: c.token }); check("rentals awaiting a decision", "GET", "/api/rentals/mine", r, 200);
const mine = r.json.items.find((x) => x.id === rentalId);
assertDb("two undecided days cost nothing", mine && mine.lateFee === 0 && mine.totalDue === 0.5, JSON.stringify(mine && [mine.lateFee, mine.totalDue]));

r = await call("PUT", `/api/rentals/${rentalId}/extend`, { token: c.token, body: {} }); check("+3 days for $0.50", "PUT", "/api/rentals/{id}/extend", r, 200);
assertDb("$1.00 total, 3 days from now", r.json.basePrice === 1 && Math.abs(days(r.json.dueAtUtc) - 3) < 0.01);
row = one(`SELECT BasePrice, LateFee, ExtensionCount FROM rentals.Rentals WHERE Id = ${q(rentalId)}`);
assertDb("DB: 1.00, no late fee, 1 extension", row.BasePrice === 1 && row.LateFee === 0 && row.ExtensionCount === 1, JSON.stringify(row));

exec(`UPDATE rentals.Rentals SET DueAtUtc = DATEADD(day, -5, SYSUTCDATETIME()) WHERE Id = '${rentalId}'`);
r = await call("PUT", `/api/rentals/${rentalId}/return`, { token: c.token }); check("return after 5 undecided days", "PUT", "/api/rentals/{id}/return", r, 200);
row = one(`SELECT BasePrice, LateFee, CASE WHEN ReturnedAtUtc IS NULL THEN 0 ELSE 1 END AS back FROM rentals.Rentals WHERE Id = ${q(rentalId)}`);
assertDb("DB: returned, fee 0, total $1.00", row.back === 1 && row.LateFee === 0 && row.BasePrice === 1, JSON.stringify(row));

const card = (number, extra = {}) => ({ card: { number, expiryMonth: 12, expiryYear: 2030, cvc: "123", holderName: "Tural Mammadov", ...extra } });
r = await call("POST", "/api/pro/subscribe", { token: c.token, body: card("4242424242424241") }); check("bad card number", "POST", "/api/pro/subscribe", r, 400);
r = await call("POST", "/api/pro/subscribe", { token: c.token, body: card("4242424242424242", { expiryYear: 2020 }) }); check("expired card", "POST", "/api/pro/subscribe", r, 400);
r = await call("POST", "/api/pro/subscribe", { token: c.token, body: card("378282246310005") }); check("Amex refused", "POST", "/api/pro/subscribe", r, 400);
r = await call("POST", "/api/pro/subscribe", { token: c.token, raw: "{bad json" }); check("broken JSON", "POST", "/api/pro/subscribe", r, 400);
r = await call("POST", "/api/pro/subscribe", { token: c.token, body: {} }); check("no card at all", "POST", "/api/pro/subscribe", r, 400);
r = await call("POST", "/api/pro/subscribe", { body: card("4242424242424242") }); check("anonymous cannot subscribe", "POST", "/api/pro/subscribe", r, 401);
assertDb("failed attempts wrote nothing", one(`SELECT COUNT(*) AS n FROM rentals.Subscriptions WHERE UserId = ${q(uid)}`).n === 0);

r = await call("POST", "/api/pro/subscribe", { token: c.token, body: card("4242 4242 4242 4242") }); check("subscribe", "POST", "/api/pro/subscribe", r, 200);
assertDb("PRO active for about a month", r.json.active === true && days(r.json.endsAtUtc) > 27 && days(r.json.endsAtUtc) < 32, r.json.endsAtUtc);
const firstEnd = new Date(r.json.endsAtUtc);
let sub = sql(`SELECT Amount, Currency, CardBrand, CardLast4, [Plan] AS PlanName FROM rentals.Subscriptions WHERE UserId = ${q(uid)}`);
assertDb("DB: $5 USD, Visa 4242, Watching PRO", sub.length === 1 && sub[0].Amount === 5 && sub[0].Currency === "USD"
  && sub[0].CardBrand === "Visa" && sub[0].CardLast4 === "4242" && sub[0].PlanName === "Watching PRO", JSON.stringify(sub));

r = await call("GET", `/api/movies/${id}/watch`, { token: c.token });
assertDb("PRO member may watch", r.json.allowed === true && r.json.access === "pro");
const other = one(`SELECT TOP 1 Id FROM catalog.Movies WHERE IsDeleted = 0 AND Id <> '${id}' ORDER BY Title DESC`).Id;
r = await call("GET", `/api/movies/${other}/watch`, { token: c.token });
assertDb("...any film, without renting", r.json.access === "pro");
r = await call("POST", "/api/rentals", { token: c.token, body: { movieId: other } }); check("renting refused while PRO", "POST", "/api/rentals", r, 409);

r = await call("POST", "/api/pro/subscribe", { token: c.token, body: card("5555555555554444") }); check("second month paid early (Mastercard)", "POST", "/api/pro/subscribe", r, 200);
assertDb("early payment adds on, no days lost", Math.abs((new Date(r.json.endsAtUtc) - firstEnd) / 86400000 - 30.5) <= 1.5 && r.json.payments.length === 2, `${firstEnd.toISOString()} -> ${r.json.endsAtUtc}`);
sub = sql(`SELECT StartsAtUtc, EndsAtUtc, CardBrand FROM rentals.Subscriptions WHERE UserId = ${q(uid)} ORDER BY StartsAtUtc`);
assertDb("DB: month 2 starts exactly when month 1 ends", sub.length === 2 && sub[1].StartsAtUtc === sub[0].EndsAtUtc && sub[1].CardBrand === "Mastercard", JSON.stringify(sub));

r = await call("GET", "/api/messages/overview", { token: c.token }); check("messages overview", "GET", "/api/messages/overview", r, 200);
r = await call("GET", "/api/messages/overview"); check("messages overview anonymous", "GET", "/api/messages/overview", r, 401);
r = await call("PUT", "/api/globe/presence", { token: c.token, body: { shareOnGlobe: true, city: "Shamakhi", countryCode: "AZE", latitude: 40.63, longitude: 48.64 } });
check("3-letter country code refused", "PUT", "/api/globe/presence", r, 400);

const failed = results.filter((x) => !x.ok);
console.log(`\n${results.length} checks, ${results.length - failed.length} passed, ${failed.length} failed`);
