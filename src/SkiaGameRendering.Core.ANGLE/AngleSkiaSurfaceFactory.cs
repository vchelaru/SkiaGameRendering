using SkiaSharp;
using static SkiaGameRendering.Core.ANGLE.AngleEgl;

namespace SkiaGameRendering.Core.ANGLE
{
    public class AngleTextureState
    {
        internal IntPtr D3DTexturePtr;
        internal IntPtr EglSurface;
    }

    /// <summary>One <see cref="AngleSkiaSurfaceFactory.BeginDirectDraw"/> pass, ended by <see cref="AngleSkiaSurfaceFactory.EndDirectDraw"/>.</summary>
    public sealed class AngleDirectPass
    {
        internal AngleDirectPass(AngleTextureState state, SKSurface surface, GRBackendRenderTarget renderTarget, int saveCount)
        {
            State = state;
            Surface = surface;
            RenderTarget = renderTarget;
            SaveCount = saveCount;
        }

        internal AngleTextureState State { get; }
        internal SKSurface Surface { get; }
        internal GRBackendRenderTarget RenderTarget { get; }
        internal int SaveCount { get; }
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
    ///   vtable (see <see cref="D3D11Com"/>) - no interop library on either side. SharpDX-based
    ///   hosts (MonoGame WindowsDX, KNI WindowsDX) read <c>CppObject.NativePointer</c> themselves,
    ///   typed, so nothing here reflects over an unknown SharpDX object (which the trimmer can't
    ///   follow). Never add a direct SharpDX PackageReference here - different host engines pin
    ///   different SharpDX versions (MonoGame WindowsDX 3.8.4.1 -> SharpDX 4.0.1, KNI's DX11
    ///   platform -> SharpDX 4.2.0); a hard PackageReference would force NuGet's resolver to pick
    ///   one version for every consumer.
    /// - ANGLE DLLs (libEGL.dll, libGLESv2.dll) are resolved at runtime. See AngleEgl.cs for the
    ///   resolution order.
    /// - ANGLE's shader compiler is not safe to drive from two displays at once: it counts live
    ///   compilers in one process-wide variable but guards it with a per-display mutex
    ///   (libANGLE/Compiler.cpp), and each factory has its own display. Two factories compiling
    ///   or tearing down on different threads crash a later glCompileShader with a null write
    ///   (issue #124). So every factory in the process shares <see cref="AngleLock"/>, held from
    ///   <see cref="BeginDraw"/> to <see cref="EndDraw"/> (the compile happens inside the host's
    ///   own Skia flush) and inside every other call that reaches ANGLE, and each context is set
    ///   to compile on the calling thread, since ANGLE's worker threads would run outside the lock.
    ///   Begin/End must run on the same thread, and a thread inside a draw span must not wait on
    ///   another factory's thread.
    /// </summary>
    public class AngleSkiaSurfaceFactory : IDisposable
    {
        internal static readonly object AngleLock = new object();

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

        /// <param name="d3dDevicePtr">Native <c>ID3D11Device*</c> from the host engine.</param>
        /// <param name="d3dContextPtr">Native <c>ID3D11DeviceContext*</c> (the immediate context) from the host engine.</param>
        public void InitializeFromNative(IntPtr d3dDevicePtr, IntPtr d3dContextPtr)
        {
            lock (AngleLock)
                Initialize(d3dDevicePtr, d3dContextPtr);
        }

        void Initialize(IntPtr d3dDevicePtr, IntPtr d3dContextPtr)
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

            // Compile shaders on this thread, under AngleLock, instead of on ANGLE's worker threads
            // (see the MAINTENANCE NOTES). An older ANGLE without the entry point is left as it is.
            try
            {
                glMaxShaderCompilerThreadsKHR(0);
            }
            catch (EntryPointNotFoundException)
            {
            }

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

        /// <summary>
        /// Starts a draw span and takes <see cref="AngleLock"/> until <see cref="EndDraw"/>, which
        /// must be called on the same thread.
        /// </summary>
        public void BeginDraw()
        {
            if (!_holdsDrawLock)
            {
                Monitor.Enter(AngleLock);
                _holdsDrawLock = true;
            }

            // Save the engine's current D3D11 state by swapping to the empty state
            _savedState = D3D11Com.SwapDeviceContextState(_context1, _emptyState);
        }

        public void EndDraw()
        {
            try
            {
                eglMakeCurrent(_eglDisplay, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);

                // Restore the engine's D3D11 state
                if (_savedState != IntPtr.Zero)
                {
                    // The swap hands back _emptyState AddRef'd, like any previous state it returns.
                    var emptyState = D3D11Com.SwapDeviceContextState(_context1, _savedState);
                    if (emptyState != IntPtr.Zero)
                        D3D11Com.Release(emptyState);
                    D3D11Com.Release(_savedState);
                    _savedState = IntPtr.Zero;
                }
            }
            finally
            {
                ReleaseDrawLock();
            }
        }

        bool _holdsDrawLock;

        void ReleaseDrawLock()
        {
            if (_holdsDrawLock)
            {
                _holdsDrawLock = false;
                Monitor.Exit(AngleLock);
            }
        }

        /// <summary>
        /// Imports the host engine's D3D11 texture into ANGLE as an EGL pbuffer surface. This is
        /// the zero-copy bridge: the pbuffer is backed by the D3D11 texture, so rendering to FBO 0
        /// while this surface is current writes directly into the engine's texture.
        /// </summary>
        public AngleTextureState CreateTextureState(IntPtr d3dTexturePtr)
        {
            lock (AngleLock)
                return CreateTextureStateCore(d3dTexturePtr);
        }

        AngleTextureState CreateTextureStateCore(IntPtr d3dTexturePtr, int sampleCount = 1)
        {
            int[] pbufferAttribs = { EGL_NONE };
            var eglSurface = eglCreatePbufferFromClientBuffer(
                _eglDisplay, EGL_D3D_TEXTURE_ANGLE, d3dTexturePtr, GetConfig(sampleCount), pbufferAttribs);

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
            lock (AngleLock)
                return CreateSurfaceCore(state, width, height, colorType, colorSpace);
        }

        (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurfaceCore(
            AngleTextureState state, int width, int height, SKColorType colorType, SKColorSpace? colorSpace)
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
            lock (AngleLock)
            {
                if (!eglMakeCurrent(_eglDisplay, state.EglSurface, state.EglSurface, _eglContext))
                    throw new Exception($"eglMakeCurrent failed. EGL error: 0x{eglGetError():X}");
                _grContext.ResetContext();
            }
        }

        public void UnbindAfterDrawing()
        {
            lock (AngleLock)
            {
                _grContext.Flush();
                // glFinish blocks until ANGLE's GPU work completes, ensuring the D3D11
                // texture is ready before the engine reads it. Could potentially relax to
                // glFlush if D3D11's internal sync is sufficient.
                glFinish();
            }
        }

        public void DisposeRenderState(AngleTextureState state)
        {
            lock (AngleLock)
            {
                if (state.EglSurface != IntPtr.Zero && state.EglSurface != EGL_NO_SURFACE)
                {
                    eglDestroySurface(_eglDisplay, state.EglSurface);
                    state.EglSurface = IntPtr.Zero;
                }
            }
        }

        /// <summary>
        /// Frees a target's Skia surface with ANGLE's context current, then its EGL pbuffer. Skia frees GL
        /// objects as it goes, and with no context current ANGLE drops those calls.
        /// </summary>
        public void DisposeSurface(AngleTextureState? state, SKSurface? surface, GRBackendRenderTarget? renderTarget)
        {
            lock (AngleLock)
            {
                RunOnAngleContext(() =>
                {
                    surface?.Dispose();
                    renderTarget?.Dispose();
                });
                if (state != null)
                    DisposeRenderState(state);
            }
        }

        /// <summary>
        /// Runs a teardown with ANGLE's context current, inside the D3D11 state swap like any other ANGLE
        /// work. Inside a draw span both are already in place, so it runs as is.
        /// </summary>
        void RunOnAngleContext(Action teardown)
        {
            if (_holdsDrawLock)
            {
                teardown();
                return;
            }

            BeginDraw();
            try
            {
                if (eglMakeCurrent(_eglDisplay, EGL_NO_SURFACE, EGL_NO_SURFACE, _eglContext))
                    _grContext.ResetContext();
                else
                    _grContext.AbandonContext();
                teardown();
            }
            finally
            {
                EndDraw();
            }
        }

        // --- Drawing straight into the engine's bound render target (SkiaDirectCanvas) ---

        readonly Dictionary<int, IntPtr> _multisampledConfigs = new();

        /// <summary>
        /// The EGL config to wrap a texture with <paramref name="sampleCount"/> samples. ANGLE refuses a
        /// multisampled texture against a single-sampled config (EGL_BAD_PARAMETER), and draws into it
        /// directly with one whose sample count matches; the engine resolves it as it always does.
        /// </summary>
        IntPtr GetConfig(int sampleCount)
        {
            if (sampleCount <= 1)
                return _eglConfig;
            if (_multisampledConfigs.TryGetValue(sampleCount, out var config))
                return config;

            int[] attribs = { EGL_RENDERABLE_TYPE, EGL_OPENGL_ES2_BIT, EGL_SURFACE_TYPE, EGL_PBUFFER_BIT,
                EGL_SAMPLE_BUFFERS, 1, EGL_SAMPLES, sampleCount, EGL_NONE };
            if (!eglChooseConfig(_eglDisplay, attribs, out config, 1, out var numConfigs) || numConfigs == 0)
                throw new NotSupportedException($"ANGLE has no {sampleCount}x multisampled pbuffer config. EGL error: 0x{eglGetError():X}");
            _multisampledConfigs.Add(sampleCount, config);
            return config;
        }

        readonly IntPtr[] _boundViews = new IntPtr[D3D11Com.SimultaneousRenderTargetCount];

        /// <summary>
        /// Starts a pass that draws into whatever render target the engine has bound on the immediate
        /// context: its swap chain's back buffer or a render target texture. Called with the engine's own
        /// state current, before any <see cref="BeginDraw"/>. The target's texture is wrapped as an EGL
        /// pbuffer for this pass only and released at <see cref="EndDirectDraw"/>, so nothing here holds a
        /// swap chain buffer across frames (DXGI's ResizeBuffers fails while anything does).
        /// </summary>
        /// <param name="expectedWidth">The width the engine says its current target has, checked against the bound texture.</param>
        /// <param name="expectedHeight">The height the engine says its current target has.</param>
        public SKCanvas BeginDirectDraw(int expectedWidth, int expectedHeight, out AngleDirectPass pass)
        {
            var texture = GetBoundTexture(expectedWidth, expectedHeight, out var desc, out var colorType);

            AngleTextureState? state = null;
            SKSurface? surface = null;
            GRBackendRenderTarget? renderTarget = null;
            BeginDraw();
            try
            {
                state = CreateTextureStateCore(texture, (int)desc.SampleCount);
                (surface, renderTarget) = CreateSurfaceCore(state, (int)desc.Width, (int)desc.Height, colorType, null);
                pass = new AngleDirectPass(state, surface, renderTarget, surface.Canvas.Save());
                return surface.Canvas;
            }
            catch
            {
                ReleaseDirectPass(state, surface, renderTarget);
                EndDraw();
                throw;
            }
            finally
            {
                // The pbuffer holds its own reference to the texture.
                D3D11Com.Release(texture);
            }
        }

        /// <summary>
        /// Ends a pass started by <see cref="BeginDirectDraw"/>: submits Skia's draws, which land on the
        /// engine's queue after its earlier draws and before its later ones, and restores the engine's
        /// D3D11 state. Restores it even if it throws.
        /// </summary>
        public void EndDirectDraw(AngleDirectPass pass)
        {
            try
            {
                pass.Surface.Canvas.RestoreToCount(pass.SaveCount);
                pass.Surface.Flush();
                _grContext.Flush();
            }
            finally
            {
                try
                {
                    ReleaseDirectPass(pass.State, pass.Surface, pass.RenderTarget);
                }
                finally
                {
                    EndDraw();
                }
            }
        }

        /// <summary>Frees a pass's Skia surface with ANGLE's context current, then its pbuffer. Inside a draw span.</summary>
        void ReleaseDirectPass(AngleTextureState? state, SKSurface? surface, GRBackendRenderTarget? renderTarget)
        {
            surface?.Dispose();
            renderTarget?.Dispose();
            // A surface is only destroyed once it is no longer current.
            eglMakeCurrent(_eglDisplay, EGL_NO_SURFACE, EGL_NO_SURFACE, _eglContext);
            if (state != null)
                DisposeRenderState(state);

            // ANGLE's swapped-out D3D11 state still binds its view of the target, which holds a reference
            // to the engine's back buffer and makes the engine's next ResizeBuffers fail. A new pbuffer
            // gets a new view, so ANGLE rebinds next pass even though its state cache missed this.
            D3D11Com.OMUnbindRenderTargets(_context1);
        }

        /// <summary>The engine's one bound render target texture, AddRef'd, checked against what the engine says it is.</summary>
        IntPtr GetBoundTexture(int expectedWidth, int expectedHeight, out Texture2DDesc desc, out SKColorType colorType)
        {
            D3D11Com.OMGetRenderTargets(_context1, _boundViews);
            var view = _boundViews[0];
            var count = 0;
            for (var i = 0; i < _boundViews.Length; i++)
            {
                if (_boundViews[i] == IntPtr.Zero)
                    continue;
                count++;
                if (i > 0)
                    D3D11Com.Release(_boundViews[i]);
                _boundViews[i] = IntPtr.Zero;
            }

            if (view == IntPtr.Zero)
                throw new InvalidOperationException(
                    count == 0
                        ? "The engine has no render target bound on its D3D11 context."
                        : "The engine's render target is not in D3D11 slot 0.");

            try
            {
                if (count > 1)
                    throw new NotSupportedException($"SkiaDirectCanvas needs a single bound render target, not {count}.");

                var viewDesc = D3D11Com.GetRenderTargetViewDesc(view);
                if (viewDesc.ViewDimension is not (RtvDimension.Texture2D or RtvDimension.Texture2DMultisampled)
                    || (viewDesc.ViewDimension == RtvDimension.Texture2D && viewDesc.MipSlice != 0))
                    throw new NotSupportedException(
                        "SkiaDirectCanvas draws into the top mip level of a 2D render target; the engine has bound " +
                        $"a view of dimension {viewDesc.ViewDimension}, mip {viewDesc.MipSlice}.");

                var resource = D3D11Com.GetViewResource(view);
                IntPtr texture;
                try
                {
                    texture = D3D11Com.QueryInterface(resource, D3D11Com.IID_ID3D11Texture2D);
                }
                finally
                {
                    D3D11Com.Release(resource);
                }

                try
                {
                    desc = D3D11Com.GetTexture2DDesc(texture);
                    if (desc.Width != expectedWidth || desc.Height != expectedHeight)
                        throw new InvalidOperationException(
                            $"The engine's bound D3D11 render target is {desc.Width}x{desc.Height}, but the engine " +
                            $"reports its current target as {expectedWidth}x{expectedHeight}.");
                    if (desc.MipLevels != 1 || desc.ArraySize != 1)
                        throw new NotSupportedException(
                            "SkiaDirectCanvas cannot draw into a render target with mipmaps or array slices " +
                            $"({desc.MipLevels} mips, {desc.ArraySize} slices).");
                    colorType = ToColorType(viewDesc.Format, desc.Format);
                    return texture;
                }
                catch
                {
                    D3D11Com.Release(texture);
                    throw;
                }
            }
            finally
            {
                D3D11Com.Release(view);
            }
        }

        /// <summary>
        /// The Skia color type for a bound render target. ANGLE wraps the texture in the texture's own
        /// format, so the texture decides whether Skia's writes get encoded. An sRGB-typed texture is
        /// refused, since ANGLE would encode Skia's writes, which no other backend's direct canvas does. An
        /// sRGB view of a UNORM texture (a flip-model swap chain's back buffer, as Stride binds it in its
        /// default Linear color space) is drawn through the UNORM format, so Skia writes the same bytes it
        /// writes into any other target.
        /// </summary>
        internal static SKColorType ToColorType(DxgiFormat viewFormat, DxgiFormat textureFormat) => (viewFormat, textureFormat) switch
        {
            (DxgiFormat.R8G8B8A8Unorm or DxgiFormat.R8G8B8A8UnormSrgb, DxgiFormat.R8G8B8A8Unorm or DxgiFormat.R8G8B8A8Typeless) => SKColorType.Rgba8888,
            (DxgiFormat.B8G8R8A8Unorm or DxgiFormat.B8G8R8A8UnormSrgb, DxgiFormat.B8G8R8A8Unorm or DxgiFormat.B8G8R8A8Typeless) => SKColorType.Bgra8888,
            _ => throw new NotSupportedException(
                "SkiaDirectCanvas supports 8-bit RGBA or BGRA render targets, or an sRGB view of one, not a view " +
                $"of DXGI format {(uint)viewFormat} on a texture of DXGI format {(uint)textureFormat}."),
        };

        public void Dispose()
        {
            lock (AngleLock)
            {
                DisposeCore();
                // A Dispose mid-draw (no EndDraw) would otherwise leave every other factory blocked.
                ReleaseDrawLock();
            }
        }

        void DisposeCore()
        {
            if (_grContext != null && _eglContext != EGL_NO_CONTEXT && _context1 != IntPtr.Zero)
            {
                var grContext = _grContext;
                RunOnAngleContext(grContext.Dispose);
                _grContext = null!;
            }
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
