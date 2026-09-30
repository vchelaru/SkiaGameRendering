using System;
using System.Collections;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SkiaSharp;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SkiaGameRendering.Unity.Tests
{
    /// <summary>
    /// Draws through <see cref="SkiaUnityRenderTarget"/> across domain reloads, in edit mode and in
    /// play mode (including a script reload while playing), with a draw still queued for the render
    /// thread each time the domain unloads. Each cycle checks that the texture holds the drawing and
    /// that the adapter hasn't leaked references to Unity's D3D11 device: its ANGLE display, GL
    /// context and D3D11 state objects hold about a dozen, so a leaked set shows up at once.
    /// The command to run it is in the repo's CLAUDE.md. It needs a D3D11 device, so not
    /// <c>-nographics</c>.
    ///
    /// Locals and fields don't survive a domain reload, so everything a test carries across one lives
    /// in <see cref="SessionState"/>.
    /// </summary>
    public sealed class DomainReloadTests
    {
        const int Size = 64;
        const int Cycles = 5;
        // Unity's own count wanders by one between otherwise identical cycles.
        const int AllowedGrowth = 2;
        const string CycleKey = "SkiaGameRendering.Tests.Cycle";
        const string BaselineKey = "SkiaGameRendering.Tests.DeviceRefs";

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
            SessionState.EraseInt(BaselineKey);
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

        // The first cycle is the baseline, since it includes one-time costs (Unity's first play mode).
        static void CheckDeviceReferences(string cycleName)
        {
            int cycle = SessionState.GetInt(CycleKey, 0);
            int references = DeviceRefCount();
            Debug.Log($"D3D11 device references after {cycleName} {cycle}: {references}");
            if (cycle == 1)
                SessionState.SetInt(BaselineKey, references);
            else
                Assert.LessOrEqual(references - SessionState.GetInt(BaselineKey, 0), AllowedGrowth,
                    $"D3D11 device references grew between {cycleName} 1 and {cycle}.");
        }

        // Red over the top half, transparent below, so the check also catches a flipped texture.
        static void Draw(SkiaUnityRenderTarget target)
        {
            using var paint = new SKPaint { Color = SKColors.Red };
            target.Begin();
            target.Canvas.DrawRect(0, 0, Size, Size / 2, paint);
            target.End();
        }

        static void DrawAndCheck()
        {
            using var target = new SkiaUnityRenderTarget(Size, Size);
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
            using var target = new SkiaUnityRenderTarget(Size, Size);
            Draw(target);
        }

        // Unity's ID3D11Device, reached through a texture: ID3D11DeviceChild::GetDevice is vtable
        // slot 3, and AddRefs the device, which the Release below gives back.
        static int DeviceRefCount()
        {
            var texture = new RenderTexture(4, 4, 0);
            texture.Create();
            try
            {
                var nativeTexture = texture.GetNativeTexturePtr();
                var vtable = Marshal.ReadIntPtr(nativeTexture);
                var getDevice = Marshal.GetDelegateForFunctionPointer<GetDeviceFn>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
                getDevice(nativeTexture, out var device);
                return Marshal.Release(device);
            }
            finally
            {
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void GetDeviceFn(IntPtr self, out IntPtr device);
    }
}
