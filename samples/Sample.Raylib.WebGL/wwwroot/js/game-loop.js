// ?es2=1 hides OES_vertex_array_object before raylib creates its context, so raylib's ES2 build takes its
// no-VAO path (it re-specifies vertex attributes on every draw). Counts the VAOs created so tests can see it.
globalThis.skiaRaylibEs2 = (() => {
    const forced = new URLSearchParams(location.search).get("es2") === "1";
    const state = { forced, vaosCreated: 0, vaoBinds: 0, attribPointers: 0 };
    const proto = WebGL2RenderingContext.prototype;
    const createVertexArray = proto.createVertexArray;
    proto.createVertexArray = function () { state.vaosCreated++; return createVertexArray.call(this); };
    // With a VAO raylib sets its vertex attributes once and rebinds the VAO per draw; without one it re-specifies the
    // attributes on every draw. ?mode=raylib draws no Skia, so these counts are raylib's alone.
    const bindVertexArray = proto.bindVertexArray;
    proto.bindVertexArray = function (vao) { state.vaoBinds++; return bindVertexArray.call(this, vao); };
    const vertexAttribPointer = proto.vertexAttribPointer;
    proto.vertexAttribPointer = function (...args) { state.attribPointers++; return vertexAttribPointer.apply(this, args); };
    if (forced) {
        const hidden = name => /vertex_array_object/i.test(name);
        const getSupportedExtensions = proto.getSupportedExtensions;
        proto.getSupportedExtensions = function () { return (getSupportedExtensions.call(this) ?? []).filter(name => !hidden(name)); };
        const getExtension = proto.getExtension;
        proto.getExtension = function (name) { return hidden(name) ? null : getExtension.call(this, name); };
    }
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
