using Brutal.GlfwApi;

namespace Tests.Brutal.OGL;

/// <summary>A hidden GLFW window with an OpenGL 3.3 core context current on the calling thread, as a BRUTAL app has.</summary>
sealed class BrutalGlWindow : IDisposable
{
    readonly GlfwWindow _window;

    public BrutalGlWindow(int width, int height)
    {
        if (!Glfw.Init())
            throw new InvalidOperationException("glfwInit failed (no display?).");
        Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 3);
        Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 3);
        Glfw.WindowHint(GlfwWindowHint.OpenGlProfile, 0x32001 /* GLFW_OPENGL_CORE_PROFILE */);
        Glfw.WindowHint(GlfwWindowHint.Visible, 0);
        var createInfo = new GlfwWindow.CreateInfo { Title = "Tests.Brutal.OGL", Size = new(width, height) };
        _window = Glfw.CreateWindow(in createInfo);
        _window.MakeContextCurrent();
    }

    public static IntPtr GetProc(string name) => Glfw.GetProcAddress(System.Text.Encoding.ASCII.GetBytes(name + "\0"));

    public void Dispose()
    {
        _window.Dispose();
        Glfw.Terminate();
    }
}
