using SkiaSharp;
using System.Reflection;
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
    /// - <see cref="Initialize"/> takes the D3D11 device/context as loosely-typed <see cref="object"/>
    ///   (boxed SharpDX instances) and resolves SharpDX types (Device1, DeviceContext1) by
    ///   reflecting off the *live object's own assembly* - never add a direct SharpDX
    ///   PackageReference here. Different host engines pin different SharpDX versions (MonoGame
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
        object _d3dContext1 = null!;
        object? _emptyState;
        object? _savedState;
        MethodInfo _swapMethod = null!;

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
        public void Initialize(object d3dDevice, object d3dContext)
        {
            var d3dDevicePtr = GetNativePointer(d3dDevice);
            if (d3dDevicePtr == IntPtr.Zero)
                throw new Exception("D3D11 device native pointer is null.");

            InitD3D11StateSwap(d3dDevice, d3dContext);

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
        /// Sets up D3D11.1 SwapDeviceContextState for save/restore around ANGLE calls.
        /// All types are accessed via reflection because we don't directly reference SharpDX.
        /// </summary>
        void InitD3D11StateSwap(object d3dDevice, object d3dContext)
        {
            var sharpDxAsm = d3dDevice.GetType().Assembly;
            var device1Type = sharpDxAsm.GetType("SharpDX.Direct3D11.Device1");
            var dc1Type = sharpDxAsm.GetType("SharpDX.Direct3D11.DeviceContext1");

            // Wrap the existing COM pointers as D3D11.1 interfaces
            var devicePtr = GetNativePointer(d3dDevice);
            var device1 = Activator.CreateInstance(device1Type, new object[] { devicePtr });

            var ctxPtr = GetNativePointer(d3dContext);
            _d3dContext1 = Activator.CreateInstance(dc1Type, new object[] { ctxPtr })!;

            // CreateDeviceContextState creates a snapshot of "empty" D3D11 state.
            // When we swap to it, the engine's state is saved; when we swap back, it's restored.
            var featureLevelType = sharpDxAsm.GetType("SharpDX.Direct3D.FeatureLevel")
                ?? Type.GetType("SharpDX.Direct3D.FeatureLevel, SharpDX");
            var flagsType = sharpDxAsm.GetType("SharpDX.Direct3D11.CreateDeviceContextStateFlags");

            var createMethod = device1Type.GetMethods()
                .First(m => m.Name == "CreateDeviceContextState" && m.IsGenericMethod);

            var genericMethod = createMethod.MakeGenericMethod(device1Type);

            var featureLevel11 = Enum.Parse(featureLevelType!, "Level_11_0");
            var flagsNone = Enum.Parse(flagsType, "None");
            var featureLevels = Array.CreateInstance(featureLevelType!, 1);
            featureLevels.SetValue(featureLevel11, 0);

            var chosenLevel = Activator.CreateInstance(featureLevelType!);
            var createParams = new object?[] { flagsNone, featureLevels, chosenLevel };
            _emptyState = genericMethod.Invoke(device1, createParams);

            _swapMethod = dc1Type.GetMethod("SwapDeviceContextState")!;
        }

        public void BeginDraw()
        {
            // Save the engine's current D3D11 state by swapping to the empty state
            var swapParams = new object?[] { _emptyState, null };
            _swapMethod.Invoke(_d3dContext1, swapParams);
            _savedState = swapParams[1];
        }

        public void EndDraw()
        {
            eglMakeCurrent(_eglDisplay, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);

            // Restore the engine's D3D11 state
            if (_savedState != null)
            {
                var restoreParams = new object?[] { _savedState, null };
                _swapMethod.Invoke(_d3dContext1, restoreParams);
                ((IDisposable)_savedState).Dispose();
                _savedState = null;
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

        public (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            AngleTextureState state, int width, int height, SKColorType colorType)
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
                var surface = SKSurface.Create(_grContext, backendRT, GRSurfaceOrigin.TopLeft, colorType)
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
