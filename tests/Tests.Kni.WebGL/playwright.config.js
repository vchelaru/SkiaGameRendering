const { defineConfig, devices } = require("@playwright/test");

// Each spec runs against its own sample, served on its own port.
const samples = [
  { name: "scene", project: "Sample.Kni.WebGL", port: 5099 },
  { name: "gum", project: "Sample.Gum.Kni.WebGL", port: 5100 },
];

const browsers = [
  { name: "chromium", use: { ...devices["Desktop Chrome"] } },
  { name: "chromium-dpr-1.25", use: { ...devices["Desktop Chrome"], deviceScaleFactor: 1.25 } },
  { name: "chromium-dpr-1.5", use: { ...devices["Desktop Chrome"], deviceScaleFactor: 1.5 } },
  { name: "chromium-dpr-2", use: { ...devices["Desktop Chrome"], deviceScaleFactor: 2 } },
  {
    name: "firefox",
    use: {
      ...devices["Desktop Firefox"],
      // Firefox's headless default disables WebGL2 via gfx config ("AllowWebgl2:false restricts
      // context creation on this system") on GPU-less CI runners unless explicitly forced on.
      // See repo issue #12 - this was the root cause of "Initializing WebGL..." hanging forever.
      launchOptions: { firefoxUserPrefs: { "webgl.force-enabled": true } },
    },
  },
  { name: "webkit", use: { ...devices["Desktop Safari"] } },
];

module.exports = defineConfig({
  testDir: "./tests",
  timeout: 90000,
  workers: 1,
  expect: { timeout: 30000 },
  use: {
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: samples.flatMap(sample => browsers.map(browser => ({
    name: `${sample.name}-${browser.name}`,
    testMatch: `${sample.name}.spec.js`,
    use: { ...browser.use, baseURL: `http://127.0.0.1:${sample.port}` },
  }))),
  webServer: samples.map(sample => ({
    command: `dotnet run --project ../../samples/${sample.project}/${sample.project}.csproj -c Release --no-build --urls http://127.0.0.1:${sample.port}`,
    url: `http://127.0.0.1:${sample.port}`,
    reuseExistingServer: !process.env.CI,
    timeout: 120000,
  })),
});
