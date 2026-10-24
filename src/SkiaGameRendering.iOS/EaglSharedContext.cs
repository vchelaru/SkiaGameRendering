using System.Runtime.InteropServices;
using OpenGLES;
using SkiaGameRendering.Core.OGL;

namespace SkiaGameRendering
{
    /// <summary>
    /// The iOS <see cref="ISharedGlContext"/>: a second <see cref="EAGLContext"/> in the host context's
    /// sharegroup, the EAGL equivalent of <see cref="EglSharedContext"/>. Textures and renderbuffers are
    /// shared across a sharegroup; FBOs are not. MonoGame makes its own background-loading context the
    /// same way. EAGL has no default framebuffer, so the context needs no drawable.
    /// </summary>
    // Apple deprecated OpenGL ES in iOS 12, and it is what MonoGame iOS renders with.
#pragma warning disable CA1422
    internal sealed class EaglSharedContext : ISharedGlContext
    {
        internal const string OpenGLESLibrary = "/System/Library/Frameworks/OpenGLES.framework/OpenGLES";

        private EAGLContext? _skiaContext;
        private EAGLContext? _previousContext;
        private IntPtr _openGles;

        /// <summary>The Skia context's API, e.g. <see cref="EAGLRenderingAPI.OpenGLES3"/>.</summary>
        public EAGLRenderingAPI Api { get; private set; }

        public void CreateSharedContext(IntPtr windowHandle)
        {
            var host = EAGLContext.CurrentContext
                ?? throw new InvalidOperationException("No EAGLContext is current on this thread.");

            Api = host.API;
            _skiaContext = new EAGLContext(host.API, host.ShareGroup);
            _openGles = NativeLibrary.Load(OpenGLESLibrary);
        }

        public void MakeSkiaContextCurrent()
        {
            _previousContext = EAGLContext.CurrentContext;
            if (!EAGLContext.SetCurrentContext(_skiaContext))
                throw new InvalidOperationException("EAGLContext.SetCurrentContext(Skia) failed.");
        }

        public void RestoreHostContext()
        {
            var previous = _previousContext;
            _previousContext = null;
            if (!EAGLContext.SetCurrentContext(previous))
                throw new InvalidOperationException("EAGLContext.SetCurrentContext(host) failed.");
        }

        /// <summary>iOS has no GetProcAddress: every GL ES entry point is a plain export of the OpenGLES framework.</summary>
        public IntPtr GetProcAddress(string name) =>
            NativeLibrary.TryGetExport(_openGles, name, out var address) ? address : IntPtr.Zero;

        /// <summary>Destroys the Skia context. It must not be current.</summary>
        public void Dispose()
        {
            _skiaContext?.Dispose();
            _skiaContext = null;
            if (_openGles != IntPtr.Zero)
            {
                NativeLibrary.Free(_openGles);
                _openGles = IntPtr.Zero;
            }
        }
    }
#pragma warning restore CA1422
}
