globalThis.skiaRaylibSample = (() => {
    function tick() {
        // ?mode=direct draws with SkiaDirectCanvas, anything else through a SkiaRenderTarget2D.
        const mode = new URLSearchParams(location.search).get("mode") ?? "target";
        const text = DotNet.invokeMethod("Sample.Raylib.WebGL", "Frame", mode);
        if (text)
            document.getElementById("diagnostic-text").textContent = text;
        requestAnimationFrame(tick);
    }

    return { start() { requestAnimationFrame(tick); } };
})();
