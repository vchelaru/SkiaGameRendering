const { defineConfig, devices } = require("@playwright/test");

// Chromium, Firefox and WebKit. `npm test` runs Chromium only; `npm run test:all` runs all three (Firefox needs an
// X display on Linux: xvfb-run).
const port = 5101;

module.exports = defineConfig({
  testDir: "./tests",
  timeout: 90000,
  workers: 1,
  expect: { timeout: 45000 },
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
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
    command: `dotnet run --project ../../samples/Sample.Raylib.WebGL/Sample.Raylib.WebGL.csproj -c Release --no-build --urls http://127.0.0.1:${port}`,
    url: `http://127.0.0.1:${port}`,
    reuseExistingServer: !process.env.CI,
    timeout: 90000,
  },
});
