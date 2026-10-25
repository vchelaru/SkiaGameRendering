using System;
using System.Collections;
using NUnit.Framework;
using SkiaSharp;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SkiaGameRendering.Tests
{
    /// <summary>
    /// Draws through <see cref="SkiaRenderTarget2D"/> across domain reloads, in edit mode and in
    /// play mode (including a script reload while playing), with a draw still queued for the render
    /// thread each time the domain unloads. Each cycle checks that the texture holds the drawing and
    /// that the adapter hasn't leaked references to Unity's device: on D3D11 its ANGLE display, GL
    /// context and D3D11 state objects hold about a dozen, and on Metal Skia's GRContext retains the
    /// MTLDevice, so a leaked set shows up at once. The command to run it is in the repo's CLAUDE.md.
    /// It needs a real device, so not <c>-nographics</c>.
    ///
    /// Locals and fields don't survive a domain reload, so everything a test carries across one lives
    /// in <see cref="SessionState"/>.
    /// </summary>
    public sealed class DomainReloadTests
    {
        internal const int Size = 64;
        const int Cycles = 5;
        // Unity's own count wanders between otherwise identical cycles: on D3D11 by a few and back,
        // even with no Skia in the process (a play mode loop with no Skia call spiked +5 for one
        // cycle in 30), and by up to three on Metal, where in-flight command buffers retain the
        // device. It doesn't trend: 30 cycles with Skia stayed within 4 of each other. A leaked
        // GRContext adds two per script reload on Metal and a dozen on D3D11, so five cycles still
        // exceed this. Because one cycle can spike, the check compares the lowest counts of the
        // first and last two cycles (a leak raises every later cycle, a spike raises one).
        // OpenGL has no device count. Its GL object count grows by the same amount per play mode
        // cycle with and without Skia (Unity's own resources), so it can't be asserted on.
        internal static int AllowedGrowth => GpuProbe.IsMetal ? 5 : 2;
        const string CycleKey = "SkiaGameRendering.Tests.Cycle";
        const string ReferencesKey ="SkiaGameRendering.Tests.DeviceRefs";

        [SetUp]
        public void SetUp()
        {
            bool playModeReloads = !EditorSettings.enterPlayModeOptionsEnabled
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0;
            Assert.IsTrue(playModeReloads, "Entering play mode must reload the domain for these tests to mean anything.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
            SessionState.EraseInt(CycleKey);
            SessionState.EraseString(ReferencesKey);
        }

        [UnityTest]
        public IEnumerator DrawsAcrossScriptReloads()
        {
            while (SessionState.GetInt(CycleKey, 0) < Cycles)
            {
                SessionState.SetInt(CycleKey, SessionState.GetInt(CycleKey, 0) + 1);
                LeaveDrawQueued();
                EditorUtility.RequestScriptReload();
                yield return new WaitForDomainReload();

                DrawAndCheck();
                CheckDeviceReferences("script reload");
            }
        }

        [UnityTest]
        public IEnumerator DrawsAcrossPlayModeAndRecompileWhilePlaying()
        {
            while (SessionState.GetInt(CycleKey, 0) < Cycles)
            {
                SessionState.SetInt(CycleKey, SessionState.GetInt(CycleKey, 0) + 1);
                LeaveDrawQueued();
                yield return new EnterPlayMode(expectDomainReload: true);
                DrawAndCheck();

                LeaveDrawQueued();
                EditorUtility.RequestScriptReload();
                yield return new WaitForDomainReload();
                Assert.IsTrue(EditorApplication.isPlaying, "The script reload should not have left play mode.");
                DrawAndCheck();

                LeaveDrawQueued();
                yield return new ExitPlayMode();
                DrawAndCheck();
                CheckDeviceReferences("play mode cycle");
            }
        }

        // The first cycles are the baseline, since they include one-time costs (Unity's first play mode).
        static void CheckDeviceReferences(string cycleName)
        {
            int cycle = SessionState.GetInt(CycleKey, 0);
            int references = GpuProbe.DeviceRefCount();
            Debug.Log($"Device references after {cycleName} {cycle}: {references}");
            var history = SessionState.GetString(ReferencesKey, "") + references + ",";
            SessionState.SetString(ReferencesKey, history);
            if (cycle < Cycles)
                return;

            var counts = Array.ConvertAll(history.TrimEnd(',').Split(','), int.Parse);
            int first = Math.Min(counts[0], counts[1]);
            int last = Math.Min(counts[counts.Length - 2], counts[counts.Length - 1]);
            Assert.LessOrEqual(last - first, AllowedGrowth,
                $"Device references grew over the {cycleName}s: {string.Join(", ", counts)}.");
        }

        // Red over the top half, transparent below, so the check also catches a flipped texture.
        internal static void Draw(SkiaRenderTarget2D target)
        {
            using var paint = new SKPaint { Color = SKColors.Red };
            target.Begin();
            target.Canvas.DrawRect(0, 0, Size, Size / 2, paint);
            target.End();
        }

        static void DrawAndCheck()
        {
            using var target = new SkiaRenderTarget2D(Size, Size);
            Draw(target);

            var readback = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = target.Texture;
            readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            RenderTexture.active = previous;
            // Texture2D rows count up from the bottom.
            Color32 top = readback.GetPixel(Size / 2, Size - 1 - Size / 4);
            Color32 bottom = readback.GetPixel(Size / 2, Size / 4);
            UnityEngine.Object.DestroyImmediate(readback);

            Assert.That(top.r > 200 && top.g < 50 && top.b < 50 && top.a > 200, $"top pixel {top} should be red");
            Assert.That(bottom.a < 10, $"bottom pixel {bottom} should be transparent");
        }

        static void LeaveDrawQueued()
        {
            using var target = new SkiaRenderTarget2D(Size, Size);
            Draw(target);
        }
    }
}
