// Demo site (7142) only: three demo people write to the test customer, so the inbox has life in it.
import fs from "node:fs";
import path from "node:path";
const BASE = "https://localhost:7142";
const repo = path.resolve(import.meta.dirname, "../..");
const demoPassword = /Password = "([^"]+)"/.exec(fs.readFileSync(`${repo}/src/MovieRental.Host/Infrastructure/DemoPeople.cs`, "utf8"))[1];
const customerPassword = fs.readFileSync(`${repo}/tools/test-accounts.md`, "utf8").split("\n")
  .find((l) => l.includes("customer@reelandrow.test")).split("|").map((c) => c.trim()).filter(Boolean).pop();

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
