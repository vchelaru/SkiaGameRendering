using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Utilities;
using SkiaGameRendering.Core.D3D12;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// SkiaBackend for MonoGame's native WindowsDX12 platform (<c>MonoGame.Framework.Native</c>
    /// 3.8.6-preview.2+). Windows only.
    ///
    /// Adapter over <see cref="D3D12SkiaSurfaceFactory"/> (see that class for how the D3D12 interop
    /// itself works). Skia draws on the very <c>ID3D12Device</c>/<c>ID3D12CommandQueue</c> MonoGame
    /// renders with, read from <c>GraphicsDevice.GetNativeHandles()</c>, into a resource this backend
    /// creates and hands back to MonoGame through <c>RenderTarget2D.FromNativeHandle()</c>. No
    /// reflection and no CPU readback.
    ///
    /// MAINTENANCE NOTES:
    /// - <b>Resource states.</b> MonoGame wraps the resource believing it is in
    ///   <c>RENDER_TARGET</c>, and from then on tracks the state itself: sampling it moves it to
    ///   <c>PIXEL_SHADER_RESOURCE | NON_PIXEL_SHADER_RESOURCE</c> and it stays there. Skia always
    ///   leaves a surface in <c>RENDER_TARGET</c> and cannot report otherwise (see
    ///   <see cref="D3D12SkiaSurfaceFactory"/>), so every draw after the first is bracketed by a
    ///   shader-resource-to-render-target transition before and a render-target-to-shader-resource one
    ///   after, which keeps MonoGame's bookkeeping and the real state equal. The first draw needs
    ///   neither, since both sides start at <c>RENDER_TARGET</c>. This assumes the host samples the
    ///   texture between two draws, which <see cref="SkiaRenderTarget2D.End"/> does; a caller that
    ///   starts a second pass without ever drawing the texture breaks the assumption.
    /// - <b>Release is deferred.</b> A destroyed target's <c>ID3D12Resource</c> is released a few
    ///   draws after its <see cref="RenderTarget2D"/> is disposed, because MonoGame may still have
    ///   frames in flight that sample it.
    /// - MonoGame does not expose its queue lock, so none is passed: Skia, this backend and MonoGame
    ///   must all submit from the thread that runs <c>Draw</c>.
    /// </summary>
    public class SkiaDx12Backend : SkiaBackend
    {
        // The draws a released resource waits out before its ID3D12Resource reference is dropped.
        const int ReleaseDelayDraws = 4;
        const uint ShaderResourceState = D3D12Constants.ResourceStateAllShaderResource;

        readonly D3D12SkiaSurfaceFactory _factory = new();
        readonly ConditionalWeakTable<Texture2D, TargetResource> _resources = new();
        readonly List<(IntPtr resource, long drawIndex)> _pendingReleases = new();
        D3D12ResourceTransitioner? _transitioner;
        IntPtr _device;
        long _drawIndex;
        TargetResource? _current;
        TargetResource? _handBack;

        public override GRContext GRContext => _factory.GRContext;

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            var handles = graphicsDevice.GetNativeHandles();
            if (handles.Backend != GraphicsBackend.DirectX12)
                throw new InvalidOperationException(
                    $"SkiaDx12Backend requires MonoGame's WindowsDX12 platform; the GraphicsDevice reported {handles.Backend}.");

            _device = handles.LogicalDevice;
            _factory.InitializeFromNative(handles.PhysicalDevice, handles.LogicalDevice, handles.Queue);
            _transitioner = new D3D12ResourceTransitioner(handles.LogicalDevice, handles.Queue);
        }

        internal override void BeginDraw()
        {
            _factory.BeginDraw();
            ReleaseFinishedResources();
        }

        internal override void EndDraw()
        {
            // No CPU wait: MonoGame submits to this same queue afterward, so its sampling is queue-ordered behind Skia's draw.
            _factory.EndDraw(synchronous: false);

            if (_handBack is { } target)
            {
                _handBack = null;
                _transitioner!.Transition(target.Resource, D3D12Constants.ResourceStateRenderTarget, ShaderResourceState);
            }
        }

        internal override SkiaTarget CreateTarget(int width, int height, SKColorType colorType)
        {
            // Skia's pixel layout has to match the DXGI format the resource was created with.
            if (colorType is not (SKColorType.Rgba8888 or SKColorType.Bgra8888))
                throw new NotSupportedException(
                    $"SkiaDx12Backend supports SKColorType.Rgba8888 and SKColorType.Bgra8888, not {colorType}.");

            return base.CreateTarget(width, height, colorType);
        }

        internal override Texture2D CreateTexture(int width, int height, SurfaceFormat format)
        {
            var dxgiFormat = format switch
            {
                SurfaceFormat.Color => D3D12Constants.FormatR8G8B8A8Unorm,
                SurfaceFormat.Bgra32 => D3D12Constants.FormatB8G8R8A8Unorm,
                _ => throw new NotSupportedException($"SkiaDx12Backend does not support SurfaceFormat.{format}."),
            };

            var resource = D3D12SkiaSurfaceFactory.CreateRenderTargetResource(_device, width, height, dxgiFormat);
            try
            {
                // MonoGame never transitions a wrapped resource before it first samples it, so it has to
                // start where MonoGame leaves it after sampling.
                _transitioner!.Transition(resource, D3D12Constants.ResourceStateRenderTarget, ShaderResourceState);
                var texture = RenderTarget2D.FromNativeHandle(GraphicsDevice, resource, width, height, format);
                _resources.Add(texture, new TargetResource(resource, dxgiFormat));
                texture.Disposing += (_, _) => _pendingReleases.Add((resource, _drawIndex));
                return texture;
            }
            catch
            {
                D3D12SkiaSurfaceFactory.ReleaseResource(resource);
                throw;
            }
        }

        internal override object CaptureTextureHandle(Texture2D texture) =>
            _resources.TryGetValue(texture, out var resource)
                ? resource
                : throw new InvalidOperationException("The texture was not created by this backend.");

        internal override (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            object textureHandle, Texture2D texture, int width, int height, SKColorType colorType, out object renderState)
        {
            var target = (TargetResource)textureHandle;
            var state = _factory.CreateTextureState(target.Resource, target.DxgiFormat, D3D12Constants.ResourceStateRenderTarget);
            renderState = target;
            return _factory.CreateSurface(state, width, height, colorType);
        }

        internal override void BindForDrawing(object renderState)
        {
            var target = (TargetResource)renderState;
            _transitioner!.Transition(target.Resource, ShaderResourceState, D3D12Constants.ResourceStateRenderTarget);
            _current = target;
        }

        internal override void UnbindAfterDrawing()
        {
            if (_current is { } target)
            {
                _current = null;
                _handBack = target;
            }
        }

        // The resource itself is released when its RenderTarget2D is disposed (see CreateTexture).
        internal override void DisposeRenderState(object renderState) { }

        void ReleaseFinishedResources()
        {
            _drawIndex++;
            for (int i = _pendingReleases.Count - 1; i >= 0; i--)
            {
                if (_drawIndex - _pendingReleases[i].drawIndex >= ReleaseDelayDraws)
                {
                    D3D12SkiaSurfaceFactory.ReleaseResource(_pendingReleases[i].resource);
                    _pendingReleases.RemoveAt(i);
                }
            }
        }

        public override void Dispose()
        {
            _transitioner?.Dispose();
            _transitioner = null;
            _factory.Dispose();

            foreach (var (resource, _) in _pendingReleases)
                D3D12SkiaSurfaceFactory.ReleaseResource(resource);
            _pendingReleases.Clear();
        }

        sealed class TargetResource(IntPtr resource, uint dxgiFormat)
        {
            internal IntPtr Resource { get; } = resource;
            internal uint DxgiFormat { get; } = dxgiFormat;
        }
    }
}
