namespace SkiaGameRendering
{
    /// <summary>
    /// The app's GL state that Skia changes when it draws on the app's own context: saved at the start of a pass and
    /// put back at its end, so app code after <c>End</c> (a <c>glBlitFramebuffer</c>, a <c>glClear</c>, its own draws)
    /// does not inherit Skia's scissor, blend, stencil, program or bindings. Desktop GL 3.3 core or compatibility.
    /// Not restored: vertex attributes of the default vertex array object (core profile has none to draw with) and
    /// texture targets other than <c>GL_TEXTURE_2D</c>.
    /// </summary>
    internal sealed unsafe class GlStateSnapshot
    {
        // Skia binds textures from unit 0 up, one per sampler in a draw.
        const int TextureUnits = 16;

        static readonly int[] Capabilities =
        {
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
            0x809D, // MULTISAMPLE
            0x8DB9, // FRAMEBUFFER_SRGB
            0x8F9D, // PRIMITIVE_RESTART
            0x8642, // PROGRAM_POINT_SIZE
            0x0BF2, // COLOR_LOGIC_OP
        };

        static readonly int[] PixelStore =
        {
            0x0CF5, 0x0CF2, 0x0CF3, 0x0CF4, // UNPACK_ALIGNMENT, ROW_LENGTH, SKIP_ROWS, SKIP_PIXELS
            0x0D05, 0x0D02, 0x0D03, 0x0D04, // PACK_ALIGNMENT, ROW_LENGTH, SKIP_ROWS, SKIP_PIXELS
        };

        readonly delegate* unmanaged<int, int*, void> _getIntegerv;
        readonly delegate* unmanaged<int, float*, void> _getFloatv;
        readonly delegate* unmanaged<int, byte*, void> _getBooleanv;
        readonly delegate* unmanaged<int, byte> _isEnabled;
        readonly delegate* unmanaged<int, void> _enable;
        readonly delegate* unmanaged<int, void> _disable;
        readonly delegate* unmanaged<int, int, void> _bindFramebuffer;
        readonly delegate* unmanaged<int, int, void> _bindRenderbuffer;
        readonly delegate* unmanaged<int, int, int, int, void> _viewport;
        readonly delegate* unmanaged<int, int, int, int, void> _scissor;
        readonly delegate* unmanaged<float, float, float, float, void> _blendColor;
        readonly delegate* unmanaged<int, int, int, int, void> _blendFuncSeparate;
        readonly delegate* unmanaged<int, int, void> _blendEquationSeparate;
        readonly delegate* unmanaged<byte, void> _depthMask;
        readonly delegate* unmanaged<int, void> _depthFunc;
        readonly delegate* unmanaged<int, void> _cullFace;
        readonly delegate* unmanaged<int, void> _frontFace;
        readonly delegate* unmanaged<int, int, void> _polygonMode;
        readonly delegate* unmanaged<int, int, int, int, void> _stencilFuncSeparate;
        readonly delegate* unmanaged<int, int, int, int, void> _stencilOpSeparate;
        readonly delegate* unmanaged<int, int, void> _stencilMaskSeparate;
        readonly delegate* unmanaged<byte, byte, byte, byte, void> _colorMask;
        readonly delegate* unmanaged<float, float, float, float, void> _clearColor;
        readonly delegate* unmanaged<int, void> _clearStencil;
        readonly delegate* unmanaged<int, void> _useProgram;
        readonly delegate* unmanaged<int, void> _bindVertexArray;
        readonly delegate* unmanaged<int, int, void> _bindBuffer;
        readonly delegate* unmanaged<int, void> _activeTexture;
        readonly delegate* unmanaged<int, int, void> _bindTexture;
        readonly delegate* unmanaged<int, int, void> _bindSampler;
        readonly delegate* unmanaged<int, int, void> _pixelStorei;

        // Saved values.
        int _drawFramebuffer, _readFramebuffer, _renderbuffer;
        readonly int[] _viewportBox = new int[4], _scissorBox = new int[4];
        readonly bool[] _enabled = new bool[Capabilities.Length];
        readonly float[] _blendColorValue = new float[4], _clearColorValue = new float[4];
        readonly int[] _blendFunc = new int[4], _blendEquation = new int[2];
        bool _depthMaskValue;
        int _depthFuncValue, _cullFaceValue, _frontFaceValue, _polygonModeValue, _clearStencilValue;
        // Front then back: func, ref, value mask, fail, pass-depth-fail, pass-depth-pass, write mask.
        readonly int[] _stencil = new int[14];
        readonly bool[] _colorMaskValue = new bool[4];
        int _program, _vertexArray, _arrayBuffer, _pixelPackBuffer, _pixelUnpackBuffer, _activeTextureValue;
        readonly int[] _textures = new int[TextureUnits], _samplers = new int[TextureUnits];
        readonly int[] _pixelStore = new int[PixelStore.Length];

        public GlStateSnapshot(Func<string, IntPtr> getProcAddress)
        {
            IntPtr P(string name)
            {
                var address = getProcAddress(name);
                return address != IntPtr.Zero
                    ? address
                    : throw new EntryPointNotFoundException($"The GL proc-address function returned null for '{name}'.");
            }
            _getIntegerv = (delegate* unmanaged<int, int*, void>)P("glGetIntegerv");
            _getFloatv = (delegate* unmanaged<int, float*, void>)P("glGetFloatv");
            _getBooleanv = (delegate* unmanaged<int, byte*, void>)P("glGetBooleanv");
            _isEnabled = (delegate* unmanaged<int, byte>)P("glIsEnabled");
            _enable = (delegate* unmanaged<int, void>)P("glEnable");
            _disable = (delegate* unmanaged<int, void>)P("glDisable");
            _bindFramebuffer = (delegate* unmanaged<int, int, void>)P("glBindFramebuffer");
            _bindRenderbuffer = (delegate* unmanaged<int, int, void>)P("glBindRenderbuffer");
            _viewport = (delegate* unmanaged<int, int, int, int, void>)P("glViewport");
            _scissor = (delegate* unmanaged<int, int, int, int, void>)P("glScissor");
            _blendColor = (delegate* unmanaged<float, float, float, float, void>)P("glBlendColor");
            _blendFuncSeparate = (delegate* unmanaged<int, int, int, int, void>)P("glBlendFuncSeparate");
            _blendEquationSeparate = (delegate* unmanaged<int, int, void>)P("glBlendEquationSeparate");
            _depthMask = (delegate* unmanaged<byte, void>)P("glDepthMask");
            _depthFunc = (delegate* unmanaged<int, void>)P("glDepthFunc");
            _cullFace = (delegate* unmanaged<int, void>)P("glCullFace");
            _frontFace = (delegate* unmanaged<int, void>)P("glFrontFace");
            _polygonMode = (delegate* unmanaged<int, int, void>)P("glPolygonMode");
            _stencilFuncSeparate = (delegate* unmanaged<int, int, int, int, void>)P("glStencilFuncSeparate");
            _stencilOpSeparate = (delegate* unmanaged<int, int, int, int, void>)P("glStencilOpSeparate");
            _stencilMaskSeparate = (delegate* unmanaged<int, int, void>)P("glStencilMaskSeparate");
            _colorMask = (delegate* unmanaged<byte, byte, byte, byte, void>)P("glColorMask");
            _clearColor = (delegate* unmanaged<float, float, float, float, void>)P("glClearColor");
            _clearStencil = (delegate* unmanaged<int, void>)P("glClearStencil");
            _useProgram = (delegate* unmanaged<int, void>)P("glUseProgram");
            _bindVertexArray = (delegate* unmanaged<int, void>)P("glBindVertexArray");
            _bindBuffer = (delegate* unmanaged<int, int, void>)P("glBindBuffer");
            _activeTexture = (delegate* unmanaged<int, void>)P("glActiveTexture");
            _bindTexture = (delegate* unmanaged<int, int, void>)P("glBindTexture");
            _bindSampler = (delegate* unmanaged<int, int, void>)P("glBindSampler");
            _pixelStorei = (delegate* unmanaged<int, int, void>)P("glPixelStorei");
        }

        int Int(int name)
        {
            int value;
            _getIntegerv(name, &value);
            return value;
        }

        void Ints(int name, int[] into)
        {
            fixed (int* p = into)
                _getIntegerv(name, p);
        }

        void Floats(int name, float[] into)
        {
            fixed (float* p = into)
                _getFloatv(name, p);
        }

        public void Save()
        {
            _drawFramebuffer = Int(0x8CA6);
            _readFramebuffer = Int(0x8CAA);
            _renderbuffer = Int(0x8CA7);
            Ints(0x0BA2, _viewportBox);
            Ints(0x0C10, _scissorBox);
            for (var i = 0; i < Capabilities.Length; i++)
                _enabled[i] = _isEnabled(Capabilities[i]) != 0;

            Floats(0x8005, _blendColorValue);
            _blendFunc[0] = Int(0x80C9); // SRC_RGB
            _blendFunc[1] = Int(0x80C8); // DST_RGB
            _blendFunc[2] = Int(0x80CB); // SRC_ALPHA
            _blendFunc[3] = Int(0x80CA); // DST_ALPHA
            _blendEquation[0] = Int(0x8009);
            _blendEquation[1] = Int(0x883D);

            byte flag;
            _getBooleanv(0x0B72, &flag);
            _depthMaskValue = flag != 0;
            _depthFuncValue = Int(0x0B74);
            _cullFaceValue = Int(0x0B45);
            _frontFaceValue = Int(0x0B46);
            // GL_POLYGON_MODE; compatibility contexts write front and back, core one value.
            var mode = stackalloc int[2];
            _getIntegerv(0x0B40, mode);
            _polygonModeValue = mode[0];

            int[] stencilNames =
            {
                0x0B92, 0x0B97, 0x0B93, 0x0B94, 0x0B95, 0x0B96, 0x0B98,
                0x8800, 0x8CA3, 0x8CA4, 0x8801, 0x8802, 0x8803, 0x8CA5,
            };
            for (var i = 0; i < stencilNames.Length; i++)
                _stencil[i] = Int(stencilNames[i]);

            var mask = stackalloc byte[4];
            _getBooleanv(0x0C23, mask);
            for (var i = 0; i < 4; i++)
                _colorMaskValue[i] = mask[i] != 0;
            Floats(0x0C22, _clearColorValue);
            _clearStencilValue = Int(0x0B91);

            _program = Int(0x8B8D);
            _vertexArray = Int(0x85B5);
            _arrayBuffer = Int(0x8894);
            _pixelPackBuffer = Int(0x88ED);
            _pixelUnpackBuffer = Int(0x88EF);

            _activeTextureValue = Int(0x84E0);
            for (var unit = 0; unit < TextureUnits; unit++)
            {
                _activeTexture(0x84C0 + unit);
                _textures[unit] = Int(0x8069);
                _samplers[unit] = Int(0x8919);
            }
            _activeTexture(_activeTextureValue);

            for (var i = 0; i < PixelStore.Length; i++)
                _pixelStore[i] = Int(PixelStore[i]);
        }

        public void Restore()
        {
            _bindFramebuffer(0x8CA9, _drawFramebuffer);
            _bindFramebuffer(0x8CA8, _readFramebuffer);
            _bindRenderbuffer(0x8D41, _renderbuffer);
            _viewport(_viewportBox[0], _viewportBox[1], _viewportBox[2], _viewportBox[3]);
            _scissor(_scissorBox[0], _scissorBox[1], _scissorBox[2], _scissorBox[3]);
            for (var i = 0; i < Capabilities.Length; i++)
            {
                if (_enabled[i])
                    _enable(Capabilities[i]);
                else
                    _disable(Capabilities[i]);
            }

            _blendColor(_blendColorValue[0], _blendColorValue[1], _blendColorValue[2], _blendColorValue[3]);
            _blendFuncSeparate(_blendFunc[0], _blendFunc[1], _blendFunc[2], _blendFunc[3]);
            _blendEquationSeparate(_blendEquation[0], _blendEquation[1]);
            _depthMask(_depthMaskValue ? (byte)1 : (byte)0);
            _depthFunc(_depthFuncValue);
            _cullFace(_cullFaceValue);
            _frontFace(_frontFaceValue);
            _polygonMode(0x0408 /* FRONT_AND_BACK */, _polygonModeValue);

            const int Front = 0x0404, Back = 0x0405;
            _stencilFuncSeparate(Front, _stencil[0], _stencil[1], _stencil[2]);
            _stencilOpSeparate(Front, _stencil[3], _stencil[4], _stencil[5]);
            _stencilMaskSeparate(Front, _stencil[6]);
            _stencilFuncSeparate(Back, _stencil[7], _stencil[8], _stencil[9]);
            _stencilOpSeparate(Back, _stencil[10], _stencil[11], _stencil[12]);
            _stencilMaskSeparate(Back, _stencil[13]);

            _colorMask(B(_colorMaskValue[0]), B(_colorMaskValue[1]), B(_colorMaskValue[2]), B(_colorMaskValue[3]));
            _clearColor(_clearColorValue[0], _clearColorValue[1], _clearColorValue[2], _clearColorValue[3]);
            _clearStencil(_clearStencilValue);

            _useProgram(_program);
            _bindVertexArray(_vertexArray);
            _bindBuffer(0x8892, _arrayBuffer);
            _bindBuffer(0x88EB, _pixelPackBuffer);
            _bindBuffer(0x88EC, _pixelUnpackBuffer);
            for (var unit = 0; unit < TextureUnits; unit++)
            {
                _activeTexture(0x84C0 + unit);
                _bindTexture(0x0DE1, _textures[unit]);
                _bindSampler(unit, _samplers[unit]);
            }
            _activeTexture(_activeTextureValue);

            for (var i = 0; i < PixelStore.Length; i++)
                _pixelStorei(PixelStore[i], _pixelStore[i]);
        }

        static byte B(bool value) => value ? (byte)1 : (byte)0;
    }
}
