// Headless driver for Claude's own indicative (non-authoritative) reading - see tests/headless-run.spec.js
// and README.md's "Running it yourself" section for the real, non-headless, human-driven flow.
const { defineConfig, devices } = require("@playwright/test");

module.exports = defineConfig({
  testDir: "./tests",
  timeout: 180000,
  workers: 1,
  expect: { timeout: 60000 },
  use: {
    baseURL: "http://127.0.0.1:5098",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
    {
      name: "firefox",
      use: {
        ...devices["Desktop Firefox"],
        // Firefox's headless default disables WebGL2 on GPU-less runners unless forced on - same
        // issue tests/Tests.Kni.WebGL/playwright.config.js works around (repo issue #12).
        launchOptions: { firefoxUserPrefs: { "webgl.force-enabled": true } },
      },
    },
  ],
  webServer: {
    command: "dotnet run --project . -c Release --no-build --urls http://127.0.0.1:5098",
    url: "http://127.0.0.1:5098",
    reuseExistingServer: !process.env.CI,
    timeout: 120000,
  },
});
