#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using AOT;
using SkiaSharp;
using UnityEngine;

namespace SkiaGameRendering
{
    /// <summary>
    /// Runs every Skia call on Unity's render thread, through the <see cref="SkiaUnityBackend"/> for
    /// Unity's graphics API. Unity owns its device context (D3D11) or command buffers (Metal) from that
    /// thread, so the main thread only queues work here and issues one
    /// <see cref="GL.IssuePluginEvent(IntPtr, int)"/> per queued command.
    ///
    /// MAINTENANCE NOTES:
    /// - The callback is a managed static method handed to Unity as a native function pointer, with
    ///   no native plugin. <see cref="MonoPInvokeCallbackAttribute"/> is what lets IL2CPP generate the
    ///   reverse P/Invoke stub for it, and the delegate is kept in a static field so it is never
    ///   collected while Unity holds its pointer.
    /// - Commands and plugin events are 1:1, so each callback dequeues exactly one command.
    /// - A domain reload (every script reload in the Editor, and entering play mode unless disabled)
    ///   resets this class but not the native side: Unity would still call the old callback
    ///   pointer for any event queued before the reload, and the old domain's backend would leak
    ///   (for D3D11, its ANGLE display, GL context and D3D11 state objects, each holding a reference
    ///   to Unity's device).
    ///   So before a reload, <see cref="ReleaseAll"/> releases all of it on the render thread and
    ///   waits for that, which also drains every earlier event, and this domain issues no more.
    /// </summary>
    internal static class SkiaUnityRenderThread
    {
        internal sealed class Command
        {
            // null means "release every target and the factory" (see ReleaseAll).
            internal SkiaRenderTarget2D.RenderState? Target;
            // null means "dispose the target's render-thread state".
            internal SKPicture? Picture;
            // A direct draw (SkiaDirectCanvas): Picture is played onto whatever render target is bound
            // when the command runs, and Target is null.
            internal bool Direct;
            internal int Width;
            internal int Height;
            // Whether row 0 of the bound target is Unity's bottom row, so the picture is played back flipped.
            internal bool Flip;
            // Any texture on Unity's device, for a backend that finds the device through one.
            internal IntPtr DeviceTexture;
        }

        delegate void RenderEventDelegate(int eventId);

        static readonly RenderEventDelegate Callback = OnRenderEvent;
        static readonly IntPtr CallbackPtr = Marshal.GetFunctionPointerForDelegate(Callback);
        static readonly ConcurrentQueue<Command> Commands = new ConcurrentQueue<Command>();

        static SkiaUnityBackend? _backend;
        // Targets with render-thread state, so ReleaseAll can reach the ones never disposed.
        static readonly HashSet<SkiaRenderTarget2D.RenderState> LiveTargets = new HashSet<SkiaRenderTarget2D.RenderState>();
        static readonly ManualResetEventSlim Released = new ManualResetEventSlim();
        // Main thread only.
        static bool _anyIssued;
        static bool _releasing;

#if UNITY_EDITOR
        static SkiaUnityRenderThread()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
        }
#endif

        internal static void Issue(Command command)
        {
            // After ReleaseAll, nothing this domain queues may reach the render thread, and the
            // target it names has already been released.
            if (_releasing)
            {
                command.Picture?.Dispose();
                return;
            }
            _anyIssued = true;
            Commands.Enqueue(command);
            if (command.Direct)
            {
                GL.IssuePluginEvent(CallbackPtr, SkiaUnityBackend.DrawEventId);
                return;
            }
            // A draw needs its prepare event first when the backend asks for one. Events run in
            // order, so the prepare event finds its draw's command still at the head of the queue.
            if (SkiaUnityBackend.UsesPrepareEvent && command.Target != null && command.Picture != null)
                GL.IssuePluginEvent(CallbackPtr, SkiaUnityBackend.PrepareEventId);
            GL.IssuePluginEvent(CallbackPtr, SkiaUnityBackend.DrawEventId);
        }

        /// <summary>
        /// Releases every target's render-thread state and the backend on the render thread,
        /// and blocks until that has run. Called before a domain reload; see the class notes.
        /// </summary>
        internal static void ReleaseAll()
        {
            if (_releasing)
                return;
            bool anyIssued = _anyIssued;
            if (anyIssued)
                Issue(new Command());
            _releasing = true;
            if (anyIssued && !Released.Wait(TimeSpan.FromSeconds(10)))
                Debug.LogError("SkiaGameRendering: the render thread did not release Skia's resources before the domain reload.");
        }

        [MonoPInvokeCallback(typeof(RenderEventDelegate))]
        static void OnRenderEvent(int eventId)
        {
            if (eventId == SkiaUnityBackend.PrepareEventId && SkiaUnityBackend.UsesPrepareEvent)
            {
                PrepareOnRenderThread();
                return;
            }
            if (!Commands.TryDequeue(out var command))
                return;

            try
            {
                if (command.Direct)
                    DrawDirect(command);
                else if (command.Target == null)
                    ReleaseOnRenderThread();
                else if (command.Picture == null)
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
                if (command.Target == null && !command.Direct)
                    Released.Set();
            }
        }

        static void PrepareOnRenderThread()
        {
            if (!Commands.TryPeek(out var command) || command.Target == null || command.Picture == null)
                return;
            try
            {
                _backend ??= SkiaUnityBackend.Create(command.Target.NativeTexture);
                _backend.Prepare(command.Target);
            }
            catch (Exception ex)
            {
                // Throwing back into Unity's native render thread would crash the player.
                Debug.LogException(ex);
            }
        }

        static void ReleaseOnRenderThread()
        {
            foreach (var target in new List<SkiaRenderTarget2D.RenderState>(LiveTargets))
                DisposeTarget(target);
            _backend?.Dispose();
            _backend = null;
        }

        static void Draw(SkiaRenderTarget2D.RenderState target, SKPicture picture)
        {
            _backend ??= SkiaUnityBackend.Create(target.NativeTexture);
            LiveTargets.Add(target);
            _backend.Draw(target, picture);
        }

        static void DrawDirect(Command command)
        {
            _backend ??= SkiaUnityBackend.Create(command.DeviceTexture);
            _backend.DrawDirect(command.Picture!, command.Width, command.Height, command.Flip);
        }

        static void DisposeTarget(SkiaRenderTarget2D.RenderState target)
        {
            LiveTargets.Remove(target);
            _backend?.DisposeTarget(target);
        }
    }
}
