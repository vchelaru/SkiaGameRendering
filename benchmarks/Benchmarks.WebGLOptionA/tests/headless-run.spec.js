// Headless indicative driver only - NOT hardware-acceptance-grade (see README.md and
// docs/webgl/performance-results.md's stated CI-headless-is-correctness-only policy).
// Runs all 3 resolutions as 3 separate page loads (?w=&h=) - one page load per resolution is
// required, not a convenience: see BenchGame.cs's comment on why KNI's BlazorGL platform has no
// working live-resize. Dumps the resulting JSON reports to the console/attachments.
const { test, expect } = require("@playwright/test");

const RESOLUTIONS = [
  { width: 1920, height: 1080 },
  { width: 2560, height: 1440 },
  { width: 3840, height: 2160 },
];

test("Option A per-frame cost - headless indicative run", async ({ page }, testInfo) => {
  const reports = [];

  for (const res of RESOLUTIONS) {
    const consoleLines = [];
    page.on("console", msg => consoleLines.push(msg.text()));
    page.on("pageerror", error => consoleLines.push(`[pageerror] ${error.message}`));

    await page.goto(`/?w=${res.width}&h=${res.height}`);
    await expect(page.locator("#report")).toContainText("Ready.", { timeout: 60000 });
    await expect(page.locator("#run")).toBeEnabled({ timeout: 10000 });

    await page.click("#run");
    // Software/CI GL stacks are much slower than real hardware - generous timeout.
    await expect(page.locator("#report")).toContainText("Done.", { timeout: 170000 });

    const report = await page.evaluate(() => window.__optionABenchmarkReport);
    reports.push(report);

    // The on-page Option A vs Option D comparison table (Pages/Index.razor's #comparison,
    // rendered by optionA-benchmark.js) should have populated after Run completes.
    const comparisonText = await page.locator("#comparison").innerText();
    expect(comparisonText, `#comparison did not render an Option D row at ${res.width}x${res.height}`).toContain("Option D");
    expect(comparisonText, `#comparison did not render an Option A row at ${res.width}x${res.height}`).toContain("Option A");
    expect(comparisonText, `#comparison did not label a Chrome/Edge/Firefox baseline at ${res.width}x${res.height}`).toMatch(/Chrome|Edge|Firefox/);

    console.log(`\n===== ${res.width}x${res.height} console output =====`);
    for (const line of consoleLines) console.log(line);
    console.log("===== end console output =====\n");

    page.removeAllListeners("console");
    page.removeAllListeners("pageerror");
  }

  await testInfo.attach("optionA-reports.json", {
    body: JSON.stringify(reports, null, 2),
    contentType: "application/json",
  });
  console.log("\n===== Option A headless indicative reports (all 3 resolutions) =====");
  console.log(JSON.stringify(reports, null, 2));
  console.log("===== end reports =====\n");

  for (const report of reports) {
    expect(report.correctness.pureGreenAfterSequence, `pureGreenAfterSequence false at ${report.resolution.width}x${report.resolution.height}`).toBe(true);
  }
});
