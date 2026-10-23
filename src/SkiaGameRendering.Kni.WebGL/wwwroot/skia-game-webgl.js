const hosts = new Map();

function getEmscriptenGl() {
    return globalThis.SkiaSharpGL || globalThis.SkiaSharpModule?.GL || globalThis.Module?.GL || globalThis.GL;
}

function getCurrentContext(glRegistry) {
    return glRegistry.currentContext?.GLctx || globalThis.GLctx;
}

function createContext(canvas, requireWebGl2, premultipliedAlpha) {
    const registry = getEmscriptenGl();
    if (!registry)
        throw new Error("SkiaSharp's Emscripten GL registry is unavailable.");

    const attributes = {
        alpha: 1,
        depth: 1,
        stencil: 8,
        antialias: 0,
        premultipliedAlpha: premultipliedAlpha ? 1 : 0,
        preserveDrawingBuffer: 0,
        preferLowPowerToHighPerformance: 1,
        failIfMajorPerformanceCaveat: 0,
        majorVersion: 2,
        minorVersion: 0,
        enableExtensionsByDefault: 1,
        explicitSwapControl: 0,
        renderViaOffscreenBackBuffer: 0,
    };

    let contextId = registry.createContext(canvas, attributes);
    if (!contextId && !requireWebGl2) {
        attributes.majorVersion = 1;
        contextId = registry.createContext(canvas, attributes);
    }
    if (!contextId)
        throw new Error(requireWebGl2 ? "WebGL 2 is unavailable." : "WebGL is unavailable.");

    registry.makeContextCurrent(contextId);
    return { registry, contextId };
}

export function initialize(elementId, dotNetReference, options) {
    if (hosts.has(elementId))
        throw new Error(`Canvas '${elementId}' is already initialized.`);

    const canvas = document.getElementById(elementId);
    if (!(canvas instanceof HTMLCanvasElement))
        throw new Error(`Canvas '${elementId}' was not found.`);

    const created = createContext(canvas, options.requireWebGl2, options.premultipliedAlpha);
    const gl = getCurrentContext(created.registry);
    const framebuffer = gl.getParameter(gl.FRAMEBUFFER_BINDING);
    const onLost = event => {
        event.preventDefault();
        dotNetReference.invokeMethodAsync("OnWebGlContextLost");
    };
    const onRestored = () => dotNetReference.invokeMethodAsync("OnWebGlContextRestored");
    canvas.addEventListener("webglcontextlost", onLost, false);
    canvas.addEventListener("webglcontextrestored", onRestored, false);

    hosts.set(elementId, { canvas, dotNetReference, onLost, onRestored, ...created });

    return {
        contextId: created.contextId,
        fboId: framebuffer ? framebuffer.id : 0,
        stencils: gl.getParameter(gl.STENCIL_BITS),
        samples: 0,
        depth: gl.getParameter(gl.DEPTH_BITS),
        devicePixelRatio: globalThis.devicePixelRatio || 1,
        version: gl.getParameter(gl.VERSION) || "",
        renderer: gl.getParameter(gl.RENDERER) || "",
    };
}

export function makeCurrent(elementId, width, height) {
    const host = hosts.get(elementId);
    if (!host)
        throw new Error(`Canvas '${elementId}' is not initialized.`);
    if (host.canvas.width !== width)
        host.canvas.width = width;
    if (host.canvas.height !== height)
        host.canvas.height = height;
    host.registry.makeContextCurrent(host.contextId);
}

export function dispose(elementId) {
    const host = hosts.get(elementId);
    if (!host)
        return;

    host.canvas.removeEventListener("webglcontextlost", host.onLost, false);
    host.canvas.removeEventListener("webglcontextrestored", host.onRestored, false);
    if (host.registry.currentContext?.handle === host.contextId)
        host.registry.makeContextCurrent(0);
    host.registry.deleteContext(host.contextId);
    hosts.delete(elementId);
}

globalThis.skiaGameWebGl = globalThis.skiaGameWebGl || {};
// gl/texture are resolved from KNI's own nkast.Wasm.Canvas JS object registry by Uid (the same
// registry nkast.Wasm.Canvas.WebGL uses for all of its JS interop) - see
// SkiaGameRendering.Kni.WebGL's KniWebGlInternals.cs for how those Uids are obtained via
// reflection into KNI internals, since KNI has no public accessor for its current WebGL context.
globalThis.skiaGameWebGl.uploadFromCanvas = function (
    contextUid, textureUid, sourceElementId, flipY, premultiplyAlpha, disableColorSpaceConversion, useTexImage) {
    const gl = globalThis.nkJSObject.GetObject(contextUid);
    const texture = globalThis.nkJSObject.GetObject(textureUid);
    const source = document.getElementById(sourceElementId);
    if (!gl)
        throw new Error("The KNI WebGL context is unavailable.");
    if (gl.isContextLost())
        throw new Error("The KNI WebGL context is lost.");
    if (!texture)
        throw new Error("The KNI destination texture is unavailable.");
    if (!(source instanceof HTMLCanvasElement))
        throw new Error(`Source canvas '${sourceElementId}' is unavailable.`);

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
};

// --- Direct canvas (SkiaDirectCanvas) ---
//
// Skia draws straight into KNI's own WebGL2 context, so there is no second canvas and no upload. WebGL
// forbids sharing objects between contexts, so the only way to do that is for both to drive the one
// context: KNI's gl object is registered with SkiaSharp's Emscripten GL runtime, which then runs Skia's GL
// calls on it. The cost is that Skia leaves GL state behind that KNI does not know about.
// beginDirectDraw saves the bindings KNI relies on, endDirectDraw puts them back, and SkiaWebGlBackend
// resets KNI's own state cache on the .NET side.

const kniContexts = new Map(); // KNI context Uid -> { gl, handle, epoch, registeredEpoch, canvas, onLost }
const kniFramebufferIds = new WeakMap(); // KNI WebGLFramebuffer -> its id in Emscripten's framebuffer table
const kniFramebuffers = new Map(); // that id -> WebGLFramebuffer
let directGlErrors = 0;
let directPasses = 0;
const directGlErrorLog = []; // [pass, GL error code] for the first few
let savedState = null;

function getKniGl(contextUid) {
    const gl = globalThis.nkJSObject.GetObject(contextUid);
    if (!gl)
        throw new Error("The KNI WebGL context is unavailable.");
    return gl;
}

function registerKniContext(registry, contextUid, gl, epoch) {
    if (typeof registry.registerContext !== "function")
        throw new Error("This SkiaSharp build cannot adopt an existing WebGL context (GL.registerContext is missing).");

    // KNI's real context attributes, as the integers Emscripten stores.
    const actual = gl.getContextAttributes() || {};
    const handle = registry.registerContext(gl, {
        alpha: actual.alpha ? 1 : 0,
        depth: actual.depth ? 1 : 0,
        stencil: actual.stencil ? 1 : 0,
        antialias: actual.antialias ? 1 : 0,
        premultipliedAlpha: actual.premultipliedAlpha ? 1 : 0,
        preserveDrawingBuffer: actual.preserveDrawingBuffer ? 1 : 0,
        preferLowPowerToHighPerformance: 0,
        failIfMajorPerformanceCaveat: 0,
        majorVersion: 2,
        minorVersion: 0,
        enableExtensionsByDefault: 1,
        explicitSwapControl: 0,
        renderViaOffscreenBackBuffer: 0,
    });
    if (!handle)
        throw new Error("GL.registerContext returned no handle for KNI's WebGL context.");

    const entry = { gl, handle, epoch, registeredEpoch: epoch, canvas: gl.canvas, onLost: null };
    // Counted rather than flagged: a loss and restore between two passes still has to rebuild Skia's context.
    entry.onLost = () => { entry.epoch++; };
    entry.canvas?.addEventListener("webglcontextlost", entry.onLost, false);
    kniContexts.set(contextUid, entry);
    return entry;
}

function unregisterKniContext(registry, contextUid, entry) {
    entry.canvas?.removeEventListener("webglcontextlost", entry.onLost, false);
    if (registry.currentContext?.handle === entry.handle)
        registry.makeContextCurrent(0);
    registry.deleteContext(entry.handle);
    kniContexts.delete(contextUid);
}

function emscriptenFramebufferId(registry, framebuffer) {
    let id = kniFramebufferIds.get(framebuffer);
    if (id !== undefined)
        return id;
    if (!Array.isArray(registry.framebuffers) || typeof registry.getNewId !== "function")
        throw new Error("This SkiaSharp build cannot adopt an existing WebGL framebuffer (GL.framebuffers is missing).");
    id = registry.getNewId(registry.framebuffers);
    framebuffer.name = id;
    registry.framebuffers[id] = framebuffer;
    kniFramebufferIds.set(framebuffer, id);
    kniFramebuffers.set(id, framebuffer);
    return id;
}

// Returns [status, handle, epoch, framebufferId, samples, stencilBits, width, height, previousHandle].
// status 0 is ready, 1 is KNI's context lost (nothing was changed). framebufferId 0 is the canvas's own.
// Constants are numeric because the raw KNI gl object does not always expose them.
globalThis.skiaGameWebGl.beginDirectDraw = function (contextUid) {
    const registry = getEmscriptenGl();
    if (!registry)
        throw new Error("SkiaSharp's Emscripten GL registry is unavailable.");
    const gl = getKniGl(contextUid);

    let entry = kniContexts.get(contextUid);
    if (gl.isContextLost())
        return [1, entry ? entry.handle : 0, entry ? entry.epoch : 0, 0, 0, 0, 0, 0, 0];

    if (entry && entry.gl !== gl) {
        // The Uid now names a different context object.
        unregisterKniContext(registry, contextUid, entry);
        entry = undefined;
    }
    if (entry && entry.registeredEpoch !== entry.epoch) {
        // Lost and restored: the old registration's extensions went with the context.
        const epoch = entry.epoch;
        unregisterKniContext(registry, contextUid, entry);
        entry = registerKniContext(registry, contextUid, gl, epoch);
    }
    entry ??= registerKniContext(registry, contextUid, gl, 0);

    const previousHandle = registry.currentContext?.handle ?? 0;
    registry.makeContextCurrent(entry.handle);

    savedState = {
        draw: gl.getParameter(0x8CA6), // DRAW_FRAMEBUFFER_BINDING
        read: gl.getParameter(0x8CAA), // READ_FRAMEBUFFER_BINDING
        vao: gl.getParameter(0x85B5), // VERTEX_ARRAY_BINDING
        activeTexture: gl.getParameter(0x84E0), // ACTIVE_TEXTURE
    };
    const framebufferId = savedState.draw ? emscriptenFramebufferId(registry, savedState.draw) : 0;

    return [
        0, entry.handle, entry.epoch, framebufferId,
        gl.getParameter(0x80A9) ?? 0, // SAMPLES
        gl.getParameter(0x0D57) ?? 0, // STENCIL_BITS
        gl.drawingBufferWidth, gl.drawingBufferHeight,
        previousHandle,
    ];
};

// Puts back the bindings KNI relies on and hands Emscripten's current context back.
globalThis.skiaGameWebGl.endDirectDraw = function (contextUid, previousHandle) {
    const registry = getEmscriptenGl();
    const gl = getKniGl(contextUid);
    if (savedState && !gl.isContextLost()) {
        gl.bindFramebuffer(0x8CA9, savedState.draw); // DRAW_FRAMEBUFFER
        gl.bindFramebuffer(0x8CA8, savedState.read); // READ_FRAMEBUFFER
        gl.bindVertexArray(savedState.vao);
        gl.activeTexture(savedState.activeTexture);
        gl.bindBuffer(0x8892, null); // ARRAY_BUFFER, which KNI binds again before any attribute pointer

        // KNI believes no vertex attribute is enabled after a pass (its cache is reset on the .NET side).
        const attributes = gl.getParameter(0x8869); // MAX_VERTEX_ATTRIBS
        for (let index = 0; index < attributes; index++)
            gl.disableVertexAttribArray(index);

        // Skia's pixel-store settings, back to the defaults KNI assumes.
        gl.pixelStorei(0x0CF5, 4); // UNPACK_ALIGNMENT
        gl.pixelStorei(0x0CF2, 0); // UNPACK_ROW_LENGTH
        gl.pixelStorei(0x0CF3, 0); // UNPACK_SKIP_ROWS
        gl.pixelStorei(0x0CF4, 0); // UNPACK_SKIP_PIXELS

        // Errors Skia left queued would be blamed on KNI's next call.
        for (let i = 0, error; i < 8 && (error = gl.getError()) !== 0; i++) {
            directGlErrors++;
            if (directGlErrorLog.length < 8)
                directGlErrorLog.push([directPasses, error]);
        }
    }
    directPasses++;
    savedState = null;
    if (registry && previousHandle > 0)
        registry.makeContextCurrent(previousHandle);
};

// Makes KNI's registered context current for Skia work outside a pass (freeing its GRContext). Returns the
// handle to give restoreContext, or -1 when KNI's context was never registered.
globalThis.skiaGameWebGl.makeKniContextCurrent = function (contextUid) {
    const registry = getEmscriptenGl();
    const entry = kniContexts.get(contextUid);
    if (!registry || !entry)
        return -1;
    const previousHandle = registry.currentContext?.handle ?? 0;
    registry.makeContextCurrent(entry.handle);
    return previousHandle;
};

globalThis.skiaGameWebGl.restoreContext = function (previousHandle) {
    const registry = getEmscriptenGl();
    if (registry && previousHandle > 0)
        registry.makeContextCurrent(previousHandle);
};

globalThis.skiaGameWebGl.releaseKniFramebuffer = function (framebufferId) {
    const registry = getEmscriptenGl();
    const framebuffer = kniFramebuffers.get(framebufferId);
    if (!framebuffer)
        return;
    kniFramebufferIds.delete(framebuffer);
    kniFramebuffers.delete(framebufferId);
    if (registry?.framebuffers)
        registry.framebuffers[framebufferId] = null;
};

globalThis.skiaGameWebGl.isKniContextLost = function (contextUid) {
    const gl = globalThis.nkJSObject.GetObject(contextUid);
    return !gl || gl.isContextLost();
};

globalThis.skiaGameWebGl.disposeKniContext = function (contextUid) {
    const registry = getEmscriptenGl();
    const entry = kniContexts.get(contextUid);
    if (registry && entry)
        unregisterKniContext(registry, contextUid, entry);
};

// GL errors Skia left in KNI's queue that endDirectDraw cleared, and the first few as [pass, code].
globalThis.skiaGameWebGl.getDirectGlErrorCount = function () { return directGlErrors; };
globalThis.skiaGameWebGl.getDirectGlErrorLog = function () { return JSON.stringify(directGlErrorLog); };
