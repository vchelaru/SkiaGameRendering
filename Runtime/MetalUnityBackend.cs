#nullable enable
using System;
using System.Runtime.InteropServices;
using SkiaGameRendering.Core.Metal;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// Metal (macOS): Skia draws with Unity's own MTLDevice and commits to Unity's own command queue,
    /// so Metal's hazard tracking orders Skia's writes after Unity's earlier reads of the texture and
    /// before its later ones. Unity's C# API exposes neither, so the SkiaUnityMetal native plugin
    /// (unity/native/SkiaUnityMetal) reads them from Unity's IUnityGraphicsMetalV2 interface. A direct draw
    /// (<see cref="SkiaDirectCanvas"/>) gets the target's MTLTexture from the plugin by the
    /// <c>UnityRenderBuffer</c> the main thread read, commits Unity's command buffer first, and draws on top
    /// of the texture's contents.
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

        internal override void Draw(SkiaRenderTarget2D.RenderState target, SKPicture picture)
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

        internal override void DrawDirect(SKPicture picture, int width, int height, bool flip, IntPtr renderBuffer)
        {
            var texture = SkiaUnityMetal_TextureFromRenderBuffer(renderBuffer);
            if (texture == IntPtr.Zero)
                throw new InvalidOperationException("Unity has no Metal texture for the render target it is rendering to.");

            // Committing what Unity has encoded so far ends its render pass on the target and puts that work
            // ahead of Skia's command buffer on the shared queue. Unity starts a new pass on the target for
            // what it draws later.
            SkiaUnityMetal_CommitCurrentCommandBuffer();
            var pool = AutoreleasePoolPush();
            try
            {
                // Validated before Skia draws, so a target it cannot draw into throws untouched. Per draw,
                // since the back buffer's texture changes every frame.
                using var target = _factory.CreateDirectSurface(texture, width, height);
                _factory.BeginDraw();
                try
                {
                    var canvas = target.Surface.Canvas;
                    if (flip)
                    {
                        canvas.Translate(0, height);
                        canvas.Scale(1, -1);
                    }
                    canvas.DrawPicture(picture);
                }
                finally
                {
                    // Not synchronous: Unity's own commands for the target are committed after this on the same
                    // queue. Flushed before the target is disposed, which releases the texture view if it made one.
                    _factory.EndDraw(synchronous: false);
                }
            }
            finally
            {
                AutoreleasePoolPop(pool);
            }
        }

        internal override void DisposeTarget(SkiaRenderTarget2D.RenderState target)
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

        [DllImport("SkiaUnityMetal")]
        static extern IntPtr SkiaUnityMetal_TextureFromRenderBuffer(IntPtr renderBuffer);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_autoreleasePoolPush")]
        static extern IntPtr AutoreleasePoolPush();

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_autoreleasePoolPop")]
        static extern void AutoreleasePoolPop(IntPtr pool);
    }
}
