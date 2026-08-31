const { test, expect } = require("@playwright/test");

test("renders current-frame Skia through KNI without a blank canvas", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/");
  await expect(page.locator("#diagnostics")).toContainText("WebGL 2");
  await page.waitForTimeout(3000);

  const canvas = page.locator("#theCanvas");
  await test.info().attach("integrated-scene", { body: await canvas.screenshot(), contentType: "image/png" });
  const variation = await canvas.evaluate(element => new Promise(resolve => {
    requestAnimationFrame(() => {
      const context = element.getContext("webgl2");
      const pixels = new Uint8Array(element.width * element.height * 4);
      context.readPixels(0, 0, element.width, element.height, context.RGBA, context.UNSIGNED_BYTE, pixels);
      let min = 255, max = 0;
      for (const value of pixels) { min = Math.min(min, value); max = Math.max(max, value); }
      resolve(max - min);
    });
  }));
  expect(variation).toBeGreaterThan(20);
  expect(errors).toEqual([]);
});

test("survives source context loss and page remount", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator("#diagnostics")).toContainText("WebGL 2");
  await page.waitForTimeout(1500);
  await page.locator('canvas[id^="skia-game-source-"]').evaluate(async canvas => {
    const gl = canvas.getContext("webgl2");
    const extension = gl.getExtension("WEBGL_lose_context");
    if (!extension) throw new Error("WEBGL_lose_context is unavailable");
    extension.loseContext();
    await new Promise(resolve => setTimeout(resolve, 200));
    extension.restoreContext();
  });
  await page.waitForTimeout(1000);
  await page.reload();
  await expect(page.locator("#diagnostics")).toContainText("WebGL 2");
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
