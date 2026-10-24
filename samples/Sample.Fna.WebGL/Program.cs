using System;
using System.Runtime.InteropServices.JavaScript;
using Microsoft.Xna.Framework;

public class SpikeGame : Game
{
    GraphicsDeviceManager _g;
    public int Frames;
    public SpikeGame() { _g = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 800, PreferredBackBufferHeight = 800 }; }
    protected override void Draw(GameTime t) { GraphicsDevice.Clear(Color.CornflowerBlue); Frames++; base.Draw(t); }
}

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public static partial class Program
{
    static SpikeGame _game;
    public static void Main() { Console.WriteLine("main"); }

    [JSExport]
    public static void Probe()
    {
        Console.WriteLine("P1 platform=" + SDL3.SDL.SDL_GetPlatform());
        Console.WriteLine("P2 ticks=" + SDL3.SDL.SDL_GetTicks());
        Console.WriteLine("P3 perf=" + SDL3.SDL.SDL_GetPerformanceCounter());
        Console.WriteLine("P4 init=" + SDL3.SDL.SDL_Init(SDL3.SDL.SDL_InitFlags.SDL_INIT_VIDEO));
        Console.WriteLine("P5 err=" + SDL3.SDL.SDL_GetError());
        var w = SDL3.SDL.SDL_CreateWindow("t", 800, 800, SDL3.SDL.SDL_WindowFlags.SDL_WINDOW_OPENGL);
        Console.WriteLine("P6 window=" + w);
        Console.WriteLine("P7 flags=" + SDL3.SDL.SDL_GetWindowFlags(w));
    }

    [JSExport]
    public static void ProbeA() { Console.WriteLine("A flags=" + SDL3.SDL.SDL_GetWindowFlags(IntPtr.Zero)); }
    [JSExport]
    public static void ProbeB() { var p = SDL3.SDL.SDL_CreateProperties(); Console.WriteLine("B props=" + p); SDL3.SDL.SDL_SetNumberProperty(p, "x", 5L); Console.WriteLine("B done"); }
    [JSExport]
    public static void ProbeC() { var w = SDL3.SDL.SDL_CreateWindow("t", 800, 800, 0UL); Console.WriteLine("C window=" + w + " " + SDL3.SDL.SDL_GetError()); }
    [JSExport]
    public static void ProbeD() { var p = SDL3.SDL.SDL_CreateProperties(); SDL3.SDL.SDL_SetStringProperty(p, SDL3.SDL.SDL_PROP_WINDOW_CREATE_TITLE_STRING, "t"); SDL3.SDL.SDL_SetNumberProperty(p, SDL3.SDL.SDL_PROP_WINDOW_CREATE_WIDTH_NUMBER, 800); SDL3.SDL.SDL_SetNumberProperty(p, SDL3.SDL.SDL_PROP_WINDOW_CREATE_HEIGHT_NUMBER, 800); SDL3.SDL.SDL_SetBooleanProperty(p, SDL3.SDL.SDL_PROP_WINDOW_CREATE_OPENGL_BOOLEAN, true); var w = SDL3.SDL.SDL_CreateWindowWithProperties(p); Console.WriteLine("D window=" + w + " " + SDL3.SDL.SDL_GetError()); }

    [System.Runtime.InteropServices.DllImport("SDL3", EntryPoint = "SDL_GetWindowFlags")] static extern ulong GetFlagsPlain(IntPtr w);
    [System.Runtime.InteropServices.DllImport("SDL3", EntryPoint = "SDL_CreateWindow")] static extern IntPtr CreateWindowPlain([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string t, int w, int h, ulong flags);
    [JSExport]
    public static void ProbeE() { Console.WriteLine("E flags=" + GetFlagsPlain(IntPtr.Zero)); }
    [JSExport]
    public static void ProbeF() { var w = CreateWindowPlain("t", 800, 800, 0x2UL); Console.WriteLine("F window=" + w + " " + SDL3.SDL.SDL_GetError()); }

    [JSExport]
    public static void Frame()
    {
        if (_game == null) { Console.WriteLine("creating game"); _game = new SpikeGame(); }
        _game.RunOneFrame();
    }
    [JSExport]
    public static int Frames() => _game?.Frames ?? -1;
}
