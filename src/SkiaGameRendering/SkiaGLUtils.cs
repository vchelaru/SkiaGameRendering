using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.OGL;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SkiaGameRendering
{
    internal static class SdlGlConstants
    {
        public const int SDL_GL_SHARE_WITH_CURRENT_CONTEXT = 22;
        public const int GL_TEXTURE_BINDING_2D = 0x8069;
    }

    internal static class GlWrapper
    {
        private const CallingConvention callingConvention = CallingConvention.Winapi;

        // Native function attribute ported from MonoGame source
        [AttributeUsage(AttributeTargets.Delegate)]
        internal sealed class NativeFunctionWrapper : Attribute { }

        static readonly FieldInfo _winHandleField;
        static readonly PropertyInfo _contextProperty;

        static readonly Delegate _sdl_GL_GetCurrentContext;
        static readonly Delegate _sdl_GL_CreateContext;
        static readonly Delegate _sdl_GL_SetAttribute;
        static readonly Delegate _makeCurrent;
        static readonly Delegate _getProcAddress;

        // Every type and member below is named by a string literal passed straight to
        // Type.GetType/GetField/GetProperty. The trimmer resolves literals like these at publish time
        // and keeps exactly those members, which is what makes this reflection NativeAOT-safe. Routing
        // a name through a variable or helper parameter loses that and brings back IL2xxx warnings.
        static GlWrapper()
        {
            const string MonoGameAssembly = ", MonoGame.Framework";

            var sdlGlType = Type.GetType("Sdl+GL" + MonoGameAssembly)
                ?? throw new InvalidOperationException("Sdl.GL type not found in MonoGame.Framework.");
            var graphicsContextType = Type.GetType("MonoGame.OpenGL.GraphicsContext" + MonoGameAssembly)
                ?? throw new InvalidOperationException("MonoGame.OpenGL.GraphicsContext type not found in MonoGame.Framework.");

            _winHandleField = graphicsContextType.GetField("_winHandle", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GraphicsContext._winHandle field not found.");
            _contextProperty = typeof(GraphicsDevice).GetProperty("Context", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GraphicsDevice.Context property not found.");

            const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
            const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
            _sdl_GL_GetCurrentContext = ReadDelegate(sdlGlType.GetField("SDL_GL_GetCurrentContext", NonPublicStatic), "SDL_GL_GetCurrentContext");
            _sdl_GL_CreateContext = ReadDelegate(sdlGlType.GetField("SDL_GL_CreateContext", NonPublicStatic), "SDL_GL_CreateContext");
            _sdl_GL_SetAttribute = ReadDelegate(sdlGlType.GetField("SDL_GL_SetAttribute", NonPublicStatic), "SDL_GL_SetAttribute");
            _makeCurrent = ReadDelegate(sdlGlType.GetField("MakeCurrent", PublicStatic), "MakeCurrent");
            _getProcAddress = ReadDelegate(sdlGlType.GetField("GetProcAddress", PublicStatic), "GetProcAddress");
        }

        /// <summary>
        /// Reads one of Sdl.GL's static delegate fields, so callers can call through it with
        /// DynamicInvoke without referencing MonoGame's internal delegate type at compile time.
        /// </summary>
        static Delegate ReadDelegate(FieldInfo? field, string fieldName)
        {
            if (field == null)
                throw new InvalidOperationException($"Sdl.GL.{fieldName} field not found.");
            return (Delegate?)field.GetValue(null)
                ?? throw new InvalidOperationException($"Sdl.GL.{fieldName} field is null.");
        }

        internal static IntPtr GetMgWindowId(GraphicsDevice graphicsDevice)
        {
            var context = _contextProperty.GetValue(graphicsDevice)
                ?? throw new InvalidOperationException("GraphicsDevice.Context is null.");
            return (IntPtr)(_winHandleField.GetValue(context)
                ?? throw new InvalidOperationException("GraphicsContext._winHandle is null."));
        }

        internal static IntPtr SDL_GL_GetCurrentContext()
        {
            return (IntPtr)_sdl_GL_GetCurrentContext.DynamicInvoke()!;
        }

        internal static IntPtr SDL_GL_CreateContext(IntPtr window)
        {
            return (IntPtr)_sdl_GL_CreateContext.DynamicInvoke(window)!;
        }

        internal static int SDL_GL_SetAttribute(int attribute, int value)
        {
            return (int)_sdl_GL_SetAttribute.DynamicInvoke(attribute, value)!;
        }

        internal static int MakeCurrent(IntPtr window, IntPtr context)
        {
            return (int)_makeCurrent.DynamicInvoke(window, context)!;
        }

        /// <summary>
        /// Resolves a GL entry point through SDL and binds it to <typeparamref name="T"/>. The
        /// generic Marshal call is visible to the AOT compiler for each delegate type, unlike
        /// closing MonoGame's own GL.LoadFunction&lt;T&gt; over T at runtime.
        /// </summary>
        internal static T LoadFunction<T>(string nativeMethodName) where T : Delegate
        {
            var address = (IntPtr)_getProcAddress.DynamicInvoke(nativeMethodName)!;
            // Null for a function the driver lacks (e.g. glInvalidateFramebuffer on macOS's GL 4.1),
            // matching MonoGame's own GL.LoadFunction with throwIfNotFound: false.
            if (address == IntPtr.Zero)
                return null!;
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        /// <summary>
        /// OpenGL functions wrapper for the MonoGame context.
        /// </summary>
        internal static class MgGlFunctions
        {
            [System.Security.SuppressUnmanagedCodeSecurity()]
            [UnmanagedFunctionPointer(callingConvention)]
            [NativeFunctionWrapper]
            internal unsafe delegate void GetIntegerDelegate(int param, [Out] int* data);
            internal static GetIntegerDelegate GetIntegerv = null!;

            internal static void LoadFunctions()
            {
                GetIntegerv = LoadFunction<GetIntegerDelegate>("glGetIntegerv");
            }

            internal unsafe static void GetInteger(int name, out int value)
            {
                fixed (int* ptr = &value)
                {
                    GetIntegerv(name, ptr);
                }
            }
        }
    }

    /// <summary>
    /// Adapts SDL's GL function loading (<see cref="GlWrapper.LoadFunction{T}"/>)
    /// to the engine-agnostic <see cref="IGlFunctionLoader"/> contract Core.OGL depends on.
    /// </summary>
    internal sealed class MonoGameGlFunctionLoader : IGlFunctionLoader
    {
        public T Load<T>(string nativeName) where T : Delegate => GlWrapper.LoadFunction<T>(nativeName);
    }
}
