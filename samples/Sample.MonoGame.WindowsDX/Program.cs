using SkiaGameRendering;

var backend = new SkiaAngleBackend();
await backend.Ready;
using var game = new Sample.Game1(backend);
game.Run();
