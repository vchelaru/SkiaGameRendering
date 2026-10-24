#!/usr/bin/env bash
# Builds SDL2, FNA3D and MojoShader as Emscripten static libs into external/fnalibs-wasm.
# Needs emcc 3.1.56 (the .NET 10 wasm-tools emsdk), cmake and ninja on PATH. SDL2, not SDL3: the
# Mono interpreter traps on pinvokes that take or return an enum with a 64-bit underlying type
# (SDL3's SDL_WindowFlags), and SDL2's flags are 32-bit. Run FNA with FNA_PLATFORM_BACKEND=SDL2.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/external/fnalibs-wasm"; work="${TMPDIR:-/tmp}/fnawasm"
mkdir -p "$out" "$work"
git clone -q --depth 1 -b release-2.32.10 https://github.com/libsdl-org/SDL "$work/SDL2"
emcmake cmake -S "$work/SDL2" -B "$work/sdl2b" -GNinja -DCMAKE_BUILD_TYPE=Release -DSDL_TESTS=OFF -DSDL_PTHREADS=OFF -DSDL_SHARED=OFF -DSDL_STATIC=ON
cmake --build "$work/sdl2b"
git -C "$root/external/FNA" submodule update --init lib/FNA3D
git -C "$root/external/FNA/lib/FNA3D" submodule update --init
emcmake cmake -S "$root/external/FNA/lib/FNA3D" -B "$work/fna3d" -GNinja -DBUILD_SDL3=OFF \
  -DSDL2_INCLUDE_DIRS="$work/SDL2/include" -DSDL2_LIBRARIES="$work/sdl2b/libSDL2.a" -DBUILD_SHARED_LIBS=OFF
cmake --build "$work/fna3d"
cp "$work/sdl2b/libSDL2.a" "$out/SDL2.a"; cp "$work/fna3d/libFNA3D.a" "$out/FNA3D.a"; cp "$work/fna3d/libmojoshader.a" "$out/mojoshader.a"
