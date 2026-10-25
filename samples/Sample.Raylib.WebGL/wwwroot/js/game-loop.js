// Counts the WebGL calls that show whether raylib uses vertex array objects. raylib's build is OpenGL ES 2 and only uses
// VAOs through the OES_vertex_array_object extension, which a WebGL 2 context (the only kind it creates here) never
// offers, so it asks for it, gets null and re-specifies its vertex attributes on every draw.
globalThis.skiaRaylibGl = (() => {
    const state = { vaoExtensionRequests: 0, vaoExtensionGranted: 0, vaoBinds: 0, attribPointers: 0 };
    const proto = WebGL2RenderingContext.prototype;
    const getExtension = proto.getExtension;
    proto.getExtension = function (name) {
        const result = getExtension.call(this, name);
        if (/vertex_array_object/i.test(name)) {
            state.vaoExtensionRequests++;
            if (result)
                state.vaoExtensionGranted++;
        }
        return result;
    };
    // ?mode=raylib draws no Skia, so these counts are raylib's alone.
    const bindVertexArray = proto.bindVertexArray;
    proto.bindVertexArray = function (vao) { state.vaoBinds++; return bindVertexArray.call(this, vao); };
    const vertexAttribPointer = proto.vertexAttribPointer;
    proto.vertexAttribPointer = function (...args) { state.attribPointers++; return vertexAttribPointer.apply(this, args); };
    return state;
})();

globalThis.skiaRaylibSample = (() => {
    let started = false;

    function tick() {
        // ?mode=direct draws with SkiaDirectCanvas, anything else through a SkiaRenderTarget2D.
        const mode = new URLSearchParams(location.search).get("mode") ?? "target";
        let text;
        try {
            text = DotNet.invokeMethod("Sample.Raylib.WebGL", "Frame", mode);
        } catch (error) {
            // Blazor.start can resolve before the app's assembly is registered with the JS interop; retry until it is.
            if (started)
                throw error;
        }
        started = started || text !== undefined;
        if (text)
            document.getElementById("diagnostic-text").textContent = text;
        requestAnimationFrame(tick);
    }

    return { start() { requestAnimationFrame(tick); } };
})();
