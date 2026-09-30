globalThis.skiaKniSample = (() => {
    let frame = 0;
    let instance = null;
    let canvas = null;

    function resizeCanvas() {
        const dpr = globalThis.devicePixelRatio || 1;
        const width = Math.max(1, Math.floor(canvas.clientWidth * dpr));
        const height = Math.max(1, Math.floor(canvas.clientHeight * dpr));
        if (canvas.width !== width) canvas.width = width;
        if (canvas.height !== height) canvas.height = height;
    }

    function tick() {
        resizeCanvas();
        const diagnosticTexImage = document.getElementById("upload-mode")?.value === "image";
        const diagnostics = instance.invokeMethod("Tick", canvas.width, canvas.height, diagnosticTexImage);
        if (diagnostics)
            document.getElementById("diagnostic-text").textContent = diagnostics;
        frame = requestAnimationFrame(tick);
    }

    function stop() {
        if (frame) cancelAnimationFrame(frame);
        frame = 0;
        instance = null;
        canvas = null;
    }

    return {
        start(dotNetInstance) {
            if (instance) stop();
            instance = dotNetInstance;
            canvas = document.getElementById("theCanvas");
            frame = requestAnimationFrame(tick);
        },
        stop,
    };
})();
