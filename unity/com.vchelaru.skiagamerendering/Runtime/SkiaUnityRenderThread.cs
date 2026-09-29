#nullable enable
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using AOT;
using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;
using UnityEngine;

namespace SkiaGameRendering.Unity
{
    /// <summary>
    /// Runs every Skia/ANGLE call on Unity's render thread. The D3D11 immediate context is not
    /// thread-safe and Unity owns it from that thread, so the main thread only queues work here and
    /// issues one <see cref="GL.IssuePluginEvent(IntPtr, int)"/> per queued command.
    ///
    /// MAINTENANCE NOTES:
    /// - The callback is a managed static method handed to Unity as a native function pointer, with
    ///   no native plugin. <see cref="MonoPInvokeCallbackAttribute"/> is what lets IL2CPP generate the
    ///   reverse P/Invoke stub for it, and the delegate is kept in a static field so it is never
    ///   collected while Unity holds its pointer.
    /// - Commands and plugin events are 1:1, so each callback dequeues exactly one command.
    /// </summary>
    internal static class SkiaUnityRenderThread
    {
        internal sealed class Command
        {
            internal SkiaUnityRenderTarget.RenderState Target = null!;
            // null means "dispose the target's render-thread state".
            internal SKPicture? Picture;
        }

        delegate void RenderEventDelegate(int eventId);

        static readonly RenderEventDelegate Callback = OnRenderEvent;
        static readonly IntPtr CallbackPtr = Marshal.GetFunctionPointerForDelegate(Callback);
        static readonly ConcurrentQueue<Command> Commands = new ConcurrentQueue<Command>();

        static AngleSkiaSurfaceFactory? _factory;

        internal static void Issue(Command command)
        {
            Commands.Enqueue(command);
            GL.IssuePluginEvent(CallbackPtr, 0);
        }

        [MonoPInvokeCallback(typeof(RenderEventDelegate))]
        static void OnRenderEvent(int eventId)
        {
            if (!Commands.TryDequeue(out var command))
                return;

            try
            {
                if (command.Picture == null)
                    DisposeTarget(command.Target);
                else
                    Draw(command.Target, command.Picture);
            }
            catch (Exception ex)
            {
                // Throwing back into Unity's native render thread would crash the player.
                Debug.LogException(ex);
            }
            finally
            {
                command.Picture?.Dispose();
            }
        }

        static void Draw(SkiaUnityRenderTarget.RenderState target, SKPicture picture)
        {
            var factory = EnsureFactory(target.NativeTexture);

            factory.BeginDraw();
            try
            {
                if (target.Surface == null)
                {
                    target.TextureState = factory.CreateTextureState(target.NativeTexture);
                    (target.Surface, target.BackendRenderTarget) = factory.CreateSurface(
                        target.TextureState, target.Width, target.Height, SKColorType.Rgba8888);
                }
                else
                {
                    factory.BindForDrawing(target.TextureState!);
                }

                var canvas = target.Surface.Canvas;
                canvas.Clear();
                // Unity treats row 0 of a texture as its bottom row, and Skia writes its top row
                // there, so the recorded picture is played back flipped.
                canvas.Save();
                canvas.Translate(0, target.Height);
                canvas.Scale(1, -1);
                canvas.DrawPicture(picture);
                canvas.Restore();
                target.Surface.Flush();
                factory.UnbindAfterDrawing();
            }
            finally
            {
                factory.EndDraw();
            }
        }

        static void DisposeTarget(SkiaUnityRenderTarget.RenderState target)
        {
            target.Surface?.Dispose();
            target.Surface = null;
            target.BackendRenderTarget?.Dispose();
            target.BackendRenderTarget = null;
            if (target.TextureState != null)
            {
                _factory!.DisposeRenderState(target.TextureState);
                target.TextureState = null;
            }
        }

        // Unity's C# API has no accessor for its ID3D11Device, so the first texture drawn to
        // supplies it: every D3D11 resource can report the device that owns it.
        static AngleSkiaSurfaceFactory EnsureFactory(IntPtr nativeTexture)
        {
            if (_factory != null)
                return _factory;

            var device = D3D11Com.GetDevice(nativeTexture);
            var context = D3D11Com.GetImmediateContext(device);
            try
            {
                var factory = new AngleSkiaSurfaceFactory();
                factory.InitializeFromNative(device, context);
                return _factory = factory;
            }
            finally
            {
                // Both come back AddRef'd, and Unity keeps them alive for the life of the app.
                D3D11Com.Release(context);
                D3D11Com.Release(device);
            }
        }
    }
}
