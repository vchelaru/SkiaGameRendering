using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
using System.Reflection;
using static SkiaGameRendering.AngleEgl;

namespace SkiaGameRendering
{
    internal class AngleTextureState
    {
        internal IntPtr D3DTexturePtr;
        internal IntPtr EglSurface;
    }

    /// <summary>
    /// SkiaBackend for MonoGame's WindowsDX (D3D11) platform. Windows only.
    ///
    /// How it works: MonoGame renders via D3D11. SkiaSharp's GPU backend speaks
    /// OpenGL. ANGLE (Google's GL-to-D3D11 translator, used by Chrome) bridges
    /// the gap — Skia thinks it's talking to GL, but ANGLE translates those calls
    /// into D3D11 operations on the same GPU device MonoGame uses.
    ///
    /// The key trick: we pass MonoGame's own D3D11 device to ANGLE via
    /// eglCreateDeviceANGLE, so both ANGLE and MonoGame operate on the same GPU
    /// resources. This enables zero-copy texture sharing.
    ///
    /// MAINTENANCE NOTES:
    /// - Field names (_d3dDevice, _d3dContext, _texture) are MonoGame internals
    ///   accessed via reflection. They may change between MG versions.
    /// - SharpDX types (Device1, DeviceContext1) are accessed via reflection too.
    ///   If MG moves away from SharpDX, this code needs a rewrite.
    /// - ANGLE DLLs (libEGL.dll, libGLESv2.dll) are resolved at runtime. See
    ///   AngleEgl.cs for the resolution order.
    /// </summary>
    public class SkiaAngleBackend : SkiaBackend
    {
        GRContext _grContext;
        IntPtr _eglDevice;
        IntPtr _eglDisplay;
        IntPtr _eglConfig;
        IntPtr _eglContext;

        // Reflection handles for MonoGame/SharpDX internals
        static FieldInfo _d3dDeviceField;
        static FieldInfo _d3dContextField;
        static FieldInfo _textureField;
        static PropertyInfo _nativePointerProperty;

        // D3D11.1 SwapDeviceContextState — saves/restores ALL D3D11 state at once.
        // Required because ANGLE modifies D3D11 state (shaders, blend, render
        // targets, etc.) behind MonoGame's back. MG caches its own view of D3D11
        // state internally, so if ANGLE changes the real state, MG's cache goes
        // stale and SpriteBatch silently draws nothing. SwapDeviceContextState is
        // the D3D11.1 mechanism designed exactly for this "multiple clients sharing
        // one device" scenario.
        object _d3dContext1;
        object _emptyState;
        object _savedState;
        MethodInfo _swapMethod;

        public override GRContext GRContext => _grContext;

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            var flags = BindingFlags.NonPublic | BindingFlags.Instance;

            // Extract MonoGame's internal D3D11 device and context via reflection.
            // These are SharpDX wrapper objects around the native COM interfaces.
            _d3dDeviceField ??= typeof(GraphicsDevice).GetField("_d3dDevice", flags);

            var d3dDevice = _d3dDeviceField?.GetValue(graphicsDevice)
                ?? throw new Exception("Could not extract _d3dDevice from GraphicsDevice.");

            // _texture on the Texture base class holds the SharpDX.Direct3D11.Resource
            _textureField ??= typeof(Texture).GetField("_texture", flags);

            // SharpDX.CppObject.NativePointer gives the raw COM interface pointer
            _nativePointerProperty ??= d3dDevice.GetType().GetProperty("NativePointer");

            _d3dContextField ??= typeof(GraphicsDevice).GetField("_d3dContext", flags);
            var d3dContext = _d3dContextField.GetValue(graphicsDevice);

            var d3dDevicePtr = (IntPtr)_nativePointerProperty.GetValue(d3dDevice);
            if (d3dDevicePtr == IntPtr.Zero)
                throw new Exception("D3D11 device native pointer is null.");

            InitD3D11StateSwap(d3dDevice, d3dContext);

            // Wrap MG's D3D11 device as an ANGLE EGL device, then create an EGL
            // display from it. This is what makes zero-copy possible — ANGLE and
            // MG share the same GPU device, so textures created by one are visible
            // to the other without any copying.
            _eglDevice = eglCreateDeviceANGLE(EGL_D3D11_DEVICE_ANGLE, d3dDevicePtr, null);
            if (_eglDevice == IntPtr.Zero)
                throw new Exception($"eglCreateDeviceANGLE failed. EGL error: 0x{eglGetError():X}");

            _eglDisplay = eglGetPlatformDisplayEXT(EGL_PLATFORM_DEVICE_EXT, _eglDevice, new[] { EGL_NONE });
            if (_eglDisplay == EGL_NO_DISPLAY)
                throw new Exception($"eglGetPlatformDisplayEXT failed. EGL error: 0x{eglGetError():X}");

            if (!eglInitialize(_eglDisplay, out _, out _))
                throw new Exception($"eglInitialize failed. EGL error: 0x{eglGetError():X}");

            // Not all ANGLE/driver combos support the same configs, so try
            // progressively less restrictive attribute lists.
            int[][] configAttempts = {
                new[] { EGL_RENDERABLE_TYPE, EGL_OPENGL_ES2_BIT, EGL_SURFACE_TYPE, EGL_PBUFFER_BIT, EGL_NONE },
                new[] { EGL_RENDERABLE_TYPE, EGL_OPENGL_ES2_BIT, EGL_NONE },
                new[] { EGL_NONE }
            };

            int numConfigs = 0;
            foreach (var attribs in configAttempts)
            {
                if (eglChooseConfig(_eglDisplay, attribs, out _eglConfig, 1, out numConfigs) && numConfigs > 0)
                    break;
                eglGetError();
                numConfigs = 0;
            }
            if (numConfigs == 0)
                throw new Exception($"eglChooseConfig failed. EGL error: 0x{eglGetError():X}");

            int[] contextAttribs = { EGL_CONTEXT_CLIENT_VERSION, 2, EGL_NONE };
            _eglContext = eglCreateContext(_eglDisplay, _eglConfig, EGL_NO_CONTEXT, contextAttribs);
            if (_eglContext == EGL_NO_CONTEXT)
                throw new Exception($"eglCreateContext failed. EGL error: 0x{eglGetError():X}");

            if (!eglMakeCurrent(_eglDisplay, EGL_NO_SURFACE, EGL_NO_SURFACE, _eglContext))
                throw new Exception($"eglMakeCurrent failed. EGL error: 0x{eglGetError():X}");

            // Create Skia's GR context using ANGLE's GL ES implementation.
            // eglGetProcAddress returns ANGLE's GL function pointers.
            _grContext = GRContext.CreateGl(GRGlInterface.CreateGles(eglGetProcAddress));

            eglMakeCurrent(_eglDisplay, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
        }

        /// <summary>
        /// Sets up D3D11.1 SwapDeviceContextState for save/restore around ANGLE calls.
        /// All types are accessed via reflection because we don't directly reference SharpDX.
        /// </summary>
        void InitD3D11StateSwap(object d3dDevice, object d3dContext)
        {
            var sharpDxAsm = d3dDevice.GetType().Assembly;
            var device1Type = sharpDxAsm.GetType("SharpDX.Direct3D11.Device1");
            var dc1Type = sharpDxAsm.GetType("SharpDX.Direct3D11.DeviceContext1");

            // Wrap the existing COM pointers as D3D11.1 interfaces
            var devicePtr = (IntPtr)_nativePointerProperty.GetValue(d3dDevice);
            var device1 = Activator.CreateInstance(device1Type, new object[] { devicePtr });

            var ctxPtr = (IntPtr)d3dContext.GetType().GetProperty("NativePointer").GetValue(d3dContext);
            _d3dContext1 = Activator.CreateInstance(dc1Type, new object[] { ctxPtr });

            // CreateDeviceContextState creates a snapshot of "empty" D3D11 state.
            // When we swap to it, MG's state is saved; when we swap back, it's restored.
            var featureLevelType = sharpDxAsm.GetType("SharpDX.Direct3D.FeatureLevel")
                ?? d3dDevice.GetType().Assembly.GetType("SharpDX.Direct3D.FeatureLevel")
                ?? Type.GetType("SharpDX.Direct3D.FeatureLevel, SharpDX");
            var flagsType = sharpDxAsm.GetType("SharpDX.Direct3D11.CreateDeviceContextStateFlags");

            var createMethod = device1Type.GetMethods()
                .First(m => m.Name == "CreateDeviceContextState" && m.IsGenericMethod);

            var emitterType = sharpDxAsm.GetType("SharpDX.Direct3D11.Device1");
            var genericMethod = createMethod.MakeGenericMethod(emitterType);

            var featureLevel11 = Enum.Parse(featureLevelType, "Level_11_0");
            var flagsNone = Enum.Parse(flagsType, "None");
            var featureLevels = Array.CreateInstance(featureLevelType, 1);
            featureLevels.SetValue(featureLevel11, 0);

            var chosenLevel = Activator.CreateInstance(featureLevelType);
            var createParams = new object[] { flagsNone, featureLevels, chosenLevel };
            _emptyState = genericMethod.Invoke(device1, createParams);

            _swapMethod = dc1Type.GetMethod("SwapDeviceContextState");
        }

        internal override void BeginDraw()
        {
            // Save MG's current D3D11 state by swapping to the empty state
            var swapParams = new object[] { _emptyState, null };
            _swapMethod.Invoke(_d3dContext1, swapParams);
            _savedState = swapParams[1];
        }

        internal override void EndDraw()
        {
            eglMakeCurrent(_eglDisplay, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);

            // Restore MG's D3D11 state
            if (_savedState != null)
            {
                var restoreParams = new object[] { _savedState, null };
                _swapMethod.Invoke(_d3dContext1, restoreParams);
                ((IDisposable)_savedState).Dispose();
                _savedState = null;
            }
        }

        IntPtr GetD3DTexturePtr(Texture2D texture)
        {
            var sharpDxResource = _textureField.GetValue(texture);
            if (sharpDxResource == null)
                throw new Exception(
                    $"D3D11 resource is null on Texture2D ({texture.Width}x{texture.Height}).");
            return (IntPtr)_nativePointerProperty.GetValue(sharpDxResource);
        }

        /// <summary>
        /// ANGLE requires textures to have D3D11_BIND_RENDER_TARGET. MonoGame's
        /// Texture2D only sets BIND_SHADER_RESOURCE. RenderTarget2D sets both.
        /// </summary>
        internal override Texture2D CreateTexture(int width, int height, SurfaceFormat format)
        {
            return new RenderTarget2D(GraphicsDevice, width, height, false, format, DepthFormat.None);
        }

        internal override object CaptureTextureHandle(Texture2D texture)
        {
            // WORKAROUND: MonoGame WindowsDX lazily allocates the D3D11 resource.
            // After new Texture2D(), the internal _texture field is null until MG
            // actually needs the GPU resource. SetData forces creation.
            // TODO: Find a cheaper way to force D3D11 resource allocation.
            texture.SetData(new byte[texture.Width * texture.Height * 4]);

            var d3dPtr = GetD3DTexturePtr(texture);

            // Import MG's D3D11 texture into ANGLE as an EGL pbuffer surface.
            // This is the zero-copy bridge: the pbuffer is backed by the D3D11
            // texture, so rendering to FBO 0 while this surface is current writes
            // directly into MG's texture.
            int[] pbufferAttribs = { EGL_NONE };
            var eglSurface = eglCreatePbufferFromClientBuffer(
                _eglDisplay, EGL_D3D_TEXTURE_ANGLE, d3dPtr, _eglConfig, pbufferAttribs);

            if (eglSurface == EGL_NO_SURFACE)
                throw new Exception($"eglCreatePbufferFromClientBuffer failed. EGL error: 0x{eglGetError():X}");

            return new AngleTextureState { D3DTexturePtr = d3dPtr, EglSurface = eglSurface };
        }

        internal override (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            object textureHandle, Texture2D texture, int width, int height, SKColorType colorType, out object renderState)
        {
            var state = (AngleTextureState)textureHandle;

            if (!eglMakeCurrent(_eglDisplay, state.EglSurface, state.EglSurface, _eglContext))
                throw new Exception($"eglMakeCurrent failed. EGL error: 0x{eglGetError():X}");

            _grContext.ResetContext();

            // FBO 0 = the default framebuffer, which is backed by the EGL surface,
            // which is backed by MG's D3D11 texture. Skia renders here.
            var fbInfo = new GRGlFramebufferInfo(0, colorType.ToGlSizedFormat());

            unsafe
            {
                int samples;
                glGetIntegerv(GL_SAMPLES, &samples);
                var maxSamples = _grContext.GetMaxSurfaceSampleCount(colorType);
                if (samples > maxSamples)
                    samples = maxSamples;

                var backendRT = new GRBackendRenderTarget(width, height, samples, 0, fbInfo);
                var surface = SKSurface.Create(_grContext, backendRT, GRSurfaceOrigin.TopLeft, colorType)
                    ?? throw new Exception("SKSurface.Create failed for ANGLE backend.");

                renderState = state;
                return (surface, backendRT);
            }
        }

        internal override void BindForDrawing(object renderState)
        {
            var state = (AngleTextureState)renderState;
            if (!eglMakeCurrent(_eglDisplay, state.EglSurface, state.EglSurface, _eglContext))
                throw new Exception($"eglMakeCurrent failed. EGL error: 0x{eglGetError():X}");
            _grContext.ResetContext();
        }

        internal override void UnbindAfterDrawing()
        {
            _grContext.Flush();
            // glFinish blocks until ANGLE's GPU work completes, ensuring the D3D11
            // texture is ready before MG reads it. Could potentially relax to
            // glFlush if D3D11's internal sync is sufficient.
            glFinish();
        }

        internal override void DisposeRenderState(object renderState)
        {
            var state = (AngleTextureState)renderState;
            if (state.EglSurface != IntPtr.Zero && state.EglSurface != EGL_NO_SURFACE)
            {
                eglDestroySurface(_eglDisplay, state.EglSurface);
                state.EglSurface = IntPtr.Zero;
            }
        }

        public override void Dispose()
        {
            _grContext?.Dispose();
            (_emptyState as IDisposable)?.Dispose();

            if (_eglDisplay != EGL_NO_DISPLAY)
            {
                if (_eglContext != EGL_NO_CONTEXT)
                {
                    eglMakeCurrent(_eglDisplay, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
                    eglDestroyContext(_eglDisplay, _eglContext);
                }
                eglTerminate(_eglDisplay);
            }

            if (_eglDevice != IntPtr.Zero)
                eglReleaseDeviceANGLE(_eglDevice);
        }
    }
}
