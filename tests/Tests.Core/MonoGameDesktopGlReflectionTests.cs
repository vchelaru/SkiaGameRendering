using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Tests.Shared;
using Xunit;

namespace Tests.Core;

/// <summary>
/// Pins the private MonoGame DesktopGL members <c>GlWrapper</c> reaches by string
/// (src/SkiaGameRendering/SkiaGLUtils.cs). A MonoGame bump that renames any of them leaves
/// <c>dotnet build</c> green and breaks the backend the first time a game creates a
/// SkiaRenderTarget2D, because it is all resolved at runtime.
///
/// The names are duplicated from the backend on purpose: it resolves them inside a static
/// constructor that also dereferences SDL delegate values, which needs a real window. Change a name
/// in one place and it has to change in the other.
///
/// Lives in Tests.Core because that project already references src/SkiaGameRendering, which is the
/// MonoGame DesktopGL package.
/// </summary>
public sealed class MonoGameDesktopGlReflectionTests
{
    static readonly Assembly MonoGame = typeof(Texture2D).Assembly;

    const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;

    /// <summary>GlWrapper.GetMgWindowId walks GraphicsDevice.Context to GraphicsContext._winHandle.</summary>
    [Fact]
    public void GraphicsDeviceWindowHandleChain_Resolves()
    {
        var graphicsDevice = EngineReflectionPin.RequireType(MonoGame, "Microsoft.Xna.Framework.Graphics.GraphicsDevice");
        EngineReflectionPin.RequireProperty(graphicsDevice, "Context", NonPublicInstance);

        var graphicsContext = EngineReflectionPin.RequireType(MonoGame, "MonoGame.OpenGL.GraphicsContext");
        EngineReflectionPin.RequireField(graphicsContext, "_winHandle", NonPublicInstance);
    }

    /// <summary>
    /// The four Sdl.GL entry points GlWrapper calls through. Each is a static delegate field invoked
    /// with a fixed-length object[], so arity is pinned alongside the name.
    /// </summary>
    [Fact]
    public void SdlGlDelegateFields_Resolve()
    {
        var sdl = EngineReflectionPin.RequireType(MonoGame, "Sdl");
        var sdlGl = EngineReflectionPin.RequireNestedType(sdl, "GL");

        EngineReflectionPin.RequireDelegateField(sdlGl, "SDL_GL_GetCurrentContext", NonPublicStatic, parameterCount: 0);
        EngineReflectionPin.RequireDelegateField(sdlGl, "SDL_GL_CreateContext", NonPublicStatic, parameterCount: 1);
        EngineReflectionPin.RequireDelegateField(sdlGl, "SDL_GL_SetAttribute", NonPublicStatic, parameterCount: 2);
        EngineReflectionPin.RequireDelegateField(sdlGl, "MakeCurrent", BindingFlags.Public | BindingFlags.Static, parameterCount: 2);
    }

    /// <summary>
    /// GlWrapper.LoadFunction closes MonoGame.OpenGL.GL.LoadFunction over the delegate type and
    /// invokes it as (name, throwIfNotFound).
    /// </summary>
    [Fact]
    public void GlLoadFunction_Resolves()
    {
        var gl = EngineReflectionPin.RequireType(MonoGame, "MonoGame.OpenGL.GL");
        var loadFunction = EngineReflectionPin.RequireMethod(gl, "LoadFunction", NonPublicStatic);

        EngineReflectionPin.RequireParameterCount(loadFunction, 2);
        Assert.True(loadFunction.IsGenericMethodDefinition);
        Assert.Single(loadFunction.GetGenericArguments());
    }
}
