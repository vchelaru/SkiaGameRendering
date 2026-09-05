# WebGL upload benchmark

Serve this directory over HTTP and open `index.html`. Each run performs 60 warm-up frames and 300 measured frames for 1, 2, or 4 sequential renderables, then emits a versioned JSON report with CPU/GPU percentiles, frame misses, renderer, context attributes, DPR, upload settings, and context-loss count.

The `readPixels` path is a negative baseline only. Production code never selects it.

## Full acceptance matrix

Click **Run full matrix** to sequentially run all 3 resolutions × 5 upload paths (15 combos,
~2 minutes) at the current "Sequential renderables" setting, then **Export all** for one combined
JSON file — this is the one-click way to gather the acceptance data per browser instead of running
each combo by hand.
