// FNA3D picks its SDL_GPU driver by default, which exposes no native context for Skia to share.
// SDL reads this as a hint when FNA3D selects its driver, so it must be set before the Game exists.
// See "FNA" in README.md.
System.Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "OpenGL");

using var game = new Sample.Game1();
game.Run();
