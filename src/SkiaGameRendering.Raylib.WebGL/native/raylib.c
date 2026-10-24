// Intentionally empty. .NET registers one P/Invoke module per native file name, and Raylib-cs imports its functions
// from the module "raylib"; the raylib sources next to this file are compiled as modules of their own
// (rcore, rtextures, ...) with ScanForPInvokes off, so this file gives the P/Invoke table its "raylib" module.
int skia_game_rendering_raylib_module;
