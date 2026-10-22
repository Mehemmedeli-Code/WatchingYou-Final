// Part 2: cinema admin, the booking and payment flow, refunds, promo codes, loyalty, door check-in.
import { call, check, assertDb, sql, one, q, codeFor, logSize, setSection, RANDOM_ID, later } from "./lib.mjs";

export async function part2(ctx) {
  const card = { number: "4242 4242 4242 4242", expiryMonth: 12, expiryYear: new Date().getFullYear() + 2, cvc: "123", holderName: "APITEST Holder" };

  // ------------------------------------------------------------------ screenings admin
  setSection("Cinema admin: screenings create → update → bulk → cancel → delete");
  const hall = one(`SELECT TOP 1 h.Id, h.Name, h.Rows, h.SeatsPerRow FROM cinema.Halls h WHERE h.IsDeleted = 0 ORDER BY h.Name`);
  ctx.hallId = hall.Id;
  const body = { id: null, movieId: ctx.movieId, hallId: hall.Id, startsAtUtc: later(24 * 6), seatPrice: 10, audioLanguage: "az", subtitleLanguage: "en",
    refundWindowHours: null, refundFeePercent: null, refundNote: null };
  let r = await call("POST", "/api/admin/screenings", { token: ctx.customer, body }); check("customer cannot schedule", "POST", "/api/admin/screenings", r, 403);
  r = await call("POST", "/api/admin/screenings", { token: ctx.admin, body: { ...body, seatPrice: 0 } }); check("price 0 refused", "POST", "/api/admin/screenings", r, 400);
  r = await call("POST", "/api/admin/screenings", { token: ctx.admin, body: { ...body, audioLanguage: "xx" } }); check("unknown language refused", "POST", "/api/admin/screenings", r, 400);
  r = await call("POST", "/api/admin/screenings", { token: ctx.admin, body: { ...body, hallId: RANDOM_ID } }); check("unknown hall", "POST", "/api/admin/screenings", r, 404);
  r = await call("POST", "/api/admin/screenings", { token: ctx.admin, body }); check("schedule a screening", "POST", "/api/admin/screenings", r, 200);
  ctx.screeningId = r.json?.id;
  ctx.screeningDate = new Date(Date.parse(body.startsAtUtc) + 4 * 3600_000).toISOString().slice(0, 10);   // Baku day
  let row = one(`SELECT MovieId, HallId, SeatPrice, Rows, SeatsPerRow, AudioLanguage, IsCancelled, IsDeleted FROM cinema.Screenings WHERE Id = ${q(ctx.screeningId)}`);
  assertDb("cinema.Screenings row with the hall's seat layout copied", row && row.HallId === hall.Id && row.Rows === hall.Rows && row.SeatsPerRow === hall.SeatsPerRow && Number(row.SeatPrice) === 10, JSON.stringify(row));

  r = await call("PUT", `/api/admin/screenings/${ctx.screeningId}`, { token: ctx.admin, body: { ...body, seatPrice: 12.5, refundWindowHours: 24, refundFeePercent: 10 } });
  check("update screening", "PUT", "/api/admin/screenings/{id}", r, 200);
  row = one(`SELECT SeatPrice, RefundWindowHours, RefundFeePercent FROM cinema.Screenings WHERE Id = ${q(ctx.screeningId)}`);
  assertDb("cinema.Screenings price 12.50 and refund rule 24h / 10%", Number(row?.SeatPrice) === 12.5 && row.RefundWindowHours === 24 && Number(row.RefundFeePercent) === 10, JSON.stringify(row));
  r = await call("PUT", `/api/admin/screenings/${RANDOM_ID}`, { token: ctx.admin, body }); check("update unknown screening", "PUT", "/api/admin/screenings/{id}", r, 404);
  r = await call("GET", "/api/admin/screenings", { token: ctx.admin }); check("admin screening list", "GET", "/api/admin/screenings", r, 200);
  r = await call("GET", `/api/screenings/${ctx.screeningId}/seats`); check("seat map", "GET", "/api/screenings/{id}/seats", r, 200);
  assertDb("seat map has rows × seats entries", r.json?.seats?.length === hall.Rows * hall.SeatsPerRow, `${r.json?.seats?.length}`);

  // A different far-off week each run, so earlier runs never block this one's slots.
  const first = new Date(Date.now() + (40 + Math.floor(Math.random() * 300)) * 86400_000).toISOString().slice(0, 10);
  const bulk = { movieId: ctx.movieId, hallId: hall.Id, firstDate: first, days: 2, times: ["10:00", "14:00"], weekdays: [], seatPrice: 9, audioLanguage: "en", subtitleLanguage: null };
  r = await call("POST", "/api/admin/screenings/bulk", { token: ctx.admin, body: bulk }); check("bulk schedule 2 days × 2 times", "POST", "/api/admin/screenings/bulk", r, 200, JSON.stringify(r.json));
  assertDb("cinema.Screenings: 4 new rows from the bulk run", r.json?.created === 4 && one(`SELECT COUNT(*) AS n FROM cinema.Screenings WHERE MovieId = ${q(ctx.movieId)} AND SeatPrice = 9`).n === 4);
  r = await call("POST", "/api/admin/screenings/bulk", { token: ctx.admin, body: bulk }); check("the same run again skips every clash", "POST", "/api/admin/screenings/bulk", r, 200, `created ${r.json?.created}, skipped ${r.json?.skipped?.length}`);
  assertDb("no double-booked hall: still 4 rows", r.json?.created === 0 && one(`SELECT COUNT(*) AS n FROM cinema.Screenings WHERE MovieId = ${q(ctx.movieId)} AND SeatPrice = 9`).n === 4);
  r = await call("POST", "/api/admin/screenings/bulk", { token: ctx.admin, body: { ...bulk, times: ["25:99"] } }); check("bulk with a bad time", "POST", "/api/admin/screenings/bulk", r, 400);
  const bulkIds = sql(`SELECT Id FROM cinema.Screenings WHERE MovieId = ${q(ctx.movieId)} AND SeatPrice = 9`).map((x) => x.Id);

  r = await call("POST", `/api/admin/screenings/${bulkIds[0]}/cancel`, { token: ctx.admin, body: { id: bulkIds[0], reason: "" } }); check("cancel without a reason", "POST", "/api/admin/screenings/{id}/cancel", r, 400);
  r = await call("POST", `/api/admin/screenings/${bulkIds[0]}/cancel`, { token: ctx.admin, body: { id: bulkIds[0], reason: "APITEST projector fault" } });
  check("cancel screening", "POST", "/api/admin/screenings/{id}/cancel", r, 204);
  assertDb("cinema.Screenings.IsCancelled = 1 with the reason", one(`SELECT IsCancelled, CancellationReason FROM cinema.Screenings WHERE Id = ${q(bulkIds[0])}`)?.CancellationReason === "APITEST projector fault");
  r = await call("POST", `/api/admin/screenings/${bulkIds[0]}/cancel`, { token: ctx.admin, body: { id: bulkIds[0], reason: "again" } }); check("cancel twice", "POST", "/api/admin/screenings/{id}/cancel", r, 409);
  r = await call("POST", `/api/screenings/${bulkIds[0]}/checkout`, { token: ctx.token, body: { screeningId: bulkIds[0], seats: [{ row: 1, number: 1 }], card } });
  check("booking a cancelled screening is refused", "POST", "/api/screenings/{id}/checkout", r, 409);
  r = await call("DELETE", `/api/admin/screenings/${bulkIds[1]}`, { token: ctx.admin }); check("delete screening", "DELETE", "/api/admin/screenings/{id}", r, 204);
  assertDb("cinema.Screenings soft-deleted", one(`SELECT IsDeleted FROM cinema.Screenings WHERE Id = ${q(bulkIds[1])}`)?.IsDeleted === true);
  r = await call("DELETE", `/api/admin/screenings/${RANDOM_ID}`, { token: ctx.admin }); check("delete unknown screening", "DELETE", "/api/admin/screenings/{id}", r, 404);

  // ------------------------------------------------------------------ promo codes
  setSection("Promo codes");
  const code = `APITEST${Date.now() % 100000}`;
  r = await call("POST", "/api/admin/promos", { token: ctx.admin, body: { code, description: "APITEST 20%", percentOff: 20, amountOff: null, minSubtotal: 0, validFromUtc: null, validUntilUtc: null, maxRedemptions: 5 } });
  check("create promo", "POST", "/api/admin/promos", r, [200, 201]);
  ctx.promoId = r.json?.id;
  assertDb("cinema.PromoCodes row active, 20%, cap 5", (() => { const p = one(`SELECT PercentOff, MaxRedemptions, IsActive FROM cinema.PromoCodes WHERE Code = ${q(code)}`); return p && Number(p.PercentOff) === 20 && p.MaxRedemptions === 5 && p.IsActive; })());
  r = await call("POST", "/api/admin/promos", { token: ctx.admin, body: { code, description: "dup", percentOff: 10, minSubtotal: 0 } }); check("duplicate promo code", "POST", "/api/admin/promos", r, [400, 409]);
  r = await call("POST", "/api/admin/promos", { token: ctx.admin, body: { code: "BOTH" + code, percentOff: 10, amountOff: 5, minSubtotal: 0 } }); check("percent and amount together refused", "POST", "/api/admin/promos", r, 400);
  r = await call("GET", "/api/admin/promos", { token: ctx.admin }); check("promo list", "GET", "/api/admin/promos", r, 200);

  // ------------------------------------------------------------------ booking flow
  setSection("Booking: quote → checkout → e-mailed code → ticket → refund");
  r = await call("POST", `/api/screenings/${ctx.screeningId}/quote`, { token: ctx.token, body: { seats: 2, promoCode: code, usePoints: false } });
  check("quote with promo", "POST", "/api/screenings/{id}/quote", r, 200, JSON.stringify(r.json));
  assertDb("quote: 2 × 12.50 = 25.00, 20% off = 20.00", r.json && Number(r.json.subtotal) === 25 && Number(r.json.total) === 20);
  r = await call("POST", `/api/screenings/${ctx.screeningId}/quote`, { token: ctx.token, body: { seats: 1, promoCode: "NOPE-NOT-REAL", usePoints: false } });
  check("quote with an unknown promo (explains, does not fail)", "POST", "/api/screenings/{id}/quote", r, [200, 400], r.json?.promoMessage ?? "");

  r = await call("POST", `/api/screenings/${ctx.screeningId}/checkout`, { token: ctx.token, body: { screeningId: ctx.screeningId, seats: [{ row: 2, number: 3 }], card: { ...card, number: "4242 4242 4242 4241" } } });
  check("card failing the Luhn check", "POST", "/api/screenings/{id}/checkout", r, 400);
  r = await call("POST", `/api/screenings/${ctx.screeningId}/checkout`, { token: ctx.token, body: { screeningId: ctx.screeningId, seats: [{ row: 99, number: 99 }], card } });
  check("seat outside the hall", "POST", "/api/screenings/{id}/checkout", r, 400);
  r = await call("POST", `/api/screenings/${ctx.screeningId}/checkout`, { token: ctx.token, body: { screeningId: ctx.screeningId, seats: [], card } });
  check("no seats", "POST", "/api/screenings/{id}/checkout", r, 400);

  const mark = logSize();
  r = await call("POST", `/api/screenings/${ctx.screeningId}/checkout`, { token: ctx.token, body: { screeningId: ctx.screeningId, seats: [{ row: 2, number: 3 }, { row: 2, number: 4 }], card, promoCode: code, usePoints: false } });
  check("checkout 2 seats with promo", "POST", "/api/screenings/{id}/checkout", r, 200, `${r.json?.reference} ${r.json?.amount}`);
  ctx.paymentId = r.json?.paymentId;
  let pay = one(`SELECT Status, Amount, Subtotal, PromoCode, Last4, CodeHash, (SELECT COUNT(*) FROM cinema.SeatBookings b WHERE b.PaymentId = p.Id AND b.IsDeleted = 0 AND b.ConfirmedAtUtc IS NULL) AS held FROM cinema.SeatPayments p WHERE Id = ${q(ctx.paymentId)}`);
  assertDb("cinema.SeatPayments AwaitingCode (1), 20.00, promo, 2 seats held", pay && pay.Status === 1 && Number(pay.Amount) === 20 && pay.PromoCode === code && pay.held === 2, JSON.stringify({ ...pay, CodeHash: "…" }));
  assertDb("card stored as last 4 digits only; no full number anywhere", pay?.Last4 === "4242" && !one(`SELECT 1 AS x FROM cinema.SeatPayments WHERE Id = ${q(ctx.paymentId)} AND (CardHolder LIKE '%4242424242%' OR Last4 LIKE '%42424242%')`));

  r = await call("POST", `/api/screenings/${ctx.screeningId}/checkout`, { token: ctx.customer, body: { screeningId: ctx.screeningId, seats: [{ row: 2, number: 3 }], card } });
  check("another customer cannot take a held seat", "POST", "/api/screenings/{id}/checkout", r, 409);
  r = await call("GET", "/api/bookings/pending", { token: ctx.token }); check("pending checkouts", "GET", "/api/bookings/pending", r, 200, `${r.json?.length}`);

  r = await call("POST", `/api/bookings/${ctx.paymentId}/confirm`, { token: ctx.token, body: { paymentId: ctx.paymentId, code: "000000" } }); check("wrong booking code", "POST", "/api/bookings/{id}/confirm", r, [400, 409]);
  assertDb("wrong code counted (Attempts = 1)", one(`SELECT Attempts FROM cinema.SeatPayments WHERE Id = ${q(ctx.paymentId)}`)?.Attempts === 1);
  const bookingCode = await codeFor(ctx.email, mark);
  r = await call("POST", `/api/bookings/${ctx.paymentId}/confirm`, { token: ctx.customer, body: { paymentId: ctx.paymentId, code: bookingCode } }); check("someone else cannot confirm my booking", "POST", "/api/bookings/{id}/confirm", r, [403, 404]);
  r = await call("POST", `/api/bookings/${ctx.paymentId}/confirm`, { token: ctx.token, body: { paymentId: ctx.paymentId, code: bookingCode } }); check("confirm with the e-mailed code", "POST", "/api/bookings/{id}/confirm", r, 200, r.json?.reference);
  ctx.reference = r.json?.reference; ctx.qr = r.json?.seats?.[0]?.qrPayload;
  pay = one(`SELECT Status, PointsEarned, (SELECT COUNT(*) FROM cinema.SeatBookings b WHERE b.PaymentId = p.Id AND b.ConfirmedAtUtc IS NOT NULL) AS confirmed FROM cinema.SeatPayments p WHERE Id = ${q(ctx.paymentId)}`);
  assertDb("cinema.SeatPayments Confirmed (2), both seats confirmed", pay?.Status === 2 && pay.confirmed === 2, JSON.stringify(pay));
  assertDb("cinema.LoyaltyEntries: points earned for the booking", one(`SELECT SUM(Points) AS n FROM cinema.LoyaltyEntries WHERE PaymentId = ${q(ctx.paymentId)}`)?.n === pay?.PointsEarned && pay?.PointsEarned > 0, `earned ${pay?.PointsEarned}`);
  assertDb("cinema.PromoCodes.Redemptions = 1", one(`SELECT Redemptions FROM cinema.PromoCodes WHERE Code = ${q(code)}`)?.Redemptions === 1);
  r = await call("POST", `/api/bookings/${ctx.paymentId}/confirm`, { token: ctx.token, body: { paymentId: ctx.paymentId, code: bookingCode } }); check("confirm twice returns the same ticket (idempotent by design)", "POST", "/api/bookings/{id}/confirm", r, 200);
  assertDb("second confirm returns the same reference", r.json?.reference === ctx.reference);

  r = await call("GET", "/api/bookings/mine", { token: ctx.token }); check("my tickets", "GET", "/api/bookings/mine", r, 200);
  assertDb("the ticket is listed with 2 QR codes", r.json?.find((x) => x.paymentId === ctx.paymentId)?.seats?.length === 2);
  r = await call("GET", `/api/bookings/${ctx.paymentId}/ticket.pdf`, { token: ctx.token }); check("ticket PDF", "GET", "/api/bookings/{id}/ticket.pdf", r, 200, `${r.type}, ${r.bytes} bytes`);
  assertDb("PDF really is a PDF", r.type.includes("application/pdf") && r.bytes > 1000);
  r = await call("GET", `/api/bookings/${ctx.paymentId}/ticket.pdf`, { token: ctx.customer }); check("someone else's ticket PDF", "GET", "/api/bookings/{id}/ticket.pdf", r, [403, 404]);
  r = await call("GET", "/api/loyalty", { token: ctx.token }); check("loyalty balance", "GET", "/api/loyalty", r, 200, `balance ${r.json?.balance}`);

  // Refund: inside the window, with the 10% fee set on this screening.
  r = await call("GET", `/api/bookings/${ctx.paymentId}/refund-terms`, { token: ctx.token }); check("refund terms", "GET", "/api/bookings/{id}/refund-terms", r, 200, JSON.stringify(r.json)?.slice(0, 120));
  r = await call("POST", `/api/bookings/${ctx.paymentId}/refund`, { token: ctx.customer, body: { paymentId: ctx.paymentId, reason: "not mine" } }); check("someone else cannot refund my booking", "POST", "/api/bookings/{id}/refund", r, [403, 404]);
  r = await call("POST", `/api/bookings/${ctx.paymentId}/refund`, { token: ctx.token, body: { paymentId: ctx.paymentId, reason: "APITEST refund" } }); check("refund", "POST", "/api/bookings/{id}/refund", r, 200, JSON.stringify(r.json)?.slice(0, 120));
  pay = one(`SELECT Status, RefundedAmount, (SELECT COUNT(*) FROM cinema.SeatBookings b WHERE b.PaymentId = p.Id AND b.IsDeleted = 0) AS liveSeats FROM cinema.SeatPayments p WHERE Id = ${q(ctx.paymentId)}`);
  assertDb("cinema.SeatPayments Refunded (5), 18.00 back (20 − 10%), seats released", pay?.Status === 5 && Number(pay.RefundedAmount) === 18 && pay.liveSeats === 0, JSON.stringify(pay));
  assertDb("cinema.LoyaltyEntries: earned points taken back (net 0)", one(`SELECT SUM(Points) AS n FROM cinema.LoyaltyEntries WHERE PaymentId = ${q(ctx.paymentId)}`)?.n === 0);
  r = await call("POST", `/api/bookings/${ctx.paymentId}/refund`, { token: ctx.token, body: { paymentId: ctx.paymentId, reason: "again" } }); check("refund twice", "POST", "/api/bookings/{id}/refund", r, [400, 409]);

  // A second booking for the door: a scanned ticket can no longer be refunded (by design),
  // so the scan is tested on its own booking, after the refund above.
  const doorMark = logSize();
  r = await call("POST", `/api/screenings/${ctx.screeningId}/checkout`, { token: ctx.token, body: { screeningId: ctx.screeningId, seats: [{ row: 5, number: 1 }], card } });
  check("checkout a seat for the door test", "POST", "/api/screenings/{id}/checkout", r, 200);
  const doorPayment = r.json?.paymentId;
  r = await call("POST", `/api/bookings/${doorPayment}/confirm`, { token: ctx.token, body: { paymentId: doorPayment, code: await codeFor(ctx.email, doorMark) } });
  check("confirm it", "POST", "/api/bookings/{id}/confirm", r, 200);
  ctx.qr = r.json?.seats?.[0]?.qrPayload; ctx.paymentId = doorPayment;
  // Door check-in: the first scan admits, the second says "already used".
  r = await call("POST", "/api/tickets/check-in", { token: ctx.token, body: { payload: ctx.qr, screeningId: null } }); check("customer cannot check people in", "POST", "/api/tickets/check-in", r, 403);
  r = await call("POST", "/api/tickets/check-in", { token: ctx.security, body: { payload: ctx.qr, screeningId: null } }); check("scan at the door", "POST", "/api/tickets/check-in", r, 200, r.json?.outcome);
  assertDb("cinema.SeatBookings.CheckedInAtUtc set for that seat", r.json?.outcome === "Admitted" && one(`SELECT COUNT(*) AS n FROM cinema.SeatBookings WHERE PaymentId = ${q(doorPayment)} AND CheckedInAtUtc IS NOT NULL`).n === 1);
  r = await call("POST", "/api/tickets/check-in", { token: ctx.security, body: { payload: ctx.qr, screeningId: null } }); check("scan the same ticket again", "POST", "/api/tickets/check-in", r, 200, r.json?.outcome);
  assertDb("second scan reports AlreadyUsed", r.json?.outcome === "AlreadyUsed");
  r = await call("POST", "/api/tickets/check-in", { token: ctx.security, body: { payload: "WATCHINGYOU|WY-NOPE00|x|A1", screeningId: null } }); check("scan a forged code", "POST", "/api/tickets/check-in", r, 200, r.json?.outcome);
  assertDb("forged code reports NotFound", r.json?.outcome === "NotFound");


  r = await call("POST", `/api/bookings/${doorPayment}/refund`, { token: ctx.token, body: { paymentId: doorPayment, reason: "after the film" } });
  check("a ticket scanned at the door cannot be refunded", "POST", "/api/bookings/{id}/refund", r, 409);

  // Abandoning a checkout releases the seats at once.
  r = await call("POST", `/api/screenings/${ctx.screeningId}/checkout`, { token: ctx.token, body: { screeningId: ctx.screeningId, seats: [{ row: 3, number: 1 }], card } });
  check("checkout another seat", "POST", "/api/screenings/{id}/checkout", r, 200);
  const abandoned = r.json?.paymentId;
  r = await call("POST", `/api/bookings/${abandoned}/cancel`, { token: ctx.token }); check("cancel the checkout", "POST", "/api/bookings/{id}/cancel", r, [200, 204]);
  assertDb("cinema.SeatPayments Cancelled (4) and the seat released", (() => { const p = one(`SELECT Status, (SELECT COUNT(*) FROM cinema.SeatBookings b WHERE b.PaymentId = p.Id AND b.IsDeleted = 0) AS live FROM cinema.SeatPayments p WHERE Id = ${q(abandoned)}`); return p?.Status === 4 && p.live === 0; })());
  r = await call("POST", `/api/bookings/${RANDOM_ID}/cancel`, { token: ctx.token }); check("cancel unknown checkout (idempotent: already gone)", "POST", "/api/bookings/{id}/cancel", r, 204);

  // Stripe is not configured on this machine: its endpoints must say so, not crash.
  r = await call("GET", "/api/payments/options", { token: ctx.token }); check("payment options", "GET", "/api/payments/options", r, 200, JSON.stringify(r.json));
  r = await call("POST", `/api/screenings/${ctx.screeningId}/checkout/stripe`, { token: ctx.token, body: { screeningId: ctx.screeningId, seats: [{ row: 4, number: 1 }], promoCode: null, usePoints: false, returnBaseUrl: "https://localhost:7139" } });
  check("Stripe checkout without a Stripe key", "POST", "/api/screenings/{id}/checkout/stripe", r, [200, 400, 409], r.json?.message ?? "");
  r = await call("POST", `/api/bookings/${ctx.paymentId}/stripe/confirm`, { token: ctx.token, body: { paymentId: ctx.paymentId, sessionId: null } });
  check("Stripe confirm on a card booking", "POST", "/api/bookings/{id}/stripe/confirm", r, [400, 404, 409]);

  r = await call("PUT", `/api/admin/promos/${ctx.promoId}/active`, { token: ctx.admin, body: { id: ctx.promoId, active: false } }); check("switch promo off", "PUT", "/api/admin/promos/{id}/active", r, [200, 204]);
  assertDb("cinema.PromoCodes.IsActive = 0", one(`SELECT IsActive FROM cinema.PromoCodes WHERE Code = ${q(code)}`)?.IsActive === false);
  r = await call("POST", `/api/screenings/${ctx.screeningId}/quote`, { token: ctx.token, body: { seats: 1, promoCode: code, usePoints: false } });
  check("switched-off promo no longer applies", "POST", "/api/screenings/{id}/quote", r, [200, 400], `applied: ${r.json?.promoApplied}`);
  if (r.status === 200) assertDb("quote ignores the inactive promo", r.json?.promoApplied === false);

  // ------------------------------------------------------------------ analytics and exports
  setSection("Analytics and exports");
  for (const p of ["/api/admin/analytics/overview", "/api/admin/analytics/cinema-totals", "/api/admin/audit"]) {
    r = await call("GET", p, { token: ctx.admin }); check("admin read", "GET", p, r, 200);
    r = await call("GET", p, { token: ctx.customer }); check("customer refused", "GET", p, r, 403);
  }
  r = await call("GET", "/api/admin/export/bookings.csv", { token: ctx.admin }); check("bookings CSV", "GET", "/api/admin/export/bookings.csv", r, 200, r.type);
  assertDb("bookings CSV contains the test booking", r.text.includes(ctx.reference ?? "missing"));
  r = await call("GET", "/api/admin/export/bookings.csv?from=not-a-date", { token: ctx.admin }); check("bookings CSV with a bad date", "GET", "/api/admin/export/bookings.csv", r, [200, 400]);
  r = await call("GET", "/api/on-display?language=az"); check("on display, filtered", "GET", "/api/on-display", r, 200);
}
