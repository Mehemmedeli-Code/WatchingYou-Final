// A small Chrome DevTools Protocol driver for headless Edge, phone-sized. Demo site only (7142).
import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";

export const BASE = "https://localhost:7142";
const EDGE = "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
const PORT = 9333;
export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

export async function launch({ width = 390, height = 844, scale = 1.2, mobile = true } = {}) {
  const profile = path.join(os.tmpdir(), "wy-phone-demo-edge");
  fs.rmSync(profile, { recursive: true, force: true });
  const edge = spawn(EDGE, [
    "--headless=new", `--remote-debugging-port=${PORT}`, `--user-data-dir=${profile}`,
    "--no-first-run", "--no-default-browser-check", "--ignore-certificate-errors",
    "--hide-scrollbars", "--mute-audio", "--autoplay-policy=no-user-gesture-required",
    "--enable-unsafe-swiftshader", `--window-size=${Math.round(width * scale)},${Math.round(height * scale)}`, "about:blank",
  ], { stdio: "ignore" });

  let targets;
  for (let i = 0; i < 50; i++) {
    try { targets = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json(); if (targets.some((t) => t.type === "page")) break; } catch { /* not up yet */ }
    await sleep(200);
  }
  const page = targets.find((t) => t.type === "page");
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((r) => ws.addEventListener("open", r, { once: true }));

  let id = 0;
  const pending = new Map();
  const listeners = new Map();
  ws.addEventListener("message", (event) => {
    const msg = JSON.parse(event.data);
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      msg.error ? reject(new Error(JSON.stringify(msg.error))) : resolve(msg.result);
    } else if (msg.method) {
      for (const fn of listeners.get(msg.method) ?? []) fn(msg.params);
    }
  });
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const n = ++id;
    pending.set(n, { resolve, reject });
    ws.send(JSON.stringify({ id: n, method, params }));
  });
  const on = (method, fn) => listeners.set(method, [...(listeners.get(method) ?? []), fn]);

  await send("Page.enable");
  await send("Runtime.enable");
  await send("Emulation.setDeviceMetricsOverride", { width, height, deviceScaleFactor: scale, mobile });
  if (mobile) await send("Emulation.setTouchEmulationEnabled", { enabled: true, maxTouchPoints: 5 });

  const evaluate = async (expression) => {
    const r = await send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description ?? r.exceptionDetails.text);
    return r.result.value;
  };

  const goto = async (url, settle = 2500) => {
    const loaded = new Promise((r) => on("Page.loadEventFired", r));
    await send("Page.navigate", { url: url.startsWith("http") ? url : BASE + url });
    await Promise.race([loaded, sleep(15000)]);
    await sleep(settle);
  };

  const shot = async (file) => {
    const { data } = await send("Page.captureScreenshot", { format: "png" });
    fs.writeFileSync(file, Buffer.from(data, "base64"));
  };

  // Signs the test customer in with the password from tools/test-accounts.md (never printed).
  const signIn = async () => {
    const table = fs.readFileSync(path.resolve(import.meta.dirname, "../test-accounts.md"), "utf8");
    const row = table.split("\n").find((l) => l.includes("customer@reelandrow.test"));
    const password = row.split("|").map((c) => c.trim()).filter(Boolean).pop();
    await goto("/lang/az?returnUrl=%2Faccount", 1500);
    const status = await evaluate(`fetch("/api/auth/login", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: "customer@reelandrow.test", password: ${JSON.stringify(password)} }) }).then((r) => r.status)`);
    if (status !== 200) throw new Error("sign-in " + status);
  };

  const close = () => { try { ws.close(); } catch { /* gone */ } edge.kill(); };
  return { send, on, evaluate, goto, shot, signIn, close };
}
