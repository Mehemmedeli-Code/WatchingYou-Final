// A small Chrome DevTools Protocol driver for headless Edge, phone-sized. Demo site only (7142).
import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";

export const BASE = "https://localhost:7142";
const EDGE = "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
const PORT = 9333;
export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

export async function launch({ width = 390, height = 844, scale = 1.2, mobile = true } = {}) {
  const profile = path.join(os.tmpdir(), `wy-edge-${process.pid}`);   // a fresh one per run: a closing Edge can hold the last one for a while
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

  // Signs the test customer in with this machine's test password (tools/test-logins.mjs; never printed).
  const signIn = async () => {
    const { customer: password } = (await import("../test-logins.mjs")).logins;
    await goto("/lang/az?returnUrl=%2Faccount", 1500);
    const status = await evaluate(`fetch("/api/auth/login", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: "customer@reelandrow.test", password: ${JSON.stringify(password)} }) }).then((r) => r.status)`);
    if (status !== 200) throw new Error("sign-in " + status);
  };

  const close = () => {
    try { ws.close(); } catch { /* gone */ }
    // The whole tree: killing only the parent left its helpers running, holding the profile.
    spawnSync("taskkill", ["/PID", String(edge.pid), "/T", "/F"], { stdio: "ignore" });
    try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }); } catch { /* Windows may hold it a moment longer; it is in %TEMP% */ }
  };
  return { send, on, evaluate, goto, shot, signIn, close };
}
