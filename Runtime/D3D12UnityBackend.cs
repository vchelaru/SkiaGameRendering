#nullable enable
using System;
using System.Runtime.InteropServices;
using SkiaGameRendering.Core.D3D12;
using SkiaSharp;

namespace SkiaGameRendering.Unity
{
    /// <summary>
    /// Direct3D 12 (Windows): Skia draws with Unity's own ID3D12Device and submits to Unity's own
    /// command queue, so the queue orders Skia's writes after Unity's earlier work and before its later
    /// work. Unity's C# API exposes neither, nor the adapter, so the SkiaUnityD3D12 native plugin
    /// (unity/native/SkiaUnityD3D12) reads them from Unity's IUnityGraphicsD3D12v7 interface, and also
    /// asks Unity to put the texture in the render target state before each draw.
    /// </summary>
    internal sealed class D3D12UnityBackend : SkiaUnityBackend
    {
        readonly D3D12SkiaSurfaceFactory _factory = new D3D12SkiaSurfaceFactory();

        /// <summary>
        /// Main thread. Unity refuses to load a native plugin from any other thread, and only calls its
        /// UnityPluginLoad, which is where the plugin gets Unity's D3D12 interface, when it loads.
        /// </summary>
        internal static void LoadPlugin() => SkiaUnityD3D12_Device();

        internal D3D12UnityBackend()
        {
            var device = SkiaUnityD3D12_Device();
            if (device == IntPtr.Zero)
                throw new InvalidOperationException(
                    "The SkiaUnityD3D12 plugin has no D3D12 device: Unity didn't load it, or isn't running on Direct3D 12.");
            var adapter = SkiaUnityD3D12_Adapter();
            if (adapter == IntPtr.Zero)
                throw new InvalidOperationException("The SkiaUnityD3D12 plugin could not find the adapter of Unity's D3D12 device.");
            _factory.InitializeFromNative(adapter, device, SkiaUnityD3D12_CommandQueue());
        }

        internal override void Draw(SkiaUnityRenderTarget.RenderState target, SKPicture picture)
        {
            // Unity moves the texture to the render target state on the queue before Skia's commands.
            if (!SkiaUnityD3D12_PrepareForDrawing(target.NativeTexture))
                throw new InvalidOperationException("The SkiaUnityD3D12 plugin could not prepare the texture for drawing.");

            _factory.BeginDraw();
            try
            {
                if (target.Surface == null)
                {
                    var textureState = _factory.CreateTextureState(
                        target.NativeTexture, D3D12Constants.FormatR8G8B8A8Unorm, D3D12Constants.ResourceStateRenderTarget);
                    target.TextureState = textureState;
                    (target.Surface, target.BackendRenderTarget) = _factory.CreateSurface(
                        textureState, target.Width, target.Height, SKColorType.Rgba8888);
                }

                PlayBack(target.Surface, picture, target.Height);
            }
            finally
            {
                // Not synchronous: Unity's own reads of the texture are submitted after this on the same queue.
                _factory.EndDraw(synchronous: false);
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

        [DllImport("SkiaUnityD3D12")]
        static extern IntPtr SkiaUnityD3D12_Device();

        [DllImport("SkiaUnityD3D12")]
        static extern IntPtr SkiaUnityD3D12_CommandQueue();

        [DllImport("SkiaUnityD3D12")]
        static extern IntPtr SkiaUnityD3D12_Adapter();

        [DllImport("SkiaUnityD3D12")]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool SkiaUnityD3D12_PrepareForDrawing(IntPtr resource);
    }
}
