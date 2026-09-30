const { test, expect } = require("@playwright/test");

// Sample.Gum.Kni.WebGL: Gum interleaved with SpriteBatch, render targets, and shader sampling, plus
// browser input driving a Gum button that recreates the backend.
// Game1 lays the scene out in 1280x720 logical units scaled to the canvas's physical size. Each point
// sits on one element and clear of the others: the screen SpriteBatch background and blue bar, the
// Gum texture blitted straight to the screen, and the same texture drawn through BasicEffect.
async function readGumPixels(canvas) {
  return canvas.evaluate(element => new Promise(resolve => {
    requestAnimationFrame(() => {
      const gl = element.getContext("webgl2");
      const read = (x, y) => {
        const pixel = new Uint8Array(4);
        const px = Math.floor(x * element.width / 1280);
        const py = Math.floor(y * element.height / 720);
        // readPixels counts rows from the bottom.
        gl.readPixels(px, element.height - 1 - py, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);
        return Array.from(pixel);
      };
      resolve({
        background: read(200, 560),
        blueBar: read(100, 89),
        directPanel: read(375, 174),
        shaderPanel: read(1185, 96),
      });
    });
  }));
}

function expectNear(actual, expected, label) {
  for (let i = 0; i < 3; i++)
    expect(Math.abs(actual[i] - expected[i]), `${label} ${actual}`).toBeLessThanOrEqual(12);
}

function expectPanel(actual, label) {
  for (let i = 0; i < 3; i++)
    expect(actual[i], `${label} ${actual}`).toBeGreaterThan(200);
}

test("draws SpriteBatch and current-frame Gum through KNI", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/");
  await expect(page.locator("#diagnostics")).toContainText("WebGL 2");
  await expect(page.locator("#diagnostic-text")).toContainText("frames");
  await page.waitForTimeout(2000);

  const canvas = page.locator("#theCanvas");
  await test.info().attach("gum-scene", { body: await canvas.screenshot(), contentType: "image/png" });
  const { background, blueBar, directPanel, shaderPanel } = await readGumPixels(canvas);
  expectNear(background, [25, 29, 35], "background");
  expectNear(blueBar, [54, 122, 178], "blue bar");
  expectPanel(directPanel, "direct panel");
  expectPanel(shaderPanel, "shader panel");
  expect(errors).toEqual([]);
});

test("maps browser input and recreates the backend while changing diagnostic upload path", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator("#diagnostics")).toContainText("WebGL 2");

  await page.locator("#upload-mode").selectOption("image");
  await expect(page.locator("#diagnostic-text")).toContainText("texImage2D(canvas)");

  const canvas = page.locator("#theCanvas");
  const box = await canvas.boundingBox();
  if (!box) throw new Error("KNI canvas has no layout box");
  await page.mouse.click(box.x + box.width * 100 / 1280, box.y + box.height * 170 / 720);
  await expect(page.locator("#diagnostic-text")).toContainText("recreate 1");

  await page.locator("#upload-mode").selectOption("sub");
  await expect(page.locator("#diagnostic-text")).toContainText("texSubImage2D(canvas)");
});
