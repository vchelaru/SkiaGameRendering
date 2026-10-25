const { test, expect } = require("@playwright/test");

// Sample.Raylib.WebGL draws samples/Shared/Scene.cs on an 800x600 canvas, through a SkiaRenderTarget2D
// (?mode=target, the default) or SkiaDirectCanvas (?mode=direct). Pixels are read back from raylib's own
// WebGL context, so what is checked is what is on screen. Scene's cells are 300 px: a red circle in the
// first, the blue SVG water drop in the second. Direct mode also layers raylib and Skia draws on the canvas and
// on a RenderTexture2D; the rectangles and colors below repeat Game.DrawDirect in samples/Sample.Raylib.WebGL.

const WIDTH = 800, HEIGHT = 600;
const TOLERANCE = 3;

const diagnostics = page => page.locator("#diagnostic-text").textContent();
const frames = async page => Number((await diagnostics(page)).match(/frames (\d+)/)?.[1] ?? NaN);

async function open(page, mode) {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto(`/?mode=${mode}`);
  await expect.poll(() => frames(page), { timeout: 60000 }).toBeGreaterThanOrEqual(10);
  return errors;
}

async function waitForFrames(page, count) {
  const target = (await frames(page)) + count;
  await expect.poll(() => frames(page), { timeout: 60000 }).toBeGreaterThanOrEqual(target);
}

// Reads on the next animation frame, after the page's own callback has drawn it: the drawing buffer is cleared
// once the frame is presented. points are [x, y] from the top left.
const readPixels = (page, points) => page.evaluate(points => new Promise(resolve => requestAnimationFrame(() => {
  const canvas = document.getElementById("canvas");
  const gl = canvas.getContext("webgl2");
  const pixels = points.map(([x, y]) => {
    const pixel = new Uint8Array(4);
    gl.readPixels(x, canvas.height - 1 - y, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel); // rows count from the bottom
    return Array.from(pixel);
  });
  resolve({ width: canvas.width, height: canvas.height, pixels });
})), points);

const countWhiteIn = (page, x, y, width, height) => page.evaluate(([x, y, width, height]) => new Promise(resolve => requestAnimationFrame(() => {
  const canvas = document.getElementById("canvas");
  const gl = canvas.getContext("webgl2");
  const data = new Uint8Array(width * height * 4);
  gl.readPixels(x, canvas.height - y - height, width, height, gl.RGBA, gl.UNSIGNED_BYTE, data);
  let white = 0;
  for (let i = 0; i < data.length; i += 4)
    if (data[i] > 200 && data[i + 1] > 200 && data[i + 2] > 200)
      white++;
  resolve(white);
})), [x, y, width, height]);

const glErrors = page => page.evaluate(() => globalThis.skiaRaylibWebGl.glErrorCount());

async function expectColors(page, probes) {
  const { width, height, pixels } = await readPixels(page, probes.map(([, x, y]) => [x, y]));
  expect([width, height], "the canvas changed size while it was being read").toEqual([WIDTH, HEIGHT]);
  const wrong = [];
  probes.forEach(([label, x, y, expected], i) => {
    const actual = pixels[i];
    if (expected.some((channel, c) => Math.abs(channel - actual[c]) > TOLERANCE) || actual[3] < 252)
      wrong.push(`${label} at (${x}, ${y}): expected ${expected}, got ${actual.slice(0, 3)} alpha ${actual[3]}`);
  });
  expect(wrong).toEqual([]);
}

async function expectScene(page) {
  const { pixels: [circle, drop, outside] } = await readPixels(page, [[150, 150], [450, 150], [WIDTH - 4, HEIGHT - 4]]);
  expect(circle[0], `circle ${circle}`).toBeGreaterThan(200);
  expect(circle[1], `circle ${circle}`).toBeLessThan(50);
  expect(circle[2], `circle ${circle}`).toBeLessThan(50);
  expect(drop[0], `drop ${drop}`).toBeLessThan(100);
  expect(drop[2], `drop ${drop}`).toBeGreaterThan(150);
  expect(outside.slice(0, 3), `outside ${outside}`).toEqual([0, 0, 0]);
}

// raylib's own text, drawn after the Skia pass: its texture state has to survive Skia.
async function expectText(page) {
  expect(await countWhiteIn(page, 10, 10, 160, 20), "white pixels of raylib's text").toBeGreaterThan(40);
}

// Green raylib rectangle, Skia's pink strip over its right half, a yellow raylib rectangle over the strip.
const layering = [
  ["raylib green", 640, 80, [30, 180, 40]],
  ["raylib green left of the strip", 690, 50, [30, 180, 40]],
  ["Skia strip over raylib", 720, 110, [210, 50, 90]],
  ["Skia strip below the yellow", 760, 110, [210, 50, 90]],
  ["Skia strip beside the yellow", 720, 50, [210, 50, 90]],
  ["raylib yellow over Skia", 760, 50, [240, 220, 10]],
];

// The RenderTexture2D, drawn at (620, 160): raylib clears it, Skia draws a square, raylib draws one over it.
const renderTexture = [
  ["raylib clear", 630, 270, [40, 90, 160]],
  ["Skia square", 645, 185, [220, 40, 40]],
  ["Skia square beside raylib's", 680, 190, [220, 40, 40]],
  ["raylib square over Skia's", 685, 225, [40, 200, 80]],
  ["raylib square", 705, 245, [40, 200, 80]],
];

test("draws the shared Scene through SkiaRenderTarget2D", async ({ page }) => {
  const errors = await open(page, "target");
  await test.info().attach("scene", { body: await page.locator("#canvas").screenshot(), contentType: "image/png" });
  await expectScene(page);
  await expectText(page);
  expect(errors).toEqual([]);
});

test("draws the shared Scene with SkiaDirectCanvas", async ({ page }) => {
  const errors = await open(page, "direct");
  await test.info().attach("scene", { body: await page.locator("#canvas").screenshot(), contentType: "image/png" });
  await expectScene(page);
  await expectText(page);
  expect(errors).toEqual([]);
});

test("layers Skia between raylib's draws on the canvas", async ({ page }) => {
  const errors = await open(page, "direct");
  await expectColors(page, layering);
  expect(errors).toEqual([]);
});

test("layers Skia between raylib's draws on a RenderTexture2D", async ({ page }) => {
  const errors = await open(page, "direct");
  await expectColors(page, renderTexture);
  expect(errors).toEqual([]);
});

for (const mode of ["target", "direct"]) {
  test(`keeps drawing correctly over many frames (${mode})`, async ({ page }) => {
    const errors = await open(page, mode);
    const errorsAtStart = await glErrors(page);
    await waitForFrames(page, 150);
    await expectScene(page);
    await expectText(page);
    if (mode === "direct") {
      await expectColors(page, layering);
      await expectColors(page, renderTexture);
    }
    // Errors Skia leaves queued are cleared after each pass; more of them than at the start means a pass failed.
    expect(await glErrors(page), "GL errors Skia left behind after the first frames").toBe(errorsAtStart);
    expect(errors).toEqual([]);
  });
}

// raylib's resources die with the context and are not rebuilt, so a lost context must fail every pass with a clear
// message, and keep failing after the browser restores it (Skia's cached GL objects are gone too).
test("fails clearly when the WebGL context is lost and stays failed after it is restored", async ({ page }) => {
  const errors = await open(page, "target");
  await page.evaluate(() => {
    const canvas = document.getElementById("canvas");
    canvas.addEventListener("webglcontextlost", event => event.preventDefault());
    // The extension object is kept: getExtension returns null while the context is lost.
    globalThis.loseContext = canvas.getContext("webgl2").getExtension("WEBGL_lose_context");
    globalThis.loseContext.loseContext();
  });
  await expect.poll(() => errors.join(" | ")).toContain("context was lost");

  await page.evaluate(() => new Promise(resolve => {
    const canvas = document.getElementById("canvas");
    canvas.addEventListener("webglcontextrestored", resolve);
    globalThis.loseContext.restoreContext();
  }));
  const afterRestore = await page.evaluate(() => {
    try { return DotNet.invokeMethod("Sample.Raylib.WebGL", "Frame", "target") ?? "returned"; }
    catch (error) { return String(error.message ?? error); }
  });
  expect(afterRestore).toContain("context was lost");
});

// SKColorType: Alpha8 = 1, Rgb565 = 2, Rgba8888 = 4, Bgra8888 = 6.
test("accepts Rgba8888 render targets and rejects other color types with NotSupportedException", async ({ page }) => {
  await open(page, "target");
  const tryColorType = color => page.evaluate(c => DotNet.invokeMethod("Sample.Raylib.WebGL", "TryColorType", c), color);
  expect(await tryColorType(4)).toBe("ok");
  for (const color of [1, 2, 6])
    expect(await tryColorType(color), `SKColorType ${color}`).toMatch(/^NotSupportedException: .*Rgba8888/);
});

// ?es2=1 hides OES_vertex_array_object from raylib (samples/Sample.Raylib.WebGL/wwwroot/js/game-loop.js), so its ES2
// build re-specifies vertex attributes on every draw instead of binding a VAO. Skia overwrites those attributes in
// each pass and the interop puts them back, so raylib's draws after a Skia pass must still be right.
for (const mode of ["target", "direct"]) {
  test(`keeps drawing correctly with raylib's ES2 no-VAO path hidden (${mode})`, async ({ page }) => {
    const errors = await open(page, `${mode}&es2=1`);
    const hidden = await page.evaluate(() => {
      const gl = document.createElement("canvas").getContext("webgl2");
      return { listed: gl.getSupportedExtensions().some(name => /vertex_array_object/i.test(name)), object: gl.getExtension("OES_vertex_array_object"), ...globalThis.skiaRaylibEs2 };
    });
    expect(hidden.forced).toBe(true);
    expect(hidden.listed).toBe(false);
    expect(hidden.object).toBeNull();
    test.info().annotations.push({ type: "vaosCreated", description: String(hidden.vaosCreated) });
    const errorsAtStart = await glErrors(page);
    await waitForFrames(page, 60);
    await expectScene(page);
    await expectText(page);
    if (mode === "direct") {
      await expectColors(page, layering);
      await expectColors(page, renderTexture);
    }
    expect(await glErrors(page), "GL errors Skia left behind after the first frames").toBe(errorsAtStart);
    expect(errors).toEqual([]);
  });
}
