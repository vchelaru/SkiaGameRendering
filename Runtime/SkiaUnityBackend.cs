#nullable enable
using System;
using SkiaSharp;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkiaGameRendering.Unity
{
    /// <summary>
    /// The graphics-API-specific half of <see cref="SkiaUnityRenderThread"/>: one Core factory wrapped
    /// around Unity's device, and the per-texture state it needs. Render thread only, except
    /// <see cref="PrepareOnMainThread"/>.
    /// </summary>
    internal abstract class SkiaUnityBackend : IDisposable
    {
        /// <summary>
        /// Main thread. Throws unless one of the backends below can run on this device, and loads
        /// anything a backend needs loaded before the render thread uses it.
        /// </summary>
        internal static void PrepareOnMainThread()
        {
            var api = SystemInfo.graphicsDeviceType;
            if (api == GraphicsDeviceType.Direct3D11)
                return;
            // The Metal plugin is only built for macOS so far; iOS needs it as a static library.
            if (api == GraphicsDeviceType.Metal
                && (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer))
            {
                MetalUnityBackend.LoadPlugin();
                return;
            }
            throw new NotSupportedException(
                $"SkiaGameRendering.Unity supports Direct3D11 on Windows and Metal on macOS so far, not {api} on {Application.platform}.");
        }

        /// <summary>Creates the backend for Unity's current device, the first time anything draws.</summary>
        internal static SkiaUnityBackend Create(IntPtr nativeTexture) =>
            SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal
                ? new MetalUnityBackend()
                : new AngleUnityBackend(nativeTexture);

        /// <summary>Plays <paramref name="picture"/> back onto the target's texture, creating its surface on the first draw.</summary>
        internal abstract void Draw(SkiaUnityRenderTarget.RenderState target, SKPicture picture);

        /// <summary>Clears the surface and draws the picture onto it the way Unity expects.</summary>
        protected static void PlayBack(SKSurface surface, SKPicture picture, int height)
        {
            var canvas = surface.Canvas;
            canvas.Clear();
            // Unity treats row 0 of a texture as its bottom row, and Skia writes its top row
            // there, so the recorded picture is played back flipped.
            canvas.Save();
            canvas.Translate(0, height);
            canvas.Scale(1, -1);
            canvas.DrawPicture(picture);
            canvas.Restore();
        }

        /// <summary>Releases the target's surface and texture state.</summary>
        internal abstract void DisposeTarget(SkiaUnityRenderTarget.RenderState target);

        public abstract void Dispose();
    }
}
