# Result files

One file per app per machine, named `<hardware-id>__<platform>-<app>.md`. They are the
`perf-results-<app>.md` files the benchmark apps write next to their exe (the browser apps' files come from
`run-browser.mjs`), renamed. The hardware id is yours to pick (`H1`, `H2`, ...); everything else is read from
the file. A run split across several files (`--page`) keeps one id and gets `-1`, `-2` suffixes; the
generator reads them as one run.

A browser cannot see the CPU model or the power state: pass them to `run-browser.mjs` (`--cpu`, `--os`,
`--power`) so the header matches the desktop files for the same machine.

- `H1` was recorded on 2026-09-30 (commit 59d06ca) before the harness had any scene but the single-texture
  sprites, so it has only that page and only the render-target Skia mode.
- `H2` is an Intel Core Ultra 9 185H with its integrated Arc graphics, plugged in. The desktop apps and
  Chrome both ran on that GPU.

After adding or replacing files, regenerate the tables:

```bash
pwsh eng/update-performance-docs.ps1
```
