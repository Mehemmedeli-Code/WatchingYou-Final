// Part 3: user administration and the whole back office.
import { call, check, assertDb, sql, one, q, login, setSection, RANDOM_ID, later } from "./lib.mjs";

export async function part3(ctx) {
  // ------------------------------------------------------------------ users admin
  setSection("User admin: list → roles → suspension");
  let r = await call("GET", "/api/admin/users", { token: ctx.admin }); check("user list", "GET", "/api/admin/users", r, 200);
  r = await call("GET", "/api/admin/users?search=apitest", { token: ctx.admin }); check("user search", "GET", "/api/admin/users?search=", r, 200);
  r = await call("GET", "/api/admin/users", { token: ctx.token }); check("customer cannot list users", "GET", "/api/admin/users", r, 403);

  r = await call("PUT", `/api/admin/users/${ctx.userId}/roles`, { token: ctx.admin, body: { userId: ctx.userId, roles: ["Wizard"] } });
  check("unknown role refused", "PUT", "/api/admin/users/{id}/roles", r, 400);
  r = await call("PUT", `/api/admin/users/${ctx.userId}/roles`, { token: ctx.admin, body: { userId: ctx.userId, roles: ["Cashier"] } });
  check("grant Cashier", "PUT", "/api/admin/users/{id}/roles", r, [200, 204]);
  assertDb("identity.Users.Roles now Cashier + Customer", (() => { const x = one(`SELECT Roles FROM identity.Users WHERE Id = ${q(ctx.userId)}`)?.Roles ?? ""; return x.includes("Cashier") && x.includes("Customer"); })());
  r = await call("PUT", `/api/admin/users/${RANDOM_ID}/roles`, { token: ctx.admin, body: { userId: RANDOM_ID, roles: ["Cashier"] } });
  check("roles for an unknown user", "PUT", "/api/admin/users/{id}/roles", r, 404);
  assertDb("identity.AuditEntries recorded the role change", one(`SELECT COUNT(*) AS n FROM identity.AuditEntries WHERE Action = 'user.roles' AND SubjectId = ${q(ctx.userId)}`).n >= 1);
  // Roles live in the token, so sign in again to act as a cashier.
  const cashier = await login(ctx.email, ctx.password);
  ctx.cashierToken = cashier.token;
  assertDb("fresh token carries the Cashier role", cashier.user?.roles?.includes("Cashier"));

  r = await call("PUT", `/api/admin/users/${ctx.userId}/suspension`, { token: ctx.admin, body: { userId: ctx.userId, suspended: true, reason: "APITEST" } });
  check("suspend", "PUT", "/api/admin/users/{id}/suspension", r, [200, 204]);
  assertDb("identity.Users.IsSuspended = 1 with the reason", one(`SELECT IsSuspended, SuspensionReason FROM identity.Users WHERE Id = ${q(ctx.userId)}`)?.SuspensionReason === "APITEST");
  r = await call("POST", "/api/auth/login", { body: { email: ctx.email, password: ctx.password } }); check("a suspended account cannot sign in", "POST", "/api/auth/login", r, 400, r.json?.code);
  r = await call("PUT", `/api/admin/users/${ctx.userId}/suspension`, { token: ctx.admin, body: { userId: ctx.userId, suspended: false, reason: null } });
  check("lift suspension", "PUT", "/api/admin/users/{id}/suspension", r, [200, 204]);
  assertDb("identity.Users.IsSuspended = 0", one(`SELECT IsSuspended FROM identity.Users WHERE Id = ${q(ctx.userId)}`)?.IsSuspended === false);
  const adminId = one("SELECT Id FROM identity.Users WHERE Email = 'admin@reelandrow.test'").Id;
  r = await call("PUT", `/api/admin/users/${adminId}/suspension`, { token: ctx.admin, body: { userId: adminId, suspended: true, reason: "x" } });
  check("an admin cannot be suspended", "PUT", "/api/admin/users/{id}/suspension", r, [400, 403, 409]);
  r = await call("PUT", `/api/admin/users/${adminId}/roles`, { token: ctx.admin, body: { userId: adminId, roles: [] } });
  check("an admin cannot remove their own Admin role", "PUT", "/api/admin/users/{id}/roles", r, [400, 403, 409]);

  // ------------------------------------------------------------------ back office set-up
  setSection("Back office set-up: ticket types, bar menu, stock, distributor deals");
  r = await call("GET", "/api/backoffice/ticket-types", { token: ctx.cashierToken }); check("cashier reads ticket types", "GET", "/api/backoffice/ticket-types", r, 200);
  r = await call("POST", "/api/backoffice/ticket-types", { token: ctx.cashierToken, body: { name: "x", percentOfBase: 50, sortOrder: 1, isActive: true } });
  check("cashier cannot create ticket types", "POST", "/api/backoffice/ticket-types", r, 403);
  const tt = { name: `APITEST Tariff ${Date.now() % 10000}`, percentOfBase: 50, sortOrder: 9, isActive: true, nameAz: "APITEST Tarif", nameRu: null, nameTr: null };
  r = await call("POST", "/api/backoffice/ticket-types", { token: ctx.admin, body: tt }); check("create ticket type", "POST", "/api/backoffice/ticket-types", r, 200);
  ctx.ticketTypeId = r.json?.id;
  assertDb("cinema.TicketTypes row with AZ name", one(`SELECT NameAz, PercentOfBase FROM cinema.TicketTypes WHERE Id = ${q(ctx.ticketTypeId)}`)?.NameAz === "APITEST Tarif");
  r = await call("POST", "/api/backoffice/ticket-types", { token: ctx.admin, body: tt }); check("duplicate ticket type name", "POST", "/api/backoffice/ticket-types", r, 409);
  r = await call("POST", "/api/backoffice/ticket-types", { token: ctx.admin, body: { ...tt, name: "Over", percentOfBase: 500 } }); check("500% refused", "POST", "/api/backoffice/ticket-types", r, 400);
  r = await call("PUT", `/api/backoffice/ticket-types/${ctx.ticketTypeId}`, { token: ctx.admin, body: { ...tt, percentOfBase: 40, nameRu: "APITEST Тариф" } });
  check("update ticket type", "PUT", "/api/backoffice/ticket-types/{id}", r, 200);
  // sqlcmd prints non-Latin text as "?" on this console, so the name is read as its raw
  // UTF-16 bytes and decoded here — the check is on what is stored, not on how it prints.
  assertDb("cinema.TicketTypes 40% and RU name (Cyrillic stored intact)", (() => {
    const x = one(`SELECT PercentOfBase, CAST(NameRu AS varbinary(200)) AS bin FROM cinema.TicketTypes WHERE Id = ${q(ctx.ticketTypeId)}`);
    const name = x?.bin ? Buffer.from(x.bin, "base64").toString("utf16le") : null;
    return Number(x?.PercentOfBase) === 40 && name === "APITEST Тариф";
  })());
  r = await call("PUT", `/api/backoffice/ticket-types/${RANDOM_ID}`, { token: ctx.admin, body: tt }); check("update unknown ticket type", "PUT", "/api/backoffice/ticket-types/{id}", r, 404);

  const item = { name: `APITEST Popcorn ${Date.now() % 10000}`, category: "Popcorn", price: 5, costPrice: 1, stock: 3, lowStockThreshold: 1, isActive: true };
  r = await call("POST", "/api/backoffice/bar/items", { token: ctx.admin, body: item }); check("create bar item", "POST", "/api/backoffice/bar/items", r, 200);
  ctx.barItemId = r.json?.id;
  assertDb("cinema.ConcessionItems row, stock 3", one(`SELECT Stock FROM cinema.ConcessionItems WHERE Id = ${q(ctx.barItemId)}`)?.Stock === 3);
  r = await call("POST", "/api/backoffice/bar/items", { token: ctx.admin, body: { ...item, price: 0 } }); check("price 0 refused", "POST", "/api/backoffice/bar/items", r, 400);
  r = await call("PUT", `/api/backoffice/bar/items/${ctx.barItemId}`, { token: ctx.admin, body: { ...item, price: 6.5 } }); check("update bar item", "PUT", "/api/backoffice/bar/items/{id}", r, 200);
  assertDb("cinema.ConcessionItems price 6.50", Number(one(`SELECT Price FROM cinema.ConcessionItems WHERE Id = ${q(ctx.barItemId)}`)?.Price) === 6.5);
  r = await call("POST", `/api/backoffice/bar/items/${ctx.barItemId}/restock`, { token: ctx.admin, body: { id: ctx.barItemId, quantity: 2 } }); check("restock +2", "POST", "/api/backoffice/bar/items/{id}/restock", r, 200);
  assertDb("cinema.ConcessionItems stock 3 → 5", one(`SELECT Stock FROM cinema.ConcessionItems WHERE Id = ${q(ctx.barItemId)}`)?.Stock === 5);
  r = await call("POST", `/api/backoffice/bar/items/${ctx.barItemId}/restock`, { token: ctx.admin, body: { id: ctx.barItemId, quantity: -5 } }); check("negative restock refused", "POST", "/api/backoffice/bar/items/{id}/restock", r, 400);
  r = await call("POST", `/api/backoffice/bar/items/${RANDOM_ID}/restock`, { token: ctx.admin, body: { id: RANDOM_ID, quantity: 1 } }); check("restock unknown item", "POST", "/api/backoffice/bar/items/{id}/restock", r, 404);
  r = await call("GET", "/api/backoffice/bar/items", { token: ctx.cashierToken }); check("cashier reads the menu", "GET", "/api/backoffice/bar/items", r, 200);

  r = await call("POST", "/api/backoffice/deals", { token: ctx.admin, body: { movieId: ctx.movieId, distributor: "APITEST Distribution", sharePercent: 55, note: null } });
  check("save distributor deal", "POST", "/api/backoffice/deals", r, 200);
  ctx.dealId = r.json?.id;
  r = await call("POST", "/api/backoffice/deals", { token: ctx.admin, body: { movieId: ctx.movieId, distributor: "APITEST Distribution 2", sharePercent: 60, note: null } });
  check("save again replaces the terms (one deal per film)", "POST", "/api/backoffice/deals", r, 200);
  assertDb("cinema.FilmDeals: exactly one row, 60%", (() => { const x = sql(`SELECT SharePercent FROM cinema.FilmDeals WHERE MovieId = ${q(ctx.movieId)} AND IsDeleted = 0`); return x.length === 1 && Number(x[0].SharePercent) === 60; })());
  r = await call("POST", "/api/backoffice/deals", { token: ctx.admin, body: { movieId: ctx.movieId, distributor: "x", sharePercent: 150 } }); check("150% share refused", "POST", "/api/backoffice/deals", r, 400);
  r = await call("POST", "/api/backoffice/deals", { token: ctx.admin, body: { movieId: RANDOM_ID, distributor: "x", sharePercent: 50 } }); check("deal for an unknown film", "POST", "/api/backoffice/deals", r, 404);
  r = await call("GET", "/api/backoffice/deals", { token: ctx.admin }); check("deal list", "GET", "/api/backoffice/deals", r, 200);

  // ------------------------------------------------------------------ till: shift, sales, Z report
  setSection("Back office till: open shift → box office + bar sales → close (Z report)");
  const sale = { screeningId: ctx.screeningId, seats: [{ row: 6, number: 1, ticketTypeId: ctx.ticketTypeId }], tender: "Cash", cashReceived: 20 };
  r = await call("POST", "/api/backoffice/box-office/sell", { token: ctx.cashierToken, body: sale }); check("selling without an open shift", "POST", "/api/backoffice/box-office/sell", r, 409);
  r = await call("GET", "/api/backoffice/shift", { token: ctx.cashierToken }); check("no shift yet", "GET", "/api/backoffice/shift", r, 204);
  r = await call("POST", "/api/backoffice/shift/open", { token: ctx.cashierToken, body: { openingFloat: 100 } }); check("open shift", "POST", "/api/backoffice/shift/open", r, 200);
  ctx.shiftId = r.json?.id;
  assertDb("cinema.CashShifts open row, float 100", (() => { const x = one(`SELECT OpeningFloat, ClosedAtUtc FROM cinema.CashShifts WHERE Id = ${q(ctx.shiftId)}`); return Number(x?.OpeningFloat) === 100 && x.ClosedAtUtc === null; })());
  r = await call("POST", "/api/backoffice/shift/open", { token: ctx.cashierToken, body: { openingFloat: 50 } }); check("a second open shift is refused", "POST", "/api/backoffice/shift/open", r, 409);

  r = await call("GET", "/api/backoffice/box-office/screenings?date=" + ctx.screeningDate, { token: ctx.cashierToken }); check("screenings for a day", "GET", "/api/backoffice/box-office/screenings", r, 200);
  r = await call("GET", "/api/backoffice/box-office/screenings?date=garbage", { token: ctx.cashierToken }); check("a bad date falls back to today", "GET", "/api/backoffice/box-office/screenings", r, 200);
  r = await call("POST", "/api/backoffice/box-office/sell", { token: ctx.cashierToken, body: { ...sale, cashReceived: 1 } }); check("not enough cash", "POST", "/api/backoffice/box-office/sell", r, 400);
  r = await call("POST", "/api/backoffice/box-office/sell", { token: ctx.cashierToken, body: { ...sale, seats: [{ row: 6, number: 1, ticketTypeId: RANDOM_ID }] } }); check("unknown ticket type", "POST", "/api/backoffice/box-office/sell", r, 400);
  r = await call("POST", "/api/backoffice/box-office/sell", { token: ctx.cashierToken, body: { ...sale, seats: [{ row: 6, number: 1, ticketTypeId: ctx.ticketTypeId }, { row: 6, number: 1, ticketTypeId: ctx.ticketTypeId }] } });
  check("the same seat twice in one sale", "POST", "/api/backoffice/box-office/sell", r, 400);
  r = await call("POST", "/api/backoffice/box-office/sell", { token: ctx.cashierToken, body: sale }); check("sell a ticket at the counter", "POST", "/api/backoffice/box-office/sell", r, 200, `${r.json?.reference}, change ${r.json?.change}`);
  const counter = r.json;
  const pay = one(`SELECT Provider, Tender, Status, Amount, ShiftId, UserId, (SELECT TOP 1 TicketType FROM cinema.SeatBookings WHERE PaymentId = p.Id) AS tt FROM cinema.SeatPayments p WHERE Id = ${q(counter?.paymentId)}`);
  assertDb("cinema.SeatPayments: BoxOffice (3), cash, confirmed, 12.50 × 40% = 5.00, on this shift, walk-in", pay && pay.Provider === 3 && pay.Tender === 1 && pay.Status === 2 && Number(pay.Amount) === 5 && pay.ShiftId?.toLowerCase() === ctx.shiftId?.toLowerCase() && pay.UserId === "00000000-0000-0000-0000-000000000000", JSON.stringify(pay));
  assertDb("change given back: 20 − 5 = 15", Number(counter?.change) === 15);
  r = await call("POST", "/api/backoffice/box-office/sell", { token: ctx.cashierToken, body: sale }); check("selling the same seat again", "POST", "/api/backoffice/box-office/sell", r, 409);
  r = await call("GET", `/api/backoffice/box-office/${counter?.paymentId}/ticket.pdf`, { token: ctx.cashierToken }); check("counter ticket PDF", "GET", "/api/backoffice/box-office/{id}/ticket.pdf", r, 200, `${r.type}, ${r.bytes} bytes`);
  r = await call("GET", `/api/backoffice/box-office/${RANDOM_ID}/ticket.pdf`, { token: ctx.cashierToken }); check("PDF for an unknown sale", "GET", "/api/backoffice/box-office/{id}/ticket.pdf", r, 404);
  r = await call("GET", `/api/screenings/${ctx.screeningId}/seats`);
  assertDb("the seat sold at the counter shows as taken online", r.json?.seats?.find((s) => s.row === 6 && s.number === 1)?.isTaken === true);

  r = await call("POST", "/api/backoffice/bar/sell", { token: ctx.cashierToken, body: { lines: [{ itemId: ctx.barItemId, quantity: 10 }], tender: "Card" } }); check("more than in stock", "POST", "/api/backoffice/bar/sell", r, 409);
  r = await call("POST", "/api/backoffice/bar/sell", { token: ctx.cashierToken, body: { lines: [{ itemId: ctx.barItemId, quantity: 99 }], tender: "Card" } }); check("over 50 on one line is refused (rule)", "POST", "/api/backoffice/bar/sell", r, 400);
  r = await call("POST", "/api/backoffice/bar/sell", { token: ctx.cashierToken, body: { lines: [], tender: "Card" } }); check("empty basket", "POST", "/api/backoffice/bar/sell", r, 400);
  r = await call("POST", "/api/backoffice/bar/sell", { token: ctx.cashierToken, body: { lines: [{ itemId: ctx.barItemId, quantity: 2 }], tender: "Card" } }); check("bar sale by card", "POST", "/api/backoffice/bar/sell", r, 200, `${r.json?.reference} ${r.json?.total}`);
  const barSale = r.json;
  assertDb("cinema.ConcessionSales + line written, total 13.00, cost 2.00", (() => { const x = one(`SELECT s.Total, s.Cost, s.Tender, (SELECT SUM(Quantity) FROM cinema.ConcessionSaleLines WHERE SaleId = s.Id) AS qty FROM cinema.ConcessionSales s WHERE s.Id = ${q(barSale?.id)}`); return Number(x?.Total) === 13 && Number(x.Cost) === 2 && x.Tender === 2 && x.qty === 2; })());
  assertDb("cinema.ConcessionItems stock 5 → 3", one(`SELECT Stock FROM cinema.ConcessionItems WHERE Id = ${q(ctx.barItemId)}`)?.Stock === 3);
  r = await call("POST", "/api/backoffice/bar/sell", { token: ctx.cashierToken, body: { lines: [{ itemId: ctx.barItemId, quantity: 4 }], tender: "Cash", cashReceived: 100 } });
  check("oversell by one", "POST", "/api/backoffice/bar/sell", r, 409);
  assertDb("stock untouched after a refused sale (still 3)", one(`SELECT Stock FROM cinema.ConcessionItems WHERE Id = ${q(ctx.barItemId)}`)?.Stock === 3);

  r = await call("GET", "/api/backoffice/shift", { token: ctx.cashierToken }); check("X report", "GET", "/api/backoffice/shift", r, 200);
  assertDb("X report: 1 ticket, cash 5.00, bar card 13.00, expected cash 105.00", r.json?.ticketsSold === 1 && Number(r.json.ticketCash) === 5 && Number(r.json.barCard) === 13 && Number(r.json.expectedCash) === 105, JSON.stringify(r.json));
  r = await call("POST", "/api/backoffice/shift/close", { token: ctx.cashierToken, body: { countedCash: 104, note: "APITEST one manat short" } }); check("close shift (Z report)", "POST", "/api/backoffice/shift/close", r, 200);
  assertDb("cinema.CashShifts closed, expected 105, counted 104, variance −1", (() => { const x = one(`SELECT ClosedAtUtc, ExpectedCash, CountedCash, Variance FROM cinema.CashShifts WHERE Id = ${q(ctx.shiftId)}`); return x?.ClosedAtUtc && Number(x.ExpectedCash) === 105 && Number(x.CountedCash) === 104 && Number(x.Variance) === -1; })());
  r = await call("POST", "/api/backoffice/shift/close", { token: ctx.cashierToken, body: { countedCash: 1 } }); check("close with no open shift", "POST", "/api/backoffice/shift/close", r, 404);
  r = await call("POST", "/api/backoffice/shift/close", { token: ctx.cashierToken, body: { countedCash: -5 } }); check("negative counted cash refused", "POST", "/api/backoffice/shift/close", r, 400);

  // ------------------------------------------------------------------ reports
  setSection("Back office reports");
  for (const p of ["/api/backoffice/dashboard", "/api/backoffice/reports/sales", "/api/backoffice/reports/settlement", "/api/backoffice/shifts"]) {
    r = await call("GET", p, { token: ctx.admin }); check("manager report", "GET", p, r, 200);
    r = await call("GET", p, { token: ctx.cashierToken }); check("cashier refused", "GET", p, r, 403);
  }
  r = await call("GET", `/api/backoffice/reports/sales?from=2020-01-01&to=2030-12-31`, { token: ctx.admin }); check("sales report over a long range (clamped)", "GET", "/api/backoffice/reports/sales", r, 200);
  r = await call("GET", `/api/backoffice/reports/sales?from=zzz&to=yyy`, { token: ctx.admin }); check("sales report with bad dates (falls back)", "GET", "/api/backoffice/reports/sales", r, 200);
  const d = ctx.screeningDate;
  r = await call("GET", `/api/backoffice/reports/settlement?from=${d}&to=${d}`, { token: ctx.admin }); check("settlement for the test day", "GET", "/api/backoffice/reports/settlement", r, 200);
  const row = r.json?.rows?.find((x) => x.movieId === ctx.movieId);
  assertDb("settlement: test film at 60% share, cinema keeps the rest", row && Number(row.sharePercent) === 60 && Math.abs(Number(row.distributorDue) + Number(row.cinemaNet) - Number(row.gross)) < 0.01, JSON.stringify(row));
  r = await call("GET", `/api/backoffice/reports/settlement.csv?from=${d}&to=${d}`, { token: ctx.admin }); check("settlement CSV", "GET", "/api/backoffice/reports/settlement.csv", r, 200, r.type);

  // ------------------------------------------------------------------ set-up deletes
  setSection("Back office deletes");
  r = await call("DELETE", `/api/backoffice/deals/${ctx.dealId}`, { token: ctx.admin }); check("delete deal", "DELETE", "/api/backoffice/deals/{id}", r, [204, 404]);
  r = await call("DELETE", `/api/backoffice/bar/items/${ctx.barItemId}`, { token: ctx.admin }); check("delete bar item", "DELETE", "/api/backoffice/bar/items/{id}", r, 204);
  assertDb("cinema.ConcessionItems soft-deleted; the old receipt keeps its line", one(`SELECT IsDeleted FROM cinema.ConcessionItems WHERE Id = ${q(ctx.barItemId)}`)?.IsDeleted === true && one(`SELECT COUNT(*) AS n FROM cinema.ConcessionSaleLines WHERE ItemId = ${q(ctx.barItemId)}`).n === 1);
  r = await call("DELETE", `/api/backoffice/ticket-types/${ctx.ticketTypeId}`, { token: ctx.admin }); check("delete ticket type", "DELETE", "/api/backoffice/ticket-types/{id}", r, 204);
  assertDb("cinema.TicketTypes soft-deleted; the sold seat keeps its tariff name", one(`SELECT IsDeleted FROM cinema.TicketTypes WHERE Id = ${q(ctx.ticketTypeId)}`)?.IsDeleted === true);
  for (const p of [`/api/backoffice/deals/${RANDOM_ID}`, `/api/backoffice/bar/items/${RANDOM_ID}`, `/api/backoffice/ticket-types/${RANDOM_ID}`]) {
    r = await call("DELETE", p, { token: ctx.admin }); check("delete unknown", "DELETE", p, r, 404);
  }
  void later;
}
