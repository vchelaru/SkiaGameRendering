const { test, expect } = require("@playwright/test");

// Sample.Fna.WebGL, FNA on WebGL2. Frames are driven by the test (?manual=1) so the canvas is read in the same
// task as the draw, before the browser presents it and clears the drawing buffer. Points are top-left origin.

async function open(page, mode, query = "") {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto(`/?mode=${mode}&manual=1${query}`);
  await page.waitForFunction(() => globalThis.fnaSample, null, { timeout: 90000 });
  return errors;
}

// Draws `frames` frames and reads the points on the last one.
const draw = (page, points, frames = 1) => page.evaluate(([points, frames]) => {
  for (let i = 1; i < frames; i++)
    globalThis.fnaSample.frame();
  return globalThis.fnaSample.frameAndRead(points);
}, [points, frames]);

const near = (actual, expected, tolerance = 12) => {
  for (let i = 0; i < 3; i++)
    expect(Math.abs(actual[i] - expected[i]), `${actual} vs ${expected}`).toBeLessThanOrEqual(tolerance);
};

const glErrors = page => page.evaluate(() => globalThis.skiaFnaWebGl.errorCount());
const glErrorLog = page => page.evaluate(() => globalThis.skiaFnaWebGl.errorLog());

test("SkiaRenderTarget2D draws the Scene into an FNA texture", async ({ page }) => {
  const errors = await open(page, "texture");
  const [circle, drop, outside] = await draw(page, [[50, 50], [150, 50], [790, 790]], 5);
  near(circle, [255, 0, 0]);
  expect(drop[0]).toBeLessThan(100);
  expect(drop[2]).toBeGreaterThan(150);
  near(outside, [0, 0, 0]);
  expect(await glErrors(page), await glErrorLog(page)).toBe(0);
  expect(errors).toEqual([]);
});

test("SkiaDirectCanvas layers Skia between FNA's draws on the back buffer and a render target", async ({ page }) => {
  const errors = await open(page, "direct");
  const points = {
    circle: [50, 50], drop: [150, 50], lime: [450, 100], blueSquare: [410, 310], redCircle: [460, 360],
    yellowBottomLeft: [405, 395], yellowTopRight: [495, 305], magenta: [620, 620], black: [790, 790],
  };
  const read = await draw(page, Object.values(points), 30);
  const pixel = Object.fromEntries(Object.keys(points).map((name, i) => [name, read[i]]));
  near(pixel.circle, [255, 0, 0]);
  expect(pixel.drop[0]).toBeLessThan(100);
  expect(pixel.drop[2]).toBeGreaterThan(150);
  near(pixel.lime, [0, 255, 0]);           // FNA's SpriteBatch after Skia drew on the back buffer
  near(pixel.blueSquare, [0, 0, 255]);     // Skia in a render target's top-left corner stays top-left
  near(pixel.redCircle, [255, 0, 0]);
  near(pixel.yellowBottomLeft, [255, 255, 0]);
  near(pixel.yellowTopRight, [255, 255, 0]);
  near(pixel.magenta, [255, 0, 255]);      // FNA draws last, so its cached GL state survived every Skia pass
  near(pixel.black, [0, 0, 0]);
  expect(await glErrors(page), await glErrorLog(page)).toBe(0);
  expect(errors).toEqual([]);
});

test("switches between the render-target path and SkiaDirectCanvas", async ({ page }) => {
  const errors = await open(page, "texture");
  for (const mode of ["direct", "texture", "direct"]) {
    await page.evaluate(m => globalThis.fnaSample.setMode(m), mode);
    const [circle, outside] = await draw(page, [[50, 50], [790, 790]], 3);
    near(circle, [255, 0, 0]);
    near(outside, [0, 0, 0]);
  }
  expect(errors).toEqual([]);
});

// FNA3D's resources die with the context and are not rebuilt, so a lost context must fail every pass with a clear
// message, and keep failing after the browser restores it (Skia's cached GL objects are gone too).
test("fails clearly when the WebGL context is lost and stays failed after it is restored", async ({ page }) => {
  const errors = await open(page, "texture");
  await draw(page, [[50, 50]], 3);
  await page.evaluate(() => {
    const canvas = document.getElementById("canvas");
    canvas.addEventListener("webglcontextlost", event => event.preventDefault());
    // The extension object is kept: getExtension returns null while the context is lost.
    globalThis.loseContext = canvas.getContext("webgl2").getExtension("WEBGL_lose_context");
    globalThis.loseContext.loseContext();
  });
  const frame = () => page.evaluate(() => {
    try { globalThis.fnaSample.frame(); return "returned"; } catch (error) { return String(error.message ?? error); }
  });
  expect(await frame()).toContain("context was lost");

  await page.evaluate(() => new Promise(resolve => {
    const canvas = document.getElementById("canvas");
    canvas.addEventListener("webglcontextrestored", resolve);
    globalThis.loseContext.restoreContext();
  }));
  expect(await frame()).toContain("context was lost");
  expect(errors).toEqual([]);
});

test("rejects a multisampled render target with NotSupportedException and keeps drawing", async ({ page }) => {
  await open(page, "texture");
  await draw(page, [[50, 50]]);
  expect(await page.evaluate(() => globalThis.fnaSample.tryMultiSample(0))).toBe("ok");
  expect(await page.evaluate(() => globalThis.fnaSample.tryMultiSample(4))).toMatch(/^NotSupportedException: .*multisampled/);
  await draw(page, [[50, 50]]);
});

// SKColorType: Alpha8 = 1, Rgb565 = 2, Rgba8888 = 4, Bgra8888 = 6.
test("accepts Rgba8888 render targets and rejects other color types with NotSupportedException", async ({ page }) => {
  await open(page, "texture");
  await draw(page, [[50, 50]]);
  const tryColorType = color => page.evaluate(c => globalThis.fnaSample.tryColorType(c), color);
  expect(await tryColorType(4)).toBe("ok");
  for (const color of [1, 2, 6])
    expect(await tryColorType(color), `SKColorType ${color}`).toMatch(/^NotSupportedException: .*Rgba8888/);
});

// ?msaa=1 sets MultiSampleCount 4 in PreparingDeviceSettings (PreferMultiSampling alone gives 0), and FNA3D grants it.
// FNA3D then draws to a multisampled FBO and blits it to the canvas each frame.
test("SkiaDirectCanvas draws correctly into FNA's multisampled back buffer", async ({ page }) => {
  await page.addInitScript(() => {
    globalThis.sampleLog = [];
    const getParameter = WebGL2RenderingContext.prototype.getParameter;
    WebGL2RenderingContext.prototype.getParameter = function (name) {
      const value = getParameter.call(this, name);
      if (name === this.SAMPLES) globalThis.sampleLog.push(value);
      return value;
    };
  });
  const errors = await open(page, "direct", "&msaa=1");
  const points = { circle: [50, 50], drop: [150, 50], lime: [450, 100], blueSquare: [410, 310], redCircle: [460, 360], magenta: [620, 620], black: [790, 790] };
  const read = await draw(page, Object.values(points), 30);
  const pixel = Object.fromEntries(Object.keys(points).map((name, i) => [name, read[i]]));
  near(pixel.circle, [255, 0, 0]);
  expect(pixel.drop[0]).toBeLessThan(100);
  expect(pixel.drop[2]).toBeGreaterThan(150);
  near(pixel.lime, [0, 255, 0]);
  near(pixel.blueSquare, [0, 0, 255]);
  near(pixel.redCircle, [255, 0, 0]);
  near(pixel.magenta, [255, 0, 255]);
  near(pixel.black, [0, 0, 0]);
  expect(await page.evaluate(() => globalThis.fnaSample.backBufferMultiSampleCount())).toBe(4);
  const samples = await page.evaluate(() => globalThis.sampleLog);
  expect(samples, "the interop saw FNA3D's multisampled FBO").toContain(4);
  expect(await glErrors(page), await glErrorLog(page)).toBe(0);
  expect(errors).toEqual([]);
});

// 2 and 8 samples are valid counts too, but a context may not offer them. Where the backend accepts the count the
// target must draw correctly; where it does not it must throw NotSupportedException, and the draw is skipped.
for (const samples of [2, 8]) {
  test(`a ${samples}-sample SkiaRenderTarget2D draws the Scene, or throws NotSupportedException if not offered`, async ({ page }) => {
    const errors = await open(page, "texture");
    await draw(page, [[50, 50]]);
    const maxSamples = await page.evaluate(() => document.getElementById("canvas").getContext("webgl2").getParameter(WebGL2RenderingContext.MAX_SAMPLES));
    const result = await page.evaluate(c => globalThis.fnaSample.tryMultiSample(c), samples);
    if (maxSamples < samples)
      expect(result, `gl.MAX_SAMPLES is ${maxSamples}`).toMatch(/^NotSupportedException/);
    if (result !== "ok") {
      expect(result).toMatch(/^NotSupportedException/);
      test.info().annotations.push({ type: "skipped-draw", description: `${samples} samples not offered in this browser (gl.MAX_SAMPLES ${maxSamples}): ${result}` });
      expect(await glErrors(page), await glErrorLog(page)).toBe(0);
      return;
    }

    // Reload so the draw path starts with a fresh context and a target created with this count.
    await page.addInitScript(() => {
      globalThis.glLog = { multisampledStorage: [], blits: 0 };
      const storage = WebGL2RenderingContext.prototype.renderbufferStorageMultisample;
      WebGL2RenderingContext.prototype.renderbufferStorageMultisample = function (target, count, ...rest) {
        globalThis.glLog.multisampledStorage.push(count);
        return storage.call(this, target, count, ...rest);
      };
      const blit = WebGL2RenderingContext.prototype.blitFramebuffer;
      WebGL2RenderingContext.prototype.blitFramebuffer = function (...args) {
        globalThis.glLog.blits++;
        return blit.apply(this, args);
      };
    });
    const drawErrors = await open(page, "texture", `&targetsamples=${samples}`);
    const [circle, drop, outside] = await draw(page, [[50, 50], [150, 50], [790, 790]], 5);
    near(circle, [255, 0, 0]);
    expect(drop[0]).toBeLessThan(100);
    expect(drop[2]).toBeGreaterThan(150);
    near(outside, [0, 0, 0]);
    const log = await page.evaluate(() => globalThis.glLog);
    expect(log.multisampledStorage, "Skia allocated a multisampled renderbuffer").toContain(samples);
    expect(log.blits, "Skia resolved it with blitFramebuffer").toBeGreaterThan(0);
    expect(await glErrors(page), await glErrorLog(page)).toBe(0);
    expect(errors).toEqual([]);
    expect(drawErrors).toEqual([]);
  });
}
