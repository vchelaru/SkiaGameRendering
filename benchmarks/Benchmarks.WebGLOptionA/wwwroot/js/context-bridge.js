// Context-bridging glue for the Option A per-frame benchmark (repo issue #12). Adapted from the
// worktree-agent-ab7b0bc9d3444d8d7 / worktree-agent-a366b823b701668f9 spikes'
// spike-context-bridge.js - same registerKniContext/getFramebufferInfo/readKniPixel shape, plus GPU
// timer query helpers borrowed from benchmarks/Benchmarks.WebGL/benchmark.js's queryGpuStart
// pattern. Not production code - do not copy into skia-game-webgl.js.

function log(...args) {
    console.log("[optionA]", ...args);
}

function getEmscriptenGl() {
    return globalThis.SkiaSharpGL || globalThis.SkiaSharpModule?.GL || globalThis.Module?.GL || globalThis.GL;
}

function getKniGl(contextUid) {
    return globalThis.nkJSObject.GetObject(contextUid);
}

export function registerKniContext(contextUid) {
    const registry = getEmscriptenGl();
    if (!registry)
        return { registerContextAvailable: false, success: false, handle: 0, error: "Emscripten GL registry unavailable." };
    if (typeof registry.registerContext !== "function")
        return { registerContextAvailable: false, success: false, handle: 0, error: "registry.registerContext is not a function." };

    const kniGl = getKniGl(contextUid);
    if (!kniGl) {
        const message = `KNI's WebGL2RenderingContext could not be resolved from Uid ${contextUid}.`;
        log(message);
        return { registerContextAvailable: true, success: false, handle: 0, error: message };
    }
    log("Resolved KNI's raw gl object, constructor =", kniGl.constructor?.name, "isContextLost =", kniGl.isContextLost?.());

    const attributes = {
        alpha: 1, depth: 1, stencil: 8, antialias: 0, premultipliedAlpha: 1,
        preserveDrawingBuffer: 0, preferLowPowerToHighPerformance: 1, failIfMajorPerformanceCaveat: 0,
        majorVersion: 2, minorVersion: 0, enableExtensionsByDefault: 1, explicitSwapControl: 0,
        renderViaOffscreenBackBuffer: 0,
    };

    let handle;
    try {
        handle = registry.registerContext(kniGl, attributes);
    } catch (error) {
        log("registerContext THREW:", error?.stack || error?.message || String(error));
        return { registerContextAvailable: true, success: false, handle: 0, error: String(error?.stack || error?.message || error) };
    }
    if (!handle)
        return { registerContextAvailable: true, success: false, handle: 0, error: "registerContext returned a falsy handle." };

    try {
        registry.makeContextCurrent(handle);
    } catch (error) {
        log("makeContextCurrent THREW:", error?.stack || error?.message || String(error));
        return { registerContextAvailable: true, success: false, handle, error: String(error?.stack || error?.message || error) };
    }
    log("registerContext + makeContextCurrent succeeded, handle =", handle);
    return { registerContextAvailable: true, success: true, handle, error: null };
}

// Generic "make this Emscripten GL context handle current" - needed once Option D's own dedicated
// context exists alongside Option A's shared/registered one, so each side's Skia draw calls land on
// the context that side actually owns instead of whichever one happened to be current last. Cheap
// safety net: called before every Skia draw on both sides even if SkiaSharp's own Emscripten GL
// binding already restores its context internally per GRContext (unverified either way - explicit
// beats assumed here), so the cost is symmetric and doesn't bias either side's benchmark number.
export function makeGlContextCurrent(handle) {
    getEmscriptenGl().makeContextCurrent(handle);
}

export function getFramebufferInfo(contextUid) {
    const gl = getKniGl(contextUid);
    const framebuffer = gl.getParameter(gl.FRAMEBUFFER_BINDING);
    return {
        fboId: framebuffer ? framebuffer.id : 0,
        stencils: gl.getParameter(gl.STENCIL_BITS),
        samples: 0,
        depth: gl.getParameter(gl.DEPTH_BITS),
        width: gl.canvas.width,
        height: gl.canvas.height,
    };
}

export function readKniPixel(contextUid, x, y) {
    const gl = getKniGl(contextUid);
    const pixel = new Uint8Array(4);
    gl.readPixels(x, y, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);
    return Array.from(pixel);
}

export function checkGlError(contextUid) {
    const gl = getKniGl(contextUid);
    const error = gl.getError();
    if (error !== gl.NO_ERROR)
        log("glGetError() ==", error, "(non-zero means the shared context is in an error state)");
    return error;
}

export function getContextInfo(contextUid) {
    const gl = getKniGl(contextUid);
    const debugInfo = gl.getExtension("WEBGL_debug_renderer_info");
    return {
        webglVersion: gl.getParameter(gl.VERSION),
        renderer: debugInfo ? gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER),
        gpuTimerAvailable: !!gl.getExtension("EXT_disjoint_timer_query_webgl2"),
    };
}

// GPU timer query helpers (EXT_disjoint_timer_query_webgl2 - Chromium only; Firefox does not expose
// it by default, same caveat as docs/webgl/performance-results.md's existing Option D table).
const pendingQueriesByContext = new Map();

export function gpuQueryBegin(contextUid) {
    const gl = getKniGl(contextUid);
    const ext = gl.getExtension("EXT_disjoint_timer_query_webgl2");
    if (!ext) return false;
    const query = gl.createQuery();
    gl.beginQuery(ext.TIME_ELAPSED_EXT, query);
    if (!pendingQueriesByContext.has(contextUid)) pendingQueriesByContext.set(contextUid, []);
    pendingQueriesByContext.get(contextUid).push(query);
    return true;
}

export function gpuQueryEnd(contextUid) {
    const gl = getKniGl(contextUid);
    const ext = gl.getExtension("EXT_disjoint_timer_query_webgl2");
    if (!ext) return;
    gl.endQuery(ext.TIME_ELAPSED_EXT);
}

// Non-blocking poll: returns any completed query results (milliseconds) since the last poll,
// dropping any query flagged disjoint (invalid) - same shape as benchmark.js's pendingQueries loop.
export function gpuQueryPoll(contextUid) {
    const gl = getKniGl(contextUid);
    const ext = gl.getExtension("EXT_disjoint_timer_query_webgl2");
    const pending = pendingQueriesByContext.get(contextUid);
    if (!ext || !pending || pending.length === 0) return [];

    const results = [];
    const disjoint = gl.getParameter(ext.GPU_DISJOINT_EXT);
    for (let index = pending.length - 1; index >= 0; index--) {
        const query = pending[index];
        if (gl.getQueryParameter(query, gl.QUERY_RESULT_AVAILABLE)) {
            if (!disjoint) results.push(gl.getQueryParameter(query, gl.QUERY_RESULT) / 1e6);
            gl.deleteQuery(query);
            pending.splice(index, 1);
        }
    }
    return results;
}
