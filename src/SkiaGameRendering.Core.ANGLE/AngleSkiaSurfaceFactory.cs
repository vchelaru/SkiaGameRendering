using SkiaSharp;
using static SkiaGameRendering.Core.ANGLE.AngleEgl;

namespace SkiaGameRendering.Core.ANGLE
{
    public class AngleTextureState
    {
        internal IntPtr D3DTexturePtr;
        internal IntPtr EglSurface;
    }

    /// <summary>
    /// Engine-agnostic ANGLE/D3D11-Skia interop, shared by any host engine that renders via D3D11
    /// (MonoGame WindowsDX, KNI WindowsDX, ...).
    ///
    /// How it works: the host engine renders via D3D11. SkiaSharp's GPU backend speaks OpenGL.
    /// ANGLE (Google's GL-to-D3D11 translator, used by Chrome) bridges the gap - Skia thinks it's
    /// talking to GL, but ANGLE translates those calls into D3D11 operations on the same GPU device
    /// the host engine uses.
    ///
    /// The key trick: the host engine's own D3D11 device is passed to ANGLE via
    /// eglCreateDeviceANGLE, so both ANGLE and the engine operate on the same GPU resources. This
    /// enables zero-copy texture sharing.
    ///
    /// MAINTENANCE NOTES:
    /// - The real entry point is <see cref="InitializeFromNative"/>, which takes the D3D11
    ///   device/context as raw <see cref="IntPtr"/>s and talks to them purely through the COM
    ///   vtable (see <see cref="D3D11Com"/>) - no interop library on either side.
    ///   <see cref="Initialize"/> is a SharpDX convenience wrapper over it: MonoGame WindowsDX and
    ///   KNI WindowsDX both hand this class boxed SharpDX <c>Device</c>/<c>DeviceContext</c>
    ///   instances, and <see cref="GetNativePointer"/> reflects out the underlying pointer so
    ///   those adapters don't need to change. A host that isn't SharpDX-based (e.g. Stride 4.4+ on
    ///   Silk.NET) calls <see cref="InitializeFromNative"/> directly. Never add a direct SharpDX
    ///   PackageReference here - different host engines pin different SharpDX versions (MonoGame
    ///   WindowsDX 3.8.4.1 -> SharpDX 4.0.1, KNI's DX11 platform -> SharpDX 4.2.0); a hard
    ///   PackageReference would force NuGet's resolver to pick one version for every consumer.
    /// - ANGLE DLLs (libEGL.dll, libGLESv2.dll) are resolved at runtime. See AngleEgl.cs for the
    ///   resolution order.
    /// </summary>
    public class AngleSkiaSurfaceFactory : IDisposable
    {
        GRContext _grContext = null!;
        IntPtr _eglDevice;
        IntPtr _eglDisplay;
        IntPtr _eglConfig;
        IntPtr _eglContext;

        // D3D11.1 SwapDeviceContextState - saves/restores ALL D3D11 state at once.
        // Required because ANGLE modifies D3D11 state (shaders, blend, render
        // targets, etc.) behind the host engine's back. Engines cache their own view of D3D11
        // state internally, so if ANGLE changes the real state, that cache goes
        // stale and drawing silently produces nothing. SwapDeviceContextState is
        // the D3D11.1 mechanism designed exactly for this "multiple clients sharing
        // one device" scenario.
        //
        // _device1, _context1 and _emptyState are each a QueryInterface/CreateDeviceContextState
        // result, so each owns a COM reference that Dispose must Release.
        IntPtr _device1;
        IntPtr _context1;
        IntPtr _emptyState;
        IntPtr _savedState;

        public GRContext GRContext => _grContext;

        /// <summary>
        /// Reflects a boxed SharpDX <c>CppObject</c>'s <c>NativePointer</c> property. Shared helper
        /// so callers don't need their own copy of this reflection just to hand a texture pointer
        /// to <see cref="CreateTextureState"/>.
        /// </summary>
        public static IntPtr GetNativePointer(object sharpDxObject)
        {
            var property = sharpDxObject.GetType().GetProperty("NativePointer")
                ?? throw new Exception($"NativePointer property not found on {sharpDxObject.GetType()}.");
            return (IntPtr)property.GetValue(sharpDxObject)!;
        }

        /// <param name="d3dDevice">Boxed SharpDX.Direct3D11.Device (or Device1) from the host engine.</param>
        /// <param name="d3dContext">Boxed SharpDX.Direct3D11.DeviceContext (or DeviceContext1) from the host engine.</param>
        public void Initialize(object d3dDevice, object d3dContext) =>
            InitializeFromNative(GetNativePointer(d3dDevice), GetNativePointer(d3dContext));

        /// <param name="d3dDevicePtr">Native <c>ID3D11Device*</c> from the host engine.</param>
        /// <param name="d3dContextPtr">Native <c>ID3D11DeviceContext*</c> (the immediate context) from the host engine.</param>
        public void InitializeFromNative(IntPtr d3dDevicePtr, IntPtr d3dContextPtr)
        {
            if (d3dDevicePtr == IntPtr.Zero)
                throw new Exception("D3D11 device native pointer is null.");
            if (d3dContextPtr == IntPtr.Zero)
                throw new Exception("D3D11 context native pointer is null.");

            InitD3D11StateSwap(d3dDevicePtr, d3dContextPtr);

            // Wrap the engine's D3D11 device as an ANGLE EGL device, then create an EGL
            // display from it. This is what makes zero-copy possible - ANGLE and the engine share
            // the same GPU device, so textures created by one are visible to the other without
            // any copying.
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
        /// Sets up D3D11.1 SwapDeviceContextState for save/restore around ANGLE calls, via the raw
        /// COM vtable calls in <see cref="D3D11Com"/>.
        /// </summary>
        void InitD3D11StateSwap(IntPtr d3dDevicePtr, IntPtr d3dContextPtr)
        {
            _device1 = D3D11Com.QueryInterface(d3dDevicePtr, D3D11Com.IID_ID3D11Device1);
            _context1 = D3D11Com.QueryInterface(d3dContextPtr, D3D11Com.IID_ID3D11DeviceContext1);

            // CreateDeviceContextState creates a snapshot of "empty" D3D11 state.
            // When we swap to it, the engine's state is saved; when we swap back, it's restored.
            _emptyState = D3D11Com.CreateDeviceContextState(_device1);
        }

        public void BeginDraw()
        {
            // Save the engine's current D3D11 state by swapping to the empty state
            _savedState = D3D11Com.SwapDeviceContextState(_context1, _emptyState);
        }

        public void EndDraw()
        {
            eglMakeCurrent(_eglDisplay, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);

            // Restore the engine's D3D11 state
            if (_savedState != IntPtr.Zero)
            {
                D3D11Com.SwapDeviceContextState(_context1, _savedState);
                D3D11Com.Release(_savedState);
                _savedState = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Imports the host engine's D3D11 texture into ANGLE as an EGL pbuffer surface. This is
        /// the zero-copy bridge: the pbuffer is backed by the D3D11 texture, so rendering to FBO 0
        /// while this surface is current writes directly into the engine's texture.
        /// </summary>
        public AngleTextureState CreateTextureState(IntPtr d3dTexturePtr)
        {
            int[] pbufferAttribs = { EGL_NONE };
            var eglSurface = eglCreatePbufferFromClientBuffer(
                _eglDisplay, EGL_D3D_TEXTURE_ANGLE, d3dTexturePtr, _eglConfig, pbufferAttribs);

            if (eglSurface == EGL_NO_SURFACE)
                throw new Exception($"eglCreatePbufferFromClientBuffer failed. EGL error: 0x{eglGetError():X}");

            return new AngleTextureState { D3DTexturePtr = d3dTexturePtr, EglSurface = eglSurface };
        }

        /// <param name="colorSpace">
        /// Optional Skia color-space tag for the surface - purely a Skia-side software conversion
        /// applied when Skia writes into the wrapped D3D11 texture (via ANGLE), independent of the
        /// texture's own DXGI format (never touched by this parameter). See the identical parameter
        /// on <c>VkSkiaSurfaceFactory.CreateSurface</c> for the full mechanism this exists for:
        /// Stride's adapter passes <c>SKColorSpace.CreateSrgbLinear()</c> to compensate for Stride's
        /// own hardware sRGB encode-on-write under its default Linear color-space pipeline. Pass
        /// <c>null</c> (the default) for the previous raw-passthrough behavior.
        /// </param>
        public (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            AngleTextureState state, int width, int height, SKColorType colorType, SKColorSpace? colorSpace = null)
        {
            if (!eglMakeCurrent(_eglDisplay, state.EglSurface, state.EglSurface, _eglContext))
                throw new Exception($"eglMakeCurrent failed. EGL error: 0x{eglGetError():X}");

            _grContext.ResetContext();

            // FBO 0 = the default framebuffer, which is backed by the EGL surface,
            // which is backed by the engine's D3D11 texture. Skia renders here.
            var fbInfo = new GRGlFramebufferInfo(0, colorType.ToGlSizedFormat());

            unsafe
            {
                int samples;
                glGetIntegerv(GL_SAMPLES, &samples);
                var maxSamples = _grContext.GetMaxSurfaceSampleCount(colorType);
                if (samples > maxSamples)
                    samples = maxSamples;

                var backendRT = new GRBackendRenderTarget(width, height, samples, 0, fbInfo);
                var surface = SKSurface.Create(_grContext, backendRT, GRSurfaceOrigin.TopLeft, colorType, colorSpace)
                    ?? throw new Exception("SKSurface.Create failed for ANGLE backend.");

                return (surface, backendRT);
            }
        }

        public void BindForDrawing(AngleTextureState state)
        {
            if (!eglMakeCurrent(_eglDisplay, state.EglSurface, state.EglSurface, _eglContext))
                throw new Exception($"eglMakeCurrent failed. EGL error: 0x{eglGetError():X}");
            _grContext.ResetContext();
        }

        public void UnbindAfterDrawing()
        {
            _grContext.Flush();
            // glFinish blocks until ANGLE's GPU work completes, ensuring the D3D11
            // texture is ready before the engine reads it. Could potentially relax to
            // glFlush if D3D11's internal sync is sufficient.
            glFinish();
        }

        public void DisposeRenderState(AngleTextureState state)
        {
            if (state.EglSurface != IntPtr.Zero && state.EglSurface != EGL_NO_SURFACE)
            {
                eglDestroySurface(_eglDisplay, state.EglSurface);
                state.EglSurface = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            _grContext?.Dispose();

            if (_savedState != IntPtr.Zero)
            {
                D3D11Com.Release(_savedState);
                _savedState = IntPtr.Zero;
            }
            if (_emptyState != IntPtr.Zero)
            {
                D3D11Com.Release(_emptyState);
                _emptyState = IntPtr.Zero;
            }
            if (_context1 != IntPtr.Zero)
            {
                D3D11Com.Release(_context1);
                _context1 = IntPtr.Zero;
            }
            if (_device1 != IntPtr.Zero)
            {
                D3D11Com.Release(_device1);
                _device1 = IntPtr.Zero;
            }

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
