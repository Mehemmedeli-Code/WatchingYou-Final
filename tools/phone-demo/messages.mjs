// Demo site (7142) only: three demo people write to the test customer, so the inbox has life in
// it, and two of them look for company on SearchingYou, so that page has someone to join.
import fs from "node:fs";
import path from "node:path";
const BASE = "https://localhost:7142";
const repo = path.resolve(import.meta.dirname, "../..");
const { demo: demoPassword, customer: customerPassword } = (await import("../test-logins.mjs")).logins;

async function login(email, password) {
  const r = await fetch(`${BASE}/api/auth/login`, { method: "POST", headers: { "Content-Type": "application/json", "X-Forwarded-For": `10.9.${Math.random() * 250 | 0}.${Math.random() * 250 | 0}` }, body: JSON.stringify({ email, password }) });
  if (r.status !== 200) throw new Error(`${email}: ${r.status}`);
  return r.json();
}

const customer = await login("customer@reelandrow.test", customerPassword);
const notes = [
  ["elvin.mammadli", "Biletləri aldım, 7-ci sıra 👌"],
  ["murad.aliyev", "Studiyaya yüklədiyim qısametrajlıya bax, fikrini yaz 🙌"],
  ["aysel.quliyeva", "Salam! Bu axşam Nizamidə «12 Angry Men» var, gəlirsən? 🎬"],
];
for (const [user, body] of notes) {
  const s = await login(`${user}.wydemo@gmail.com`, demoPassword);
  const r = await fetch(`${BASE}/api/messages/${customer.user.id}`, { method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${s.accessToken}` }, body: JSON.stringify({ body }) });
  console.log(user, r.status, r.status >= 300 ? (await r.text()).slice(0, 160) : "");
  await new Promise((r) => setTimeout(r, 1200));
}

const shows = await (await fetch(`${BASE}/api/screenings`)).json();
const ahead = shows.filter((x) => x.capacity - x.seatsTaken > 6 && new Date(x.startsAtUtc) > new Date(Date.now() + 3 * 3600000));
const searches = [["aysel.quliyeva", "Mixed", 2], ["murad.aliyev", "Male", 1]];
for (const [i, [user, partner, people]] of searches.entries()) {
  const s = await login(`${user}.wydemo@gmail.com`, demoPassword);
  const r = await fetch(`${BASE}/api/searching`, { method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${s.accessToken}` },
    body: JSON.stringify({ screeningId: ahead[i].id, partner, people, hasTickets: false }) });
  console.log(user, "searching", r.status, r.status >= 300 ? (await r.text()).slice(0, 160) : "");
}
