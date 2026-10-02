// --smoke-test renders a few frames, checks the back buffer for the shared scene, and exits with 0 or 1.
var smokeTest = System.Array.IndexOf(args, "--smoke-test") >= 0;

// Opt-in (SKIAGAMERENDERING_D3D12_DEBUG=1): the D3D12 debug layer has to be on before the device exists.
var debugLayer = smokeTest && Sample.D3D12DebugLayer.Requested;
if (debugLayer)
    Sample.D3D12DebugLayer.Enable();

using var game = new Sample.Game1(smokeTest);
if (debugLayer)
    game.ExtraSmokeCheck = Sample.D3D12DebugLayer.Passed;
if (debugLayer)
    game.DIAG_Mark = Sample.D3D12DebugLayer.Mark;
game.Run();
return game.ExitCode;
