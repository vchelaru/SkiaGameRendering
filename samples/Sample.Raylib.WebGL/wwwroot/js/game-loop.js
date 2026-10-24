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
