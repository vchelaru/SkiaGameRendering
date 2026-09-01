using SkiaGameRendering.Kni.DesktopGL;

var backend = new SkiaKniGlBackend();
await backend.Ready;
using var game = new Sample.Kni.DesktopGL.Game1(backend);
game.Run();
