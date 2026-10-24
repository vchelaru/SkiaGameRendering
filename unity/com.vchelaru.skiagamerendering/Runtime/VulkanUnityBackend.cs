#nullable enable
using System;
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
        PreparedImage _prepared;

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
            if (!SkiaUnityVulkan_PrepareForDrawing(target.NativeTexture, out _prepared))
                throw new InvalidOperationException("The SkiaUnityVulkan plugin could not prepare the texture for drawing.");
        }

        internal override void Draw(SkiaRenderTarget2D.RenderState target, SKPicture picture)
        {
            if (_prepared.Image == 0)
                throw new InvalidOperationException("The texture was not prepared for drawing.");

            _factory.BeginDraw();
            try
            {
                // Unity can recreate a texture's image; the surface is only good for the one it wrapped.
                if (target.Surface != null && ((VkTextureState)target.TextureState!).VkImage != _prepared.Image)
                    DisposeTarget(target);

                if (target.Surface == null)
                {
                    var format = (uint)_prepared.Format;
                    var textureState = _factory.CreateTextureState(
                        _prepared.Image, format, (uint)_prepared.Layout, _prepared.Usage, (uint)_prepared.Tiling,
                        sampleCount: (uint)_prepared.Samples, levelCount: (uint)_prepared.MipCount);
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
                _prepared = default;
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
        static extern bool SkiaUnityVulkan_PrepareForDrawing(IntPtr nativeTexture, out PreparedImage image);
    }
}
