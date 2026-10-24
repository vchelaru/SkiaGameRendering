import { dotnet } from './_framework/dotnet.js';
const runtime = await dotnet.withEnvironmentVariable("FNA_PLATFORM_BACKEND","SDL2").withDiagnosticTracing(false).create();
globalThis.dotnetRuntime = runtime;
runtime.Module.canvas = document.getElementById("canvas");
const config = runtime.getConfig();
const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
globalThis.exports = exports;
await runtime.runMain();
function loop() { try { exports.Program.Frame(); } catch (e) { console.error(e); return; } requestAnimationFrame(loop); }
requestAnimationFrame(loop);
