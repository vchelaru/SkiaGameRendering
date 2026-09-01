using System.Reflection;
using SkiaGameRendering.Core.OGL;

namespace SkiaGameRendering.Kni.DesktopGL
{
    /// <summary>
    /// Reflects into KNI's SDL2 GL platform (assembly "Kni.Platform", type "Sdl") to create and
    /// switch to a second GL context that shares objects with the engine's context - the same
    /// technique KNI's own ConcreteGraphicsContext.SDL.cs uses internally for its worker-thread
    /// shared context. Unlike MonoGame's equivalent (GlWrapper in SkiaGameRendering), KNI exposes
    /// these as public instance members, and Sdl lives in a separate assembly from Texture2D, so
    /// the type has to be located by assembly name instead of typeof(Texture2D).Assembly.
    /// </summary>
    internal static class KniGlWrapper
    {
        static readonly object _glInstance;
        static readonly MethodInfo _createGLContextMethod;
        static readonly MethodInfo _getCurrentContextMethod;
        static readonly MethodInfo _setAttributeMethod;
        static readonly FieldInfo _makeCurrentField;
        static readonly FieldInfo _getProcAddressField;
        static readonly object _shareWithCurrentContextAttribute;

        static KniGlWrapper()
        {
            var kniPlatformAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Kni.Platform")
                ?? throw new InvalidOperationException(
                    "Kni.Platform assembly is not loaded. Reference nkast.Kni.Platform.SDL2.GL from the game project.");

            var sdlType = kniPlatformAssembly.GetType("Sdl")
                ?? throw new InvalidOperationException("Sdl type not found in Kni.Platform.");
            var glType = sdlType.GetNestedType("GL")
                ?? throw new InvalidOperationException("Sdl.GL type not found in Kni.Platform.");

            var currentProperty = sdlType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("Sdl.Current property not found.");
            var sdlInstance = currentProperty.GetValue(null)!;

            var openGlProperty = sdlType.GetProperty("OpenGL", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Sdl.OpenGL property not found.");
            _glInstance = openGlProperty.GetValue(sdlInstance)!;

            _createGLContextMethod = glType.GetMethod("CreateGLContext", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Sdl.GL.CreateGLContext method not found.");
            _getCurrentContextMethod = glType.GetMethod("GetCurrentContext", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Sdl.GL.GetCurrentContext method not found.");
            _setAttributeMethod = glType.GetMethod("SetAttribute", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Sdl.GL.SetAttribute method not found.");
            _makeCurrentField = glType.GetField("MakeCurrent", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Sdl.GL.MakeCurrent field not found.");
            _getProcAddressField = glType.GetField("GetProcAddress", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Sdl.GL.GetProcAddress field not found.");

            var attributeEnumType = glType.GetNestedType("Attribute")
                ?? throw new InvalidOperationException("Sdl.GL.Attribute enum not found.");
            _shareWithCurrentContextAttribute = Enum.Parse(attributeEnumType, "ShareWithCurrentContext");
        }

        internal static IntPtr GetCurrentContext()
        {
            return (IntPtr)_getCurrentContextMethod.Invoke(_glInstance, null)!;
        }

        internal static IntPtr CreateGLContext(IntPtr window)
        {
            return (IntPtr)_createGLContextMethod.Invoke(_glInstance, new object[] { window })!;
        }

        internal static void ShareWithCurrentContext()
        {
            var result = (int)_setAttributeMethod.Invoke(_glInstance, new object[] { _shareWithCurrentContextAttribute, 1 })!;
            if (result < 0)
                throw new Exception("Sdl.GL.SetAttribute(ShareWithCurrentContext) failed.");
        }

        internal static void MakeCurrent(IntPtr window, IntPtr context)
        {
            var makeCurrentDelegate = (Delegate)_makeCurrentField.GetValue(_glInstance)!;
            var result = (int)makeCurrentDelegate.DynamicInvoke(window, context)!;
            if (result < 0)
                throw new Exception("Sdl.GL.MakeCurrent failed.");
        }

        internal static IntPtr GetProcAddress(string name)
        {
            var getProcAddressDelegate = (Delegate)_getProcAddressField.GetValue(_glInstance)!;
            return (IntPtr)getProcAddressDelegate.DynamicInvoke(name)!;
        }
    }

    /// <summary>
    /// Adapts <see cref="KniGlWrapper.GetProcAddress"/> to the engine-agnostic
    /// <see cref="IGlFunctionLoader"/> contract Core.OGL depends on.
    /// </summary>
    internal sealed class KniGlFunctionLoader : IGlFunctionLoader
    {
        public T Load<T>(string nativeName) where T : Delegate
        {
            var address = KniGlWrapper.GetProcAddress(nativeName);
            if (address == IntPtr.Zero)
                throw new EntryPointNotFoundException($"Entry point not found for function '{nativeName}'.");
            return System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<T>(address);
        }
    }
}
