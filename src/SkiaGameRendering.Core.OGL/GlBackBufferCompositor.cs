using System.Runtime.InteropServices;

namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// A back-buffer-sized GL texture that Skia draws into on its own context, and the full-screen draw that
    /// blends it (premultiplied) over the engine's back buffer on the engine's context. The texture is plain
    /// GL, so the engine's own texture size limits (XNA's Reach profile) do not apply. The draw puts back
    /// every piece of GL state it changes, so an engine that caches GL state never sees a change. Needs
    /// GLES 3 on both contexts.
    /// </summary>
    public sealed class GlBackBufferCompositor
    {
        private const CallingConvention Convention = CallingConvention.Winapi;

        [UnmanagedFunctionPointer(Convention)] private delegate void GenDelegate(int count, out int name);
        [UnmanagedFunctionPointer(Convention)] private delegate void DeleteDelegate(int count, ref int name);
        [UnmanagedFunctionPointer(Convention)] private delegate void TexImage2DDelegate(int target, int level, int internalFormat,
            int width, int height, int border, int format, int type, IntPtr pixels);
        [UnmanagedFunctionPointer(Convention)] private delegate void TexParameteriDelegate(int target, int pname, int value);
        [UnmanagedFunctionPointer(Convention)] private delegate int CreateShaderDelegate(int type);
        [UnmanagedFunctionPointer(Convention)] private delegate void ShaderSourceDelegate(int shader, int count, string[] source, IntPtr length);
        [UnmanagedFunctionPointer(Convention)] private delegate void IntDelegate(int value);
        [UnmanagedFunctionPointer(Convention)] private delegate int CreateProgramDelegate();
        [UnmanagedFunctionPointer(Convention)] private delegate void IntIntDelegate(int a, int b);
        [UnmanagedFunctionPointer(Convention)] private delegate void GetivDelegate(int name, int pname, out int value);
        [UnmanagedFunctionPointer(Convention)] private delegate int GetUniformLocationDelegate(int program, string name);
        [UnmanagedFunctionPointer(Convention)] private delegate void DrawArraysDelegate(int mode, int first, int count);
        [UnmanagedFunctionPointer(Convention)] private delegate byte IsEnabledDelegate(int cap);
        [UnmanagedFunctionPointer(Convention)] private delegate void FourIntDelegate(int a, int b, int c, int d);
        [UnmanagedFunctionPointer(Convention)] private delegate void ColorMaskDelegate(byte r, byte g, byte b, byte a);
        [UnmanagedFunctionPointer(Convention)] private unsafe delegate void GetBooleanvDelegate(int pname, byte* data);
        [UnmanagedFunctionPointer(Convention)] private unsafe delegate void GetIntegervDelegate(int pname, int* data);

        private const int Texture2D = 0x0DE1, Rgba8 = 0x8058, Rgba = 0x1908, UnsignedByte = 0x1401;
        private const int TextureMinFilter = 0x2801, TextureMagFilter = 0x2800, Nearest = 0x2600;
        private const int TextureWrapS = 0x2802, TextureWrapT = 0x2803, ClampToEdge = 0x812F;
        private const int VertexShader = 0x8B31, FragmentShader = 0x8B30, CompileStatus = 0x8B81, LinkStatus = 0x8B82;
        private const int Triangles = 0x0004, Texture0 = 0x84C0, One = 1, OneMinusSrcAlpha = 0x0303, FuncAdd = 0x8006;
        private const int Blend = 0x0BE2, DepthTest = 0x0B71, StencilTest = 0x0B90, ScissorTest = 0x0C11, CullFace = 0x0B44;
        private const int CurrentProgram = 0x8B8D, VertexArrayBinding = 0x85B5, ActiveTexture = 0x84E0;
        private const int TextureBinding2D = 0x8069, SamplerBinding = 0x8919, ViewportParam = 0x0BA2, ColorWritemask = 0x0C23;
        private const int BlendSrcRgb = 0x80C9, BlendDstRgb = 0x80C8, BlendSrcAlpha = 0x80CB, BlendDstAlpha = 0x80CA;
        private const int BlendEquationRgb = 0x8009, BlendEquationAlpha = 0x883D;

        // A triangle covering the viewport, made from gl_VertexID alone, so no vertex buffer or attribute is touched.
        private const string VertexSource = "#version 300 es\n" +
            "out vec2 uv;\n" +
            "void main() { vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2)); uv = p; gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0); }\n";
        private const string FragmentSource = "#version 300 es\n" +
            "precision mediump float;\n" +
            "in vec2 uv; uniform sampler2D image; out vec4 color;\n" +
            "void main() { color = texture(image, uv); }\n";

        private readonly GlFunctions _gl;
        private readonly GenDelegate _genTextures, _genVertexArrays;
        private readonly DeleteDelegate _deleteTextures, _deleteVertexArrays;
        private readonly TexImage2DDelegate _texImage2D;
        private readonly TexParameteriDelegate _texParameteri;
        private readonly CreateShaderDelegate _createShader;
        private readonly ShaderSourceDelegate _shaderSource;
        private readonly IntDelegate _compileShader, _linkProgram, _useProgram, _deleteShader, _deleteProgram,
            _bindVertexArray, _activeTexture, _enable, _disable;
        private readonly CreateProgramDelegate _createProgram;
        private readonly IntIntDelegate _attachShader, _bindSampler, _uniform1i, _blendEquationSeparate;
        private readonly GetivDelegate _getShaderiv, _getProgramiv;
        private readonly GetUniformLocationDelegate _getUniformLocation;
        private readonly DrawArraysDelegate _drawArrays;
        private readonly IsEnabledDelegate _isEnabled;
        private readonly FourIntDelegate _viewport, _blendFuncSeparate;
        private readonly ColorMaskDelegate _colorMask;
        private readonly GetBooleanvDelegate _getBooleanv;
        private readonly GetIntegervDelegate _getIntegerv;

        private int _program, _vertexArray;

        public GlBackBufferCompositor(GlFunctions gl)
        {
            _gl = gl;
            var l = gl.Loader;
            _genTextures = l.Load<GenDelegate>("glGenTextures");
            _genVertexArrays = l.Load<GenDelegate>("glGenVertexArrays");
            _deleteTextures = l.Load<DeleteDelegate>("glDeleteTextures");
            _deleteVertexArrays = l.Load<DeleteDelegate>("glDeleteVertexArrays");
            _texImage2D = l.Load<TexImage2DDelegate>("glTexImage2D");
            _texParameteri = l.Load<TexParameteriDelegate>("glTexParameteri");
            _createShader = l.Load<CreateShaderDelegate>("glCreateShader");
            _shaderSource = l.Load<ShaderSourceDelegate>("glShaderSource");
            _compileShader = l.Load<IntDelegate>("glCompileShader");
            _linkProgram = l.Load<IntDelegate>("glLinkProgram");
            _useProgram = l.Load<IntDelegate>("glUseProgram");
            _deleteShader = l.Load<IntDelegate>("glDeleteShader");
            _deleteProgram = l.Load<IntDelegate>("glDeleteProgram");
            _bindVertexArray = l.Load<IntDelegate>("glBindVertexArray");
            _activeTexture = l.Load<IntDelegate>("glActiveTexture");
            _enable = l.Load<IntDelegate>("glEnable");
            _disable = l.Load<IntDelegate>("glDisable");
            _createProgram = l.Load<CreateProgramDelegate>("glCreateProgram");
            _attachShader = l.Load<IntIntDelegate>("glAttachShader");
            _bindSampler = l.Load<IntIntDelegate>("glBindSampler");
            _uniform1i = l.Load<IntIntDelegate>("glUniform1i");
            _blendEquationSeparate = l.Load<IntIntDelegate>("glBlendEquationSeparate");
            _getShaderiv = l.Load<GetivDelegate>("glGetShaderiv");
            _getProgramiv = l.Load<GetivDelegate>("glGetProgramiv");
            _getUniformLocation = l.Load<GetUniformLocationDelegate>("glGetUniformLocation");
            _drawArrays = l.Load<DrawArraysDelegate>("glDrawArrays");
            _isEnabled = l.Load<IsEnabledDelegate>("glIsEnabled");
            _viewport = l.Load<FourIntDelegate>("glViewport");
            _blendFuncSeparate = l.Load<FourIntDelegate>("glBlendFuncSeparate");
            _colorMask = l.Load<ColorMaskDelegate>("glColorMask");
            _getBooleanv = l.Load<GetBooleanvDelegate>("glGetBooleanv");
            _getIntegerv = l.Load<GetIntegervDelegate>("glGetIntegerv");
        }

        /// <summary>Makes an RGBA8 texture with no mipmaps for Skia to draw into. Call on Skia's context.</summary>
        public int CreateTexture(int width, int height)
        {
            var previous = GetInteger(TextureBinding2D);
            _genTextures(1, out var texture);
            _gl.BindTexture(Texture2D, texture);
            _texImage2D(Texture2D, 0, Rgba8, width, height, 0, Rgba, UnsignedByte, IntPtr.Zero);
            _texParameteri(Texture2D, TextureMinFilter, Nearest);
            _texParameteri(Texture2D, TextureMagFilter, Nearest);
            _texParameteri(Texture2D, TextureWrapS, ClampToEdge);
            _texParameteri(Texture2D, TextureWrapT, ClampToEdge);
            _gl.BindTexture(Texture2D, previous);
            return texture;
        }

        /// <summary>Call on Skia's context.</summary>
        public void DeleteTexture(int texture) => _deleteTextures(1, ref texture);

        /// <summary>
        /// Blends <paramref name="texture"/> (premultiplied, texel row 0 at the bottom) over the whole bound
        /// framebuffer. Call on the engine's context; every state it sets is put back.
        /// </summary>
        public unsafe void Composite(int texture, int width, int height)
        {
            if (_program == 0)
                CreateProgram();

            var program = GetInteger(CurrentProgram);
            var vertexArray = GetInteger(VertexArrayBinding);
            var activeTexture = GetInteger(ActiveTexture);
            _activeTexture(Texture0);
            var textureBinding = GetInteger(TextureBinding2D);
            var sampler = GetInteger(SamplerBinding);
            var srcRgb = GetInteger(BlendSrcRgb);
            var dstRgb = GetInteger(BlendDstRgb);
            var srcAlpha = GetInteger(BlendSrcAlpha);
            var dstAlpha = GetInteger(BlendDstAlpha);
            var equationRgb = GetInteger(BlendEquationRgb);
            var equationAlpha = GetInteger(BlendEquationAlpha);
            var viewport = stackalloc int[4];
            _getIntegerv(ViewportParam, viewport);
            var colorMask = stackalloc byte[4];
            _getBooleanv(ColorWritemask, colorMask);
            var blend = _isEnabled(Blend);
            var depth = _isEnabled(DepthTest);
            var stencil = _isEnabled(StencilTest);
            var scissor = _isEnabled(ScissorTest);
            var cull = _isEnabled(CullFace);

            _useProgram(_program);
            _bindVertexArray(_vertexArray);
            _gl.BindTexture(Texture2D, texture);
            _bindSampler(0, 0);
            _viewport(0, 0, width, height);
            _colorMask(1, 1, 1, 1);
            _enable(Blend);
            _blendFuncSeparate(One, OneMinusSrcAlpha, One, OneMinusSrcAlpha);
            _blendEquationSeparate(FuncAdd, FuncAdd);
            _disable(DepthTest);
            _disable(StencilTest);
            _disable(ScissorTest);
            _disable(CullFace);

            _drawArrays(Triangles, 0, 3);

            Set(Blend, blend);
            Set(DepthTest, depth);
            Set(StencilTest, stencil);
            Set(ScissorTest, scissor);
            Set(CullFace, cull);
            _blendEquationSeparate(equationRgb, equationAlpha);
            _blendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha);
            _colorMask(colorMask[0], colorMask[1], colorMask[2], colorMask[3]);
            _viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
            _bindSampler(0, sampler);
            _gl.BindTexture(Texture2D, textureBinding);
            _activeTexture(activeTexture);
            _bindVertexArray(vertexArray);
            _useProgram(program);
        }

        /// <summary>Frees the program and vertex array. Call on the engine's context, where the vertex array lives.</summary>
        public void DisposeEngineObjects()
        {
            if (_program == 0)
                return;
            _deleteProgram(_program);
            _deleteVertexArrays(1, ref _vertexArray);
            _program = 0;
            _vertexArray = 0;
        }

        private unsafe int GetInteger(int pname)
        {
            int value;
            _getIntegerv(pname, &value);
            return value;
        }

        private void Set(int cap, byte enabled)
        {
            if (enabled != 0)
                _enable(cap);
            else
                _disable(cap);
        }

        private void CreateProgram()
        {
            var vertex = Compile(VertexShader, VertexSource);
            var fragment = Compile(FragmentShader, FragmentSource);
            var program = _createProgram();
            _attachShader(program, vertex);
            _attachShader(program, fragment);
            _linkProgram(program);
            _deleteShader(vertex);
            _deleteShader(fragment);
            _getProgramiv(program, LinkStatus, out var linked);
            if (linked == 0)
            {
                _deleteProgram(program);
                throw new InvalidOperationException("The back buffer composite shader failed to link.");
            }

            var previous = GetInteger(CurrentProgram);
            _useProgram(program);
            _uniform1i(_getUniformLocation(program, "image"), 0);
            _useProgram(previous);

            _genVertexArrays(1, out _vertexArray);
            _program = program;
        }

        private int Compile(int type, string source)
        {
            var shader = _createShader(type);
            _shaderSource(shader, 1, new[] { source }, IntPtr.Zero);
            _compileShader(shader);
            _getShaderiv(shader, CompileStatus, out var compiled);
            if (compiled == 0)
            {
                _deleteShader(shader);
                throw new InvalidOperationException("The back buffer composite shader failed to compile.");
            }
            return shader;
        }
    }
}
