#!/bin/sh
# Builds the Unity package's macOS Metal plugin (unity/native/SkiaUnityMetal) as a universal dylib.
# Needs a Mac with the Xcode command line tools. The output is committed, so only rerun this after
# changing the source; eng/build-unity-package.ps1 copies it into the package.
set -e
repo="$(cd "$(dirname "$0")/.." && pwd)"
src="$repo/unity/native/SkiaUnityMetal"
clang -dynamiclib -O2 -Wall -Werror -arch x86_64 -arch arm64 -mmacosx-version-min=11.0 \
    -install_name @rpath/libSkiaUnityMetal.dylib \
    -o "$src/macOS/libSkiaUnityMetal.dylib" "$src/SkiaUnityMetal.c"
echo "Wrote $src/macOS/libSkiaUnityMetal.dylib"
