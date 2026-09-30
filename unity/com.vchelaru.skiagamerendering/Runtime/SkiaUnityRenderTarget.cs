#nullable enable
using System;
using SkiaSharp;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace SkiaGameRendering.Unity
{
    /// <summary>
    /// A <see cref="RenderTexture"/> that SkiaSharp draws into on the GPU, with the Begin/Canvas/End
    /// shape of the other engine adapters. Call Begin/End from the main thread (e.g. in
    /// <c>Update</c>) and draw <see cref="Texture"/> with <see cref="PremultipliedMaterial"/>.
    /// <code>
    /// var target = new SkiaUnityRenderTarget(512, 512);
    /// target.Begin();
    /// target.Canvas.DrawCircle(256, 256, 200, paint);
    /// target.End();
    /// </code>
    ///
    /// Unity renders on its own thread, so <see cref="Canvas"/> is a recording canvas: <see cref="End"/>
    /// turns the recording into an <see cref="SKPicture"/> and hands it to
    /// <see cref="SkiaUnityRenderThread"/>, which plays it back onto the texture through ANGLE on the
    /// render thread. Only Windows on Direct3D 11 is supported so far.
    /// </summary>
    public sealed class SkiaUnityRenderTarget : IDisposable
    {
        // Everything the render thread owns for this target. The main thread only reads the fields
        // set in the constructor.
        internal sealed class RenderState
        {
            internal IntPtr NativeTexture;
            internal int Width;
            internal int Height;
            internal Core.ANGLE.AngleTextureState? TextureState;
            internal SKSurface? Surface;
            internal GRBackendRenderTarget? BackendRenderTarget;
        }

        readonly RenderState _state;
        RenderTexture? _texture;
        SKPictureRecorder? _recorder;
        SKCanvas? _canvas;

        public SkiaUnityRenderTarget(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                throw new NotSupportedException(
                    $"SkiaGameRendering.Unity only supports Direct3D11 so far, not {SystemInfo.graphicsDeviceType}. " +
                    "Set Player Settings > Other Settings > Graphics APIs for Windows to Direct3D11.");

            // An explicit UNorm format: ANGLE imports the texture as a plain RGBA pbuffer, and in a
            // Linear color space project Unity would otherwise create a typeless texture with sRGB views.
            _texture = new RenderTexture(width, height, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None)
            {
                name = nameof(SkiaUnityRenderTarget),
            };
            _texture.Create();

            _state = new RenderState
            {
                NativeTexture = _texture.GetNativeTexturePtr(),
                Width = width,
                Height = height,
            };
        }

        static Material? _premultipliedMaterial;

        public RenderTexture Texture => _texture ?? throw new ObjectDisposedException(nameof(SkiaUnityRenderTarget));

        /// <summary>
        /// A material that draws <see cref="Texture"/> correctly over other content. Skia writes
        /// premultiplied alpha, and Unity's default blending expects straight alpha, which darkens
        /// edges and anything translucent. Use it with <c>Graphics.DrawTexture</c>, as a
        /// <c>RawImage</c>'s material, or copy it for a mesh (set its <c>mainTexture</c>). Its
        /// <c>_Color</c> property tints the texture.
        /// </summary>
        public static Material PremultipliedMaterial => _premultipliedMaterial != null
            ? _premultipliedMaterial
            : _premultipliedMaterial = new Material(
                Resources.Load<Shader>("SkiaGameRendering-Premultiplied")
                    ?? throw new InvalidOperationException("SkiaGameRendering-Premultiplied shader is missing from the package's Resources."))
            {
                name = "SkiaGameRendering Premultiplied",
            };

        /// <summary>The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>.</summary>
        public SKCanvas Canvas => _canvas ?? throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        public void Begin()
        {
            if (_texture == null)
                throw new ObjectDisposedException(nameof(SkiaUnityRenderTarget));
            if (_canvas != null)
                throw new InvalidOperationException("Begin cannot be called again until End has been called.");

            _recorder ??= new SKPictureRecorder();
            _canvas = _recorder.BeginRecording(new SKRect(0, 0, _state.Width, _state.Height));
        }

        /// <summary>
        /// Queues the recorded drawing for the render thread. The texture holds it once Unity's
        /// render thread reaches this point, which is before anything queued after it samples the texture.
        /// </summary>
        public void End()
        {
            if (_canvas == null)
                throw new InvalidOperationException("Begin must be called before calling End.");

            _canvas = null;
            var picture = _recorder!.EndRecording();
            SkiaUnityRenderThread.Issue(new SkiaUnityRenderThread.Command { Target = _state, Picture = picture });
        }

        public void Dispose()
        {
            if (_texture == null)
                return;
            if (_canvas != null)
                throw new InvalidOperationException("Dispose cannot be called between Begin and End; call End first.");

            // Queued before the texture's own release, so the render thread lets go of it first.
            SkiaUnityRenderThread.Issue(new SkiaUnityRenderThread.Command { Target = _state });
            _recorder?.Dispose();
            _recorder = null;
            _texture.Release();
            UnityEngine.Object.Destroy(_texture);
            _texture = null;
        }
    }
}
