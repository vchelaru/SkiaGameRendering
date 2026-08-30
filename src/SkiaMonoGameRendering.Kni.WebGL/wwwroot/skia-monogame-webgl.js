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

globalThis.skiaMonoGameWebGl = globalThis.skiaMonoGameWebGl || {};
// gl/texture are resolved from KNI's own nkast.Wasm.Canvas JS object registry by Uid (the same
// registry nkast.Wasm.Canvas.WebGL uses for all of its JS interop) - see
// SkiaMonoGameRendering.Kni.WebGL's KniWebGlInternals.cs for how those Uids are obtained via
// reflection into KNI internals, since KNI has no public accessor for its current WebGL context.
globalThis.skiaMonoGameWebGl.uploadFromCanvas = function (
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
