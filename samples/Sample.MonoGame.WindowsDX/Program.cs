// --smoke-test renders a few frames on WARP, checks the back buffer for the scene, and exits with 0
// or 1. CI runs the NativeAOT-published exe this way to prove the library still works trimmed.
var smokeTest = System.Array.IndexOf(args, "--smoke-test") >= 0;
if (smokeTest)
    Microsoft.Xna.Framework.Graphics.GraphicsAdapter.UseDriverType =
        Microsoft.Xna.Framework.Graphics.GraphicsAdapter.DriverType.FastSoftware;

using var game = new Sample.Game1(smokeTest);
game.Run();
return game.ExitCode;
