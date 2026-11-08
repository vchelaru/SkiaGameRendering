#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SkiaGameRendering.Core.VK;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// Vulkan (Windows): Skia draws with Unity's own VkDevice and submits to Unity's own graphics queue,
    /// so the queue orders Skia's writes after Unity's earlier work and before its later work. Unity's
    /// C# API exposes neither, so the SkiaUnityVulkan native plugin (unity/native/SkiaUnityVulkan) reads
    /// them from Unity's IUnityGraphicsVulkan interface. Each draw is two plugin events: the first has
    /// Unity record a barrier that moves the texture to the color attachment layout, the second runs
    /// with access to the queue after Unity has submitted that barrier, and is where Skia draws.
    /// </summary>
    internal sealed class VulkanUnityBackend : SkiaUnityBackend
    {
        // Match SkiaUnityVulkan.cpp.
        internal const int PrepareEventId = 0x534B01;
        internal const int DrawEventId = 0x534B02;

        const uint FormatR8G8B8A8Unorm = 37;
        const uint FormatR8G8B8A8Srgb = 43;
        const uint FormatB8G8R8A8Unorm = 44;
        const uint FormatB8G8R8A8Srgb = 50;

        [StructLayout(LayoutKind.Sequential)]
        struct PreparedImage
        {
            public ulong Image;
            public int Format;
            public uint Usage;
            public int Tiling;
            public int Layout;
            public int Samples;
            public int MipCount;
        }

        readonly VkSkiaSurfaceFactory _factory = new VkSkiaSurfaceFactory();
        // Unity runs every prepare event of a frame before the draw events that follow them, so a draw's
        // image is queued until its draw event comes. A prepare that failed queues an empty one.
        readonly Queue<PreparedImage> _prepared = new Queue<PreparedImage>();

        /// <summary>
        /// Main thread. Unity refuses to load a native plugin from any other thread, and only calls its
        /// UnityPluginLoad, which is where the plugin gets Unity's Vulkan interface, when it loads.
        /// </summary>
        internal static void LoadPlugin() => SkiaUnityVulkan_IsAvailable();

        internal VulkanUnityBackend()
        {
            if (!SkiaUnityVulkan_GetInstance(out var instance, out var physicalDevice, out var device, out var queue, out var queueFamilyIndex))
                throw new InvalidOperationException(
                    "The SkiaUnityVulkan plugin has no Vulkan device: Unity didn't load it, or isn't running on Vulkan.");

            // Skia needs Vulkan 1.1 at most; ask for no more than the device offers.
            var apiVersion = Math.Min(SkiaUnityVulkan_ApiVersion(), VkConstants.MakeApiVersion(1, 1));
            _factory.InitializeFromNative(
                instance, physicalDevice, device, queue,
                graphicsQueueFamilyIndex: queueFamilyIndex,
                apiVersion: apiVersion,
                instanceExtensions: Array.Empty<string>(),
                deviceExtensions: Array.Empty<string>(),
                acquireQueueLock: null);
        }

        internal override void Prepare(SkiaRenderTarget2D.RenderState target)
        {
            var ok = SkiaUnityVulkan_PrepareForDrawing(target.NativeTexture, out var image);
            _prepared.Enqueue(ok ? image : default);
            if (!ok)
                throw new InvalidOperationException("The SkiaUnityVulkan plugin could not prepare the texture for drawing.");
        }

        internal override void PrepareDirect(IntPtr renderBuffer)
        {
            var ok = SkiaUnityVulkan_PrepareRenderBufferForDrawing(renderBuffer, out var image);
            _prepared.Enqueue(ok ? image : default);
            if (!ok)
                throw new InvalidOperationException("The SkiaUnityVulkan plugin could not prepare the render target for drawing.");
        }

        internal override void Draw(SkiaRenderTarget2D.RenderState target, SKPicture picture)
        {
            var prepared = _prepared.Count > 0 ? _prepared.Dequeue() : default;
            if (prepared.Image == 0)
                throw new InvalidOperationException("The texture was not prepared for drawing.");

            _factory.BeginDraw();
            try
            {
                // Unity can recreate a texture's image; the surface is only good for the one it wrapped.
                if (target.Surface != null && ((VkTextureState)target.TextureState!).VkImage != prepared.Image)
                    DisposeTarget(target);

                if (target.Surface == null)
                {
                    var format = (uint)prepared.Format;
                    var textureState = _factory.CreateTextureState(
                        prepared.Image, format, (uint)prepared.Layout, prepared.Usage, (uint)prepared.Tiling,
                        sampleCount: (uint)prepared.Samples, levelCount: (uint)prepared.MipCount);
                    target.TextureState = textureState;
                    var colorType = format == FormatB8G8R8A8Unorm || format == FormatB8G8R8A8Srgb
                        ? SKColorType.Bgra8888
                        : SKColorType.Rgba8888;
                    (target.Surface, target.BackendRenderTarget) = _factory.CreateSurface(textureState, target.Width, target.Height, colorType);
                }

                PlayBack(target.Surface, picture, target.Height);
            }
            finally
            {
                // Not synchronous: Unity's own reads of the texture are submitted after this on the same queue.
                _factory.EndDraw(synchronous: false);
            }
        }

        internal override void DrawDirect(SKPicture picture, int width, int height, bool flip, IntPtr renderBuffer)
        {
            var prepared = _prepared.Count > 0 ? _prepared.Dequeue() : default;
            if (prepared.Image == 0)
                throw new InvalidOperationException("The render target was not prepared for drawing.");
            var format = (uint)prepared.Format;
            // Skia cannot wrap an sRGB-format image, and Unity's Linear color space renders into one, so it is
            // wrapped as its UNORM twin (same texel layout): Skia then writes the bytes as given, as on every other
            // backend. Unity creates the image without VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT, so the twin is outside
            // the spec's letter, which drivers accept for same-size formats (Stride's Vulkan backend does the same).
            if (format == FormatR8G8B8A8Srgb)
                format = FormatR8G8B8A8Unorm;
            else if (format == FormatB8G8R8A8Srgb)
                format = FormatB8G8R8A8Unorm;
            else if (format != FormatB8G8R8A8Unorm && format != FormatR8G8B8A8Unorm)
                throw new NotSupportedException($"SkiaDirectCanvas draws into 8-bit RGBA or BGRA targets, not VkFormat {format}.");
            if (prepared.Samples != 1)
                throw new NotSupportedException($"SkiaDirectCanvas does not draw into multisampled targets ({prepared.Samples} samples).");

            _factory.BeginDraw();
            SKSurface? surface = null;
            GRBackendRenderTarget? renderTarget = null;
            try
            {
                // The surface wraps the image of this frame's target, which Unity can change from frame to frame.
                var textureState = _factory.CreateTextureState(
                    // The image's layout is the one the prepare event's barrier moved it to, not the one Unity reports (it
                    // reports UNDEFINED, which would let Skia discard what the camera drew).
                    prepared.Image, format, VkConstants.ImageLayoutColorAttachmentOptimal, prepared.Usage, (uint)prepared.Tiling,
                    sampleCount: 1, levelCount: (uint)prepared.MipCount);
                var colorType = format == FormatB8G8R8A8Unorm ? SKColorType.Bgra8888 : SKColorType.Rgba8888;
                (surface, renderTarget) = _factory.CreateSurface(textureState, width, height, colorType);
                var canvas = surface.Canvas;
                if (flip)
                {
                    canvas.Translate(0, height);
                    canvas.Scale(1, -1);
                }
                canvas.DrawPicture(picture);
            }
            finally
            {
                // Submits to the queue; the surface must stay alive until then.
                _factory.EndDraw(synchronous: false);
                surface?.Dispose();
                renderTarget?.Dispose();
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

        [DllImport("SkiaUnityVulkan")]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool SkiaUnityVulkan_IsAvailable();

        [DllImport("SkiaUnityVulkan")]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool SkiaUnityVulkan_GetInstance(
            out IntPtr instance, out IntPtr physicalDevice, out IntPtr device, out IntPtr queue, out uint queueFamilyIndex);

        [DllImport("SkiaUnityVulkan")]
        static extern uint SkiaUnityVulkan_ApiVersion();

        [DllImport("SkiaUnityVulkan")]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool SkiaUnityVulkan_PrepareRenderBufferForDrawing(IntPtr renderBuffer, out PreparedImage image);

        [DllImport("SkiaUnityVulkan")]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool SkiaUnityVulkan_PrepareForDrawing(IntPtr nativeTexture, out PreparedImage image);
    }
}
