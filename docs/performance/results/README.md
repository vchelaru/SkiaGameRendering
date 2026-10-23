# Result files

One file per app per machine, named `<hardware-id>__<platform>-<app>.md`. They are the
`perf-results-<app>.md` files the benchmark apps write next to their exe, renamed. The hardware id is
yours to pick (`H1`, `H2`, ...); everything else is read from the file.

`H1` was recorded on 2026-09-30 (commit 59d06ca) before the harness had any scene but the single-texture
sprites, so it has only that page and only the render-target Skia mode.

After adding or replacing files, regenerate the tables:

```bash
pwsh eng/update-performance-docs.ps1
```
