import { dotnet } from "./_framework/dotnet.js";

// FNA runs on its SDL2 platform backend: SDL3's enum-typed 64-bit pinvokes trap on Mono's WebAssembly interpreter.
const runtime = await dotnet.withEnvironmentVariable("FNA_PLATFORM_BACKEND", "SDL2").create();
// Emscripten's EGL path (SDL2's GL context) looks the canvas up here.
runtime.Module.canvas = document.getElementById("canvas");
const config = runtime.getConfig();
const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
const program = exports.Sample.Fna.WebGL.Program;

const mode = new URLSearchParams(location.search).get("mode") ?? "texture";
// ?msaa=1 asks FNA3D for a multisampled back buffer (GraphicsDeviceManager.PreferMultiSampling).
program.SetMultiSampling(new URLSearchParams(location.search).has("msaa"));
program.SetMode(mode);
await runtime.runMain();

// Tests drive frames themselves so they can read the canvas in the same task as the draw.
globalThis.fnaSample = {
  mode,
  frame: () => program.Frame(),
  frames: () => program.Frames(),
  setMode: m => program.SetMode(m),
  tryColorType: c => program.TryColorType(c),
  tryMultiSample: n => program.TryMultiSample(n),
  backBufferMultiSampleCount: () => program.BackBufferMultiSampleCount(),
  // Draws one frame, then reads pixels [x, y] (top-left origin) from the canvas before it is presented.
  frameAndRead(points) {
    program.Frame();
    const gl = document.getElementById("canvas").getContext("webgl2");
    const height = gl.drawingBufferHeight, px = new Uint8Array(4);
    return points.map(([x, y]) => { gl.readPixels(x, height - 1 - y, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px); return Array.from(px); });
  },
};
document.title = "Sample.Fna.WebGL ready";

if (!new URLSearchParams(location.search).has("manual")) {
  const loop = () => { program.Frame(); requestAnimationFrame(loop); };
  requestAnimationFrame(loop);
}
