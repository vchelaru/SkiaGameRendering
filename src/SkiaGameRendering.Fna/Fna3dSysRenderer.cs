using System.Runtime.InteropServices;

namespace SkiaGameRendering
{
    /// <summary>
    /// FNA3D's driver, as reported by <see cref="Fna3dSysRenderer.Get"/>. Values mirror
    /// <c>FNA3D_SysRendererTypeEXT</c> in FNA3D's <c>include/FNA3D_SysRenderer.h</c>.
    /// </summary>
    internal enum Fna3dSysRendererType
    {
        OpenGL = 0,
        Vulkan = 1, // Removed from FNA3D; SDL_GPU took its place.
        D3D11 = 2,
        Metal = 3, // Removed from FNA3D.
        SdlGpu = 4,
    }

    /// <summary>
    /// <c>FNA3D_SysRendererEXT</c>: 4-byte <c>version</c>, 4-byte <c>rendererType</c>, then a
    /// 64-byte union of per-driver handles. Only the D3D11 and OpenGL members are spelled out; the
    /// explicit size keeps the native write inside the struct regardless of which driver filled it.
    /// Offsets assume 8-byte pointers, same as <c>Core.ANGLE</c>'s win-x64/win-arm64 runtimes.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8 + 64)]
    internal struct Fna3dSysRendererInfo
    {
        [FieldOffset(0)] public uint Version;
        [FieldOffset(4)] public Fna3dSysRendererType RendererType;

        /// <summary><c>ID3D11Device*</c> when <see cref="RendererType"/> is <see cref="Fna3dSysRendererType.D3D11"/>.</summary>
        [FieldOffset(8)] public IntPtr D3D11Device;
        /// <summary><c>ID3D11DeviceContext*</c> (the immediate context) when <see cref="RendererType"/> is <see cref="Fna3dSysRendererType.D3D11"/>.</summary>
        [FieldOffset(16)] public IntPtr D3D11Context;

        /// <summary><c>SDL_GLContext</c> when <see cref="RendererType"/> is <see cref="Fna3dSysRendererType.OpenGL"/>.</summary>
        [FieldOffset(8)] public IntPtr OpenGLContext;
    }

    /// <summary>
    /// Binding for the one FNA3D extension this library needs: <c>FNA3D_GetSysRendererEXT</c>,
    /// which hands out the native device behind FNA's <c>GraphicsDevice</c>. FNA's own C# binding
    /// (<c>src/Graphics/FNA3D.cs</c>) doesn't declare it, so this is a second P/Invoke into the same
    /// <c>FNA3D</c> native library FNA already loaded.
    ///
    /// Only FNA3D's D3D11 and OpenGL drivers fill the struct in. The SDL_GPU driver, which FNA3D
    /// picks by default on SDL3 builds, leaves everything but <c>rendererType</c> zeroed (its
    /// implementation is a TODO in <c>FNA3D_Driver_SDL.c</c>, and SDL3 itself exposes no native
    /// handles from an <c>SDL_GPUDevice</c>). Callers check <see cref="Fna3dSysRendererInfo.RendererType"/>
    /// and tell the user to set <c>FNA3D_FORCE_DRIVER</c> when it's the wrong one.
    /// </summary>
    internal static class Fna3dSysRenderer
    {
        const string NativeLibName = "FNA3D";

        /// <summary>The <c>FNA3D_SYSRENDERER_VERSION_EXT</c> this binding was written against.</summary>
        const uint VersionExt = 0;

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        static extern void FNA3D_GetSysRendererEXT(IntPtr device, ref Fna3dSysRendererInfo sysrenderer);

        /// <param name="fna3dDevice">The <c>FNA3D_Device*</c> behind FNA's <c>GraphicsDevice</c> (its <c>GLDevice</c> field).</param>
        internal static Fna3dSysRendererInfo Get(IntPtr fna3dDevice)
        {
            if (fna3dDevice == IntPtr.Zero)
                throw new ArgumentException("The FNA3D device pointer is null.", nameof(fna3dDevice));

            var info = new Fna3dSysRendererInfo { Version = VersionExt };
            FNA3D_GetSysRendererEXT(fna3dDevice, ref info);
            return info;
        }
    }
}
