globalThis.skiaKniSample = (() => {
    let frame = 0;
    let instance = null;
    let canvas = null;
    let handlers = null;
    const input = {
        x: 0, y: 0, down: false, downPending: false, releasePending: false,
        wheel: 0, text: "", pointerType: "none"
    };

    function resizeCanvas() {
        const dpr = globalThis.devicePixelRatio || 1;
        const width = Math.max(1, Math.floor(canvas.clientWidth * dpr));
        const height = Math.max(1, Math.floor(canvas.clientHeight * dpr));
        if (canvas.width !== width) canvas.width = width;
        if (canvas.height !== height) canvas.height = height;
        return dpr;
    }

    function updatePointer(event) {
        const rect = canvas.getBoundingClientRect();
        input.x = (event.clientX - rect.left) * 1280 / Math.max(1, rect.width);
        input.y = (event.clientY - rect.top) * 720 / Math.max(1, rect.height);
        input.pointerType = event.pointerType || "mouse";
    }

    function attachInput() {
        handlers = {
            contextmenu: event => event.preventDefault(),
            pointermove: event => updatePointer(event),
            pointerdown: event => {
                updatePointer(event);
                input.down = true;
                input.downPending = true;
                canvas.focus({ preventScroll: true });
                canvas.setPointerCapture?.(event.pointerId);
                event.preventDefault();
            },
            pointerup: event => {
                updatePointer(event);
                if (input.downPending) input.releasePending = true;
                else input.down = false;
                canvas.releasePointerCapture?.(event.pointerId);
                event.preventDefault();
            },
            pointercancel: event => {
                if (input.downPending) input.releasePending = true;
                else input.down = false;
                canvas.releasePointerCapture?.(event.pointerId);
            },
            wheel: event => {
                updatePointer(event);
                input.wheel += -event.deltaY;
                event.preventDefault();
            },
            keydown: event => {
                if (!event.ctrlKey && !event.metaKey && !event.altKey) {
                    if (event.key.length === 1) input.text += event.key;
                    else if (event.key === "Backspace") input.text += "\b";
                }
                event.preventDefault();
            },
        };

        for (const [name, handler] of Object.entries(handlers))
            canvas.addEventListener(name, handler, name === "wheel" ? { passive: false } : false);
    }

    function detachInput() {
        if (!canvas || !handlers) return;
        for (const [name, handler] of Object.entries(handlers))
            canvas.removeEventListener(name, handler, false);
        handlers = null;
    }

    function tick() {
        const dpr = resizeCanvas();
        const diagnosticTexImage = document.getElementById("upload-mode")?.value === "image";
        const consumedPendingDown = input.downPending;
        const diagnostics = instance.invokeMethod(
            "Tick", dpr, canvas.width, canvas.height, input.x, input.y, input.down,
            input.wheel, input.text, input.pointerType, diagnosticTexImage);
        input.wheel = 0;
        input.text = "";
        if (consumedPendingDown) {
            input.downPending = false;
            if (input.releasePending) {
                input.down = false;
                input.releasePending = false;
            }
        }
        if (diagnostics)
            document.getElementById("diagnostic-text").textContent = diagnostics;
        frame = requestAnimationFrame(tick);
    }

    function stop() {
        if (frame) cancelAnimationFrame(frame);
        detachInput();
        frame = 0;
        instance = null;
        canvas = null;
        input.down = false;
        input.downPending = false;
        input.releasePending = false;
        input.wheel = 0;
        input.text = "";
    }

    return {
        start(dotNetInstance) {
            if (instance) stop();
            instance = dotNetInstance;
            canvas = document.getElementById("theCanvas");
            attachInput();
            frame = requestAnimationFrame(tick);
        },
        stop,
    };
})();
