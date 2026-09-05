"use strict";

const WARMUP_FRAMES = 60;
const MEASURED_FRAMES = 300;
const sourceCanvas = document.getElementById("source");
const destinationCanvas = document.getElementById("destination");
const renderableCountSelect = document.getElementById("renderable-count");
const runAllButton = document.getElementById("run-all");
const exportAllButton = document.getElementById("export-all");
const reportElement = document.getElementById("report");
const RESOLUTIONS = ["1920x1080", "2560x1440", "3840x2160"];
const PATHS = ["texSubImage2D", "texImage2D", "imageBitmap", "offscreenBitmap", "readPixels"];
let latestFullMatrixReports = null;
const sourceGl = sourceCanvas.getContext("webgl2", { alpha: true, premultipliedAlpha: true, antialias: false });
const destinationGl = destinationCanvas.getContext("webgl2", { alpha: true, premultipliedAlpha: true, antialias: false });
if (!sourceGl || !destinationGl) throw new Error("WebGL 2 is required.");

const timerExtension = destinationGl.getExtension("EXT_disjoint_timer_query_webgl2");
const debugRenderer = destinationGl.getExtension("WEBGL_debug_renderer_info");
let contextLossCount = 0;
let running = false;
for (const canvas of [sourceCanvas, destinationCanvas]) {
  canvas.addEventListener("webglcontextlost", event => { event.preventDefault(); contextLossCount++; });
}

function shader(gl, type, source) {
  const value = gl.createShader(type);
  gl.shaderSource(value, source);
  gl.compileShader(value);
  if (!gl.getShaderParameter(value, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(value));
  return value;
}

function program(gl, fragment) {
  const value = gl.createProgram();
  gl.attachShader(value, shader(gl, gl.VERTEX_SHADER, `#version 300 es
    in vec2 position; out vec2 uv;
    void main(){ uv=position*.5+.5; gl_Position=vec4(position,0,1); }`));
  gl.attachShader(value, shader(gl, gl.FRAGMENT_SHADER, fragment));
  gl.linkProgram(value);
  if (!gl.getProgramParameter(value, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(value));
  const vao = gl.createVertexArray();
  gl.bindVertexArray(vao);
  const buffer = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1,-1,1,-1,-1,1,1,1]), gl.STATIC_DRAW);
  const location = gl.getAttribLocation(value, "position");
  gl.enableVertexAttribArray(location);
  gl.vertexAttribPointer(location, 2, gl.FLOAT, false, 0, 0);
  return { value, vao };
}

const sourceProgram = program(sourceGl, `#version 300 es
  precision highp float; in vec2 uv; uniform float time; out vec4 color;
  void main(){
    vec3 q = uv.x < .5 ? (uv.y < .5 ? vec3(1,0,0) : vec3(0,1,0))
                       : (uv.y < .5 ? vec3(0,0,1) : vec3(1,.8,0));
    float ring = smoothstep(.018,0.,abs(length(uv-vec2(.63+.08*sin(time),.38))-.18));
    color = vec4(mix(q,vec3(1,0,1),ring), .35 + .65*uv.x);
  }`);
const destinationProgram = program(destinationGl, `#version 300 es
  precision highp float; in vec2 uv; uniform sampler2D texture0; out vec4 color;
  void main(){ color=texture(texture0,uv); }`);
const destinationTexture = destinationGl.createTexture();
destinationGl.bindTexture(destinationGl.TEXTURE_2D, destinationTexture);
destinationGl.texParameteri(destinationGl.TEXTURE_2D, destinationGl.TEXTURE_MIN_FILTER, destinationGl.LINEAR);
destinationGl.texParameteri(destinationGl.TEXTURE_2D, destinationGl.TEXTURE_MAG_FILTER, destinationGl.LINEAR);
destinationGl.texParameteri(destinationGl.TEXTURE_2D, destinationGl.TEXTURE_WRAP_S, destinationGl.CLAMP_TO_EDGE);
destinationGl.texParameteri(destinationGl.TEXTURE_2D, destinationGl.TEXTURE_WRAP_T, destinationGl.CLAMP_TO_EDGE);

let offscreen = null;
let offscreenGl = null;
let offscreenProgram = null;
if (typeof OffscreenCanvas !== "undefined") {
  offscreen = new OffscreenCanvas(1, 1);
  offscreenGl = offscreen.getContext("webgl2", { alpha: true, premultipliedAlpha: true, antialias: false });
  if (offscreenGl) offscreenProgram = program(offscreenGl, `#version 300 es
    precision highp float; in vec2 uv; uniform float time; out vec4 color;
    void main(){ color=vec4(uv.x,uv.y,.5+.5*sin(time),.35+.65*uv.x); }`);
}

function resize(resolutionString) {
  const [width, height] = resolutionString.split("x").map(Number);
  sourceCanvas.width = destinationCanvas.width = width;
  sourceCanvas.height = destinationCanvas.height = height;
  if (offscreen) { offscreen.width = width; offscreen.height = height; }
  destinationGl.bindTexture(destinationGl.TEXTURE_2D, destinationTexture);
  destinationGl.texImage2D(destinationGl.TEXTURE_2D, 0, destinationGl.RGBA, width, height, 0,
    destinationGl.RGBA, destinationGl.UNSIGNED_BYTE, null);
  return { width, height };
}

function drawPattern(gl, state, width, height, time) {
  gl.viewport(0, 0, width, height);
  gl.useProgram(state.value);
  gl.bindVertexArray(state.vao);
  gl.uniform1f(gl.getUniformLocation(state.value, "time"), time);
  gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
  gl.flush();
}

function percentile(values, amount) {
  if (!values.length) return null;
  const sorted = [...values].sort((a,b) => a-b);
  return sorted[Math.min(sorted.length - 1, Math.floor((sorted.length - 1) * amount))];
}

function stats(values) {
  if (!values.length) return { samples: 0, p50: null, p95: null, p99: null, max: null };
  return { samples: values.length, p50: percentile(values,.5), p95: percentile(values,.95),
    p99: percentile(values,.99), max: Math.max(...values) };
}

function queryGpuStart() {
  if (!timerExtension) return null;
  const query = destinationGl.createQuery();
  destinationGl.beginQuery(timerExtension.TIME_ELAPSED_EXT, query);
  return query;
}

async function runOnce(resolutionString, path, progressPrefix) {
  const size = resize(resolutionString);
  const renderableCount = Number(renderableCountSelect.value);
  if (path === "offscreenBitmap" && !offscreenGl) throw new Error("OffscreenCanvas WebGL2 is unavailable.");
  const readback = new Uint8Array(size.width * size.height * 4);
  const sourceTimes = [], uploadTimes = [], frameTimes = [], gpuTimes = [];
  const pendingQueries = [];
  let missedFrames = 0;
  let previousFrame = performance.now();

  for (let frame = 0; frame < WARMUP_FRAMES + MEASURED_FRAMES; frame++) {
    await new Promise(requestAnimationFrame);
    const frameStart = performance.now();
    const interval = frameStart - previousFrame;
    previousFrame = frameStart;
    const measured = frame >= WARMUP_FRAMES;
    if (measured && interval > 16.67) missedFrames++;

    const sourceStart = performance.now();
    const useOffscreen = path === "offscreenBitmap";
    drawPattern(useOffscreen ? offscreenGl : sourceGl,
      useOffscreen ? offscreenProgram : sourceProgram, size.width, size.height, frameStart / 1000);
    if (measured) sourceTimes.push(performance.now() - sourceStart);

    destinationGl.bindTexture(destinationGl.TEXTURE_2D, destinationTexture);
    destinationGl.pixelStorei(destinationGl.UNPACK_FLIP_Y_WEBGL, true);
    destinationGl.pixelStorei(destinationGl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, true);
    destinationGl.pixelStorei(destinationGl.UNPACK_COLORSPACE_CONVERSION_WEBGL, destinationGl.NONE);
    const uploadStart = performance.now();
    let bitmap = null;
    if (path === "imageBitmap") {
      bitmap = await createImageBitmap(sourceCanvas, { imageOrientation: "flipY", premultiplyAlpha: "premultiply", colorSpaceConversion: "none" });
    } else if (path === "offscreenBitmap") {
      bitmap = offscreen.transferToImageBitmap();
    }

    const query = measured ? queryGpuStart() : null;
    if (path === "readPixels")
      sourceGl.readPixels(0, 0, size.width, size.height, sourceGl.RGBA, sourceGl.UNSIGNED_BYTE, readback);

    for (let renderable = 0; renderable < renderableCount; renderable++) {
      if (path === "texImage2D") {
        destinationGl.texImage2D(destinationGl.TEXTURE_2D, 0, destinationGl.RGBA, destinationGl.RGBA,
          destinationGl.UNSIGNED_BYTE, sourceCanvas);
      } else if (path === "readPixels") {
        destinationGl.texSubImage2D(destinationGl.TEXTURE_2D, 0, 0, 0, size.width, size.height,
          destinationGl.RGBA, destinationGl.UNSIGNED_BYTE, readback);
      } else {
        destinationGl.texSubImage2D(destinationGl.TEXTURE_2D, 0, 0, 0, destinationGl.RGBA,
          destinationGl.UNSIGNED_BYTE, bitmap || sourceCanvas);
      }
    }
    if (query) { destinationGl.endQuery(timerExtension.TIME_ELAPSED_EXT); pendingQueries.push(query); }
    bitmap?.close();
    if (measured) uploadTimes.push(performance.now() - uploadStart);

    destinationGl.viewport(0, 0, size.width, size.height);
    destinationGl.useProgram(destinationProgram.value);
    destinationGl.bindVertexArray(destinationProgram.vao);
    destinationGl.drawArrays(destinationGl.TRIANGLE_STRIP, 0, 4);
    if (measured) frameTimes.push(performance.now() - frameStart);

    for (let index = pendingQueries.length - 1; index >= 0; index--) {
      const pending = pendingQueries[index];
      if (destinationGl.getQueryParameter(pending, destinationGl.QUERY_RESULT_AVAILABLE)) {
        if (!destinationGl.getParameter(timerExtension.GPU_DISJOINT_EXT))
          gpuTimes.push(destinationGl.getQueryParameter(pending, destinationGl.QUERY_RESULT) / 1e6);
        destinationGl.deleteQuery(pending);
        pendingQueries.splice(index, 1);
      }
    }
    reportElement.textContent = `${progressPrefix}` + (frame < WARMUP_FRAMES
      ? `Warm-up ${frame + 1}/${WARMUP_FRAMES}`
      : `Measured ${frame - WARMUP_FRAMES + 1}/${MEASURED_FRAMES}`);
  }

  return {
    schemaVersion: 1,
    timestampUtc: new Date().toISOString(),
    browser: navigator.userAgent,
    platform: navigator.userAgentData?.platform || navigator.platform,
    webglVersion: destinationGl.getParameter(destinationGl.VERSION),
    renderer: debugRenderer ? destinationGl.getParameter(debugRenderer.UNMASKED_RENDERER_WEBGL) : destinationGl.getParameter(destinationGl.RENDERER),
    resolution: size,
    devicePixelRatio: devicePixelRatio,
    contextAttributes: destinationGl.getContextAttributes(),
    uploadPath: path,
    sequentialRenderableCount: renderableCount,
    pixelStore: { flipY: true, premultiplyAlpha: true, colorSpaceConversion: "none" },
    warmupFrames: WARMUP_FRAMES,
    measuredFrames: MEASURED_FRAMES,
    missedFrames,
    contextLossCount,
    timingsMilliseconds: { sourceCpu: stats(sourceTimes), uploadCpu: stats(uploadTimes), uploadGpu: stats(gpuTimes), frameCpu: stats(frameTimes) },
  };
}

function downloadJson(data, filename) {
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  URL.revokeObjectURL(url);
}

runAllButton.addEventListener("click", async () => {
  if (running) return;
  running = true;
  runAllButton.disabled = true;
  exportAllButton.disabled = true;
  const reports = [];
  try {
    let index = 0;
    const total = RESOLUTIONS.length * PATHS.length;
    for (const res of RESOLUTIONS) {
      for (const path of PATHS) {
        index++;
        const report = await runOnce(res, path, `Combo ${index}/${total} (${res}, ${path}) — `);
        reports.push(report);
      }
    }
    latestFullMatrixReports = reports;
    reportElement.textContent = `Done. ${reports.length} combos captured — click "Export all" to download.`;
    exportAllButton.disabled = false;
  } catch (error) {
    reportElement.textContent = error.stack || String(error);
  } finally {
    running = false;
    runAllButton.disabled = false;
  }
});
exportAllButton.addEventListener("click", () => {
  downloadJson(latestFullMatrixReports, `webgl-full-matrix-${new Date().toISOString().replace(/[:.]/g, "-")}.json`);
});
resize(RESOLUTIONS[0]);
