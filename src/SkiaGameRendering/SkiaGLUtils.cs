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

        static FieldInfo _winHandleField;
        static PropertyInfo _contextProperty;

        static object _sdl_GL_GetCurrentContextValue;
        static MethodInfo _sdl_GL_GetCurrentContextMethod;

        static object _sdl_GL_CreateContextValue;
        static MethodInfo _sdl_GL_CreateContextMethod;

        static object _sdl_GL_SetAttributeValue;
        static MethodInfo _sdl_GL_SetAttributeMethod;

        static object _makeCurrentValue;
        static MethodInfo _makeCurrentMethod;

        static MethodInfo _loadFunctionMethod;

        static GlWrapper()
        {
            var monoGameAssembly = typeof(Texture2D).Assembly;
            var sdlGlType = monoGameAssembly.GetType("Sdl").GetNestedType("GL");
            var mgGlType = monoGameAssembly.GetType("MonoGame.OpenGL.GL");

            _winHandleField = monoGameAssembly.GetType("MonoGame.OpenGL.GraphicsContext").GetField("_winHandle", BindingFlags.NonPublic | BindingFlags.Instance);
            _contextProperty = monoGameAssembly.GetType("Microsoft.Xna.Framework.Graphics.GraphicsDevice").GetProperty("Context", BindingFlags.Instance | BindingFlags.NonPublic);

            var sdl_GL_GetCurrentContextField = sdlGlType.GetField("SDL_GL_GetCurrentContext", BindingFlags.NonPublic | BindingFlags.Static);
            _sdl_GL_GetCurrentContextValue = sdl_GL_GetCurrentContextField.GetValue(null);
            _sdl_GL_GetCurrentContextMethod = _sdl_GL_GetCurrentContextValue.GetType().GetMethod("Invoke");

            var sdl_GL_CreateContextField = sdlGlType.GetField("SDL_GL_CreateContext", BindingFlags.NonPublic | BindingFlags.Static);
            _sdl_GL_CreateContextValue = sdl_GL_CreateContextField.GetValue(null);
            _sdl_GL_CreateContextMethod = _sdl_GL_CreateContextValue.GetType().GetMethod("Invoke");

            var sdl_GL_SetAttributeField = sdlGlType.GetField("SDL_GL_SetAttribute", BindingFlags.NonPublic | BindingFlags.Static);
            _sdl_GL_SetAttributeValue = sdl_GL_SetAttributeField.GetValue(null);
            _sdl_GL_SetAttributeMethod = _sdl_GL_SetAttributeValue.GetType().GetMethod("Invoke");

            var makeCurrentField = sdlGlType.GetField("MakeCurrent", BindingFlags.Public | BindingFlags.Static);
            _makeCurrentValue = makeCurrentField.GetValue(null);
            _makeCurrentMethod = _makeCurrentValue.GetType().GetMethod("Invoke");

            _loadFunctionMethod = mgGlType.GetMethod("LoadFunction", BindingFlags.NonPublic | BindingFlags.Static);
        }

        internal static IntPtr GetMgWindowId(GraphicsDevice graphicsDevice)
        {
            var context = _contextProperty.GetValue(graphicsDevice);
            return (IntPtr)_winHandleField.GetValue(context);
        }

        internal static IntPtr SDL_GL_GetCurrentContext()
        {
            return (IntPtr)_sdl_GL_GetCurrentContextMethod.Invoke(_sdl_GL_GetCurrentContextValue, null);
        }

        internal static IntPtr SDL_GL_CreateContext(IntPtr window)
        {
            return (IntPtr)_sdl_GL_CreateContextMethod.Invoke(_sdl_GL_CreateContextValue, new object[] { window });
        }

        internal static int SDL_GL_SetAttribute(int attribute, int value)
        {
            return (int)_sdl_GL_SetAttributeMethod.Invoke(_sdl_GL_SetAttributeValue, new object[] { attribute, value });
        }

        // This allocates a little, we can make it a little quieter by reusing this object array:
        static object[] makeCurrentArray = new object[2];
        internal static int MakeCurrent(IntPtr window, IntPtr context)
        {
            makeCurrentArray[0] = window;
            makeCurrentArray[1] = context;
            return (int)_makeCurrentMethod.Invoke(_makeCurrentValue, makeCurrentArray);
        }

        internal static T LoadFunction<T>(string nativeMethodName) where T : Delegate
        {
            var method = _loadFunctionMethod.MakeGenericMethod(new Type[] { typeof(T) });
            return (T)method.Invoke(null, new object[] { nativeMethodName, false });
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
            internal static GetIntegerDelegate GetIntegerv;

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
    /// Adapts MonoGame's reflection-based native GL function loading (<see cref="GlWrapper.LoadFunction{T}"/>)
    /// to the engine-agnostic <see cref="IGlFunctionLoader"/> contract Core.OGL depends on.
    /// </summary>
    internal sealed class MonoGameGlFunctionLoader : IGlFunctionLoader
    {
        public T Load<T>(string nativeName) where T : Delegate => GlWrapper.LoadFunction<T>(nativeName);
    }
}
