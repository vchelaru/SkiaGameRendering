const { test, expect } = require("@playwright/test");

// Sample.Fna.WebGL, FNA on WebGL2. Frames are driven by the test (?manual=1) so the canvas is read in the same
// task as the draw, before the browser presents it and clears the drawing buffer. Points are top-left origin.

async function open(page, mode) {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto(`/?mode=${mode}&manual=1`);
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

test("SkiaRenderTarget2D draws the Scene into an FNA texture", async ({ page }) => {
  const errors = await open(page, "texture");
  const [circle, drop, outside] = await draw(page, [[50, 50], [150, 50], [790, 790]], 5);
  near(circle, [255, 0, 0]);
  expect(drop[0]).toBeLessThan(100);
  expect(drop[2]).toBeGreaterThan(150);
  near(outside, [0, 0, 0]);
  expect(await glErrors(page)).toBe(0);
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
  expect(await glErrors(page)).toBe(0);
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
