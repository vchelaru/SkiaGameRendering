const { test, expect } = require("@playwright/test");

// Sample.Kni.WebGL draws samples/Shared/Scene.cs over the whole canvas in physical pixels. Scene's
// cells are half the canvas's shorter side: a red circle in the first, the blue SVG water drop in the
// second. Same cells the desktop samples' --smoke-test checks.
async function readScenePixels(canvas) {
  return canvas.evaluate(element => new Promise(resolve => {
    requestAnimationFrame(() => {
      const gl = element.getContext("webgl2");
      const cell = Math.min(element.width, element.height) / 2;
      const read = (x, y) => {
        const pixel = new Uint8Array(4);
        // readPixels counts rows from the bottom.
        gl.readPixels(Math.floor(x), element.height - 1 - Math.floor(y), 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);
        return Array.from(pixel);
      };
      resolve({
        circle: read(cell / 2, cell / 2),
        drop: read(cell * 1.5, cell / 2),
        outside: read(element.width - 4, element.height - 4),
      });
    });
  }));
}

test("draws the shared Scene through KNI", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/");
  await expect(page.locator("#diagnostics")).toContainText("WebGL 2");
  await expect(page.locator("#diagnostic-text")).toContainText("frames");

  const canvas = page.locator("#theCanvas");
  await test.info().attach("scene", { body: await canvas.screenshot(), contentType: "image/png" });
  const { circle, drop, outside } = await readScenePixels(canvas);
  expect(circle[0], `circle ${circle}`).toBeGreaterThan(200);
  expect(circle[1], `circle ${circle}`).toBeLessThan(50);
  expect(circle[2], `circle ${circle}`).toBeLessThan(50);
  expect(drop[0], `drop ${drop}`).toBeLessThan(100);
  expect(drop[2], `drop ${drop}`).toBeGreaterThan(150);
  expect(outside.slice(0, 3), `outside ${outside}`).toEqual([0, 0, 0]);
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

// The WebAssembly heap holds both Skia's native allocations and .NET's managed heap, and it only ever
// grows, so after a warm-up any growth over hundreds of frames is a leak (or a cache still filling,
// which the first measured window absorbs).
test("draws hundreds of frames without growing the WebAssembly heap", async ({ page }) => {
  test.skip(test.info().project.name !== "scene-chromium", "One browser is enough; the heap is the runtime's, not the browser's.");
  const frames = async () => Number((await page.locator("#diagnostic-text").textContent()).match(/frames (\d+)/)?.[1] ?? 0);
  const heapBytes = () => page.evaluate(() => {
    const runtime = globalThis.getDotnetRuntime?.(0) ?? globalThis.Blazor?.runtime;
    return runtime?.Module?.HEAPU8?.byteLength ?? null;
  });
  const waitForFrames = async count => {
    const target = (await frames()) + count;
    await expect.poll(frames, { timeout: 60000 }).toBeGreaterThanOrEqual(target);
  };

  await page.goto("/");
  await expect(page.locator("#diagnostics")).toContainText("WebGL 2");
  await waitForFrames(120);
  const start = await heapBytes();
  expect(start, "the .NET runtime's WebAssembly heap should be reachable").not.toBeNull();
  await waitForFrames(300);
  const middle = await heapBytes();
  await waitForFrames(300);
  const end = await heapBytes();

  const mb = bytes => (bytes / (1024 * 1024)).toFixed(1);
  console.log(`WebAssembly heap over 600 frames: ${mb(start)} -> ${mb(middle)} -> ${mb(end)} MB`);
  expect(end - middle, `heap grew from ${mb(middle)} to ${mb(end)} MB over the last 300 frames`).toBe(0);
});

test("switches the upload path and keeps drawing the Scene", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator("#diagnostics")).toContainText("WebGL 2");

  await page.locator("#upload-mode").selectOption("image");
  await expect(page.locator("#diagnostic-text")).toContainText("texImage2D(canvas)");
  const { circle } = await readScenePixels(page.locator("#theCanvas"));
  expect(circle[0], `circle ${circle}`).toBeGreaterThan(200);

  await page.locator("#upload-mode").selectOption("sub");
  await expect(page.locator("#diagnostic-text")).toContainText("texSubImage2D(canvas)");
});
