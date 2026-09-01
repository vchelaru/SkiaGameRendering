using SkiaGameRendering;
using SkiaGameRendering.Kni.WindowsDX;

var backend = new SkiaKniAngleBackend();
await backend.Ready;
using var game = new Sample.Game1(backend);
game.Run();
