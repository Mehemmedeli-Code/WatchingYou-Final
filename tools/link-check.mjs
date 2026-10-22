// Every link a signed-in person can reach, opened in a real (headless) Edge, once per role:
// the customer, the admin, the security desk and the cashier. A page is a problem when it
// answers with an error, shows the site's "nothing here" page, throws in the browser, or one of
// its API calls fails on the server (5xx). Throwaway copies of the site only: it signs in with
// the test logins.
//
//   node tools/link-check.mjs https://localhost:7140
import { launch, sleep } from "./phone-demo/cdp.mjs";
import { logins } from "./test-logins.mjs";
import { staffPath } from "./staff-paths.mjs";

process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";
const BASE = process.argv[2] ?? "https://localhost:7140";
const roles = [
  { name: "customer", email: "customer@reelandrow.test", password: logins.customer, start: ["/"] },
  { name: "admin", email: "admin@reelandrow.test", password: logins.admin, start: ["/", staffPath("admin"), staffPath("backoffice")], staff: true },
  { name: "security", email: "security@reelandrow.test", password: logins.security, start: ["/", staffPath("security")], staff: true },
  { name: "cashier", email: "kassa@reelandrow.test", password: logins.kassa, start: [staffPath("backoffice")], staff: true },
];

// Addresses that are one page with many values (a film, a person, a screening) are opened once.
const shape = (u) => u.pathname.replace(/\/u\/[^/]+/, "/u/*").replace(/[0-9a-f]{8}-[0-9a-f-]{27}/g, "*")
  + [...u.searchParams.keys()].sort().map((k) => `?${k}`).join("")
  + (u.hash.startsWith("#/") ? u.hash : "");   // the back office's screens: #/bar, #/schedule…
const skip = (u) => u.origin !== BASE || u.pathname.startsWith("/api/") || u.pathname.startsWith("/lang/")
  || u.pathname.startsWith("/swagger") || /\.(pdf|csv|xlsx|ics|png|jpe?g|webp|mp4|svg)$/i.test(u.pathname);

const problems = [];
let opened = 0;
for (const role of roles) {
  const b = await launch({ width: 1366, height: 900, scale: 1, mobile: false });
  const errors = [];
  let current = "";
  let documentStatus = 0;
  await b.send("Network.enable");
  b.on("Runtime.exceptionThrown", ({ exceptionDetails: e }) => {
    const message = (e.exception?.description ?? e.text).split("\n")[0];
    // The browser cutting a page-to-page cross-fade short because this check moved on before it
    // ended: nothing a visitor sees, and not the page's doing.
    if (!/Transition was aborted/.test(message)) errors.push(`${current}: script error: ${message}`);
  });
  b.on("Network.responseReceived", ({ type, response }) => {
    if (type === "Document" && response.url.startsWith(BASE)) documentStatus = response.status;
    if (response.status >= 500 && response.url.startsWith(BASE)) errors.push(`${current}: ${response.status} from ${new URL(response.url).pathname}`);
  });
  try {
    await b.goto(`${BASE}/lang/az?returnUrl=%2Fwelcome`, 1500);
    // Signed in the way the account page does it: the cookie, and the tab's note of whose session
    // it holds (client/src/lib/api.ts, rr.refresh.user), without which the pages count as signed out.
    const status = await b.evaluate(`fetch("/api/auth/login", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: ${JSON.stringify(role.email)}, password: ${JSON.stringify(role.password)} }) })
      .then(async (r) => { if (r.ok) { const id = (await r.json()).user.id; sessionStorage.setItem("rr.refresh.user", id); localStorage.setItem("rr.refresh.user", id); } return r.status; })`);
    if (status !== 200) { problems.push(`${role.name}: could not sign in (${status})`); continue; }
    if (role.staff) {
      await b.goto(BASE + staffPath("unlock"), 1500);
      await b.evaluate(`(() => { const f = document.querySelector("input[name=pin]"); f.value = ${JSON.stringify(logins.staffPin)}; f.form.submit(); })()`);
      await sleep(2500);
    }

    const seen = new Set();
    const queue = role.start.map((p) => BASE + p);
    while (queue.length && seen.size < 90) {
      const url = new URL(queue.shift());
      const key = shape(url);
      if (seen.has(key)) continue;
      seen.add(key);
      current = `${role.name} ${url.pathname}${url.search}`;
      documentStatus = 0;
      // A back-office screen is the same page with another #/route: no new load to wait for.
      const here = await b.evaluate("location.origin + location.pathname + location.search").catch(() => "");
      if (url.hash.startsWith("#/") && here === url.origin + url.pathname + url.search) {
        await b.evaluate(`location.hash = ${JSON.stringify(url.hash)}`); await sleep(1800); documentStatus = 200;
      } else await b.goto(url.href, 2200);
      opened++;
      const page = await b.evaluate(`({ title: document.title, path: location.pathname, staffOnly: /Yalnız əməkdaşlar üçün|Staff only/.test(document.body.innerText),
        links: [...document.querySelectorAll("a[href]")].map((a) => a.href) })`);
      if (documentStatus >= 400) problems.push(`${current}: the page answered ${documentStatus}`);
      if (/yoxdur|Nothing here/i.test(page.title)) problems.push(`${current}: shows "nothing here"`);
      // A staff screen telling the staff member it is for staff only: they count as signed out there.
      if (role.staff && page.staffOnly) problems.push(`${current}: says "staff only" to a staff member`);
      for (const href of page.links) {
        try { const next = new URL(href); if (!next.hash.startsWith("#/")) next.hash = ""; if (!skip(next) && !seen.has(shape(next))) queue.push(next.href); } catch { /* not a URL */ }
      }
    }
    // The back office's own way to the admin screen (it once pointed at an address that was gone).
    if (role.name === "admin") {
      await b.goto(BASE + staffPath("backoffice"), 2500);
      const target = await b.evaluate(`[...document.querySelectorAll("a[href]")].map((a) => new URL(a.href).pathname)`);
      if (!target.includes(staffPath("admin"))) problems.push("admin: the back office has no link to the admin screen");
    }
    const screens = [...seen].filter((k) => k.includes("#/")).map((k) => k.split("#/")[1]);
    console.log(`${role.name}: ${seen.size} pages opened${screens.length ? ` (back office: ${screens.join(", ")})` : ""}`);
  } finally { b.close(); await sleep(800); }
  problems.push(...errors);
}

for (const p of [...new Set(problems)]) console.log("FAIL " + p);
console.log(`${opened} pages, ${new Set(problems).size} problem(s).`);
process.exit(problems.length ? 1 : 0);
