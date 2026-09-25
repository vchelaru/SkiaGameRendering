using System.Runtime.InteropServices;

namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// Loads GL entry points via <see cref="ISharedGlContext.GetProcAddress"/> while the Skia context
    /// is current, so the function pointers are resolved against the context they will be called on.
    /// </summary>
    public sealed class SharedGlContextFunctionLoader : IGlFunctionLoader
    {
        private readonly ISharedGlContext _context;

        public SharedGlContextFunctionLoader(ISharedGlContext context)
        {
            _context = context;
        }

        public T Load<T>(string nativeName) where T : Delegate
        {
            var procAddress = _context.GetProcAddress(nativeName);
            if (procAddress == IntPtr.Zero)
                throw new InvalidOperationException($"GetProcAddress returned null for '{nativeName}'.");

            return Marshal.GetDelegateForFunctionPointer<T>(procAddress);
        }
    }
}
