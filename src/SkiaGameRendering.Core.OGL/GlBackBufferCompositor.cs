using System.Runtime.InteropServices;

namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// A back-buffer-sized GL texture that Skia draws into on its own context, and the full-screen draw that
    /// blends it (premultiplied) over the engine's back buffer on the engine's context. The texture is plain
    /// GL, so the engine's own texture size limits (XNA's Reach profile) do not apply. The draw puts back
    /// every piece of GL state it changes, so an engine that caches GL state never sees a change. Works on
    /// GLES 2 and 3: on GLES 3 a vertex array of its own holds the quad's attribute, on GLES 2 the
    /// attribute it borrows is put back.
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
        [UnmanagedFunctionPointer(Convention)] private delegate void BufferDataDelegate(int target, IntPtr size, float[] data, int usage);
        [UnmanagedFunctionPointer(Convention)] private delegate void VertexAttribPointerDelegate(int index, int size, int type,
            byte normalized, int stride, IntPtr pointer);
        [UnmanagedFunctionPointer(Convention)] private delegate void GetVertexAttribivDelegate(int index, int pname, out int value);
        [UnmanagedFunctionPointer(Convention)] private delegate void GetVertexAttribPointervDelegate(int index, int pname, out IntPtr pointer);
        [UnmanagedFunctionPointer(Convention)] private delegate void BindAttribLocationDelegate(int program, int index, string name);

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
        private const int ArrayBuffer = 0x8892, ArrayBufferBinding = 0x8894, StaticDraw = 0x88E4, Float = 0x1406;
        private const int AttribEnabled = 0x8622, AttribSize = 0x8623, AttribStride = 0x8624, AttribType = 0x8625,
            AttribNormalized = 0x886A, AttribBufferBinding = 0x889F, AttribPointer = 0x8645, Version = 0x1F02;

        // A triangle covering the viewport: (0,0), (2,0), (0,2) in texture space. GLSL ES 1.00, so GLES 2 and 3 both take it.
        private const string VertexSource =
            "attribute vec2 position;\n" +
            "varying vec2 uv;\n" +
            "void main() { uv = position; gl_Position = vec4(position * 2.0 - 1.0, 0.0, 1.0); }\n";
        private const string FragmentSource =
            "precision mediump float;\n" +
            "varying vec2 uv; uniform sampler2D image;\n" +
            "void main() { gl_FragColor = texture2D(image, uv); }\n";
        private static readonly float[] Triangle = { 0, 0, 2, 0, 0, 2 };

        private readonly GlFunctions _gl;
        private readonly bool _gles3;
        private readonly GenDelegate _genTextures, _genBuffers;
        private readonly GenDelegate? _genVertexArrays;
        private readonly DeleteDelegate _deleteTextures, _deleteBuffers;
        private readonly DeleteDelegate? _deleteVertexArrays;
        private readonly TexImage2DDelegate _texImage2D;
        private readonly TexParameteriDelegate _texParameteri;
        private readonly CreateShaderDelegate _createShader;
        private readonly ShaderSourceDelegate _shaderSource;
        private readonly IntDelegate _compileShader, _linkProgram, _useProgram, _deleteShader, _deleteProgram,
            _activeTexture, _enable, _disable, _enableVertexAttribArray, _disableVertexAttribArray;
        private readonly IntDelegate? _bindVertexArray;
        private readonly CreateProgramDelegate _createProgram;
        private readonly IntIntDelegate _attachShader, _uniform1i, _blendEquationSeparate, _bindBuffer;
        private readonly IntIntDelegate? _bindSampler;
        private readonly BufferDataDelegate _bufferData;
        private readonly VertexAttribPointerDelegate _vertexAttribPointer;
        private readonly GetVertexAttribivDelegate _getVertexAttribiv;
        private readonly GetVertexAttribPointervDelegate _getVertexAttribPointerv;
        private readonly BindAttribLocationDelegate _bindAttribLocation;
        private readonly GetivDelegate _getShaderiv, _getProgramiv;
        private readonly GetUniformLocationDelegate _getUniformLocation;
        private readonly DrawArraysDelegate _drawArrays;
        private readonly IsEnabledDelegate _isEnabled;
        private readonly FourIntDelegate _viewport, _blendFuncSeparate;
        private readonly ColorMaskDelegate _colorMask;
        private readonly GetBooleanvDelegate _getBooleanv;
        private readonly GetIntegervDelegate _getIntegerv;

        private int _program, _vertexArray, _buffer;

        public GlBackBufferCompositor(GlFunctions gl)
        {
            _gl = gl;
            var l = gl.Loader;
            // Both contexts share one client version, and eglGetProcAddress can hand out GLES 3 entry points
            // on a GLES 2 context, so the version string decides.
            _gles3 = !(gl.GetString(Version) ?? "").StartsWith("OpenGL ES 2", StringComparison.Ordinal);
            _genTextures = l.Load<GenDelegate>("glGenTextures");
            _genBuffers = l.Load<GenDelegate>("glGenBuffers");
            _deleteTextures = l.Load<DeleteDelegate>("glDeleteTextures");
            _deleteBuffers = l.Load<DeleteDelegate>("glDeleteBuffers");
            _bindBuffer = l.Load<IntIntDelegate>("glBindBuffer");
            _bufferData = l.Load<BufferDataDelegate>("glBufferData");
            _vertexAttribPointer = l.Load<VertexAttribPointerDelegate>("glVertexAttribPointer");
            _enableVertexAttribArray = l.Load<IntDelegate>("glEnableVertexAttribArray");
            _disableVertexAttribArray = l.Load<IntDelegate>("glDisableVertexAttribArray");
            _getVertexAttribiv = l.Load<GetVertexAttribivDelegate>("glGetVertexAttribiv");
            _getVertexAttribPointerv = l.Load<GetVertexAttribPointervDelegate>("glGetVertexAttribPointerv");
            _bindAttribLocation = l.Load<BindAttribLocationDelegate>("glBindAttribLocation");
            if (_gles3)
            {
                _genVertexArrays = l.Load<GenDelegate>("glGenVertexArrays");
                _deleteVertexArrays = l.Load<DeleteDelegate>("glDeleteVertexArrays");
                _bindVertexArray = l.Load<IntDelegate>("glBindVertexArray");
                _bindSampler = l.Load<IntIntDelegate>("glBindSampler");
            }
            _texImage2D = l.Load<TexImage2DDelegate>("glTexImage2D");
            _texParameteri = l.Load<TexParameteriDelegate>("glTexParameteri");
            _createShader = l.Load<CreateShaderDelegate>("glCreateShader");
            _shaderSource = l.Load<ShaderSourceDelegate>("glShaderSource");
            _compileShader = l.Load<IntDelegate>("glCompileShader");
            _linkProgram = l.Load<IntDelegate>("glLinkProgram");
            _useProgram = l.Load<IntDelegate>("glUseProgram");
            _deleteShader = l.Load<IntDelegate>("glDeleteShader");
            _deleteProgram = l.Load<IntDelegate>("glDeleteProgram");
            _activeTexture = l.Load<IntDelegate>("glActiveTexture");
            _enable = l.Load<IntDelegate>("glEnable");
            _disable = l.Load<IntDelegate>("glDisable");
            _createProgram = l.Load<CreateProgramDelegate>("glCreateProgram");
            _attachShader = l.Load<IntIntDelegate>("glAttachShader");
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
            _texImage2D(Texture2D, 0, _gles3 ? Rgba8 : Rgba, width, height, 0, Rgba, UnsignedByte, IntPtr.Zero);
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
            var vertexArray = _gles3 ? GetInteger(VertexArrayBinding) : 0;
            var activeTexture = GetInteger(ActiveTexture);
            _activeTexture(Texture0);
            var textureBinding = GetInteger(TextureBinding2D);
            var sampler = _gles3 ? GetInteger(SamplerBinding) : 0;
            var attribute = _gles3 ? default : SaveAttribute();
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
            if (_gles3)
                _bindVertexArray!(_vertexArray);
            else
                PointAttributeAtTriangle();
            _gl.BindTexture(Texture2D, texture);
            _bindSampler?.Invoke(0, 0);
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
            _bindSampler?.Invoke(0, sampler);
            _gl.BindTexture(Texture2D, textureBinding);
            _activeTexture(activeTexture);
            if (_gles3)
                _bindVertexArray!(vertexArray);
            else
                RestoreAttribute(attribute);
            _useProgram(program);
        }

        /// <summary>Frees the program and vertex array. Call on the engine's context, where the vertex array lives.</summary>
        public void DisposeEngineObjects()
        {
            if (_program == 0)
                return;
            _deleteProgram(_program);
            _deleteBuffers(1, ref _buffer);
            if (_gles3)
                _deleteVertexArrays!(1, ref _vertexArray);
            _program = 0;
            _buffer = 0;
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
            _bindAttribLocation(program, 0, "position");
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

            var arrayBuffer = GetInteger(ArrayBufferBinding);
            _genBuffers(1, out _buffer);
            _bindBuffer(ArrayBuffer, _buffer);
            _bufferData(ArrayBuffer, (IntPtr)(Triangle.Length * sizeof(float)), Triangle, StaticDraw);
            if (_gles3)
            {
                var vertexArray = GetInteger(VertexArrayBinding);
                _genVertexArrays!(1, out _vertexArray);
                _bindVertexArray!(_vertexArray);
                PointAttributeAtTriangle();
                _bindVertexArray(vertexArray);
            }
            _bindBuffer(ArrayBuffer, arrayBuffer);
            _program = program;
        }

        // GLES 2 has no vertex arrays, so the composite borrows attribute 0 from whatever the engine left there.
        private readonly struct Attribute
        {
            internal readonly int ArrayBuffer, Enabled, Size, Stride, Type, Normalized, Buffer;
            internal readonly IntPtr Pointer;

            internal Attribute(int arrayBuffer, int enabled, int size, int stride, int type, int normalized, int buffer, IntPtr pointer)
            {
                ArrayBuffer = arrayBuffer; Enabled = enabled; Size = size; Stride = stride; Type = type;
                Normalized = normalized; Buffer = buffer; Pointer = pointer;
            }
        }

        private Attribute SaveAttribute()
        {
            _getVertexAttribiv(0, AttribEnabled, out var enabled);
            _getVertexAttribiv(0, AttribSize, out var size);
            _getVertexAttribiv(0, AttribStride, out var stride);
            _getVertexAttribiv(0, AttribType, out var type);
            _getVertexAttribiv(0, AttribNormalized, out var normalized);
            _getVertexAttribiv(0, AttribBufferBinding, out var buffer);
            _getVertexAttribPointerv(0, AttribPointer, out var pointer);
            return new Attribute(GetInteger(ArrayBufferBinding), enabled, size, stride, type, normalized, buffer, pointer);
        }

        private void RestoreAttribute(Attribute attribute)
        {
            _bindBuffer(ArrayBuffer, attribute.Buffer);
            _vertexAttribPointer(0, attribute.Size, attribute.Type, (byte)attribute.Normalized, attribute.Stride, attribute.Pointer);
            if (attribute.Enabled != 0)
                _enableVertexAttribArray(0);
            else
                _disableVertexAttribArray(0);
            _bindBuffer(ArrayBuffer, attribute.ArrayBuffer);
        }

        /// <summary>Leaves the array buffer binding on the triangle's buffer.</summary>
        private void PointAttributeAtTriangle()
        {
            _bindBuffer(ArrayBuffer, _buffer);
            _vertexAttribPointer(0, 2, Float, 0, 0, IntPtr.Zero);
            _enableVertexAttribArray(0);
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
