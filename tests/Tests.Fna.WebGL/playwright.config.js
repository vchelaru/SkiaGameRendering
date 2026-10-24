const { defineConfig, devices } = require("@playwright/test");

// The published sample (dotnet publish samples/Sample.Fna.WebGL -c Release), served statically on a fixed port.
const port = 5873;

module.exports = defineConfig({
  testDir: "./tests",
  timeout: 120000,
  workers: 1,
  expect: { timeout: 60000 },
  use: { baseURL: `http://127.0.0.1:${port}`, trace: "retain-on-failure", screenshot: "only-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: `node serve.js ../../samples/Sample.Fna.WebGL/bin/Release/net10.0/publish/wwwroot ${port}`,
    url: `http://127.0.0.1:${port}`,
    reuseExistingServer: !process.env.CI,
    timeout: 30000,
  },
});
