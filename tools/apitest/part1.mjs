// Part 1: public reads, the account lifecycle, catalogue admin, reviews, watchlist, rentals.
import { call, check, assertDb, sql, one, q, codeFor, logSize, login, setSection, RANDOM_ID } from "./lib.mjs";

export async function part1(ctx) {
  // ------------------------------------------------------------------ public, anonymous
  setSection("Public reads (anonymous)");
  let r = await call("GET", "/api/health"); check("health", "GET", "/api/health", r, 200);
  r = await call("GET", "/api/movies?pageSize=5"); check("movie list", "GET", "/api/movies", r, 200);
  const apiTotal = r.json?.totalCount;
  const dbTotal = one("SELECT COUNT(*) AS n FROM catalog.Movies WHERE IsDeleted = 0").n;
  assertDb("movie count: API totalCount = rows in catalog.Movies", apiTotal === dbTotal, `api ${apiTotal}, db ${dbTotal}`);
  ctx.anyMovieId = r.json?.items?.[0]?.id;
  r = await call("GET", "/api/movies?search=zzzz-no-such-film"); check("search with no match", "GET", "/api/movies?search=", r, 200, `items ${r.json?.items?.length}`);
  r = await call("GET", "/api/movies?page=0&pageSize=0"); check("silly paging values are clamped, not an error", "GET", "/api/movies?page=0", r, [200, 400]);
  r = await call("GET", `/api/movies/${ctx.anyMovieId}`); check("movie detail", "GET", "/api/movies/{id}", r, 200);
  r = await call("GET", `/api/movies/${RANDOM_ID}`); check("movie detail, unknown id", "GET", "/api/movies/{id}", r, 404);
  r = await call("GET", "/api/movies/not-a-guid"); check("movie detail, malformed id", "GET", "/api/movies/{id}", r, [400, 404]);
  r = await call("GET", "/api/movies/genres"); check("genres", "GET", "/api/movies/genres", r, 200);
  r = await call("GET", "/api/on-display"); check("on display", "GET", "/api/on-display", r, 200);
  r = await call("GET", "/api/screenings"); check("screenings", "GET", "/api/screenings", r, 200);
  const dbUpcoming = one("SELECT COUNT(*) AS n FROM cinema.Screenings WHERE IsDeleted = 0 AND StartsAtUtc > DATEADD(hour, -2, SYSUTCDATETIME())").n;
  assertDb("screenings: API count = rows starting after now-2h", r.json?.length === dbUpcoming, `api ${r.json?.length}, db ${dbUpcoming}`);
  r = await call("GET", "/api/venues"); check("venues", "GET", "/api/venues", r, 200);
  ctx.venues = r.json;
  ctx.hallId = r.json?.[0]?.halls?.[0]?.id;
  r = await call("GET", `/api/halls/${ctx.hallId}`); check("hall geometry", "GET", "/api/halls/{id}", r, 200);
  r = await call("GET", `/api/halls/${RANDOM_ID}`); check("hall, unknown id", "GET", "/api/halls/{id}", r, 404);
  r = await call("GET", `/api/screenings/${RANDOM_ID}/seats`); check("seat map, unknown screening", "GET", "/api/screenings/{id}/seats", r, 404);
  r = await call("GET", "/api/gallery/ai"); check("AI gallery", "GET", "/api/gallery/ai", r, 200);
  r = await call("GET", "/api/gallery/human"); check("human gallery", "GET", "/api/gallery/human", r, 200);
  r = await call("GET", "/api/payments/options"); check("payment options", "GET", "/api/payments/options", r, [200, 401]);
  r = await call("GET", "/api/globe/cities"); check("globe cities", "GET", "/api/globe/cities", r, [200, 401]);
  r = await call("GET", "/lang/az?returnUrl=/cinema"); check("language switch redirects", "GET", "/lang/{code}", r, 302, r.headers.get("location") ?? "");
  r = await call("GET", "/lang/az?returnUrl=https://evil.example"); check("language switch refuses an open redirect", "GET", "/lang/{code}", r, 302, `→ ${r.headers.get("location")}`);
  if (r.headers.get("location") !== "/") assertDb("open redirect blocked", false, r.headers.get("location"));

  // Everything behind sign-in answers 401 to an anonymous caller — never 500, never data.
  for (const [m, p] of [["GET", "/api/auth/me"], ["GET", "/api/bookings/mine"], ["GET", "/api/rentals/mine"], ["GET", "/api/watchlist"],
    ["GET", "/api/loyalty"], ["GET", "/api/messages"], ["GET", "/api/admin/users"], ["GET", "/api/backoffice/dashboard"], ["POST", "/api/rentals"]]) {
    r = await call(m, p, { body: m === "POST" ? {} : undefined });
    check("anonymous is refused", m, p, r, 401);
  }

  // ------------------------------------------------------------------ account lifecycle
  setSection("Account: register → verify → sign in → profile → password reset");
  const email = `apitest.${Date.now()}@example.test`;
  ctx.email = email; ctx.password = "ApiTest-Pass1"; ctx.fullName = "APITEST Customer";
  const mark = logSize();
  r = await call("POST", "/api/auth/register", { body: { fullName: ctx.fullName, email, password: ctx.password, phoneNumber: null } });
  check("register", "POST", "/api/auth/register", r, [200, 201]);
  let user = one(`SELECT Id, Email, FullName, IsEmailConfirmed, Roles, IsDeleted FROM identity.Users WHERE Email = ${q(email)}`);
  assertDb("identity.Users has the new row, unconfirmed, Customer", user && !user.IsEmailConfirmed && user.Roles === "Customer", JSON.stringify(user));
  ctx.userId = user?.Id;
  assertDb("password stored hashed, never in clear", !one(`SELECT 1 AS x FROM identity.Users WHERE Email = ${q(email)} AND PasswordHash LIKE ${q("%" + ctx.password + "%")}`));

  r = await call("POST", "/api/auth/register", { body: { fullName: "Dup", email, password: ctx.password } });
  check("register the same e-mail again", "POST", "/api/auth/register", r, [400, 409]);
  r = await call("POST", "/api/auth/register", { body: { fullName: "", email: "not-an-email", password: "x" } });
  check("register with invalid fields", "POST", "/api/auth/register", r, 400);

  r = await call("POST", "/api/auth/login", { body: { email, password: ctx.password } });
  check("sign-in before confirming e-mail", "POST", "/api/auth/login", r, 400, r.json?.code);
  const code = await codeFor(email, mark);
  assertDb("verification code was e-mailed (console log)", !!code, code ?? "none");
  r = await call("POST", "/api/auth/verification/confirm", { body: { email, channel: "Email", code: "000000" } });
  check("confirm with a wrong code", "POST", "/api/auth/verification/confirm", r, 400);
  r = await call("POST", "/api/auth/verification/confirm", { body: { email, channel: "Email", code } });
  check("confirm with the right code", "POST", "/api/auth/verification/confirm", r, [200, 204]);
  assertDb("identity.Users.IsEmailConfirmed = 1", one(`SELECT IsEmailConfirmed FROM identity.Users WHERE Email = ${q(email)}`)?.IsEmailConfirmed === true);

  r = await call("POST", "/api/auth/login", { body: { email, password: "wrong" } });
  check("sign-in with a wrong password", "POST", "/api/auth/login", r, 401);
  let session = await login(email, ctx.password);
  check("sign-in", "POST", "/api/auth/login", { status: session.status }, 200);
  ctx.token = session.token; ctx.refresh = session.refresh;
  assertDb("identity.RefreshTokens stored for the session", one(`SELECT COUNT(*) AS n FROM identity.RefreshTokens WHERE UserId = ${q(ctx.userId)} AND RevokedAtUtc IS NULL`).n >= 1);

  r = await call("GET", "/api/auth/me", { token: ctx.token }); check("who am I", "GET", "/api/auth/me", r, 200, r.json?.email);
  r = await call("PUT", "/api/auth/profile", { token: ctx.token, body: { fullName: "APITEST Customer Renamed", phoneNumber: "+994501112233" } });
  check("update profile", "PUT", "/api/auth/profile", r, 200);
  user = one(`SELECT FullName, PhoneNumber FROM identity.Users WHERE Id = ${q(ctx.userId)}`);
  assertDb("identity.Users name and phone updated", user?.FullName === "APITEST Customer Renamed" && user?.PhoneNumber === "+994501112233", JSON.stringify(user));
  r = await call("PUT", "/api/auth/profile", { token: ctx.token, body: { fullName: "", phoneNumber: "abc" } });
  check("update profile with invalid fields", "PUT", "/api/auth/profile", r, 400);

  const smsMark = logSize();
  r = await call("POST", "/api/auth/verification/send", { body: { email, channel: "Sms" } });
  check("send SMS code", "POST", "/api/auth/verification/send", r, [200, 204]);

  r = await call("POST", "/api/auth/refresh", { body: { refreshToken: ctx.refresh } });
  check("refresh the session", "POST", "/api/auth/refresh", r, 200);
  const oldRefresh = ctx.refresh; ctx.token = r.json?.accessToken; ctx.refresh = r.json?.refreshToken;
  assertDb("identity.RefreshTokens: old token revoked as 'rotated'", one(`SELECT RevokedReason FROM identity.RefreshTokens WHERE Token = ${q(oldRefresh)}`)?.RevokedReason === "rotated");
  r = await call("POST", "/api/auth/refresh", { body: { refreshToken: "garbage" } });
  check("refresh with an unknown token", "POST", "/api/auth/refresh", r, 401);

  const resetMark = logSize();
  r = await call("POST", "/api/auth/password/forgot", { body: { email } });
  check("forgot password", "POST", "/api/auth/password/forgot", r, [200, 204]);
  r = await call("POST", "/api/auth/password/forgot", { body: { email: "nobody-" + Date.now() + "@example.test" } });
  check("forgot password, unknown e-mail (must not reveal it)", "POST", "/api/auth/password/forgot", r, [200, 204]);
  const resetCode = await codeFor(email, resetMark);
  ctx.password = "ApiTest-Pass2";
  r = await call("POST", "/api/auth/password/reset", { body: { email, code: resetCode, newPassword: ctx.password } });
  check("reset password with the e-mailed code", "POST", "/api/auth/password/reset", r, [200, 204]);
  session = await login(email, ctx.password);
  check("sign-in with the new password", "POST", "/api/auth/login", { status: session.status }, 200);
  ctx.token = session.token; ctx.refresh = session.refresh;
  void smsMark;

  // Staff sessions for everything below.
  ctx.admin = (await login("admin@reelandrow.test", "Admin1234")).token;
  ctx.security = (await login("security@reelandrow.test", "Security1234")).token;
  ctx.cashier = (await login("kassa@reelandrow.test", "Kassa1234")).token;
  ctx.customer = (await login("customer@reelandrow.test", "Customer1234")).token;
  assertDb("staff and seeded customer can sign in", ctx.admin && ctx.security && ctx.cashier && ctx.customer);
  ctx.customerId = one("SELECT Id FROM identity.Users WHERE Email = 'customer@reelandrow.test'")?.Id;

  // ------------------------------------------------------------------ catalogue admin
  setSection("Catalogue admin: create → update → stock → delete → restore");
  const movie = { title: `APITEST Film ${Date.now()}`, description: "Test", genre: "Drama", releaseYear: 2024, durationMinutes: 95,
    director: "API Tester", posterUrl: null, trailerUrl: null, videoUrl: null, dailyPrice: 2.5, totalCopies: 3 };
  r = await call("POST", "/api/admin/movies", { token: ctx.customer, body: movie }); check("customer cannot create a film", "POST", "/api/admin/movies", r, 403);
  r = await call("POST", "/api/admin/movies", { token: ctx.admin, body: { ...movie, title: "" } }); check("create with no title", "POST", "/api/admin/movies", r, 400);
  r = await call("POST", "/api/admin/movies", { token: ctx.admin, body: movie }); check("create film", "POST", "/api/admin/movies", r, [200, 201]);
  ctx.movieId = r.json?.id;
  let row = one(`SELECT Title, TotalCopies, AvailableCopies, DailyPrice, IsDeleted FROM catalog.Movies WHERE Id = ${q(ctx.movieId)}`);
  assertDb("catalog.Movies row created with 3/3 copies", row?.Title === movie.title && row.TotalCopies === 3 && row.AvailableCopies === 3, JSON.stringify(row));
  r = await call("POST", "/api/admin/movies", { token: ctx.admin, body: movie }); check("create the same title+year again", "POST", "/api/admin/movies", r, 409);

  r = await call("PUT", `/api/admin/movies/${ctx.movieId}`, { token: ctx.admin, body: { ...movie, id: ctx.movieId, title: movie.title + " (edited)", dailyPrice: 3.75 } });
  check("update film", "PUT", "/api/admin/movies/{id}", r, [200, 204]);
  row = one(`SELECT Title, DailyPrice FROM catalog.Movies WHERE Id = ${q(ctx.movieId)}`);
  assertDb("catalog.Movies title and price updated", row?.Title.endsWith("(edited)") && Number(row.DailyPrice) === 3.75, JSON.stringify(row));
  ctx.movieTitle = row?.Title;
  r = await call("PUT", `/api/admin/movies/${RANDOM_ID}`, { token: ctx.admin, body: { ...movie, id: RANDOM_ID } }); check("update unknown film", "PUT", "/api/admin/movies/{id}", r, 404);

  r = await call("PATCH", `/api/admin/movies/${ctx.movieId}/stock`, { token: ctx.admin, body: { id: ctx.movieId, totalCopies: 5 } });
  check("change stock", "PATCH", "/api/admin/movies/{id}/stock", r, [200, 204]);
  row = one(`SELECT TotalCopies, AvailableCopies FROM catalog.Movies WHERE Id = ${q(ctx.movieId)}`);
  assertDb("catalog.Movies stock 5/5", row?.TotalCopies === 5 && row.AvailableCopies === 5, JSON.stringify(row));
  r = await call("PATCH", `/api/admin/movies/${ctx.movieId}/stock`, { token: ctx.admin, body: { id: ctx.movieId, totalCopies: -1 } });
  check("negative stock refused", "PATCH", "/api/admin/movies/{id}/stock", r, 400);

  r = await call("DELETE", `/api/admin/movies/${ctx.movieId}`, { token: ctx.admin }); check("delete film", "DELETE", "/api/admin/movies/{id}", r, [200, 204]);
  assertDb("catalog.Movies soft-deleted (IsDeleted = 1, row kept)", one(`SELECT IsDeleted FROM catalog.Movies WHERE Id = ${q(ctx.movieId)}`)?.IsDeleted === true);
  r = await call("GET", `/api/movies/${ctx.movieId}`); check("deleted film is gone from the public API", "GET", "/api/movies/{id}", r, 404);
  r = await call("POST", `/api/admin/movies/${ctx.movieId}/restore`, { token: ctx.admin }); check("restore film", "POST", "/api/admin/movies/{id}/restore", r, [200, 204]);
  assertDb("catalog.Movies restored (IsDeleted = 0)", one(`SELECT IsDeleted FROM catalog.Movies WHERE Id = ${q(ctx.movieId)}`)?.IsDeleted === false);
  r = await call("DELETE", `/api/admin/movies/${RANDOM_ID}`, { token: ctx.admin }); check("delete unknown film", "DELETE", "/api/admin/movies/{id}", r, 404);
  r = await call("POST", "/api/admin/movies/seed", { token: ctx.admin }); check("seed import (idempotent)", "POST", "/api/admin/movies/seed", r, [200, 204, 400, 409]);

  // ------------------------------------------------------------------ watchlist
  setSection("Watchlist");
  r = await call("PUT", `/api/watchlist/${ctx.movieId}`, { token: ctx.token }); check("add to watchlist", "PUT", "/api/watchlist/{movieId}", r, [200, 204]);
  r = await call("PUT", `/api/watchlist/${ctx.movieId}`, { token: ctx.token }); check("add the same film again (idempotent)", "PUT", "/api/watchlist/{movieId}", r, [200, 204]);
  assertDb("catalog.WatchlistItems has exactly one row", one(`SELECT COUNT(*) AS n FROM catalog.WatchlistItems WHERE UserId = ${q(ctx.userId)} AND MovieId = ${q(ctx.movieId)}`).n === 1);
  r = await call("GET", "/api/watchlist", { token: ctx.token }); check("watchlist", "GET", "/api/watchlist", r, 200, `${r.json?.length} item(s)`);
  r = await call("GET", "/api/watchlist/ids", { token: ctx.token }); check("watchlist ids", "GET", "/api/watchlist/ids", r, 200);
  r = await call("PUT", `/api/watchlist/${RANDOM_ID}`, { token: ctx.token }); check("add unknown film", "PUT", "/api/watchlist/{movieId}", r, 404);
  r = await call("DELETE", `/api/watchlist/${ctx.movieId}`, { token: ctx.token }); check("remove from watchlist", "DELETE", "/api/watchlist/{movieId}", r, [200, 204]);
  assertDb("catalog.WatchlistItems row removed", one(`SELECT COUNT(*) AS n FROM catalog.WatchlistItems WHERE UserId = ${q(ctx.userId)} AND MovieId = ${q(ctx.movieId)}`).n === 0);

  // ------------------------------------------------------------------ rentals and reviews
  setSection("Rentals and reviews");
  r = await call("POST", `/api/movies/${ctx.movieId}/reviews`, { token: ctx.token, body: { movieId: ctx.movieId, stars: 5, comment: "APITEST" } });
  check("review before renting is refused", "POST", "/api/movies/{id}/reviews", r, 403);
  // Since Watching PRO: always 3 days for $0.50, whatever "days" says.
  r = await call("POST", "/api/rentals", { token: ctx.token, body: { movieId: ctx.movieId, days: 3 } }); check("rent", "POST", "/api/rentals", r, [200, 201]);
  ctx.rentalId = r.json?.id;
  row = one(`SELECT r.MovieId, DATEDIFF(hour, r.RentedAtUtc, r.DueAtUtc) AS hours, r.ReturnedAtUtc, r.BasePrice, m.AvailableCopies FROM rentals.Rentals r JOIN catalog.Movies m ON m.Id = r.MovieId WHERE r.Id = ${q(ctx.rentalId)}`);
  assertDb("rentals.Rentals row: 3 days, $0.50; a copy taken off the shelf (5 → 4)", row && row.hours === 72 && row.BasePrice === 0.5 && row.AvailableCopies === 4 && row.ReturnedAtUtc === null, JSON.stringify(row));
  r = await call("POST", "/api/rentals", { token: ctx.token, body: { movieId: ctx.movieId, days: 3 } }); check("rent the same film twice", "POST", "/api/rentals", r, 409);
  r = await call("PUT", `/api/rentals/${ctx.rentalId}/extend`, { token: ctx.token, body: {} });
  check("+3 days is refused while the paid 3 are running", "PUT", "/api/rentals/{id}/extend", r, 409);
  assertDb("due date unchanged by the refused extension", one(`SELECT DATEDIFF(hour, RentedAtUtc, DueAtUtc) AS h FROM rentals.Rentals WHERE Id = ${q(ctx.rentalId)}`)?.h === 72);
  r = await call("PUT", `/api/rentals/${ctx.rentalId}/extend`, { token: ctx.customer, body: { rentalId: ctx.rentalId, extraDays: 1 } });
  check("someone else cannot extend my rental", "PUT", "/api/rentals/{id}/extend", r, [403, 404]);
  r = await call("GET", "/api/rentals/mine", { token: ctx.token }); check("my rentals", "GET", "/api/rentals/mine", r, 200);
  r = await call("POST", `/api/movies/${ctx.movieId}/reviews`, { token: ctx.token, body: { movieId: ctx.movieId, stars: 6, comment: "x" } });
  check("review with 6 stars", "POST", "/api/movies/{id}/reviews", r, 400);
  r = await call("POST", `/api/movies/${ctx.movieId}/reviews`, { token: ctx.token, body: { movieId: ctx.movieId, stars: 4, comment: "APITEST review" } });
  check("review after renting", "POST", "/api/movies/{id}/reviews", r, [200, 201]);
  row = one(`SELECT m.AverageRating, m.ReviewCount, (SELECT COUNT(*) FROM catalog.Reviews WHERE MovieId = m.Id) AS rows FROM catalog.Movies m WHERE m.Id = ${q(ctx.movieId)}`);
  assertDb("catalog.Reviews row + film rating updated to 4.0 (1 review)", row && Number(row.AverageRating) === 4 && row.ReviewCount === 1 && row.rows === 1, JSON.stringify(row));
  r = await call("PUT", `/api/rentals/${ctx.rentalId}/return`, { token: ctx.token }); check("return rental", "PUT", "/api/rentals/{id}/return", r, 200);
  row = one(`SELECT r.ReturnedAtUtc, m.AvailableCopies FROM rentals.Rentals r JOIN catalog.Movies m ON m.Id = r.MovieId WHERE r.Id = ${q(ctx.rentalId)}`);
  assertDb("rentals.Rentals returned; copy back on the shelf (4 → 5)", row?.ReturnedAtUtc !== null && row.AvailableCopies === 5, JSON.stringify(row));
  r = await call("PUT", `/api/rentals/${ctx.rentalId}/return`, { token: ctx.token }); check("return twice", "PUT", "/api/rentals/{id}/return", r, 409);
  r = await call("GET", "/api/recommendations", { token: ctx.token }); check("recommendations", "GET", "/api/recommendations", r, 200);
  r = await call("GET", `/api/taste/compare/${ctx.customerId}`, { token: ctx.token }); check("taste compare", "GET", "/api/taste/compare/{userId}", r, 200);
  r = await call("GET", "/api/admin/rentals/stats", { token: ctx.admin }); check("rental stats", "GET", "/api/admin/rentals/stats", r, 200);
  r = await call("GET", "/api/admin/rentals/overdue", { token: ctx.admin }); check("overdue rentals", "GET", "/api/admin/rentals/overdue", r, 200);
  r = await call("GET", "/api/admin/export/rentals.csv", { token: ctx.admin }); check("rentals CSV", "GET", "/api/admin/export/rentals.csv", r, 200, r.type);
}
