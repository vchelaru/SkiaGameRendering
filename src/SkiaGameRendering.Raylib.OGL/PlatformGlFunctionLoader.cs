using System.Runtime.InteropServices;
using SkiaGameRendering.Core.OGL;

namespace SkiaGameRendering.Raylib.OGL
{
    /// <summary>
    /// Loads GL entry points via <see cref="IPlatformGlContext.GetProcAddress"/> while the
    /// Skia-dedicated context is current, so the function pointers are resolved against the context
    /// we'll actually call them on. Works for any <see cref="IPlatformGlContext"/> implementation
    /// (<see cref="Wgl"/> on Windows, <see cref="Glx"/> on Linux).
    /// </summary>
    internal sealed class PlatformGlFunctionLoader : IGlFunctionLoader
    {
        private readonly IPlatformGlContext _platform;

        public PlatformGlFunctionLoader(IPlatformGlContext platform)
        {
            _platform = platform;
        }

        public T Load<T>(string nativeName) where T : Delegate
        {
            var procAddress = _platform.GetProcAddress(nativeName);
            if (procAddress == IntPtr.Zero)
                throw new InvalidOperationException($"GetProcAddress returned null for '{nativeName}'.");

            return Marshal.GetDelegateForFunctionPointer<T>(procAddress);
        }
    }
}
