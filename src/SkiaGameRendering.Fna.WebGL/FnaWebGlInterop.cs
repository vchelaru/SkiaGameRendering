using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace SkiaGameRendering;

// FNA3D draws on Emscripten's WebGL2 context for the page's canvas, and Skia drives the same context.
// FNA3D caches the GL state it has applied, so each Skia pass saves everything FNA3D can have cached
// and puts it back afterward. Done in JS because the browser's WebGL object has every query and setter;
// the library defines the functions with one eval, so there is no script file to deploy.
[SupportedOSPlatform("browser")]
internal static partial class FnaWebGlInterop
{
    private static bool _defined;

    internal static void EnsureDefined()
    {
        if (_defined)
            return;
        Eval(Script);
        _defined = true;
    }

    [JSImport("globalThis.eval")]
    private static partial void Eval(string code);

    // [status (0 ready, 1 lost), draw framebuffer name (0 = the canvas), samples, stencil bits,
    //  drawing buffer width, height]
    [JSImport("globalThis.skiaFnaWebGl.begin")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static partial int[] Begin(string canvasId);

    [JSImport("globalThis.skiaFnaWebGl.end")]
    internal static partial void End(string canvasId);

    // Name of the texture bound to TEXTURE_2D on the active unit, 0 if none.
    [JSImport("globalThis.skiaFnaWebGl.boundTexture")]
    internal static partial int BoundTexture(string canvasId);

    [JSImport("globalThis.skiaFnaWebGl.errorCount")]
    internal static partial int ErrorCount();

    private const string Script = """
(() => {
  const g = globalThis.skiaFnaWebGl = globalThis.skiaFnaWebGl || {};
  let saved = null, glErrors = 0, passes = 0;
  const errorLog = [];
  const glOf = id => {
    const canvas = document.getElementById(id);
    const gl = canvas && canvas.getContext("webgl2");
    if (!gl) throw new Error("No WebGL2 context on canvas '" + id + "'.");
    return gl;
  };
  const caps = gl => [gl.BLEND, gl.DEPTH_TEST, gl.STENCIL_TEST, gl.SCISSOR_TEST, gl.CULL_FACE, gl.DITHER,
    gl.POLYGON_OFFSET_FILL, gl.SAMPLE_ALPHA_TO_COVERAGE, gl.SAMPLE_COVERAGE, gl.RASTERIZER_DISCARD];
  const params = gl => [gl.BLEND_SRC_RGB, gl.BLEND_DST_RGB, gl.BLEND_SRC_ALPHA, gl.BLEND_DST_ALPHA,
    gl.BLEND_EQUATION_RGB, gl.BLEND_EQUATION_ALPHA, gl.BLEND_COLOR, gl.COLOR_WRITEMASK, gl.DEPTH_WRITEMASK,
    gl.DEPTH_FUNC, gl.DEPTH_RANGE, gl.COLOR_CLEAR_VALUE, gl.DEPTH_CLEAR_VALUE, gl.STENCIL_CLEAR_VALUE,
    gl.CULL_FACE_MODE, gl.FRONT_FACE, gl.POLYGON_OFFSET_FACTOR, gl.POLYGON_OFFSET_UNITS, gl.LINE_WIDTH,
    gl.SAMPLE_COVERAGE_VALUE, gl.SAMPLE_COVERAGE_INVERT, gl.VIEWPORT, gl.SCISSOR_BOX];
  const copy = v => (v && v.length !== undefined && typeof v !== "string") ? Array.from(v) : v;

  g.begin = id => {
    const gl = glOf(id);
    if (gl.isContextLost()) return [1, 0, 0, 0, 0, 0];
    passes++;
    const pre = gl.getError(); if (pre !== 0) errorLog.push([passes, 'pre', pre]);
    const s = saved = { gl, caps: {}, p: {}, attribs: [], units: [] };
    for (const c of caps(gl)) s.caps[c] = gl.isEnabled(c);
    for (const p of params(gl)) s.p[p] = copy(gl.getParameter(p));
    s.stencil = [gl.STENCIL_FUNC, gl.STENCIL_REF, gl.STENCIL_VALUE_MASK, gl.STENCIL_FAIL, gl.STENCIL_PASS_DEPTH_FAIL,
      gl.STENCIL_PASS_DEPTH_PASS, gl.STENCIL_WRITEMASK, gl.STENCIL_BACK_FUNC, gl.STENCIL_BACK_REF,
      gl.STENCIL_BACK_VALUE_MASK, gl.STENCIL_BACK_FAIL, gl.STENCIL_BACK_PASS_DEPTH_FAIL,
      gl.STENCIL_BACK_PASS_DEPTH_PASS, gl.STENCIL_BACK_WRITEMASK].map(p => gl.getParameter(p));
    s.pixel = [gl.UNPACK_ALIGNMENT, gl.UNPACK_ROW_LENGTH, gl.UNPACK_SKIP_ROWS, gl.UNPACK_SKIP_PIXELS,
      gl.PACK_ALIGNMENT, gl.PACK_ROW_LENGTH, gl.PACK_SKIP_ROWS, gl.PACK_SKIP_PIXELS].map(p => [p, gl.getParameter(p)]);
    s.program = gl.getParameter(gl.CURRENT_PROGRAM);
    s.vao = gl.getParameter(gl.VERTEX_ARRAY_BINDING);
    s.array = gl.getParameter(gl.ARRAY_BUFFER_BINDING);
    s.element = gl.getParameter(gl.ELEMENT_ARRAY_BUFFER_BINDING);
    s.unpack = gl.getParameter(gl.PIXEL_UNPACK_BUFFER_BINDING);
    s.pack = gl.getParameter(gl.PIXEL_PACK_BUFFER_BINDING);
    s.draw = gl.getParameter(gl.DRAW_FRAMEBUFFER_BINDING);
    s.read = gl.getParameter(gl.READ_FRAMEBUFFER_BINDING);
    s.renderbuffer = gl.getParameter(gl.RENDERBUFFER_BINDING);
    s.active = gl.getParameter(gl.ACTIVE_TEXTURE);
    const attribCount = gl.getParameter(gl.MAX_VERTEX_ATTRIBS);
    for (let i = 0; i < attribCount; i++) {
      const a = { enabled: gl.getVertexAttrib(i, gl.VERTEX_ATTRIB_ARRAY_ENABLED) };
      a.buffer = gl.getVertexAttrib(i, gl.VERTEX_ATTRIB_ARRAY_BUFFER_BINDING);
      a.size = gl.getVertexAttrib(i, gl.VERTEX_ATTRIB_ARRAY_SIZE);
      a.type = gl.getVertexAttrib(i, gl.VERTEX_ATTRIB_ARRAY_TYPE);
      a.normalized = gl.getVertexAttrib(i, gl.VERTEX_ATTRIB_ARRAY_NORMALIZED);
      a.stride = gl.getVertexAttrib(i, gl.VERTEX_ATTRIB_ARRAY_STRIDE);
      a.divisor = gl.getVertexAttrib(i, gl.VERTEX_ATTRIB_ARRAY_DIVISOR);
      a.integer = gl.getVertexAttrib(i, gl.VERTEX_ATTRIB_ARRAY_INTEGER);
      a.offset = gl.getVertexAttribOffset(i, gl.VERTEX_ATTRIB_ARRAY_POINTER);
      s.attribs.push(a);
    }
    const unitCount = Math.min(16, gl.getParameter(gl.MAX_COMBINED_TEXTURE_IMAGE_UNITS));
    for (let u = 0; u < unitCount; u++) {
      gl.activeTexture(gl.TEXTURE0 + u);
      s.units.push([gl.getParameter(gl.TEXTURE_BINDING_2D), gl.getParameter(gl.SAMPLER_BINDING)]);
    }
    gl.activeTexture(s.active);
    const post = gl.getError(); if (post !== 0) errorLog.push([passes, 'save', post]);
    return [0, s.draw ? s.draw.name | 0 : 0, gl.getParameter(gl.SAMPLES) | 0, gl.getParameter(gl.STENCIL_BITS) | 0,
      gl.drawingBufferWidth, gl.drawingBufferHeight];
  };

  g.end = id => {
    const s = saved; saved = null;
    if (!s || s.gl.isContextLost()) return;
    const gl = s.gl;
    gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, s.draw);
    gl.bindFramebuffer(gl.READ_FRAMEBUFFER, s.read);
    gl.bindRenderbuffer(gl.RENDERBUFFER, s.renderbuffer);
    gl.bindVertexArray(s.vao);
    gl.useProgram(s.program);
    s.units.forEach(([tex, sampler], u) => {
      gl.activeTexture(gl.TEXTURE0 + u);
      gl.bindTexture(gl.TEXTURE_2D, tex);
      gl.bindSampler(u, sampler);
    });
    gl.activeTexture(s.active);
    s.attribs.forEach((a, i) => {
      if (a.buffer) {
        gl.bindBuffer(gl.ARRAY_BUFFER, a.buffer);
        if (a.integer) gl.vertexAttribIPointer(i, a.size, a.type, a.stride, a.offset);
        else gl.vertexAttribPointer(i, a.size, a.type, a.normalized, a.stride, a.offset);
      }
      gl.vertexAttribDivisor(i, a.divisor);
      if (a.enabled) gl.enableVertexAttribArray(i); else gl.disableVertexAttribArray(i);
    });
    gl.bindBuffer(gl.ARRAY_BUFFER, s.array);
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, s.element);
    gl.bindBuffer(gl.PIXEL_UNPACK_BUFFER, s.unpack);
    gl.bindBuffer(gl.PIXEL_PACK_BUFFER, s.pack);
    for (const c of caps(gl)) { if (s.caps[c]) gl.enable(c); else gl.disable(c); }
    const p = s.p;
    gl.blendFuncSeparate(p[gl.BLEND_SRC_RGB], p[gl.BLEND_DST_RGB], p[gl.BLEND_SRC_ALPHA], p[gl.BLEND_DST_ALPHA]);
    gl.blendEquationSeparate(p[gl.BLEND_EQUATION_RGB], p[gl.BLEND_EQUATION_ALPHA]);
    gl.blendColor(...p[gl.BLEND_COLOR]);
    gl.colorMask(...p[gl.COLOR_WRITEMASK]);
    gl.depthMask(p[gl.DEPTH_WRITEMASK]);
    gl.depthFunc(p[gl.DEPTH_FUNC]);
    gl.depthRange(...p[gl.DEPTH_RANGE]);
    gl.clearColor(...p[gl.COLOR_CLEAR_VALUE]);
    gl.clearDepth(p[gl.DEPTH_CLEAR_VALUE]);
    gl.clearStencil(p[gl.STENCIL_CLEAR_VALUE]);
    gl.cullFace(p[gl.CULL_FACE_MODE]);
    gl.frontFace(p[gl.FRONT_FACE]);
    gl.polygonOffset(p[gl.POLYGON_OFFSET_FACTOR], p[gl.POLYGON_OFFSET_UNITS]);
    gl.lineWidth(p[gl.LINE_WIDTH]);
    gl.sampleCoverage(p[gl.SAMPLE_COVERAGE_VALUE], p[gl.SAMPLE_COVERAGE_INVERT]);
    gl.viewport(...p[gl.VIEWPORT]);
    gl.scissor(...p[gl.SCISSOR_BOX]);
    const st = s.stencil;
    gl.stencilFuncSeparate(gl.FRONT, st[0], st[1], st[2]);
    gl.stencilOpSeparate(gl.FRONT, st[3], st[4], st[5]);
    gl.stencilMaskSeparate(gl.FRONT, st[6]);
    gl.stencilFuncSeparate(gl.BACK, st[7], st[8], st[9]);
    gl.stencilOpSeparate(gl.BACK, st[10], st[11], st[12]);
    gl.stencilMaskSeparate(gl.BACK, st[13]);
    for (const [pname, value] of s.pixel) gl.pixelStorei(pname, value);
    // Errors Skia left queued would be blamed on FNA3D's next call.
    for (let i = 0, e; i < 8 && (e = gl.getError()) !== 0; i++) { glErrors++; errorLog.push([passes, 'end', e]); }
  };

  g.boundTexture = id => { const t = glOf(id).getParameter(0x8069); return t ? t.name | 0 : 0; };
  g.errorCount = () => glErrors;
  g.errorLog = () => JSON.stringify(errorLog);
})();
""";
}
