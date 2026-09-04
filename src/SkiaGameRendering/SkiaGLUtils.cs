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

        static readonly object _sdl_GL_GetCurrentContextValue;
        static readonly MethodInfo _sdl_GL_GetCurrentContextMethod;

        static readonly object _sdl_GL_CreateContextValue;
        static readonly MethodInfo _sdl_GL_CreateContextMethod;

        static readonly object _sdl_GL_SetAttributeValue;
        static readonly MethodInfo _sdl_GL_SetAttributeMethod;

        static readonly object _makeCurrentValue;
        static readonly MethodInfo _makeCurrentMethod;

        static readonly MethodInfo _loadFunctionMethod;

        static GlWrapper()
        {
            var monoGameAssembly = typeof(Texture2D).Assembly;

            var sdlType = monoGameAssembly.GetType("Sdl")
                ?? throw new InvalidOperationException("Sdl type not found in MonoGame.Framework.");
            var sdlGlType = sdlType.GetNestedType("GL")
                ?? throw new InvalidOperationException("Sdl.GL type not found in MonoGame.Framework.");
            var mgGlType = monoGameAssembly.GetType("MonoGame.OpenGL.GL")
                ?? throw new InvalidOperationException("MonoGame.OpenGL.GL type not found in MonoGame.Framework.");
            var graphicsContextType = monoGameAssembly.GetType("MonoGame.OpenGL.GraphicsContext")
                ?? throw new InvalidOperationException("MonoGame.OpenGL.GraphicsContext type not found in MonoGame.Framework.");
            var graphicsDeviceType = monoGameAssembly.GetType("Microsoft.Xna.Framework.Graphics.GraphicsDevice")
                ?? throw new InvalidOperationException("GraphicsDevice type not found in MonoGame.Framework.");

            _winHandleField = graphicsContextType.GetField("_winHandle", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GraphicsContext._winHandle field not found.");
            _contextProperty = graphicsDeviceType.GetProperty("Context", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GraphicsDevice.Context property not found.");

            (_sdl_GL_GetCurrentContextValue, _sdl_GL_GetCurrentContextMethod) =
                GetStaticDelegate(sdlGlType, "SDL_GL_GetCurrentContext", BindingFlags.NonPublic | BindingFlags.Static);
            (_sdl_GL_CreateContextValue, _sdl_GL_CreateContextMethod) =
                GetStaticDelegate(sdlGlType, "SDL_GL_CreateContext", BindingFlags.NonPublic | BindingFlags.Static);
            (_sdl_GL_SetAttributeValue, _sdl_GL_SetAttributeMethod) =
                GetStaticDelegate(sdlGlType, "SDL_GL_SetAttribute", BindingFlags.NonPublic | BindingFlags.Static);
            (_makeCurrentValue, _makeCurrentMethod) =
                GetStaticDelegate(sdlGlType, "MakeCurrent", BindingFlags.Public | BindingFlags.Static);

            _loadFunctionMethod = mgGlType.GetMethod("LoadFunction", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("MonoGame.OpenGL.GL.LoadFunction method not found.");
        }

        /// <summary>
        /// Reads a static delegate field and returns it alongside its Invoke method, so callers can
        /// call through it without referencing MonoGame's internal delegate type at compile time.
        /// </summary>
        static (object Value, MethodInfo Invoke) GetStaticDelegate(Type type, string fieldName, BindingFlags flags)
        {
            var field = type.GetField(fieldName, flags)
                ?? throw new InvalidOperationException($"{type.FullName}.{fieldName} field not found.");
            var value = field.GetValue(null)
                ?? throw new InvalidOperationException($"{type.FullName}.{fieldName} field is null.");
            var invoke = value.GetType().GetMethod("Invoke")
                ?? throw new InvalidOperationException($"{type.FullName}.{fieldName} delegate has no Invoke method.");
            return (value, invoke);
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
            return (IntPtr)_sdl_GL_GetCurrentContextMethod.Invoke(_sdl_GL_GetCurrentContextValue, null)!;
        }

        internal static IntPtr SDL_GL_CreateContext(IntPtr window)
        {
            return (IntPtr)_sdl_GL_CreateContextMethod.Invoke(_sdl_GL_CreateContextValue, new object[] { window })!;
        }

        internal static int SDL_GL_SetAttribute(int attribute, int value)
        {
            return (int)_sdl_GL_SetAttributeMethod.Invoke(_sdl_GL_SetAttributeValue, new object[] { attribute, value })!;
        }

        // This allocates a little, we can make it a little quieter by reusing this object array:
        static object[] makeCurrentArray = new object[2];
        internal static int MakeCurrent(IntPtr window, IntPtr context)
        {
            makeCurrentArray[0] = window;
            makeCurrentArray[1] = context;
            return (int)_makeCurrentMethod.Invoke(_makeCurrentValue, makeCurrentArray)!;
        }

        internal static T LoadFunction<T>(string nativeMethodName) where T : Delegate
        {
            var method = _loadFunctionMethod.MakeGenericMethod(new Type[] { typeof(T) });
            return (T)method.Invoke(null, new object[] { nativeMethodName, false })!;
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
    /// Adapts MonoGame's reflection-based native GL function loading (<see cref="GlWrapper.LoadFunction{T}"/>)
    /// to the engine-agnostic <see cref="IGlFunctionLoader"/> contract Core.OGL depends on.
    /// </summary>
    internal sealed class MonoGameGlFunctionLoader : IGlFunctionLoader
    {
        public T Load<T>(string nativeName) where T : Delegate => GlWrapper.LoadFunction<T>(nativeName);
    }
}
