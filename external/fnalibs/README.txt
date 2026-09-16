Windows x64 FNA native libraries (FNA3D, SDL3, FAudio, libtheorafile), taken unmodified from the
"fnalibs" artifact of FNA-XNA/fnalibs-dailies (github.com/FNA-XNA/fnalibs-dailies/actions), the
official prebuilt download FNA's docs point at. Artifact 10428484168, built 2026-09-16, matching the
FNA 26.09 submodule under external/FNA.

Checked in rather than downloaded at build time because the dailies are only published as GitHub
Actions artifacts, which need an authenticated download and expire. ~7.5 MB total.

Only the Windows x64 set is vendored; the Fna.WindowsDX backend is Windows-only. The archive's D3D12
folder (Agility SDK) is left out - it only matters for FNA3D's SDL_GPU driver, which this backend
can't use (see SkiaFnaAngleBackend).

SkiaGameRendering.Fna.WindowsDX reads the ID3D11Resource* out of FNA3D's internal D3D11Texture struct
(first field), so the FNA3D.dll here is the one that layout was verified against. Re-verify when
updating it - see SkiaFnaAngleBackend's MAINTENANCE NOTES.
