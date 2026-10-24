// --smoke-test renders a few frames, checks the back buffer for the scene, and exits with 0 or 1.
// CI runs the NativeAOT-published exe this way to prove the library still works trimmed.
// --screenshot <path> saves the Scene as a PNG (the README image) and exits.
var screenshot = System.Array.IndexOf(args, "--screenshot");
using var game = new Sample.Game1(smokeTest: System.Array.IndexOf(args, "--smoke-test") >= 0)
{
    ScreenshotPath = screenshot >= 0 && screenshot + 1 < args.Length ? args[screenshot + 1] : null,
};
game.Run();
return game.ExitCode;
