const { defineConfig, devices } = require("@playwright/test");

// Chromium only: raylib's web build has no browser-specific code, and this job builds a native wasm app with
// its own Emscripten link, which is the slow part. Firefox and WebKit cover WebGL itself in tests/Tests.Kni.WebGL.
const port = 5101;

module.exports = defineConfig({
  testDir: "./tests",
  timeout: 90000,
  workers: 1,
  expect: { timeout: 45000 },
  use: {
    ...devices["Desktop Chrome"],
    baseURL: `http://127.0.0.1:${port}`,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium" }],
  webServer: {
    command: `dotnet run --project ../../samples/Sample.Raylib.WebGL/Sample.Raylib.WebGL.csproj -c Release --no-build --urls http://127.0.0.1:${port}`,
    url: `http://127.0.0.1:${port}`,
    reuseExistingServer: !process.env.CI,
    timeout: 90000,
  },
});
