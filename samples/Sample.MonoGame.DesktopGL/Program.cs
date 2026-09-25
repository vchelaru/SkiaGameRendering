// --smoke-test renders a few frames, checks the back buffer for the scene, and exits with 0 or 1.
// CI runs the NativeAOT-published exe this way to prove the library still works trimmed.
using var game = new Sample.Game1(smokeTest: System.Array.IndexOf(args, "--smoke-test") >= 0);
game.Run();
return game.ExitCode;
