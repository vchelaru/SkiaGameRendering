// --smoke-test renders a few frames, checks the back buffer for the shared scene, and exits with 0 or 1.
var smokeTest = System.Array.IndexOf(args, "--smoke-test") >= 0;

using var game = new Sample.Game1(smokeTest);
game.Run();
return game.ExitCode;
