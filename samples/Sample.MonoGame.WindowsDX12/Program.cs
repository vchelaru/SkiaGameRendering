// --smoke-test renders a few frames, checks the back buffer for the shared scene, and exits with 0 or 1.
// --direct-smoke-test draws with SkiaDirectCanvas for a number of frames and checks every layer; see DirectDrawGame.
// --direct-smoke-test-msaa does the same on a multisampled back buffer, where SkiaDirectCanvas must refuse.
// --render-target-smoke-test-msaa checks that new SkiaRenderTarget2D(device, w, h, multiSampleCount: 4) throws NotSupportedException.
if (System.Array.IndexOf(args, "--render-target-smoke-test-msaa") >= 0)
{
    using var msaaGame = new Sample.MultisampledTargetGame();
    msaaGame.Run();
    return msaaGame.ExitCode;
}

var smokeTest = System.Array.IndexOf(args, "--smoke-test") >= 0;
var directSmokeTestMsaa = System.Array.IndexOf(args, "--direct-smoke-test-msaa") >= 0;
var directSmokeTest = directSmokeTestMsaa || System.Array.IndexOf(args, "--direct-smoke-test") >= 0;

// Opt-in (SKIAGAMERENDERING_D3D12_DEBUG=1): the D3D12 debug layer has to be on before the device exists.
var debugLayer = (smokeTest || directSmokeTest) && Sample.D3D12DebugLayer.Requested;
if (debugLayer)
    Sample.D3D12DebugLayer.Enable();

if (directSmokeTest)
{
    using var directGame = new Sample.DirectDrawGame(directSmokeTestMsaa);
    if (debugLayer)
        directGame.ExtraCheck = Sample.D3D12DebugLayer.Passed;
    directGame.Run();
    return directGame.ExitCode;
}

using var game = new Sample.Game1(smokeTest);
if (debugLayer)
    game.ExtraSmokeCheck = Sample.D3D12DebugLayer.Passed;
game.Run();
return game.ExitCode;
