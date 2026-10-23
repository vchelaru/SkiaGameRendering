using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.D3D12;
using SkiaSharp;

namespace SkiaGameRendering
{
    // SkiaDirectCanvas on MonoGame WindowsDX12. MonoGame has no API for the resource behind its current target or
    // for submitting what it has recorded, so this reads both out of its native objects (MonoGameD3D12Native) and
    // cuts MonoGame's command list in two around Skia's pass, with no CPU readback:
    //
    //   Begin  - find the bound target's ID3D12Resource, then close MonoGame's open list and execute it, so
    //            everything MonoGame drew so far is ahead of Skia's work in the queue.
    //   End    - Skia's draws are submitted behind it; MonoGame's list is reopened on a fresh allocator and the state
    //            MonoGame believes is bound (root signature, descriptor heaps, render targets, and via dirty flags
    //            everything else) is put back, so MonoGame's later draws are recorded behind Skia's.
    //
    // MonoGame keeps the target in RENDER_TARGET while it is bound, and Skia leaves it there, so no barrier is needed.
    public partial class SkiaDx12Backend
    {
        MonoGameD3D12Native? _native;
        D3D12CommandListSplitter? _splitter;
        string? _directCanvasUnsupportedReason;

        internal override bool SupportsDirectCanvas => _native != null;

        internal override NotSupportedException DirectCanvasNotSupported() =>
            _directCanvasUnsupportedReason == null
                ? base.DirectCanvasNotSupported()
                : new NotSupportedException($"SkiaDx12Backend cannot draw straight into MonoGame's current target: {_directCanvasUnsupportedReason}");

        void InitializeDirectCanvas(GraphicsDevice graphicsDevice, NativeGraphicsHandles handles)
        {
            DisposeDirectCanvas();

            _native = MonoGameD3D12Native.TryCreate(graphicsDevice, handles, out _directCanvasUnsupportedReason);
            if (_native != null)
            {
                _splitter = new D3D12CommandListSplitter(handles.LogicalDevice, handles.Queue);
                graphicsDevice.DeviceResetting += OnDeviceResetting;
            }
        }

        // A pass on the back buffer leaves Skia holding that swap chain buffer until the GPU has finished with it and
        // Skia next sweeps its finished command lists, and DXGI refuses to resize the swap chain while anything holds
        // a buffer (MonoGame then aborts). The event fires before MonoGame resizes, so wait for Skia's work and let
        // it sweep.
        void OnDeviceResetting(object? sender, EventArgs e) =>
            _factory.GRContext.Flush(submit: true, synchronous: true);

        void DisposeDirectCanvas()
        {
            if (_native != null)
                GraphicsDevice.DeviceResetting -= OnDeviceResetting;
            _splitter?.Dispose();
            _splitter = null;
            _native = null;
        }

        internal override SKCanvas BeginDirectDraw(out object pass)
        {
            if (_native == null)
                throw DirectCanvasNotSupported();
            if (!_native.IsRecording)
                throw new InvalidOperationException(
                    "SkiaDirectCanvas can only draw while MonoGame is recording a frame, between Game.BeginDraw and Game.EndDraw.");

            // Everything that can refuse comes first, so a refused Begin leaves MonoGame's list untouched.
            var (resource, state) = _native.CurrentTarget();
            var (width, height) = ExpectedTargetSize();
            var target = D3D12SkiaSurfaceFactory.DescribeDirectTarget(resource, width, height);
            if (state != D3D12Constants.ResourceStateRenderTarget)
                throw new InvalidOperationException(
                    $"MonoGame has its current target in D3D12 resource state 0x{state:X}, not RENDER_TARGET.");

            var list = _native.CommandList;
            _splitter!.Submit(list);
            try
            {
                var canvas = _factory.BeginDirectDraw(target, out var corePass);
                pass = new DirectPass(corePass, list);
                return canvas;
            }
            catch
            {
                ResumeMonoGame(list);
                throw;
            }
        }

        internal override void EndDirectDraw(object pass)
        {
            var direct = (DirectPass)pass;
            try
            {
                // No CPU wait: MonoGame's list, reopened below, runs behind this on the same queue.
                _factory.EndDirectDraw(direct.CorePass, synchronous: false);
            }
            finally
            {
                ResumeMonoGame(direct.List);
            }
        }

        void ResumeMonoGame(IntPtr list)
        {
            _splitter!.Reopen(list, _native!.Frame);
            _native.BindListState();
            _native.MarkStateDirty();
        }

        // The size MonoGame says its current target is, which DescribeDirectTarget checks the resource against.
        (int width, int height) ExpectedTargetSize()
        {
            var targets = GraphicsDevice.GetRenderTargets();
            if (targets.Length == 0)
            {
                var parameters = GraphicsDevice.PresentationParameters;
                return (parameters.BackBufferWidth, parameters.BackBufferHeight);
            }
            if (targets.Length > 1)
                throw new NotSupportedException($"SkiaDirectCanvas draws into a single render target, not {targets.Length}.");

            return targets[0].RenderTarget is RenderTarget2D renderTarget
                ? (renderTarget.Width, renderTarget.Height)
                : throw new NotSupportedException("SkiaDirectCanvas draws into the back buffer or a RenderTarget2D.");
        }

        sealed class DirectPass(D3D12DirectPass corePass, IntPtr list)
        {
            internal D3D12DirectPass CorePass { get; } = corePass;
            internal IntPtr List { get; } = list;
        }
    }
}
