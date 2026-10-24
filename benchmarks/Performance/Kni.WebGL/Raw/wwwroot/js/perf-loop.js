// Keep identical to ../../../Skia/wwwroot/js/perf-loop.js (a static web asset has to live in its own project's wwwroot).
// Drives a PerfGame in the browser: one Game.Tick per animation frame, then hands the results to
// window.__perfResults. The frame rate is whatever the browser gives requestAnimationFrame, so run the
// browser with its frame-rate limit and vsync off to compare with the desktop numbers.
globalThis.perfLoop = (() => {
    let frame = 0;
    let instance = null;
    let canvas = null;

    function tick() {
        // The scenes are laid out for 1600x900 physical pixels, whatever the page or the display's scale.
        if (canvas.width !== 1600) canvas.width = 1600;
        if (canvas.height !== 900) canvas.height = 900;
        const done = instance.invokeMethod("Tick");
        if (done) {
            document.getElementById("status").textContent = "Done.";
            globalThis.__perfDone = true;
            stop();
            return;
        }
        frame = requestAnimationFrame(tick);
    }

    function stop() {
        if (frame) cancelAnimationFrame(frame);
        frame = 0;
    }

    function browserName() {
        const ua = navigator.userAgent;
        if (ua.includes("Firefox/")) return "Firefox";
        if (ua.includes("Edg/")) return "Edge";
        return "Chrome";
    }

    function gpuName() {
        try {
            const gl = document.createElement("canvas").getContext("webgl2");
            const ext = gl.getExtension("WEBGL_debug_renderer_info");
            return ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER);
        } catch {
            return "unknown";
        }
    }

    return {
        // The page's own description of the machine (the browser cannot see the CPU model), set before the
        // page loads by whatever drives it: window.__perfInfo = { os, cpu, power, commit }.
        info() {
            const given = globalThis.__perfInfo || {};
            return JSON.stringify({
                browser: browserName(),
                gpu: gpuName(),
                os: given.os || navigator.userAgent,
                cpu: given.cpu || `${navigator.hardwareConcurrency} threads`,
                power: given.power || "unknown",
                commit: given.commit || "unknown",
            });
        },
        // ?page=shapes&quick=1&scene=3 become --page shapes --quick --scene 3.
        args() {
            const query = new URLSearchParams(location.search);
            const args = [];
            if (query.has("page")) args.push("--page", query.get("page"));
            if (query.has("scene")) args.push("--scene", query.get("scene"));
            if (query.has("quick")) args.push("--quick");
            return args;
        },
        publish(name, markdown) {
            globalThis.__perfResults = globalThis.__perfResults || {};
            globalThis.__perfResults[name] = markdown;
        },
        start(dotNetInstance) {
            instance = dotNetInstance;
            canvas = document.getElementById("theCanvas");
            frame = requestAnimationFrame(tick);
        },
        stop,
    };
})();
