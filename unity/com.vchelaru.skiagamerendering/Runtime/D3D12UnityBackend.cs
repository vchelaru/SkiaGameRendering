#nullable enable
using System;
using System.Runtime.InteropServices;
using SkiaGameRendering.Core.D3D12;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// Direct3D 12 (Windows): Skia draws with Unity's own ID3D12Device and submits to Unity's own
    /// command queue, so the queue orders Skia's writes after Unity's earlier work and before its later
    /// work. Unity's C# API exposes neither, nor the adapter, so the SkiaUnityD3D12 native plugin
    /// (unity/native/SkiaUnityD3D12) reads them from Unity's IUnityGraphicsD3D12v7 interface, and also
    /// asks Unity to put the texture in the render target state before each draw. A direct draw
    /// (<see cref="SkiaDirectCanvas"/>) runs in a plugin event the plugin configures so Unity submits what it
    /// has recorded first; the target comes from the <c>UnityRenderBuffer</c> the main thread read.
    /// </summary>
    internal sealed class D3D12UnityBackend : SkiaUnityBackend
    {
        readonly D3D12SkiaSurfaceFactory _factory = new D3D12SkiaSurfaceFactory();

        /// <summary>The plugin event id <see cref="SkiaDirectCanvas"/> draws with; the plugin configures it on load.</summary>
        internal const int DirectEventId = 0x534B03;

        /// <summary>The plugin event id <see cref="SkiaRenderTarget2D"/> draws and releases run on; the plugin configures it like <see cref="DirectEventId"/>, minus the bound target.</summary>
        internal const int DrawEventId = 0x534B04;

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

        internal override void Draw(SkiaRenderTarget2D.RenderState target, SKPicture picture)
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

        internal override void DrawDirect(SKPicture picture, int width, int height, bool flip, IntPtr renderBuffer)
        {
            var resource = SkiaUnityD3D12_ResourceFromRenderBuffer(renderBuffer);
            if (resource == IntPtr.Zero)
                throw new InvalidOperationException("Unity has no Direct3D 12 resource for the render target it is rendering to.");
            var info = new ulong[7];
            if (!SkiaUnityD3D12_DescribeResource(resource, info))
                throw new InvalidOperationException("The SkiaUnityD3D12 plugin could not read the render target's description.");
            // Validated before anything is queued, so a target Skia cannot draw into throws untouched.
            var target = D3D12SkiaSurfaceFactory.DescribeDirectTarget(resource, new D3D12ResourceDescription
            {
                IsTexture2D = info[0] != 0,
                AllowsRenderTarget = info[1] != 0,
                SampleCount = (uint)info[2],
                DepthOrArraySize = (int)info[3],
                Format = (uint)info[4],
                Width = info[5],
                Height = (uint)info[6],
            }, width, height);

            // The event is configured (see the plugin) so Unity has already submitted everything it recorded
            // before this call, and the bound target is in RENDER_TARGET.
            var canvas = _factory.BeginDirectDraw(target, out var pass);
            try
            {
                if (flip)
                {
                    canvas.Translate(0, height);
                    canvas.Scale(1, -1);
                }
                canvas.DrawPicture(picture);
            }
            finally
            {
                _factory.EndDirectDraw(pass, synchronous: false);
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

        [DllImport("SkiaUnityD3D12")]
        static extern IntPtr SkiaUnityD3D12_Device();

        [DllImport("SkiaUnityD3D12")]
        static extern IntPtr SkiaUnityD3D12_CommandQueue();

        [DllImport("SkiaUnityD3D12")]
        static extern IntPtr SkiaUnityD3D12_Adapter();

        [DllImport("SkiaUnityD3D12")]
        static extern IntPtr SkiaUnityD3D12_ResourceFromRenderBuffer(IntPtr renderBuffer);

        [DllImport("SkiaUnityD3D12")]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool SkiaUnityD3D12_DescribeResource(IntPtr resource, [Out] ulong[] out7);

        [DllImport("SkiaUnityD3D12")]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool SkiaUnityD3D12_PrepareForDrawing(IntPtr resource);
    }
}
