const { test, expect } = require("@playwright/test");
const layout = require("./direct-layout");

// Sample.Kni.WebGL in its SkiaDirectCanvas modes (?mode=direct, ?mode=scene): Skia draws straight into
// KNI's own WebGL context, between KNI's draws. The pixels are read back from KNI's canvas, so what is
// checked is what is on screen.

const diagnostics = page => page.locator("#diagnostic-text").textContent();
const number = (text, name) => Number(text.match(new RegExp(`${name} (\\d+)`))?.[1] ?? NaN);

// Opens the sample and waits for ten frames. Returns the page's uncaught errors, filled in as they happen,
// and the GL errors Skia has left behind so far: Firefox raises one INVALID_OPERATION once, from Skia's
// color-read-format capability query on the first pass, which is harmless. What a test checks is that no
// more arrive (a stale state KNI did not reset shows up here first).
async function open(page, mode) {
  const session = { errors: [], glErrorsAtStart: 0 };
  page.on("pageerror", error => session.errors.push(error.message));
  await page.goto(`/?mode=${mode}`);
  await expect(page.locator("#diagnostics")).toContainText("WebGL 2");
  await expect(page.locator("#diagnostic-text")).toContainText("SkiaDirectCanvas");
  await waitForFrames(page, 10);
  session.glErrorsAtStart = await directGlErrors(page);
  return session;
}

async function waitForFrames(page, count) {
  const frames = async () => number(await diagnostics(page), "frames");
  const target = (await frames()) + count;
  await expect.poll(frames, { timeout: 60000 }).toBeGreaterThanOrEqual(target);
}

const readPixels = (page, points) => page.evaluate(`(${layout.readPixelsInPage})(${JSON.stringify(points)})`);

async function expectRegion(page, index) {
  const size = await page.locator("#theCanvas").evaluate(element => ({ width: element.width, height: element.height }));
  const probes = layout.probesFor(index, size.width, size.height);
  const read = await readPixels(page, probes.map(([, x, y]) => [x, y]));
  expect(read.width, "the canvas changed size while it was being read").toBe(size.width);
  expect(layout.mismatches(probes, read.pixels), layout.regions[index].name).toEqual([]);
}

// The Scene's first two cells and a corner nothing draws on: a red circle, the blue water drop, black.
async function expectScene(page) {
  const size = await page.locator("#theCanvas").evaluate(element => ({ width: element.width, height: element.height }));
  const cell = Math.min(size.width, size.height) / 2;
  const points = [[cell / 2, cell / 2], [cell * 1.5, cell / 2], [size.width - 4, size.height - 4]].map(p => p.map(Math.floor));
  const { pixels: [circle, drop, outside] } = await readPixels(page, points);
  expect(circle[0], `circle ${circle}`).toBeGreaterThan(200);
  expect(circle[1], `circle ${circle}`).toBeLessThan(50);
  expect(circle[2], `circle ${circle}`).toBeLessThan(50);
  expect(drop[0], `drop ${drop}`).toBeLessThan(100);
  expect(drop[2], `drop ${drop}`).toBeGreaterThan(150);
  expect(outside.slice(0, 3), `outside ${outside}`).toEqual([0, 0, 0]);
}

const directGlErrors = page => page.evaluate(() => globalThis.skiaGameWebGl.getDirectGlErrorCount());

async function expectNoNewGlErrors(page, session) {
  const log = await page.evaluate(() => globalThis.skiaGameWebGl.getDirectGlErrorLog());
  expect(await directGlErrors(page), `GL errors Skia left in KNI's queue after the first frames: ${log}`).toBe(session.glErrorsAtStart);
}

async function expectAllRegions(page) {
  await expectScene(page);
  for (let index = 0; index < layout.regions.length; index++) {
    if (layout.regions[index].msaa && number(await diagnostics(page), "msaa") !== 4)
      continue; // The browser gave no multisampled render target.
    await expectRegion(page, index);
  }
}

test("draws the Scene straight into KNI's back buffer", async ({ page }) => {
  const session = await open(page, "direct");
  await test.info().attach("scene", { body: await page.locator("#theCanvas").screenshot(), contentType: "image/png" });
  await expectScene(page);
  expect(number(await diagnostics(page), "skipped")).toBe(0);
  await expectNoNewGlErrors(page, session);
  expect(session.errors).toEqual([]);
});

test("layers Skia between KNI's draws on the back buffer and on render targets", async ({ page }) => {
  const session = await open(page, "direct");
  await expectAllRegions(page);
  await expectNoNewGlErrors(page, session);
  expect(session.errors).toEqual([]);
});

test("keeps layering correctly over many frames", async ({ page }) => {
  const session = await open(page, "direct");
  await waitForFrames(page, 150);
  await expectAllRegions(page);
  const text = await diagnostics(page);
  expect(number(text, "skipped")).toBe(0);
  await expectNoNewGlErrors(page, session);
  expect(session.errors).toEqual([]);
});

test("switches between the render-target path and SkiaDirectCanvas", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator("#diagnostics")).toContainText("WebGL 2");
  await expect(page.locator("#diagnostic-text")).toContainText("frames");
  await expectScene(page);

  for (const mode of ["direct", "texture", "direct", "scene", "texture"]) {
    await page.locator("#draw-mode").selectOption(mode);
    await expect(page.locator("#diagnostic-text")).toContainText(mode === "texture" ? "texSubImage2D(canvas)" : "SkiaDirectCanvas");
    await waitForFrames(page, 5);
    await expectScene(page);
    if (mode === "direct")
      await expectAllRegions(page);
  }
  // Skia's one harmless capability-query error (see open), once for the one Skia context all modes share.
  expect(await directGlErrors(page), "GL errors Skia left in KNI's queue").toBeLessThanOrEqual(1);
});

test("recreates the backend and keeps drawing", async ({ page }) => {
  const session = await open(page, "direct");
  for (let round = 0; round < 3; round++) {
    await page.locator("#recreate").click();
    // The new backend's diagnostics start from zero.
    await expect.poll(async () => number(await diagnostics(page), "direct"), { timeout: 60000 }).toBeGreaterThan(18);
    await expectAllRegions(page);
  }
  expect(number(await diagnostics(page), "skipped")).toBe(0);
  // At most that one harmless error (see open) for each of the three Skia contexts the recreations built.
  expect(await directGlErrors(page), "GL errors Skia left in KNI's queue").toBeLessThanOrEqual(session.glErrorsAtStart + 3);
  expect(session.errors).toEqual([]);
});

// Only the Scene is checked: KNI's BlazorGL platform cannot resize its back buffer (its ChangeClientSize is
// an empty stub), and at a device pixel ratio other than 1 its own draws land shifted after the canvas
// changes size, in the render-target path as much as here. Skia's side has to follow the canvas, which
// means rebuilding its surface on the new drawing buffer.
test("follows a canvas resize", async ({ page }) => {
  const session = await open(page, "direct");
  const widthBefore = await page.locator("#theCanvas").evaluate(element => element.width);
  await page.setViewportSize({ width: 1000, height: 640 });
  await expect.poll(() => page.locator("#theCanvas").evaluate(element => element.width), { timeout: 30000 }).not.toBe(widthBefore);
  await waitForFrames(page, 5);
  await expectScene(page);
  await page.setViewportSize({ width: 1400, height: 800 });
  await waitForFrames(page, 5);
  await expectScene(page);
  expect(number(await diagnostics(page), "skipped")).toBe(0);
  expect(session.errors).toEqual([]);
});

test("ignores the loss of the host's hidden canvas", async ({ page }) => {
  const session = await open(page, "direct");
  await page.locator('canvas[id^="skia-game-source-"]').evaluate(async canvas => {
    const extension = canvas.getContext("webgl2").getExtension("WEBGL_lose_context");
    if (!extension) throw new Error("WEBGL_lose_context is unavailable");
    extension.loseContext();
    await new Promise(resolve => setTimeout(resolve, 200));
    extension.restoreContext();
  });
  await waitForFrames(page, 20);
  await expectAllRegions(page);
  expect(number(await diagnostics(page), "skipped")).toBe(0);
  expect(session.errors).toEqual([]);
});

// KNI does not recover from a lost context itself (its buffers, programs and textures are gone), so this
// runs the Scene-only mode, which uses no KNI resource: SkiaDirectCanvas has to report the loss instead of
// throwing from inside GL, then rebuild Skia's context and surfaces once the context comes back.
test("reports a lost KNI context and draws again once it is restored", async ({ page }) => {
  const session = await open(page, "scene");
  await expectScene(page);

  await page.locator("#theCanvas").evaluate(canvas => {
    // Without this the browser never restores the context.
    canvas.addEventListener("webglcontextlost", event => event.preventDefault());
    globalThis.__loseContext = canvas.getContext("webgl2").getExtension("WEBGL_lose_context");
    globalThis.__loseContext.loseContext();
  });
  await expect(page.locator("#diagnostic-text")).toContainText("is lost");
  expect(number(await diagnostics(page), "skipped")).toBeGreaterThan(0);

  await page.evaluate(() => globalThis.__loseContext.restoreContext());
  await expect(page.locator("#diagnostic-text")).toContainText("directloss 1");
  const skippedAtRestore = number(await diagnostics(page), "skipped");
  await waitForFrames(page, 10);
  await expectScene(page);
  expect(number(await diagnostics(page), "skipped"), "frames skipped after the restore").toBe(skippedAtRestore);
  expect(session.errors).toEqual([]);
});

// The WebAssembly heap holds Skia's native allocations and .NET's managed heap, and it only ever grows, so
// after a warm-up any growth over hundreds of frames is a leak. Each frame here is nine passes, five of
// them with a KNI state reset.
test("draws hundreds of frames without growing the WebAssembly heap", async ({ page }) => {
  test.skip(test.info().project.name !== "scene-chromium", "One browser is enough; the heap is the runtime's, not the browser's.");
  const heapBytes = () => page.evaluate(() => {
    const runtime = globalThis.getDotnetRuntime?.(0) ?? globalThis.Blazor?.runtime;
    return runtime?.Module?.HEAPU8?.byteLength ?? null;
  });

  await open(page, "direct");
  await waitForFrames(page, 120);
  const start = await heapBytes();
  expect(start, "the .NET runtime's WebAssembly heap should be reachable").not.toBeNull();
  await waitForFrames(page, 300);
  const middle = await heapBytes();
  await waitForFrames(page, 300);
  const end = await heapBytes();

  const mb = bytes => (bytes / (1024 * 1024)).toFixed(1);
  console.log(`WebAssembly heap over 600 direct frames: ${mb(start)} -> ${mb(middle)} -> ${mb(end)} MB`);
  expect(end - middle, `heap grew from ${mb(middle)} to ${mb(end)} MB over the last 300 frames`).toBe(0);
});
