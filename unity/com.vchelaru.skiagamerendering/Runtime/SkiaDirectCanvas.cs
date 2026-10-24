#nullable enable
using System;
using SkiaSharp;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkiaGameRendering
{
    /// <summary>
    /// Draws with SkiaSharp straight into the render target Unity is rendering to when <see cref="End"/>
    /// reaches the render thread: the back buffer or the active camera's target texture, with no
    /// intermediate texture. Same shape as <see cref="SkiaRenderTarget2D"/>, minus the size and the texture.
    /// Call <see cref="Begin"/> and <see cref="End"/> from the main thread while a camera renders, for
    /// example in a <c>MonoBehaviour.OnPostRender</c> on the camera (built-in render pipeline). Skia then
    /// draws after everything the camera has drawn so far and before anything drawn later, including a
    /// camera with a higher depth.
    /// <code>
    /// void OnPostRender()
    /// {
    ///     _skia.Begin();
    ///     _skia.Canvas.DrawCircle(100, 100, 50, paint);
    ///     _skia.End();
    /// }
    /// </code>
    /// Like <see cref="SkiaRenderTarget2D"/>, <see cref="Canvas"/> records, and the drawing is played back
    /// on the render thread by <see cref="SkiaUnityRenderThread"/>. <see cref="Begin"/> does not clear;
    /// Skia draws on top of what is there. Coordinates are target pixels with the origin at the top left.
    /// The target's size is read when <see cref="Begin"/> runs: the active <see cref="RenderTexture"/>, or the
    /// screen. Supported on Direct3D 11, Direct3D 12 and OpenGL (Windows) so far; the constructor throws <see cref="NotSupportedException"/>
    /// elsewhere.
    /// </summary>
    public sealed class SkiaDirectCanvas : IDisposable
    {
        SKPictureRecorder? _recorder;
        SKCanvas? _canvas;
        int _width;
        int _height;
        bool _flip;
        IntPtr _renderBuffer;
        bool _isDisposed;

        public SkiaDirectCanvas()
        {
            SkiaUnityBackend.PrepareOnMainThread();
            if (!SkiaUnityBackend.SupportsDirectCanvas)
                throw new NotSupportedException(
                    $"SkiaDirectCanvas is not supported on {SystemInfo.graphicsDeviceType} yet; use SkiaRenderTarget2D.");
        }

        /// <summary>The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>.</summary>
        public SKCanvas Canvas => _canvas ?? throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        /// <summary>Starts recording for the target Unity is rendering to right now.</summary>
        public void Begin()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(SkiaDirectCanvas));
            if (_canvas != null)
                throw new InvalidOperationException("Begin cannot be called again until End has been called.");

            var active = RenderTexture.active;
            _width = active != null ? active.width : Screen.width;
            _height = active != null ? active.height : Screen.height;
            // A texture's row 0 is Unity's bottom row; the back buffer's is the top.
            _flip = active != null;
            _renderBuffer = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
                ? Graphics.activeColorBuffer.GetNativeRenderBufferPtr()
                : IntPtr.Zero;
            _recorder ??= new SKPictureRecorder();
            _canvas = _recorder.BeginRecording(new SKRect(0, 0, _width, _height));
        }

        /// <summary>
        /// Queues the recording for the render thread, which draws it onto the target that is bound at this
        /// point in Unity's rendering. A failure there is logged, since it happens off the main thread.
        /// </summary>
        public void End()
        {
            if (_canvas == null)
                throw new InvalidOperationException("Begin must be called before calling End.");

            _canvas = null;
            var picture = _recorder!.EndRecording();
            SkiaUnityRenderThread.Issue(new SkiaUnityRenderThread.Command
            {
                Direct = true,
                Picture = picture,
                Width = _width,
                Height = _height,
                Flip = _flip,
                // Texture2D.whiteTexture is always there, and D3D11 finds its device through any texture.
                DeviceTexture = Texture2D.whiteTexture.GetNativeTexturePtr(),
                // Only Direct3D 12 needs it: it has no "currently bound target" to ask the device for.
                RenderBuffer = _renderBuffer,
            });
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            if (_canvas != null)
                throw new InvalidOperationException("Dispose cannot be called between Begin and End; call End first.");

            _isDisposed = true;
            _recorder?.Dispose();
            _recorder = null;
        }
    }
}
