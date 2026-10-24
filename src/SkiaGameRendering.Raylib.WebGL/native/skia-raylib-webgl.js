// Linked into the app's dotnet.native.js with Emscripten's --pre-js (see build/SkiaGameRendering.Raylib.WebGL.props),
// so it runs inside the same Module as raylib and SkiaSharp and needs no <script> tag on the page.
//
// raylib (PLATFORM_WEB) and SkiaSharp are both statically linked into that Module, so they drive one WebGL2
// context through Emscripten's GL runtime: raylib creates it, Skia's GRGlInterface uses whichever context is
// current. The only thing left to do is to keep their GL state from clobbering each other. beginPass saves
// every piece of state raylib relies on, endPass puts it back, and the .NET side resets Skia's own cache
// (GRContext.ResetContext). Framebuffers and textures are addressed by Emscripten's integer ids, the same
// ids raylib gets from glGen* and Skia passes to glBind*.

Module["preRun"] = Module["preRun"] || [];
Module["preRun"].push(function () {
    // raylib reads Module.canvas; the page chooses the element (default id "canvas").
    const id = globalThis.skiaRaylibCanvasId || "canvas";
    const canvas = document.getElementById(id);
    if (canvas)
        Module["canvas"] = canvas;
});

globalThis.skiaRaylibWebGl = (function () {
    const GL_FRAMEBUFFER = 0x8D40, GL_DRAW_FRAMEBUFFER = 0x8CA9, GL_READ_FRAMEBUFFER = 0x8CA8;
    const GL_DRAW_FRAMEBUFFER_BINDING = 0x8CA6, GL_READ_FRAMEBUFFER_BINDING = 0x8CAA;
    const GL_RENDERBUFFER = 0x8D41, GL_RENDERBUFFER_BINDING = 0x8CA7;
    const GL_COLOR_ATTACHMENT0 = 0x8CE0, GL_DEPTH_STENCIL_ATTACHMENT = 0x821A;
    const GL_DEPTH24_STENCIL8 = 0x88F0, GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    const GL_OBJECT_TYPE = 0x8CD0, GL_OBJECT_NAME = 0x8CD1, GL_TEXTURE = 0x1702, GL_TEXTURE_2D = 0x0DE1;
    const TEXTURE_UNITS = 4;

    // The raw state restored after every pass: [capability, ...] for enable/disable and [getter, setter] pairs.
    const capabilities = [
        0x0BE2, // BLEND
        0x0B71, // DEPTH_TEST
        0x0B44, // CULL_FACE
        0x0C11, // SCISSOR_TEST
        0x0B90, // STENCIL_TEST
        0x0BD0, // DITHER
        0x8037, // POLYGON_OFFSET_FILL
        0x809E, // SAMPLE_ALPHA_TO_COVERAGE
        0x80A0, // SAMPLE_COVERAGE
        0x8C89, // RASTERIZER_DISCARD
    ];

    const renderbuffers = new Map(); // handle -> WebGLRenderbuffer, for the ones created here
    let nextRenderbuffer = 1;
    let saved = null;
    let errors = 0;
    const errorLog = [];
    let passes = 0;

    function context() {
        const current = GL.currentContext;
        if (!current || !current.GLctx)
            throw new Error("raylib's WebGL context is not current. Call Raylib.InitWindow before using SkiaGameRendering.");
        return current;
    }

    function framebufferId(framebuffer) {
        if (!framebuffer)
            return 0;
        if (framebuffer.name)
            return framebuffer.name;
        // A framebuffer created outside Emscripten's table: register it so Skia can name it.
        const id = GL.getNewId(GL.framebuffers);
        framebuffer.name = id;
        GL.framebuffers[id] = framebuffer;
        return id;
    }

    function save(gl) {
        const state = {
            draw: gl.getParameter(GL_DRAW_FRAMEBUFFER_BINDING),
            read: gl.getParameter(GL_READ_FRAMEBUFFER_BINDING),
            renderbuffer: gl.getParameter(GL_RENDERBUFFER_BINDING),
            viewport: gl.getParameter(0x0BA2),
            scissor: gl.getParameter(0x0C10),
            enabled: capabilities.map(cap => gl.isEnabled(cap)),
            blendColor: gl.getParameter(0x8005),
            blendFunc: [gl.getParameter(0x80C9), gl.getParameter(0x80C8), gl.getParameter(0x80CB), gl.getParameter(0x80CA)],
            blendEquation: [gl.getParameter(0x8009), gl.getParameter(0x883D)],
            depthMask: gl.getParameter(0x0B72),
            depthFunc: gl.getParameter(0x0B74),
            cullFace: gl.getParameter(0x0B45),
            frontFace: gl.getParameter(0x0B46),
            stencil: [gl.getParameter(0x0B92), gl.getParameter(0x0B97), gl.getParameter(0x0B93),
                gl.getParameter(0x0B94), gl.getParameter(0x0B95), gl.getParameter(0x0B96)],
            stencilMask: gl.getParameter(0x0B98),
            colorMask: gl.getParameter(0x0C23),
            clearColor: gl.getParameter(0x0C22),
            program: gl.getParameter(0x8B8D),
            vao: gl.getParameter(0x85B5),
            arrayBuffer: gl.getParameter(0x8894),
            activeTexture: gl.getParameter(0x84E0),
            textures: [],
            samplers: [],
            unpack: [gl.getParameter(0x0CF5), gl.getParameter(0x0CF2), gl.getParameter(0x0CF3), gl.getParameter(0x0CF4)],
            pack: [gl.getParameter(0x0D05), gl.getParameter(0x0D02), gl.getParameter(0x0D03), gl.getParameter(0x0D04)],
        };
        // Without a vertex array object (raylib's ES2 build, which is the one Raylib-cs ships for the browser, has
        // none on WebGL 2) its attribute setup lives on the default one, where Skia's draws overwrite it.
        if (!state.vao) {
            state.elementBuffer = gl.getParameter(0x8895);
            state.attributes = [];
            const count = gl.getParameter(0x8869);
            for (let index = 0; index < count; index++) {
                state.attributes.push({
                    enabled: gl.getVertexAttrib(index, 0x8622),
                    buffer: gl.getVertexAttrib(index, 0x889F),
                    size: gl.getVertexAttrib(index, 0x8623),
                    type: gl.getVertexAttrib(index, 0x8625),
                    normalized: gl.getVertexAttrib(index, 0x886A),
                    stride: gl.getVertexAttrib(index, 0x8624),
                    offset: gl.getVertexAttribOffset(index, 0x8645),
                    divisor: gl.getVertexAttrib(index, 0x88FE),
                });
            }
        }
        for (let unit = 0; unit < TEXTURE_UNITS; unit++) {
            gl.activeTexture(0x84C0 + unit);
            state.textures.push(gl.getParameter(0x8069));
            state.samplers.push(gl.getParameter(0x8919));
        }
        gl.activeTexture(state.activeTexture);
        return state;
    }

    function restore(gl, state) {
        gl.bindFramebuffer(GL_DRAW_FRAMEBUFFER, state.draw);
        gl.bindFramebuffer(GL_READ_FRAMEBUFFER, state.read);
        gl.bindRenderbuffer(GL_RENDERBUFFER, state.renderbuffer);
        gl.viewport(state.viewport[0], state.viewport[1], state.viewport[2], state.viewport[3]);
        gl.scissor(state.scissor[0], state.scissor[1], state.scissor[2], state.scissor[3]);
        capabilities.forEach((cap, index) => state.enabled[index] ? gl.enable(cap) : gl.disable(cap));
        gl.blendColor(state.blendColor[0], state.blendColor[1], state.blendColor[2], state.blendColor[3]);
        gl.blendFuncSeparate(state.blendFunc[0], state.blendFunc[1], state.blendFunc[2], state.blendFunc[3]);
        gl.blendEquationSeparate(state.blendEquation[0], state.blendEquation[1]);
        gl.depthMask(state.depthMask);
        gl.depthFunc(state.depthFunc);
        gl.cullFace(state.cullFace);
        gl.frontFace(state.frontFace);
        gl.stencilFunc(state.stencil[0], state.stencil[1], state.stencil[2]);
        gl.stencilOp(state.stencil[3], state.stencil[4], state.stencil[5]);
        gl.stencilMask(state.stencilMask);
        gl.colorMask(state.colorMask[0], state.colorMask[1], state.colorMask[2], state.colorMask[3]);
        gl.clearColor(state.clearColor[0], state.clearColor[1], state.clearColor[2], state.clearColor[3]);

        // Skia leaves its own vertex array, buffers and program bound; raylib binds what it needs again, but
        // only on top of the bindings it believes are in place.
        gl.bindVertexArray(state.vao);
        gl.bindBuffer(0x8892, state.arrayBuffer);
        gl.useProgram(state.program);
        for (let unit = 0; unit < TEXTURE_UNITS; unit++) {
            gl.activeTexture(0x84C0 + unit);
            gl.bindTexture(GL_TEXTURE_2D, state.textures[unit]);
            gl.bindSampler(unit, state.samplers[unit]);
        }
        gl.activeTexture(state.activeTexture);

        // Skia changes vertex attributes on the default vertex array when it has none of its own; put back the
        // ones saved above (raylib's attributes live on its own VAOs when it has any).
        if (!state.vao) {
            state.attributes.forEach((attribute, index) => {
                gl.bindBuffer(0x8892, attribute.buffer);
                if (attribute.buffer)
                    gl.vertexAttribPointer(index, attribute.size, attribute.type, attribute.normalized, attribute.stride, attribute.offset);
                gl.vertexAttribDivisor(index, attribute.divisor);
                if (attribute.enabled)
                    gl.enableVertexAttribArray(index);
                else
                    gl.disableVertexAttribArray(index);
            });
            gl.bindBuffer(0x8892, state.arrayBuffer);
            gl.bindBuffer(0x8893, state.elementBuffer);
        }

        gl.pixelStorei(0x0CF5, state.unpack[0]);
        gl.pixelStorei(0x0CF2, state.unpack[1]);
        gl.pixelStorei(0x0CF3, state.unpack[2]);
        gl.pixelStorei(0x0CF4, state.unpack[3]);
        gl.pixelStorei(0x0D05, state.pack[0]);
        gl.pixelStorei(0x0D02, state.pack[1]);
        gl.pixelStorei(0x0D03, state.pack[2]);
        gl.pixelStorei(0x0D04, state.pack[3]);

        // Errors Skia left queued would be blamed on raylib's next call.
        for (let i = 0, error; i < 8 && (error = gl.getError()) !== 0; i++) {
            errors++;
            if (errorLog.length < 8)
                errorLog.push([passes, error]);
        }
    }

    return {
        // Returns [contextHandle, isWebGl2, samples, stencilBits, alpha]. Throws until raylib has a context.
        attach() {
            const current = context();
            const gl = current.GLctx;
            const attributes = gl.getContextAttributes() || {};
            return [current.handle, gl instanceof WebGL2RenderingContext ? 1 : 0,
                attributes.antialias ? 1 : 0, attributes.stencil ? 1 : 0, attributes.alpha ? 1 : 0];
        },

        isContextLost() {
            const current = GL.currentContext;
            return !current || !current.GLctx || current.GLctx.isContextLost();
        },

        // Saves raylib's GL state. Returns [framebufferId, drawingBufferWidth, drawingBufferHeight, samples,
        // stencilBits, attachmentKind] where framebufferId is the draw framebuffer raylib is rendering into
        // (0 for the canvas) and attachmentKind is 0 for none, 1 texture, 2 renderbuffer. samples and stencilBits
        // describe the canvas, and are 0 for a framebuffer object.
        beginPass() {
            const gl = context().GLctx;
            if (saved)
                throw new Error("A Skia pass is already open.");
            saved = save(gl);
            const id = framebufferId(saved.draw);
            let kind = 0;
            if (saved.draw) {
                const type = gl.getFramebufferAttachmentParameter(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_OBJECT_TYPE);
                kind = type === GL_TEXTURE ? 1 : (type === GL_RENDERBUFFER ? 2 : 0);
            }
            return [id, gl.drawingBufferWidth, gl.drawingBufferHeight,
                saved.draw ? 0 : (gl.getParameter(0x80A9) || 0), saved.draw ? 0 : (gl.getParameter(0x0D57) || 0), kind];
        },

        // Puts raylib's GL state back, whatever Skia changed.
        endPass() {
            const gl = context().GLctx;
            if (!saved)
                return;
            const state = saved;
            saved = null;
            restore(gl, state);
            passes++;
        },

        // A framebuffer around the color attachment of the framebuffer raylib is drawing into, plus a
        // depth/stencil renderbuffer for Skia. Returns [framebufferId, renderbufferHandle]. Only valid
        // between beginPass and endPass.
        createAttachmentFramebuffer(width, height) {
            const gl = context().GLctx;
            if (!saved || !saved.draw)
                throw new Error("raylib is not drawing into a framebuffer object.");
            const type = gl.getFramebufferAttachmentParameter(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_OBJECT_TYPE);
            const color = gl.getFramebufferAttachmentParameter(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_OBJECT_NAME);
            return createFramebuffer(gl, type === GL_TEXTURE ? 0 : 1, color, width, height);
        },

        // A framebuffer around raylib's texture textureId, plus a depth/stencil renderbuffer for Skia. Saves and
        // restores the bindings it touches, so it is safe between passes.
        createTextureFramebuffer(textureId, width, height) {
            const gl = context().GLctx;
            const texture = GL.textures[textureId];
            if (!texture)
                throw new Error("Unknown texture id " + textureId + ".");
            const draw = gl.getParameter(GL_DRAW_FRAMEBUFFER_BINDING);
            const read = gl.getParameter(GL_READ_FRAMEBUFFER_BINDING);
            const renderbuffer = gl.getParameter(GL_RENDERBUFFER_BINDING);
            try {
                return createFramebuffer(gl, 0, texture, width, height);
            } finally {
                gl.bindFramebuffer(GL_DRAW_FRAMEBUFFER, draw);
                gl.bindFramebuffer(GL_READ_FRAMEBUFFER, read);
                gl.bindRenderbuffer(GL_RENDERBUFFER, renderbuffer);
            }
        },

        // Frees a framebuffer made above, keeping the color attachment (raylib still owns it).
        deleteFramebuffer(id, renderbufferHandle) {
            const current = GL.currentContext;
            if (!current || !current.GLctx || current.GLctx.isContextLost())
                return;
            const gl = current.GLctx;
            const framebuffer = GL.framebuffers[id];
            if (framebuffer) {
                gl.deleteFramebuffer(framebuffer);
                GL.framebuffers[id] = null;
            }
            const renderbuffer = renderbuffers.get(renderbufferHandle);
            if (renderbuffer) {
                gl.deleteRenderbuffer(renderbuffer);
                renderbuffers.delete(renderbufferHandle);
            }
        },

        glErrorCount() { return errors; },
        glErrorLog() { return JSON.stringify(errorLog); },
    };

    function createFramebuffer(gl, kind, color, width, height) {
        const renderbuffer = gl.createRenderbuffer();
        gl.bindRenderbuffer(GL_RENDERBUFFER, renderbuffer);
        gl.renderbufferStorage(GL_RENDERBUFFER, GL_DEPTH24_STENCIL8, width, height);

        const framebuffer = gl.createFramebuffer();
        const id = GL.getNewId(GL.framebuffers);
        framebuffer.name = id;
        GL.framebuffers[id] = framebuffer;

        gl.bindFramebuffer(GL_FRAMEBUFFER, framebuffer);
        if (kind === 0)
            gl.framebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, color, 0);
        else
            gl.framebufferRenderbuffer(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_RENDERBUFFER, color);
        gl.framebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_RENDERBUFFER, renderbuffer);
        const status = gl.checkFramebufferStatus(GL_FRAMEBUFFER);
        if (status !== GL_FRAMEBUFFER_COMPLETE) {
            gl.deleteFramebuffer(framebuffer);
            GL.framebuffers[id] = null;
            gl.deleteRenderbuffer(renderbuffer);
            throw new Error("Skia framebuffer is incomplete (0x" + status.toString(16) + ").");
        }

        const handle = nextRenderbuffer++;
        renderbuffers.set(handle, renderbuffer);
        return [id, handle];
    }
})();
