const { defineConfig, devices } = require("@playwright/test");

// The published sample (dotnet publish samples/Sample.Fna.WebGL -c Release), served statically on a fixed port.
const port = 5873;

module.exports = defineConfig({
  testDir: "./tests",
  timeout: 120000,
  workers: 1,
  expect: { timeout: 60000 },
  use: { baseURL: `http://127.0.0.1:${port}`, trace: "retain-on-failure", screenshot: "only-on-failure" },
  // `npm test` runs Chromium only; `npm run test:all` runs all three (Firefox needs an X display on Linux: xvfb-run).
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
    {
      name: "firefox",
      use: {
        ...devices["Desktop Firefox"],
        // Headless Firefox disables WebGL2 on GPU-less machines unless it is forced on (see tests/Tests.Kni.WebGL).
        launchOptions: { firefoxUserPrefs: { "webgl.force-enabled": true } },
      },
    },
    { name: "webkit", use: { ...devices["Desktop Safari"] } },
  ],
  webServer: {
    command: `node serve.js ../../samples/Sample.Fna.WebGL/bin/Release/net10.0/publish/wwwroot ${port}`,
    url: `http://127.0.0.1:${port}`,
    reuseExistingServer: !process.env.CI,
    timeout: 30000,
  },
});
