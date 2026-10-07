// Part 4: messages, help desk, globe, the short-film pipeline, SignalR, cookies, account
// deletion, a robustness sweep of all 123 operations, and the rate limit.
import { readFileSync } from "node:fs";
import { call, check, assertDb, sql, one, q, login, setSection, RANDOM_ID, BASE, results } from "./lib.mjs";

export async function part4(ctx) {
  const RUN = Date.now() % 1000000;   // makes every test text unique to this run
  // ------------------------------------------------------------------ globe
  setSection("Globe presence");
  let r = await call("PUT", "/api/globe/presence", { token: ctx.token, body: { shareOnGlobe: true, city: "Baku", countryCode: "AZ", latitude: 40.4, longitude: 49.9, avatarUrl: null } });
  check("appear on the globe", "PUT", "/api/globe/presence", r, [200, 204]);
  assertDb("identity.Users presence saved", (() => { const u = one(`SELECT ShareOnGlobe, City FROM identity.Users WHERE Id = ${q(ctx.userId)}`); return u?.ShareOnGlobe === true && u.City === "Baku"; })());
  r = await call("PUT", "/api/globe/presence", { token: ctx.token, body: { shareOnGlobe: true, city: "Baku", countryCode: "AZ", latitude: 999, longitude: 49.9 } }); check("impossible latitude refused", "PUT", "/api/globe/presence", r, 400);
  r = await call("GET", "/api/globe/cities", { token: ctx.token }); check("cities", "GET", "/api/globe/cities", r, 200);
  r = await call("GET", "/api/globe/members?city=Baku&page=1&pageSize=10", { token: ctx.token }); check("members in a city", "GET", "/api/globe/members", r, 200);

  // ------------------------------------------------------------------ direct messages + reports
  // Only people who show themselves on the globe can be written to (a privacy rule the server
  // enforces), so the test user joins the globe above and the seeded customer writes to them.
  setSection("Direct messages, blocking, reports");
  r = await call("POST", `/api/messages/${ctx.userId}`, { token: ctx.customer, body: { otherUserId: ctx.userId, body: `APITEST hello ${RUN}` } });
  check("send a direct message", "POST", "/api/messages/{userId}", r, 200);
  assertDb("identity.DirectMessages row written", one(`SELECT COUNT(*) AS n FROM identity.DirectMessages WHERE Body = 'APITEST hello ${RUN}'`).n === 1);
  r = await call("POST", `/api/messages/${ctx.customerId}`, { token: ctx.token, body: { otherUserId: ctx.customerId, body: "hi" } });
  check("someone hidden from the globe cannot be written to (privacy rule)", "POST", "/api/messages/{userId}", r, 409);
  r = await call("POST", `/api/messages/${ctx.userId}`, { token: ctx.customer, body: { otherUserId: ctx.userId, body: "" } }); check("empty message refused", "POST", "/api/messages/{userId}", r, 400);
  r = await call("POST", `/api/messages/${ctx.userId}`, { token: ctx.token, body: { otherUserId: ctx.userId, body: "me" } }); check("messaging yourself refused", "POST", "/api/messages/{userId}", r, [400, 409]);
  r = await call("POST", `/api/messages/${RANDOM_ID}`, { token: ctx.token, body: { otherUserId: RANDOM_ID, body: "x" } }); check("messaging an unknown user", "POST", "/api/messages/{userId}", r, 404);
  r = await call("GET", "/api/messages", { token: ctx.token }); check("threads (receiving side)", "GET", "/api/messages", r, 200);
  assertDb("the receiver sees the thread", JSON.stringify(r.json ?? "").includes("APITEST"));
  r = await call("GET", `/api/messages/${ctx.customerId}`, { token: ctx.token }); check("open the conversation", "GET", "/api/messages/{userId}", r, 200);
  r = await call("POST", `/api/messages/${ctx.customerId}/report`, { token: ctx.token, body: { aboutUserId: ctx.customerId, quote: `APITEST hello ${RUN}`, reason: `APITEST report ${RUN}` } });
  check("report a message", "POST", "/api/messages/{userId}/report", r, [200, 204]);
  const report = one(`SELECT Id, Handled FROM identity.MessageReports WHERE Reason = 'APITEST report ${RUN}'`);
  assertDb("identity.MessageReports row, not handled", report && report.Handled === false);
  r = await call("PUT", `/api/messages/${ctx.customerId}/block`, { token: ctx.token, body: { otherUserId: ctx.customerId, blocked: true } }); check("block", "PUT", "/api/messages/{userId}/block", r, [200, 204]);
  assertDb("identity.UserBlocks row", one(`SELECT COUNT(*) AS n FROM identity.UserBlocks WHERE BlockerId = ${q(ctx.userId)} AND BlockedId = ${q(ctx.customerId)}`).n === 1);
  r = await call("POST", `/api/messages/${ctx.userId}`, { token: ctx.customer, body: { otherUserId: ctx.userId, body: "after block" } }); check("a blocked sender is refused", "POST", "/api/messages/{userId}", r, [403, 409]);
  r = await call("PUT", `/api/messages/${ctx.customerId}/block`, { token: ctx.token, body: { otherUserId: ctx.customerId, blocked: false } }); check("unblock", "PUT", "/api/messages/{userId}/block", r, [200, 204]);
  assertDb("identity.UserBlocks row removed", one(`SELECT COUNT(*) AS n FROM identity.UserBlocks WHERE BlockerId = ${q(ctx.userId)} AND BlockedId = ${q(ctx.customerId)}`).n === 0);
  r = await call("POST", `/api/messages/${ctx.userId}`, { token: ctx.customer, body: { otherUserId: ctx.userId, body: "after unblock" } }); check("messages flow again after unblocking", "POST", "/api/messages/{userId}", r, 200);
  r = await call("GET", "/api/reports", { token: ctx.token }); check("customer cannot read reports", "GET", "/api/reports", r, 403);
  r = await call("GET", "/api/reports", { token: ctx.security }); check("security reads reports", "GET", "/api/reports", r, 200);
  r = await call("POST", `/api/reports/${report?.Id}/resolve`, { token: ctx.security }); check("resolve report", "POST", "/api/reports/{id}/resolve", r, [200, 204]);
  assertDb("identity.MessageReports.Handled = 1", one(`SELECT Handled FROM identity.MessageReports WHERE Id = ${q(report?.Id)}`)?.Handled === true);
  r = await call("POST", `/api/reports/${RANDOM_ID}/resolve`, { token: ctx.security }); check("resolve unknown report", "POST", "/api/reports/{id}/resolve", r, 404);

  // ------------------------------------------------------------------ help desk
  setSection("Help desk chat");
  r = await call("POST", "/api/help/chat", { token: ctx.token, body: { body: `APITEST I need help ${RUN}` } }); check("customer writes to support", "POST", "/api/help/chat", r, 200);
  const convId = r.json?.id;
  assertDb("identity.SupportConversations + message rows", one(`SELECT COUNT(*) AS n FROM identity.SupportChatMessages WHERE Body = 'APITEST I need help ${RUN}'`).n === 1);
  r = await call("GET", "/api/help/chat", { token: ctx.token }); check("customer reads their chat", "GET", "/api/help/chat", r, 200);
  r = await call("GET", "/api/help/inbox", { token: ctx.token }); check("customer cannot read the inbox", "GET", "/api/help/inbox", r, 403);
  r = await call("GET", "/api/help/inbox", { token: ctx.security }); check("desk inbox", "GET", "/api/help/inbox", r, 200);
  r = await call("GET", `/api/help/inbox/${convId}`, { token: ctx.security }); check("desk opens the conversation", "GET", "/api/help/inbox/{id}", r, 200);
  r = await call("POST", `/api/help/inbox/${convId}`, { token: ctx.security, body: { conversationId: convId, body: `APITEST desk reply ${RUN}` } }); check("desk replies", "POST", "/api/help/inbox/{id}", r, 200);
  assertDb("reply stored as from the desk", one(`SELECT COUNT(*) AS n FROM identity.SupportChatMessages WHERE Body = 'APITEST desk reply ${RUN}' AND FromDesk = 1`).n === 1);
  r = await call("POST", `/api/help/inbox/${convId}/close`, { token: ctx.security }); check("desk closes the chat", "POST", "/api/help/inbox/{id}/close", r, [200, 204]);
  assertDb("identity.SupportConversations status closed", (one(`SELECT Status FROM identity.SupportConversations WHERE Id = ${q(convId)}`)?.Status ?? 0) !== 1);
  r = await call("GET", `/api/help/inbox/${RANDOM_ID}`, { token: ctx.security }); check("unknown conversation", "GET", "/api/help/inbox/{id}", r, 404);

  // ------------------------------------------------------------------ short films
  setSection("Short films: upload → security review → admin decision → gallery");
  const form = new FormData();
  form.append("title", "APITEST Short");
  form.append("synopsis", "A test upload");
  form.append("origin", "HandCrafted");
  form.append("visibility", "Public");
  form.append("file", new Blob([Buffer.alloc(2048, 7)], { type: "video/mp4" }), "apitest.mp4");
  r = await call("POST", "/api/shorts", { token: ctx.token, form }); check("upload a short film", "POST", "/api/shorts", r, 200, r.json?.status);
  ctx.shortId = r.json?.id;
  const film = one(`SELECT Status, Visibility, SizeBytes, StoredFileName FROM media.ShortFilms WHERE Id = ${q(ctx.shortId)}`);
  assertDb("media.ShortFilms row Pending, 2048 bytes", film?.Status === 1 && film.SizeBytes === 2048, JSON.stringify(film));
  ctx.shortFile = film?.StoredFileName;
  const bad = new FormData(); bad.append("title", "x"); bad.append("origin", "HandCrafted"); bad.append("file", new Blob([Buffer.alloc(10)]), "virus.exe");
  r = await call("POST", "/api/shorts", { token: ctx.token, form: bad }); check("a non-video file is refused", "POST", "/api/shorts", r, 400);
  const noFile = new FormData(); noFile.append("title", "x");
  r = await call("POST", "/api/shorts", { token: ctx.token, form: noFile }); check("no file attached", "POST", "/api/shorts", r, 400);
  r = await call("POST", "/api/shorts", { token: ctx.token, body: { title: "json" } }); check("JSON instead of a form", "POST", "/api/shorts", r, 400);

  r = await call("GET", "/api/shorts/mine", { token: ctx.token }); check("my shorts", "GET", "/api/shorts/mine", r, 200);
  r = await call("PUT", `/api/shorts/${ctx.shortId}/visibility`, { token: ctx.token, body: { id: ctx.shortId, visibility: "Private" } }); check("make it private", "PUT", "/api/shorts/{id}/visibility", r, [200, 204]);
  assertDb("media.ShortFilms.Visibility = Private (1)", one(`SELECT Visibility FROM media.ShortFilms WHERE Id = ${q(ctx.shortId)}`)?.Visibility === 1);
  r = await call("PUT", `/api/shorts/${ctx.shortId}/visibility`, { token: ctx.customer, body: { id: ctx.shortId, visibility: "Public" } }); check("someone else cannot change it", "PUT", "/api/shorts/{id}/visibility", r, [403, 404]);
  r = await call("GET", `/api/shorts/${ctx.shortId}/stream`, { token: ctx.token }); check("owner streams the video", "GET", "/api/shorts/{id}/stream", r, 200, `${r.type}, ${r.bytes} bytes`);
  r = await call("GET", `/api/shorts/${ctx.shortId}/stream`, { token: ctx.customer }); check("a private, unapproved film is not streamable by others", "GET", "/api/shorts/{id}/stream", r, [403, 404]);

  r = await call("GET", "/api/security/shorts/queue", { token: ctx.security }); check("security queue", "GET", "/api/security/shorts/queue", r, 200);
  r = await call("GET", "/api/security/shorts/queue", { token: ctx.token }); check("customer cannot see the queue", "GET", "/api/security/shorts/queue", r, 403);
  r = await call("PUT", `/api/admin/shorts/${ctx.shortId}/decision`, { token: ctx.admin, body: { id: ctx.shortId, approve: true, note: null, correctOriginTo: null } });
  check("admin cannot decide before a security report", "PUT", "/api/admin/shorts/{id}/decision", r, [400, 409]);
  r = await call("POST", `/api/security/shorts/${ctx.shortId}/claim`, { token: ctx.security }); check("security claims it", "POST", "/api/security/shorts/{id}/claim", r, [200, 204]);
  assertDb("media.ShortFilms UnderSecurityReview (2)", one(`SELECT Status FROM media.ShortFilms WHERE Id = ${q(ctx.shortId)}`)?.Status === 2);
  const checks = ["Copyright", "SexualContent", "GraphicViolence", "MinorsWithoutConsent", "HateOrExtremism", "PersonalData", "FileIntegrity", "OriginDeclaration"]
    .map((c) => ({ check: c, outcome: "Pass", note: null }));
  r = await call("POST", `/api/security/shorts/${ctx.shortId}/report`, { token: ctx.security, body: { id: ctx.shortId, watchedInFull: true, summary: "APITEST", checks: checks.slice(0, 3) } });
  check("an incomplete checklist is refused", "POST", "/api/security/shorts/{id}/report", r, 400);
  r = await call("POST", `/api/security/shorts/${ctx.shortId}/report`, { token: ctx.security, body: { id: ctx.shortId, watchedInFull: true, summary: "APITEST all clear", checks } });
  check("file the security report", "POST", "/api/security/shorts/{id}/report", r, [200, 204]);
  assertDb("media.SecurityReports + 8 check rows; film SecurityCleared (3)", (() => {
    const rep = one(`SELECT Id FROM media.SecurityReports WHERE ShortFilmId = ${q(ctx.shortId)}`);
    return rep && one(`SELECT COUNT(*) AS n FROM media.SecurityCheckResults WHERE SecurityReportId = ${q(rep.Id)}`).n === 8 && one(`SELECT Status FROM media.ShortFilms WHERE Id = ${q(ctx.shortId)}`)?.Status === 3;
  })());
  r = await call("POST", `/api/shorts/${ctx.shortId}/comments`, { token: ctx.token, body: { id: ctx.shortId, body: `APITEST comment ${RUN}` } }); check("comment on the submission", "POST", "/api/shorts/{id}/comments", r, [200, 204]);
  assertDb("media.SubmissionComments row", one(`SELECT COUNT(*) AS n FROM media.SubmissionComments WHERE Body = 'APITEST comment ${RUN}'`).n === 1);
  r = await call("GET", "/api/admin/shorts/queue", { token: ctx.admin }); check("admin queue", "GET", "/api/admin/shorts/queue", r, 200);
  r = await call("PUT", `/api/admin/shorts/${ctx.shortId}/decision`, { token: ctx.admin, body: { id: ctx.shortId, approve: true, note: "APITEST approved", correctOriginTo: null } });
  check("admin approves", "PUT", "/api/admin/shorts/{id}/decision", r, [200, 204]);
  assertDb("media.ShortFilms Approved (5)", one(`SELECT Status, ApprovedAtUtc FROM media.ShortFilms WHERE Id = ${q(ctx.shortId)}`)?.Status === 5);
  r = await call("PUT", `/api/shorts/${ctx.shortId}/visibility`, { token: ctx.token, body: { id: ctx.shortId, visibility: "Public" } }); check("make it public", "PUT", "/api/shorts/{id}/visibility", r, [200, 204]);
  r = await call("GET", "/api/gallery/human"); check("human gallery", "GET", "/api/gallery/human", r, 200);
  assertDb("approved + public film appears in the Human Craft gallery", JSON.stringify(r.json).includes(ctx.shortId));
  r = await call("GET", `/api/shorts/${ctx.shortId}/stream`); check("an approved public film streams to anyone", "GET", "/api/shorts/{id}/stream", r, 200);
  r = await call("DELETE", `/api/admin/shorts/${ctx.shortId}`, { token: ctx.admin }); check("admin removes the film", "DELETE", "/api/admin/shorts/{id}", r, 204);
  r = await call("GET", "/api/gallery/human"); assertDb("gone from the gallery", !JSON.stringify(r.json).includes(ctx.shortId));
  r = await call("DELETE", `/api/admin/shorts/${RANDOM_ID}`, { token: ctx.admin }); check("remove unknown film", "DELETE", "/api/admin/shorts/{id}", r, 404);

  // ------------------------------------------------------------------ real-time hub + cookies
  setSection("SignalR hub and cookie sessions");
  r = await call("POST", "/hubs/chat/negotiate?negotiateVersion=1", { token: ctx.token }); check("hub negotiate (signed in)", "POST", "/hubs/chat/negotiate", r, 200);
  r = await call("POST", "/hubs/chat/negotiate?negotiateVersion=1"); check("hub negotiate (anonymous)", "POST", "/hubs/chat/negotiate", r, 401);
  const res = await fetch(BASE + "/api/auth/login", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email: "customer@reelandrow.test", password: "Customer1234" }) });
  const cookie = (res.headers.get("set-cookie") ?? "").split(";")[0];
  assertDb("sign-in sets the session cookie", cookie.startsWith("rr.session="));
  r = await call("GET", "/api/auth/me", { headers: { Cookie: cookie } }); check("cookie session reaches the API", "GET", "/api/auth/me", r, 200);
  r = await call("GET", "/api/admin/users", { headers: { Cookie: cookie } }); check("cookie session, wrong role → 403 (not a redirect)", "GET", "/api/admin/users", r, 403);
  r = await call("GET", "/admin", { headers: { Cookie: cookie } }); check("pages still redirect to sign-in", "GET", "/admin", r, [200, 302]);

  // ------------------------------------------------------------------ robustness sweep
  setSection("Robustness sweep: every operation with broken input must answer 4xx, never 5xx");
  const swagger = JSON.parse(readFileSync("C:/wy/apitest/swagger.json", "utf8"));
  let fiveHundreds = 0, total = 0;
  for (const [path, ops] of Object.entries(swagger.paths)) {
    for (const method of Object.keys(ops)) {
      const concrete = path.replace(/\{[^}]+\}/g, RANDOM_ID);
      const M = method.toUpperCase();
      // Only broken input: invalid JSON bodies and ids that do not exist. Nothing here can
      // change real data.
      const variants = M === "GET" ? [{}] : [{ raw: "{not json" }];
      for (const v of variants) {
        for (const token of [undefined, ctx.admin]) {
          const res2 = await call(M, concrete, { token, ...v });
          total++;
          if (res2.status >= 500 || res2.status === 302) {
            fiveHundreds++;
            check(`broken input (${token ? "admin" : "anonymous"})`, M, path, res2, "4xx");
          }
        }
      }
      // A malformed id where a Guid is expected.
      if (path.includes("{")) {
        const res3 = await call(M, path.replace(/\{[^}]+\}/g, "not-a-guid"), { token: ctx.admin, raw: M === "GET" ? undefined : "{}" });
        total++;
        if (res3.status >= 500) { fiveHundreds++; check("malformed id", M, path, res3, "4xx"); }
      }
    }
  }
  results.push({ section: "Robustness sweep", name: `${total} broken requests across all operations; 5xx or redirects: ${fiveHundreds}`, method: "ALL", path: "", status: "", expect: "", ok: fiveHundreds === 0, detail: "" });
  console.log(`${fiveHundreds === 0 ? "PASS" : "FAIL"} sweep: ${total} broken requests, ${fiveHundreds} answered 5xx/302`);

  // ------------------------------------------------------------------ account deletion
  setSection("Account deletion");
  r = await call("POST", "/api/auth/account/delete", { token: ctx.admin, body: { password: "Admin1234" } }); check("an admin cannot delete their own account", "POST", "/api/auth/account/delete", r, 403);
  r = await call("POST", "/api/auth/account/delete", { token: ctx.token, body: { password: "wrong" } }); check("wrong password", "POST", "/api/auth/account/delete", r, 400);
  const fresh = await login(ctx.email, ctx.password); ctx.token = fresh.token ?? ctx.token;
  r = await call("POST", "/api/auth/account/delete", { token: ctx.token, body: { password: ctx.password } }); check("delete my account", "POST", "/api/auth/account/delete", r, 204);
  const gone = one(`SELECT Email, FullName, PhoneNumber, City, IsDeleted, (SELECT COUNT(*) FROM identity.RefreshTokens WHERE UserId = u.Id AND RevokedAtUtc IS NULL) AS live FROM identity.Users u WHERE Id = ${q(ctx.userId)}`);
  assertDb("identity.Users anonymised, soft-deleted, no live sessions", gone && gone.IsDeleted && gone.FullName === "Deleted user" && gone.PhoneNumber === null && gone.City === null && !gone.Email.includes("apitest") && gone.live === 0, JSON.stringify(gone));
  assertDb("bookings kept for accounting (now anonymous)", one(`SELECT COUNT(*) AS n FROM cinema.SeatPayments WHERE UserId = ${q(ctx.userId)}`).n > 0);
  r = await call("POST", "/api/auth/login", { body: { email: ctx.email, password: ctx.password } }); check("the deleted account can no longer sign in", "POST", "/api/auth/login", r, 401);

  // ------------------------------------------------------------------ rate limit (last: it blocks sign-ins for a minute)
  setSection("Brute-force protection");
  globalThis.NO_RETRY_429 = true;
  let limited = null;
  for (let i = 0; i < 12 && !limited; i++) {
    const x = await call("POST", "/api/auth/login", { body: { email: "nobody@example.test", password: "wrong" + i } });
    if (x.status === 429) limited = x;
  }
  check("repeated wrong sign-ins are throttled", "POST", "/api/auth/login", limited ?? { status: 0, text: "never throttled" }, 429, `Retry-After: ${limited?.headers.get("retry-after")}`);
  assertDb("the 429 says how long to wait", Number(limited?.headers.get("retry-after")) > 0);
  globalThis.NO_RETRY_429 = false;
  void sql;
}
