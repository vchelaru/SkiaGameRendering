// Live, in-page Option D path (repo issue #12): Skia renders to its OWN separate canvas/context,
// then a cross-context texSubImage2D blit copies the result into a dedicated KNI destination
// texture - this repo's actual shipped architecture (src/SkiaGameRendering.Kni.WebGL). Added
// alongside the Option A path (context-bridge.js) so both can be measured back-to-back in the SAME
// page load/browser session/power state, instead of comparing a live Option A run against
// docs/webgl/performance-results.md's static baseline captured a different day under unknown power
// conditions. Mirrors src/SkiaGameRendering.Kni.WebGL/wwwroot/skia-game-webgl.js's production
// createContext()/uploadFromCanvas() mechanism - NOT imported from there, duplicated here to keep
// this benchmark self-contained (same "must not depend on production" convention already documented
// on Benchmarks.WebGLOptionA.csproj's SkiaSharp package references).

function log(...args) {
    console.log("[optionD]", ...args);
}

function getEmscriptenGl() {
    return globalThis.SkiaSharpGL || globalThis.SkiaSharpModule?.GL || globalThis.Module?.GL || globalThis.GL;
}

// Module-local, not registered in KNI's nkJSObject registry - Option D's canvas/context is Skia's
// own, created directly via Emscripten's GL registry, never touched by KNI at all.
let optionDGl = null;

export function createOptionDContext(canvasElementId) {
    const canvas = document.getElementById(canvasElementId);
    if (!(canvas instanceof HTMLCanvasElement))
        throw new Error(`Option D source canvas '${canvasElementId}' was not found.`);

    const registry = getEmscriptenGl();
    if (!registry)
        throw new Error("SkiaSharp's Emscripten GL registry is unavailable.");

    // Same attributes skia-game-webgl.js's production createContext() uses.
    const attributes = {
        alpha: 1, depth: 1, stencil: 8, antialias: 0, premultipliedAlpha: 1,
        preserveDrawingBuffer: 0, preferLowPowerToHighPerformance: 1, failIfMajorPerformanceCaveat: 0,
        majorVersion: 2, minorVersion: 0, enableExtensionsByDefault: 1, explicitSwapControl: 0,
        renderViaOffscreenBackBuffer: 0,
    };

    const handle = registry.createContext(canvas, attributes);
    if (!handle)
        throw new Error("Option D's dedicated Skia canvas: WebGL2 createContext failed.");
    registry.makeContextCurrent(handle);

    optionDGl = registry.currentContext?.GLctx || globalThis.GLctx;
    if (!optionDGl)
        throw new Error("Could not resolve the GL object for Option D's newly-created context.");

    const framebuffer = optionDGl.getParameter(optionDGl.FRAMEBUFFER_BINDING);
    log("createOptionDContext succeeded, handle =", handle);
    return {
        handle,
        fboId: framebuffer ? framebuffer.id : 0,
        stencils: optionDGl.getParameter(optionDGl.STENCIL_BITS),
        samples: 0,
        depth: optionDGl.getParameter(optionDGl.DEPTH_BITS),
        width: optionDGl.canvas.width,
        height: optionDGl.canvas.height,
    };
}

// Duplicate of context-bridge.js's makeGlContextCurrent - OptionDFrameRunner.cs calls this on ITS
// OWN module reference (this file), not context-bridge.js's, so it needs its own copy (see
// Index.razor.cs's comment on why each dynamically-imported module needs its functions called on
// its own reference).
export function makeGlContextCurrent(handle) {
    getEmscriptenGl().makeContextCurrent(handle);
}

export function getOptionDFramebufferInfo() {
    const gl = optionDGl;
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

// Cross-context blit into KNI's own texture - same body as skia-game-webgl.js's production
// globalThis.skiaGameWebgl.uploadFromCanvas, duplicated here (see file header).
export function uploadOptionDCanvasToKniTexture(
    kniContextUid, kniTextureUid, sourceElementId, flipY, premultiplyAlpha, disableColorSpaceConversion, useTexImage) {
    const gl = globalThis.nkJSObject.GetObject(kniContextUid);
    const texture = globalThis.nkJSObject.GetObject(kniTextureUid);
    const source = document.getElementById(sourceElementId);
    if (!gl) throw new Error("The KNI WebGL context is unavailable.");
    if (gl.isContextLost()) throw new Error("The KNI WebGL context is lost.");
    if (!texture) throw new Error("The KNI destination texture is unavailable.");
    if (!(source instanceof HTMLCanvasElement)) throw new Error(`Source canvas '${sourceElementId}' is unavailable.`);

    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 4);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, flipY ? 1 : 0);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, premultiplyAlpha ? 1 : 0);
    gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, disableColorSpaceConversion ? gl.NONE : gl.BROWSER_DEFAULT_WEBGL);

    try {
        if (useTexImage)
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, source);
        else
            gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, gl.RGBA, gl.UNSIGNED_BYTE, source);
    } finally {
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, 0);
        gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, 0);
        gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.BROWSER_DEFAULT_WEBGL);
        gl.pixelStorei(gl.UNPACK_ALIGNMENT, 4);
    }
}
