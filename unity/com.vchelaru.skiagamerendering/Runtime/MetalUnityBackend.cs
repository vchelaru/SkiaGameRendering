#nullable enable
using System;
using System.Runtime.InteropServices;
using SkiaGameRendering.Core.Metal;
using SkiaSharp;

namespace SkiaGameRendering.Unity
{
    /// <summary>
    /// Metal (macOS): Skia draws with Unity's own MTLDevice and commits to Unity's own command queue,
    /// so Metal's hazard tracking orders Skia's writes after Unity's earlier reads of the texture and
    /// before its later ones. Unity's C# API exposes neither, so the SkiaUnityMetal native plugin
    /// (unity/native/SkiaUnityMetal) reads them from Unity's IUnityGraphicsMetalV2 interface.
    /// </summary>
    internal sealed class MetalUnityBackend : SkiaUnityBackend
    {
        readonly MetalSkiaSurfaceFactory _factory = new MetalSkiaSurfaceFactory();

        /// <summary>
        /// Main thread. Unity refuses to load a native plugin from any other thread, and only calls its
        /// UnityPluginLoad, which is where the plugin gets Unity's Metal interface, when it loads.
        /// </summary>
        internal static void LoadPlugin() => SkiaUnityMetal_Device();

        internal MetalUnityBackend()
        {
            var device = SkiaUnityMetal_Device();
            if (device == IntPtr.Zero)
                throw new InvalidOperationException(
                    "The SkiaUnityMetal plugin has no Metal device: Unity didn't load it, or isn't running on Metal.");
            var queue = SkiaUnityMetal_CommandQueue();
            _factory.InitializeFromNative(device, queue);
        }

        internal override void Draw(SkiaUnityRenderTarget.RenderState target, SKPicture picture)
        {
            // Skia's command buffer has to run after everything Unity encoded before this event
            // (including reads of the texture's previous contents) and before everything after it.
            // Unity's current command buffer isn't committed yet, so on the shared queue it would run
            // after Skia's; committing it now puts it first.
            SkiaUnityMetal_CommitCurrentCommandBuffer();
            // Skia autoreleases its command buffers and encoders, each retaining the device, and
            // nothing drains a pool on Unity's render thread until Unity's own frame loop does.
            var pool = AutoreleasePoolPush();
            _factory.BeginDraw();
            try
            {
                if (target.Surface == null)
                {
                    var textureState = _factory.CreateTextureState(target.NativeTexture);
                    target.TextureState = textureState;
                    (target.Surface, target.BackendRenderTarget) = _factory.CreateSurface(
                        textureState, target.Width, target.Height, SKColorType.Rgba8888);
                }

                PlayBack(target.Surface, picture, target.Height);
            }
            finally
            {
                // Not synchronous: Unity's own reads of the texture are committed after this on the same queue.
                _factory.EndDraw(synchronous: false);
                AutoreleasePoolPop(pool);
            }
        }

        internal override void DisposeTarget(SkiaUnityRenderTarget.RenderState target)
        {
            target.Surface?.Dispose();
            target.Surface = null;
            target.BackendRenderTarget?.Dispose();
            target.BackendRenderTarget = null;
            target.TextureState = null;
        }

        public override void Dispose() => _factory.Dispose();

        [DllImport("SkiaUnityMetal")]
        static extern IntPtr SkiaUnityMetal_Device();

        [DllImport("SkiaUnityMetal")]
        static extern IntPtr SkiaUnityMetal_CommandQueue();

        [DllImport("SkiaUnityMetal")]
        static extern void SkiaUnityMetal_CommitCurrentCommandBuffer();

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_autoreleasePoolPush")]
        static extern IntPtr AutoreleasePoolPush();

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_autoreleasePoolPop")]
        static extern void AutoreleasePoolPop(IntPtr pool);
    }
}
