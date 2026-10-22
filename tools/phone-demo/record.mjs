// Records somebody using the demo site (7142) on a phone: real touch taps in headless Edge, the
// page scrolled the way a thumb would, captured with the DevTools screencast, then encoded into
// src/MovieRental.Host/wwwroot/media/phone-demo.mp4 (and its first frame, .jpg). Run by make.ps1.
import { launch, sleep } from "./cdp.mjs";
import path from "node:path";
import fs from "node:fs";
import os from "node:os";
import { execFileSync } from "node:child_process";

const W = 390, H = 844;
const dir = path.join(os.tmpdir(), "wy-phone-demo-frames");
fs.rmSync(dir, { recursive: true, force: true });
fs.mkdirSync(dir);

const b = await launch({ width: W, height: H, scale: 2 });
const frames = [];
b.on("Page.screencastFrame", ({ data, metadata, sessionId }) => {
  const file = `f${String(frames.length).padStart(5, "0")}.jpg`;
  fs.writeFileSync(path.join(dir, file), Buffer.from(data, "base64"));
  frames.push({ file, t: metadata.timestamp });
  b.send("Page.screencastFrameAck", { sessionId }).catch(() => {});
});

let started = Date.now();
const log = (...a) => console.log(((Date.now() - started) / 1000).toFixed(1).padStart(5), ...a);

// The centre of a visible match: a CSS selector, optionally narrowed by its text.
const find = (selector, text = "", nth = 0) => b.evaluate(`(() => {
  const all = [...document.querySelectorAll(${JSON.stringify(selector)})].filter((e) => {
    const r = e.getBoundingClientRect();
    return r.width > 0 && r.height > 0 && getComputedStyle(e).visibility !== "hidden" && e.textContent.includes(${JSON.stringify(text)});
  });
  const e = all[${nth < 0 ? `all.length + ${nth}` : nth}];
  if (!e) return null;
  const r = e.getBoundingClientRect();
  return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
})()`);

// A thumb's scroll: eases in and out. `inside` scrolls that element instead of the page.
const scroll = (dy, ms = Math.max(500, Math.abs(dy) * 1.6), inside = "") => b.evaluate(`new Promise((done) => {
  const box = ${JSON.stringify(inside)} ? document.querySelector(${JSON.stringify(inside)}) : null;
  const get = () => box ? box.scrollTop : scrollY;
  const set = (y) => box ? (box.scrollTop = y) : scrollTo({ top: y, behavior: "instant" });
  const y0 = get(), t0 = performance.now();
  const step = (now) => {
    const p = Math.min(1, (now - t0) / ${ms});
    set(y0 + ${dy} * (p < 0.5 ? 4 * p * p * p : 1 - Math.pow(-2 * p + 2, 3) / 2));
    p < 1 ? requestAnimationFrame(step) : done();
  };
  requestAnimationFrame(step);
})`);

async function bring(selector, text = "", nth = 0, at = 0.5, ms) {
  const el = await find(selector, text, nth);
  if (!el) { log("not found:", selector, text); return null; }
  const dy = el.y - H * at;
  if (Math.abs(dy) > 30) { await scroll(dy, ms); await sleep(300); }
  return find(selector, text, nth);
}

// A finger: a soft circle presses, the tap lands, the circle spreads and fades.
async function tap(x, y) {
  await b.evaluate(`(() => {
    const d = document.createElement("div");
    d.style.cssText = "position:fixed;left:${x - 24}px;top:${y - 24}px;width:48px;height:48px;border-radius:50%;" +
      "background:rgba(255,255,255,.28);border:2px solid rgba(255,255,255,.85);box-shadow:0 0 18px rgba(0,230,118,.55);" +
      "z-index:2147483647;pointer-events:none;transform:scale(.55);opacity:0;transition:transform .18s ease-out,opacity .18s ease-out";
    document.documentElement.appendChild(d);
    requestAnimationFrame(() => { d.style.transform = "scale(.9)"; d.style.opacity = "1"; });
    setTimeout(() => { d.style.transition = "transform .5s ease-out,opacity .5s ease-out"; d.style.transform = "scale(1.5)"; d.style.opacity = "0"; }, 260);
    setTimeout(() => d.remove(), 900);
  })()`);
  await sleep(220);
  await b.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: [{ x, y }] });
  await sleep(70);
  await b.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
}

async function tapOn(selector, text = "", nth = 0, at = 0.5) {
  const el = await bring(selector, text, nth, at);
  if (!el) return false;
  await tap(el.x, el.y);
  return true;
}

// A sideways finger drag, with the circle following it (the poster ribbon turns under it).
async function swipe(x0, x1, y, ms = 450) {
  const steps = 16;
  await b.evaluate(`(() => {
    const d = document.createElement("div"); d.id = "finger";
    d.style.cssText = "position:fixed;left:${x0 - 24}px;top:${y - 24}px;width:48px;height:48px;border-radius:50%;background:rgba(255,255,255,.28);" +
      "border:2px solid rgba(255,255,255,.85);box-shadow:0 0 18px rgba(0,230,118,.55);z-index:2147483647;pointer-events:none;transition:opacity .3s";
    document.documentElement.appendChild(d);
  })()`);
  await sleep(120);
  await b.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: [{ x: x0, y }] });
  for (let i = 1; i <= steps; i++) {
    await sleep(ms / steps);
    const x = x0 + ((x1 - x0) * i) / steps;
    await b.send("Input.dispatchTouchEvent", { type: "touchMove", touchPoints: [{ x, y }] });
    await b.evaluate(`document.getElementById("finger").style.left = "${x - 24}px"`);
  }
  await b.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
  await b.evaluate(`(() => { const d = document.getElementById("finger"); d.style.opacity = "0"; setTimeout(() => d.remove(), 350); })()`);
}

async function type(text) {
  for (const ch of [...text]) { await b.send("Input.insertText", { text: ch }); await sleep(85 + Math.random() * 70); }
}

try {
  await b.signIn();
  await b.goto("/", 3500);
  await b.send("Page.startScreencast", { format: "jpeg", quality: 92, maxWidth: W * 2, maxHeight: H * 2, everyNthFrame: 1 });
  started = Date.now();

  // 1. The catalogue: through the hero, past the offers, to the poster ribbon.
  await sleep(1300);
  await scroll(1250, 3400); log("hero");
  const ribbon = await bring("[aria-roledescription=carousel]", "", -1, 0.45, 900);
  await sleep(500);
  if (ribbon) {
    await swipe(300, 110, ribbon.y); await sleep(950);
    await swipe(300, 110, ribbon.y); await sleep(950); log("ribbon");
  }

  // 2. A film card: save it, then open it.
  const heart = await bring("article button[aria-label*='siyahısına']", "", 0, 0.3, 1100);
  if (heart) { await sleep(300); await tap(heart.x, heart.y); await sleep(900); log("heart"); }
  if (await tapOn("article button", "Ətraflı", 0, 0.62)) { await sleep(2800); log("details"); }
  const close = await find("[role=dialog] button[aria-label='Bağla']");
  if (close) { await tap(close.x, close.y); await sleep(700); }
  else { await b.send("Input.dispatchKeyEvent", { type: "keyDown", key: "Escape", code: "Escape", windowsVirtualKeyCode: 27 }); await sleep(600); }

  // 3. What is on: the map, then a screening, then seats.
  await scroll(-(await b.evaluate("scrollY")), 900); await sleep(300);
  if (await tapOn(".rr-nav a[href='/on-display']", "", 0, 0.08)) { await sleep(2600); log("on-display"); }
  await scroll(430, 1000); await sleep(500);
  if (await tapOn("a[href^='/cinema?screening='] button", "", 0, 0.62)) { await sleep(2700); log("cinema"); }
  const seat = (n) => `button[aria-label^='D sırası, ${n} yeri']`;
  if (await bring(seat(4), "", 0, 0.5, 1000)) {
    await sleep(300);
    for (const n of [3, 4, 5]) {
      const s = await find(seat(n));
      if (s) { await tap(s.x, s.y); await sleep(480); }
    }
    await sleep(1200); log("seats", await b.evaluate("document.querySelectorAll('button[aria-pressed=true]').length"));
  }

  // 4. Messages from the header: open the newest, answer it.
  await scroll(-(await b.evaluate("scrollY")), 900); await sleep(300);
  if (await tapOn("button.rr-inbox", "", 0, 0.05)) { await sleep(1500); log("inbox"); }
  if (await tapOn("[role=dialog] button", "Aç", 0, 0.4)) { await sleep(1800); log("chat"); }
  const box = await find("[role=dialog] textarea, [role=dialog] input[type=text], [role=dialog] input:not([type])", "", -1);
  if (box) {
    await tap(box.x, box.y); await sleep(400);
    await type("Gəlirəm! 🍿");
    await sleep(350);
    const sendButton = await b.evaluate(`(() => {
      const field = [...document.querySelectorAll("[role=dialog] textarea, [role=dialog] input")].filter((e) => e.offsetParent).pop();
      const button = field?.closest("form")?.querySelector("button[type=submit]") ?? field?.parentElement?.parentElement?.querySelector("button:last-of-type");
      if (!button) return null;
      const r = button.getBoundingClientRect();
      return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
    })()`);
    if (sendButton) await tap(sendButton.x, sendButton.y);
    else await b.send("Input.dispatchKeyEvent", { type: "keyDown", key: "Enter", code: "Enter", windowsVirtualKeyCode: 13, text: "\r" });
    await sleep(2000); log("sent");
  }

  await b.send("Page.stopScreencast");
  log("frames", frames.length);
} finally { b.close(); }

// Each frame held as long as it was on screen, at 30 fps, with a short fade at both ends so the
// loop starts over softly.
let list = "ffconcat version 1.0\n";
frames.forEach((f, i) => { list += `file ${f.file}\nduration ${((frames[i + 1]?.t ?? f.t + 0.6) - f.t).toFixed(4)}\n`; });
list += `file ${frames.at(-1).file}\n`;
fs.writeFileSync(path.join(dir, "list.ffconcat"), list);
const length = frames.at(-1).t - frames[0].t + 0.6;
const media = path.resolve(import.meta.dirname, "../../src/MovieRental.Host/wwwroot/media");
execFileSync("ffmpeg", ["-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", path.join(dir, "list.ffconcat"),
  "-vf", `fps=30,format=yuv420p,fade=t=in:st=0:d=0.4,fade=t=out:st=${(length - 0.55).toFixed(2)}:d=0.5`,
  "-c:v", "libx264", "-preset", "slow", "-crf", "25", "-profile:v", "high", "-tune", "animation", "-movflags", "+faststart", "-an",
  path.join(media, "phone-demo.mp4")]);
execFileSync("ffmpeg", ["-v", "error", "-y", "-ss", "0.6", "-i", path.join(media, "phone-demo.mp4"), "-frames:v", "1", "-q:v", "4", path.join(media, "phone-demo.jpg")]);
console.log(`phone-demo.mp4: ${length.toFixed(1)} s, ${(fs.statSync(path.join(media, "phone-demo.mp4")).size / 1e6).toFixed(1)} MB`);
