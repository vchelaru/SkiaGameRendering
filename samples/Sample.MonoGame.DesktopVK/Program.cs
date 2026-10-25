// --smoke-test renders a few frames, checks the back buffer for the shared scene, and exits with 0 or 1.
// --render-target-smoke-test-msaa checks that new SkiaRenderTarget2D(device, w, h, multiSampleCount: 4) throws NotSupportedException.
if (System.Array.IndexOf(args, "--render-target-smoke-test-msaa") >= 0)
{
    using var msaaGame = new Sample.MultisampledTargetGame();
    msaaGame.Run();
    return msaaGame.ExitCode;
}

var smokeTest = System.Array.IndexOf(args, "--smoke-test") >= 0;

using var game = new Sample.Game1(smokeTest);
game.Run();
return game.ExitCode;
