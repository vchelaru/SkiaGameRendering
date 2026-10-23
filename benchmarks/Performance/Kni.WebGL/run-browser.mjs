// Runs one of the browser performance apps in a browser and saves its results file.
//
//   npm install            (once, in this folder)
//   node run-browser.mjs --url http://127.0.0.1:5098/ --browser chromium --channel chrome --out results
//   node run-browser.mjs --url http://127.0.0.1:5098/ --browser firefox --firefox-path "C:\Program Files\Mozilla Firefox\firefox.exe" --out results
//
// Serve the app first (see docs/performance.md). Options: --page <slug> and --quick limit the run;
// --os, --cpu, --power and --commit fill in what a browser cannot see (the CPU model, whether the machine
// is plugged in), so the results header matches the desktop runs on the same machine.
//
// --channel picks the Chromium build: chrome, msedge, or chromium (Playwright's own). Chromium is driven with Playwright and started with its frame-rate limit and vsync off, on the real GPU.
// Firefox is driven over WebDriver BiDi without Playwright, so a stock Firefox install works; pass its exe
// with --firefox-path. Without it, Playwright's own Firefox is used.
import { spawn } from "node:child_process";
import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const options = {};
for (let i = 2; i < process.argv.length; i++) {
  const arg = process.argv[i];
  if (!arg.startsWith("--")) continue;
  const next = process.argv[i + 1];
  if (next === undefined || next.startsWith("--")) options[arg.slice(2)] = true;
  else { options[arg.slice(2)] = next; i++; }
}

const browser = options.browser ?? "chromium";
const out = options.out ?? "results";
const query = new URLSearchParams();
if (options.page) query.set("page", options.page);
if (options.quick) query.set("quick", "1");
const url = `${options.url}${query.size ? "?" + query : ""}`;
const info = { os: options.os, cpu: options.cpu, power: options.power, commit: options.commit };
const timeoutMs = 60 * 60 * 1000;
const sleep = ms => new Promise(r => setTimeout(r, ms));

mkdirSync(out, { recursive: true });

function save(results) {
  for (const [name, markdown] of Object.entries(results)) {
    writeFileSync(join(out, name), markdown);
    console.log("saved", join(out, name));
  }
}

async function runChromium() {
  const { chromium } = await import("playwright");
  const instance = await chromium.launch({
    channel: options.channel ?? "chromium",
    headless: true,
    args: [
      "--use-angle=d3d11", "--enable-gpu", "--ignore-gpu-blocklist", "--enable-webgl",
      "--disable-frame-rate-limit", "--disable-gpu-vsync", "--disable-renderer-backgrounding",
      "--disable-background-timer-throttling",
    ],
  });
  const context = await instance.newContext({ viewport: { width: 1600, height: 900 }, deviceScaleFactor: 1 });
  await context.addInitScript(i => { window.__perfInfo = i; }, info);
  const page = await context.newPage();
  page.on("console", m => { if (m.type() === "error") console.log("[page]", m.text()); });
  page.on("pageerror", e => console.log("[pageerror]", e.message));
  await page.goto(url);
  await page.waitForFunction(() => window.__perfDone === true, null, { timeout: timeoutMs, polling: 2000 });
  const results = await page.evaluate(() => window.__perfResults);
  await instance.close();
  return results;
}

async function runFirefoxBidi(firefoxPath) {
  const port = 9333 + Math.floor(Math.random() * 200);
  const profile = mkdtempSync(join(tmpdir(), "ffprof-"));
  writeFileSync(join(profile, "user.js"), [
    'user_pref("remote.active-protocols", 3);',
    'user_pref("datareporting.policy.dataSubmissionEnabled", false);',
    // A fixed 1000 Hz timer instead of the display's refresh rate, so requestAnimationFrame is not capped at 60.
    'user_pref("layout.frame_rate", 1000);',
  ].join("\n"));
  const process_ = spawn(firefoxPath, ["-no-remote", "-profile", profile, `--remote-debugging-port=${port}`, "-headless", "about:blank"], { stdio: "ignore" });

  let ws;
  for (let i = 0; i < 60 && !ws; i++) {
    try {
      ws = await new Promise((res, rej) => { const s = new WebSocket(`ws://127.0.0.1:${port}/session`); s.onopen = () => res(s); s.onerror = rej; });
    } catch { await sleep(500); }
  }
  if (!ws) { process_.kill(); throw new Error("Could not connect to Firefox over BiDi."); }

  let id = 0;
  const pending = new Map();
  ws.onmessage = ev => { const m = JSON.parse(ev.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } };
  const send = (method, params = {}) => new Promise(res => { const i = ++id; pending.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  const evaluate = async (context, expression) => {
    const r = await send("script.evaluate", { expression, target: { context }, awaitPromise: true, resultOwnership: "none" });
    if (r.error || r.result?.type === "exception") throw new Error(JSON.stringify(r).slice(0, 400));
    return r.result.result.value;
  };

  await send("session.new", { capabilities: {} });
  const tree = await send("browsingContext.getTree", {});
  const context = tree.result.contexts[0].context;
  await send("browsingContext.setViewport", { context, viewport: { width: 1600, height: 900 }, devicePixelRatio: 1 });
  await send("script.addPreloadScript", { functionDeclaration: `() => { window.__perfInfo = ${JSON.stringify(info)}; }` });
  await send("browsingContext.navigate", { context, url, wait: "none" });

  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    await sleep(2000);
    try {
      if (await evaluate(context, "window.__perfDone === true")) break;
    } catch { /* the page is still loading */ }
  }
  const json = await evaluate(context, "JSON.stringify(window.__perfResults ?? null)");
  ws.close();
  process_.kill();
  return JSON.parse(json);
}

async function runPlaywrightFirefox() {
  const { firefox } = await import("playwright");
  const instance = await firefox.launch({ firefoxUserPrefs: { "webgl.force-enabled": true, "layout.frame_rate": 1000 } });
  const context = await instance.newContext({ viewport: { width: 1600, height: 900 }, deviceScaleFactor: 1 });
  await context.addInitScript(i => { window.__perfInfo = i; }, info);
  const page = await context.newPage();
  await page.goto(url);
  await page.waitForFunction(() => window.__perfDone === true, null, { timeout: timeoutMs, polling: 2000 });
  const results = await page.evaluate(() => window.__perfResults);
  await instance.close();
  return results;
}

const results = browser === "chromium" ? await runChromium()
  : options["firefox-path"] ? await runFirefoxBidi(options["firefox-path"])
  : await runPlaywrightFirefox();
if (!results) throw new Error("The page finished without results.");
save(results);
process.exit(0);
