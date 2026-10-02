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
    /// <c>Update</c>) and draw <see cref="Texture"/> with <see cref="PremultipliedMaterial"/>, or
    /// <see cref="PremultipliedGuiMaterial"/> in <c>OnGUI</c>.
    /// <code>
    /// var target = new SkiaUnityRenderTarget(512, 512);
    /// target.Begin();
    /// target.Canvas.DrawCircle(256, 256, 200, paint);
    /// target.End();
    /// </code>
    ///
    /// Unity renders on its own thread, so <see cref="Canvas"/> is a recording canvas: <see cref="End"/>
    /// turns the recording into an <see cref="SKPicture"/> and hands it to
    /// <see cref="SkiaUnityRenderThread"/>, which plays it back onto the texture on the render thread.
    /// Supported so far: Direct3D 11 on Windows (through ANGLE) and Metal on macOS.
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
            // The backend's per-texture state (AngleTextureState or MetalTextureState).
            internal object? TextureState;
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
            SkiaUnityBackend.PrepareOnMainThread();

            // An explicit UNorm format, so sampling returns Skia's sRGB-encoded bytes as they are and
            // the premultiplied shader does the Linear-project decode. An sRGB format also imports
            // into ANGLE, but hardware decode of premultiplied values darkens translucent pixels.
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
        static Material? _premultipliedGuiMaterial;

        public RenderTexture Texture => _texture ?? throw new ObjectDisposedException(nameof(SkiaUnityRenderTarget));

        /// <summary>
        /// A material that draws <see cref="Texture"/> correctly over other content that a camera or
        /// uGUI renders. Skia writes premultiplied alpha, and Unity's default blending expects
        /// straight alpha, which darkens edges and anything translucent; in a Linear color space
        /// project it also decodes Skia's sRGB colors, which would otherwise come out too light. Use
        /// it as a <c>RawImage</c>'s material, or copy it for a mesh (set its <c>mainTexture</c>).
        /// Its <c>_Color</c> property tints the texture. For <c>Graphics.DrawTexture</c> in
        /// <c>OnGUI</c>, use <see cref="PremultipliedGuiMaterial"/>.
        /// </summary>
        public static Material PremultipliedMaterial => _premultipliedMaterial != null
            ? _premultipliedMaterial
            : _premultipliedMaterial = CreatePremultipliedMaterial("SkiaGameRendering Premultiplied", gammaOutput: false);

        /// <summary>
        /// <see cref="PremultipliedMaterial"/> for <c>Graphics.DrawTexture</c> in <c>OnGUI</c>. IMGUI
        /// writes gamma values even in a Linear color space project, so it must skip the sRGB decode;
        /// in a Gamma project the two materials draw the same.
        /// </summary>
        public static Material PremultipliedGuiMaterial => _premultipliedGuiMaterial != null
            ? _premultipliedGuiMaterial
            : _premultipliedGuiMaterial = CreatePremultipliedMaterial("SkiaGameRendering Premultiplied GUI", gammaOutput: true);

        static Material CreatePremultipliedMaterial(string name, bool gammaOutput)
        {
            var material = new Material(
                Resources.Load<Shader>("SkiaGameRendering-Premultiplied")
                    ?? throw new InvalidOperationException("SkiaGameRendering-Premultiplied shader is missing from the package's Resources."))
            {
                name = name,
            };
            material.SetFloat("_GammaOutput", gammaOutput ? 1 : 0);
            return material;
        }

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
            // Destroy is an error outside play mode, e.g. from an [ExecuteAlways] component.
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(_texture);
            else
                UnityEngine.Object.DestroyImmediate(_texture);
            _texture = null;
        }
    }
}
