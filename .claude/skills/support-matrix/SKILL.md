---
name: support-matrix
description: "Printing the supported platforms table (engine x graphics API, with direct-canvas support). Triggers: "supported platforms", "support table", "platform matrix", "what's supported", "direct to backbuffer"."
---

# Support Matrix

When the user asks for the supported platforms table, print the **Direct Canvas Coverage** table
from `TODO.md` (the `## Direct Canvas Coverage` section). Copy its legend line, the table and the
notes under it as they are, from the current `master`. Rows are engines, columns are graphics
APIs, and each cell says whether a render target and `SkiaDirectCanvas` (drawing straight into
the back buffer) are supported. Platform limits are in the cell text and in the notes.

## Landmines

- The README's package table (engine, backend, package, OS) is a different table. It has no
  graphics API columns and no direct-canvas column, so never answer with it.
- `TODO.md`'s `## Platform Matrix` (one row per sample, with a status column) is not this table
  either.
- Pull `master` first. A local, uncommitted `TODO.md` can be older than master.
