using System.Runtime.InteropServices;
using static SkiaGameRendering.Core.OGL.GlConstants;

namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// The raw OpenGL entry points needed to wrap an existing GL texture in an FBO that Skia can
    /// render into. Load once per GL context via <see cref="Load"/>.
    /// </summary>
    public sealed class GlFunctions
    {
        private const CallingConvention CallingConvention = System.Runtime.InteropServices.CallingConvention.Winapi;

        [System.Security.SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention)]
        internal delegate void GenRenderbuffersDelegate(int count, [Out] out int buffer);

        [System.Security.SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention)]
        internal delegate void BindRenderbufferDelegate(RenderbufferTarget target, int buffer);

        [System.Security.SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention)]
        internal delegate void DeleteRenderbuffersDelegate(int count, [In][Out] ref int buffer);

        [System.Security.SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention)]
        internal delegate void GenFramebuffersDelegate(int count, out int buffer);

        [System.Security.SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention)]
        internal delegate void BindFramebufferDelegate(FramebufferTarget target, int buffer);

        [System.Security.SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention)]
        internal delegate void DeleteFramebuffersDelegate(int count, ref int buffer);

        [System.Security.SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention)]
        internal delegate void InvalidateFramebufferDelegate(FramebufferTarget target, int numAttachments, FramebufferAttachment[] attachments);

        [System.Security.SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention)]
        internal delegate void FramebufferTexture2DDelegate(FramebufferTarget target, FramebufferAttachment attachment,
            TextureTarget textureTarget, int texture, int level);

        [System.Security.SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention)]
        internal delegate void FramebufferRenderbufferDelegate(FramebufferTarget target, FramebufferAttachment attachment,
            RenderbufferTarget renderBufferTarget, int buffer);

        [System.Security.SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention)]
        internal delegate void RenderbufferStorageDelegate(RenderbufferTarget target, RenderbufferStorage storage, int width, int height);

        [System.Security.SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention)]
        internal delegate FramebufferErrorCode CheckFramebufferStatusDelegate(FramebufferTarget target);

        [System.Security.SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention)]
        internal unsafe delegate void GetIntegerDelegate(int param, [Out] int* data);

        [System.Security.SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention)]
        internal delegate void FlushDelegate();

        internal GenRenderbuffersDelegate GenRenderbuffers { get; private init; } = null!;
        internal BindRenderbufferDelegate BindRenderbuffer { get; private init; } = null!;
        internal DeleteRenderbuffersDelegate DeleteRenderbuffers { get; private init; } = null!;
        internal GenFramebuffersDelegate GenFramebuffers { get; private init; } = null!;
        internal BindFramebufferDelegate BindFramebuffer { get; private init; } = null!;
        internal DeleteFramebuffersDelegate DeleteFramebuffers { get; private init; } = null!;
        /// <summary>Null when the driver lacks it (GL 4.3 / ES 3.0, so not macOS's GL 4.1).</summary>
        internal InvalidateFramebufferDelegate? InvalidateFramebuffer { get; private init; }
        internal FramebufferTexture2DDelegate FramebufferTexture2D { get; private init; } = null!;
        internal FramebufferRenderbufferDelegate FramebufferRenderbuffer { get; private init; } = null!;
        internal RenderbufferStorageDelegate RenderbufferStorage { get; private init; } = null!;
        internal CheckFramebufferStatusDelegate CheckFramebufferStatus { get; private init; } = null!;
        internal FlushDelegate Flush { get; private init; } = null!;
        private GetIntegerDelegate GetIntegerv { get; init; } = null!;

        private GlFunctions() { }

        public static GlFunctions Load(IGlFunctionLoader loader)
        {
            return new GlFunctions
            {
                GenRenderbuffers = loader.Load<GenRenderbuffersDelegate>("glGenRenderbuffers"),
                BindRenderbuffer = loader.Load<BindRenderbufferDelegate>("glBindRenderbuffer"),
                DeleteRenderbuffers = loader.Load<DeleteRenderbuffersDelegate>("glDeleteRenderbuffers"),
                GenFramebuffers = loader.Load<GenFramebuffersDelegate>("glGenFramebuffers"),
                BindFramebuffer = loader.Load<BindFramebufferDelegate>("glBindFramebuffer"),
                DeleteFramebuffers = loader.Load<DeleteFramebuffersDelegate>("glDeleteFramebuffers"),
                InvalidateFramebuffer = LoadOptional<InvalidateFramebufferDelegate>(loader, "glInvalidateFramebuffer"),
                FramebufferTexture2D = loader.Load<FramebufferTexture2DDelegate>("glFramebufferTexture2D"),
                FramebufferRenderbuffer = loader.Load<FramebufferRenderbufferDelegate>("glFramebufferRenderbuffer"),
                RenderbufferStorage = loader.Load<RenderbufferStorageDelegate>("glRenderbufferStorage"),
                CheckFramebufferStatus = loader.Load<CheckFramebufferStatusDelegate>("glCheckFramebufferStatus"),
                GetIntegerv = loader.Load<GetIntegerDelegate>("glGetIntegerv"),
                Flush = loader.Load<FlushDelegate>("glFlush"),
            };
        }

        // Loaders disagree on a missing entry point: some return null, others throw.
        private static T? LoadOptional<T>(IGlFunctionLoader loader, string nativeName) where T : Delegate
        {
            try
            {
                return loader.Load<T>(nativeName);
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
        }

        internal unsafe void GetInteger(int name, out int value)
        {
            fixed (int* ptr = &value)
            {
                GetIntegerv(name, ptr);
            }
        }
    }
}
