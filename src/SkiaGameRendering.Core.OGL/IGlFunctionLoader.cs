namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// Resolves native OpenGL entry points into delegates. Implemented per host engine, since
    /// how a proc address is obtained (SDL, GLFW, wgl/glX/egl, ...) differs by windowing library.
    /// </summary>
    public interface IGlFunctionLoader
    {
        T Load<T>(string nativeName) where T : Delegate;
    }
}
