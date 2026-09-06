// Orchestration for the Option A per-frame benchmark (repo issue #12): warm-up/measured loop,
// percentile stats, JSON report assembly, and the page's two buttons. Mirrors
// benchmarks/Benchmarks.WebGL/benchmark.js's methodology (60 warm-up + 300 measured frames,
// p50/p95/p99/max) so results are directly comparable to docs/webgl/performance-results.md's
// existing Option D table. The actual GL/Skia/KNI work under test lives in C#
// (OptionAFrameRunner.RunFrame) - this file only paces frames, times the call boundary, and
// brackets a GPU timer query around it.
//
// One resolution per page load (see BenchGame.cs's comment for why: KNI's BlazorGL platform has no
// working live-resize). Run the page 3 times, once per ?w=&h= combination, to cover the full
// resolution matrix - see README.md. A localStorage-backed history (below) accumulates each
// resolution's result across those page loads so the on-page comparison table can still show all 3
// at once, per browser, without needing a live in-session resize.
import * as contextBridge from "./context-bridge.js";
import { OPTION_D_BASELINE, OPTION_D_BUDGET_MS, OPTION_D_SOURCE_DOC, detectOptionDBrowserKey, optionDBrowserLabel } from "./option-d-baseline.js";

const WARMUP_FRAMES = 60;
const MEASURED_FRAMES = 300;
const RESOLUTION_KEYS = ["1920x1080", "2560x1440", "3840x2160"];
const HISTORY_STORAGE_KEY = "optionA-benchmark-history-v1";

// NOT looked up at module-load time: this module is a <script type="module">, which the browser
// executes at DOMContentLoaded - well before Blazor has booted and rendered Index.razor's markup
// (the static wwwroot/index.html only has a "Initializing GPU contexts..." placeholder at that
// point). Looked up lazily instead, inside init() (called from Index.razor.cs.OnAfterRenderAsync,
// i.e. after Blazor has actually rendered the buttons).
let reportElement = null;
let runButton = null;
let exportButton = null;
let comparisonElement = null;

let dotNetRef = null;
let contextUid = null;
let width = 0;
let height = 0;
let resKey = "";
let browserKey = null;
let running = false;
let hasOptionD = false; // whether OptionDFrameRunner initialized successfully this page load -
                         // see Index.razor.cs.OnAfterRenderAsync; false degrades gracefully to
                         // Option-A-only (e.g. if Option D's dedicated context creation fails).
window.__optionABenchmarkReport = null; // headless drivers (Playwright) read this directly - no
                                         // download dialog needed, unlike the human "Export" flow.

// Per-browser, per-resolution history of completed Option A runs, persisted in this browser
// profile's localStorage (private to this page's own origin - see the Artifact/browser-storage
// docs this benchmark otherwise has no analog of, but the same "per-viewer, not shared" model
// applies here to "per-browser-profile, not shared across browsers"). Lets the comparison table
// show all 3 resolutions at once for the current browser even though each resolution is its own
// page load (BenchGame.cs's ChangeClientSize no-op comment explains why there's no live resize).
function loadHistory() {
    try {
        return JSON.parse(localStorage.getItem(HISTORY_STORAGE_KEY) || "{}");
    } catch {
        return {};
    }
}

function saveHistoryEntry(browser, resolution, report) {
    const history = loadHistory();
    if (!history[browser]) history[browser] = {};
    history[browser][resolution] = report;
    try {
        localStorage.setItem(HISTORY_STORAGE_KEY, JSON.stringify(history));
    } catch {
        // Best-effort only (private browsing / storage disabled) - the comparison table just won't
        // remember earlier resolutions in that case, same degradation as not having history at all.
    }
}

function percentile(values, amount) {
    if (!values.length) return null;
    const sorted = [...values].sort((a, b) => a - b);
    return sorted[Math.min(sorted.length - 1, Math.floor((sorted.length - 1) * amount))];
}

function stats(values) {
    if (!values.length) return { samples: 0, mean: null, p50: null, p95: null, p99: null, max: null };
    // mean is included alongside the percentiles specifically to see whether averaging over 300
    // samples recovers a real sub-resolution number on browsers that clamp performance.now() to a
    // coarse grid (Chrome/Edge, ~0.1ms steps) - a percentile alone can't do this (it just returns
    // one already-quantized sample), but the mean of many *different* true elapsed times, each
    // independently quantized, can land between grid steps if the browser's clamping doesn't apply
    // the exact same rounding to every sample. Whether it actually does here is an open question -
    // this is how to find out empirically instead of arguing about Chromium's clamping internals.
    const mean = values.reduce((sum, v) => sum + v, 0) / values.length;
    return {
        samples: values.length,
        mean,
        p50: percentile(values, .5),
        p95: percentile(values, .95),
        p99: percentile(values, .99),
        max: Math.max(...values),
    };
}

async function runBenchmark(progressPrefix) {
    for (let frame = 0; frame < WARMUP_FRAMES; frame++) {
        await new Promise(requestAnimationFrame);
        dotNetRef.invokeMethod("RunFrame", width, height);
        // Interleaved every frame, not run as a separate phase before/after Option A's loop - both
        // architectures see identical power/thermal conditions at every sampled instant this way,
        // which is the whole point of measuring them in the same page load (see repo issue #12
        // discussion: a live run compared against a different day's static baseline conflates
        // architecture with uncontrolled session/power differences).
        if (hasOptionD) dotNetRef.invokeMethod("RunOptionDFrame", width, height);
        reportElement.textContent = `${progressPrefix}Warm-up ${frame + 1}/${WARMUP_FRAMES}`;
    }

    // Correctness check BEFORE trusting any timing below - same readback check the second spike
    // used (pure green = InvalidateStateCache defeated the state-cache corruption; anything else
    // means the harness itself is not proving what it claims to, and the timing numbers below are
    // meaningless regardless of how fast/slow they are.
    dotNetRef.invokeMethod("RunFrame", width, height);
    const sampledPixelRgba = dotNetRef.invokeMethod("ReadCenterPixel", width, height);
    const pureGreenAfterSequence = sampledPixelRgba[0] === 0 && sampledPixelRgba[1] === 255
        && sampledPixelRgba[2] === 0 && sampledPixelRgba[3] === 255;
    contextBridge.checkGlError(contextUid);

    // Option D's correctness check: after it uploads Skia's magenta-background draw into KNI's
    // texture and KNI draws that full-viewport, a corner pixel (away from the circle in the middle)
    // should read back pure magenta off KNI's own canvas - proves the cross-context blit actually
    // landed real content, not stale/garbage texture data.
    let optionDCorrectness = null;
    if (hasOptionD) {
        dotNetRef.invokeMethod("RunOptionDFrame", width, height);
        const cornerRgba = contextBridge.readKniPixel(contextUid, 2, 2);
        const pureMagentaCorner = cornerRgba[0] === 255 && cornerRgba[1] === 0
            && cornerRgba[2] === 255 && cornerRgba[3] === 255;
        optionDCorrectness = { pureMagentaCorner, cornerRgba };
    }

    // Collected per-step, NOT as one lump sum - see OptionAFrameRunner.RunFrame's doc comment for
    // why: Option D's published uploadCpu/uploadGpu numbers never included any real Skia rendering
    // cost (its harness draws a trivial synthetic WebGL quad, timed separately and excluded from
    // the published table), so interopOverheadMs (invalidate + KNI's redraw, excluding Skia's own
    // draw) is the only one of these directly comparable to Option D's numbers.
    const frameCpu = [], totalCpu = [], invalidateCpu = [], skiaDrawCpu = [], interopOverheadCpu = [], gpuTimes = [];
    const dSkiaDrawCpu = [], dUploadCpu = [], dKniDrawCpu = [], dTotalCpu = [];
    for (let frame = 0; frame < MEASURED_FRAMES; frame++) {
        await new Promise(requestAnimationFrame);
        const gpuQueryStarted = contextBridge.gpuQueryBegin(contextUid);
        const frameStart = performance.now();
        const timing = dotNetRef.invokeMethod("RunFrame", width, height);
        frameCpu.push(performance.now() - frameStart);
        totalCpu.push(timing.totalMs);
        invalidateCpu.push(timing.invalidateMs);
        skiaDrawCpu.push(timing.skiaDrawMs);
        interopOverheadCpu.push(timing.interopOverheadMs);
        if (gpuQueryStarted) contextBridge.gpuQueryEnd(contextUid);
        gpuTimes.push(...contextBridge.gpuQueryPoll(contextUid));

        if (hasOptionD) {
            const dTiming = dotNetRef.invokeMethod("RunOptionDFrame", width, height);
            dSkiaDrawCpu.push(dTiming.skiaDrawMs);
            dUploadCpu.push(dTiming.uploadMs);
            dKniDrawCpu.push(dTiming.kniDrawMs);
            dTotalCpu.push(dTiming.totalMs);
        }
        reportElement.textContent = `${progressPrefix}Measured ${frame + 1}/${MEASURED_FRAMES}`;
    }
    // Drain any GPU queries still in flight (results lag a few frames behind on some drivers).
    for (let drain = 0; drain < 10; drain++) {
        await new Promise(requestAnimationFrame);
        gpuTimes.push(...contextBridge.gpuQueryPoll(contextUid));
    }

    const info = contextBridge.getContextInfo(contextUid);
    return {
        schemaVersion: 3, // v3: adds live in-page Option D measurement (optionDLiveTimingsMilliseconds)
                          // alongside Option A, run interleaved in the same session/power state.
        timestampUtc: new Date().toISOString(),
        browser: navigator.userAgent,
        platform: navigator.userAgentData?.platform || navigator.platform,
        architecture: "OptionA-ContextBridge-InvalidateStateCache",
        sequenceMeasuredPerFrame: [
            "GraphicsDevice.InvalidateStateCache() (patched KNI)",
            "Skia draw into KNI's shared WebGL2 context: magenta clear + filled AA circle",
            "KNI SpriteBatch full-viewport pure-green quad draw (reused SpriteBatch/Texture2D/BlendState.Opaque)",
        ],
        webglVersion: info.webglVersion,
        renderer: info.renderer,
        resolution: { width, height },
        devicePixelRatio: devicePixelRatio,
        warmupFrames: WARMUP_FRAMES,
        measuredFrames: MEASURED_FRAMES,
        correctness: { pureGreenAfterSequence, sampledPixelRgba, optionD: optionDCorrectness },
        timingsMilliseconds: {
            // Whole invokeMethod round trip as timed from JS, all 3 steps included - NOT the number
            // to compare against Option D's uploadCpu (see interopOverheadCpu below for that).
            frameCpu: stats(frameCpu),
            // C#-side Stopwatch total across all 3 steps, excluding the JS<->WASM call boundary.
            totalCpu: stats(totalCpu),
            // Step 1 only: GraphicsDevice.InvalidateStateCache().
            invalidateCpu: stats(invalidateCpu),
            // Step 2 only: Skia's own draw (clear + filled AA circle). Informative context, NOT
            // comparable to Option D - Option D's own live Skia draw (optionDLiveTimingsMilliseconds
            // .skiaDrawCpu below) is the real apples-to-apples comparison for this one, if you want it.
            skiaDrawCpu: stats(skiaDrawCpu),
            // Steps 1+3 (invalidate + KNI's redraw), EXCLUDING Skia's own draw time (step 2) - this
            // is the number directly comparable to Option D's uploadCpu, since both measure only the
            // cost attributable to the interop architecture, not the cost of rendering the content.
            interopOverheadCpu: stats(interopOverheadCpu),
            // EXT_disjoint_timer_query_webgl2 - Chromium only. null (not zero samples) on browsers
            // that don't expose the extension, e.g. Firefox - see docs/webgl/performance-results.md.
            uploadGpu: info.gpuTimerAvailable ? stats(gpuTimes) : null,
        },
        // Live, this-session, interleaved-with-Option-A measurement of this repo's ACTUAL shipped
        // architecture (OptionDFrameRunner.cs) - null if it failed to initialize this page load
        // (Index.razor.cs degrades gracefully; see its console log for why). Directly comparable to
        // timingsMilliseconds.interopOverheadCpu (uploadCpu here vs interopOverheadCpu there), and
        // to skiaDrawCpu above (both draw the identical magenta-clear-plus-circle content).
        optionDLiveTimingsMilliseconds: hasOptionD ? {
            skiaDrawCpu: stats(dSkiaDrawCpu),
            uploadCpu: stats(dUploadCpu),
            kniDrawCpu: stats(dKniDrawCpu),
            totalCpu: stats(dTotalCpu),
        } : null,
    };
}

function downloadJson(data, filename) {
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = filename;
    anchor.click();
    URL.revokeObjectURL(url);
}

function fmtMs(value, digits = 3) {
    return value == null ? "n/a" : value.toFixed(digits);
}

function budgetVerdict(cpuP95, gpuP95) {
    const cpuPass = cpuP95 != null && cpuP95 <= OPTION_D_BUDGET_MS.uploadCpu;
    const cpuText = cpuP95 == null ? "CPU n/a" : `CPU ${cpuPass ? "PASS" : "MISS"}`;
    const gpuText = gpuP95 == null ? "GPU n/a" : `GPU ${gpuP95 <= OPTION_D_BUDGET_MS.uploadGpu ? "PASS" : "MISS"}`;
    return `${cpuText} / ${gpuText}`;
}

// Renders the side-by-side comparison table into #comparison: Option D's already-published
// baseline (option-d-baseline.js, fixed reference data) next to whatever Option A results this
// browser has accumulated in localStorage so far (loadHistory()), for all 3 resolutions at once -
// not just the one this page load is running. The row for the resolution this page load just
// measured (or is about to measure) is marked "(this page)".
function renderComparisonTable() {
    if (!comparisonElement) return;

    if (!browserKey) {
        comparisonElement.innerHTML =
            `<p>No Option D baseline is published for this browser's user agent - comparison table ` +
            `unavailable. Published baselines cover Chrome, Edge, and Firefox only.</p>`;
        return;
    }

    const history = loadHistory()[browserKey] || {};
    const label = optionDBrowserLabel(browserKey);
    const tableRowsHtml = [];
    const verdictItemsHtml = [];

    for (const key of RESOLUTION_KEYS) {
        const optionDPublished = OPTION_D_BASELINE[browserKey][key];
        const optionA = history[key];
        const isCurrent = key === resKey;
        const resLabel = key + (isCurrent ? " (this page)" : "");

        tableRowsHtml.push(
            `<tr class="option-d${isCurrent ? " current" : ""}">` +
            `<td>${resLabel}</td><td>Option D (published, different session)</td>` +
            `<td>${fmtMs(optionDPublished.uploadCpuP50)}</td><td>${fmtMs(optionDPublished.uploadCpuP95)}</td>` +
            `<td>${fmtMs(optionDPublished.uploadGpuP50)}</td><td>${fmtMs(optionDPublished.uploadGpuP95)}</td>` +
            `<td>${budgetVerdict(optionDPublished.uploadCpuP95, optionDPublished.uploadGpuP95)}</td></tr>`);

        if (!optionA) {
            tableRowsHtml.push(
                `<tr class="option-a${isCurrent ? " current" : ""}">` +
                `<td>${resLabel}</td><td>Option A / Option D (live)</td>` +
                `<td colspan="5">not run yet${isCurrent ? " - click \"Run this resolution\"" : " (open ?w=&h= for this resolution)"}</td></tr>`);
            verdictItemsHtml.push(`<li><strong>${resLabel}:</strong> not run yet.</li>`);
            continue;
        }

        const t = optionA.timingsMilliseconds;
        // schemaVersion < 3 history lacks interopOverheadCpu (v1: one lump sum) or
        // optionDLiveTimingsMilliseconds (v1/v2: no live Option D at all) - stale data from before
        // this session's live-comparison methodology existed. Flag it instead of silently misreading it.
        if (!t.interopOverheadCpu) {
            tableRowsHtml.push(
                `<tr class="option-a${isCurrent ? " current" : ""}">` +
                `<td>${resLabel}</td><td>Option A / Option D (live)</td>` +
                `<td colspan="5">stale result (pre-v3 schema) - please re-run</td></tr>`);
            verdictItemsHtml.push(`<li><strong>${resLabel}:</strong> stale result - please re-run.</li>`);
            continue;
        }

        const gpuP50 = t.uploadGpu?.p50 ?? null;
        const gpuP95 = t.uploadGpu?.p95 ?? null;
        tableRowsHtml.push(
            `<tr class="option-a${isCurrent ? " current" : ""}">` +
            `<td>${resLabel}</td><td>Option A (interop only, live)</td>` +
            `<td>${fmtMs(t.interopOverheadCpu.p50)}</td><td>${fmtMs(t.interopOverheadCpu.p95)}</td>` +
            `<td>${fmtMs(gpuP50)}</td><td>${fmtMs(gpuP95)}</td>` +
            `<td>${budgetVerdict(t.interopOverheadCpu.p95, gpuP95)}</td></tr>`);

        const dLive = optionA.optionDLiveTimingsMilliseconds;
        if (dLive) {
            tableRowsHtml.push(
                `<tr class="option-d${isCurrent ? " current" : ""}">` +
                `<td>${resLabel}</td><td>Option D (upload only, live - THIS session/power state)</td>` +
                `<td>${fmtMs(dLive.uploadCpu.p50)}</td><td>${fmtMs(dLive.uploadCpu.p95)}</td>` +
                `<td>n/a</td><td>n/a</td>` +
                `<td>${budgetVerdict(dLive.uploadCpu.p95, null)}</td></tr>`);
        }

        // aCpu is invalidate + KNI's redraw ONLY, excluding Skia's own draw time - the fair
        // comparison against Option D's upload-only number. See RunFrame's doc comment. Prefer the
        // LIVE Option D number (same session/power state) over the published baseline whenever both
        // exist - that's the whole point of measuring them together (repo issue #12 discussion).
        const aCpu = t.interopOverheadCpu.p50;
        const dCpu = dLive ? dLive.uploadCpu.p50 : optionDPublished.uploadCpuP50;
        const dLabel = dLive ? "Option D (live, this session)" : "Option D (published baseline)";
        const comparison = dCpu <= 0
            ? `Option A's interop overhead adds ~${aCpu.toFixed(2)} ms (${dLabel} measured ~0 ms here)`
            : (aCpu <= dCpu
                ? `Option A's interop overhead is ${(dCpu / aCpu).toFixed(1)}x FASTER than ${dLabel}`
                : `Option A's interop overhead is ${(aCpu / dCpu).toFixed(1)}x SLOWER than ${dLabel}`);
        const correctnessFailed = !optionA.correctness.pureGreenAfterSequence
            || (optionA.correctness.optionD && !optionA.correctness.optionD.pureMagentaCorner);
        const correctnessNote = correctnessFailed
            ? " [WARNING: a correctness check FAILED on this run - do not trust this row]"
            : "";
        const skiaCompare = dLive
            ? `Option A's own Skia draw was ${fmtMs(t.skiaDrawCpu?.p50 ?? null, 2)} ms p50 vs Option D's ` +
              `live Skia draw at ${fmtMs(dLive.skiaDrawCpu.p50, 2)} ms p50 (same visual content in both - ` +
              `should track closely; a big gap would itself be a finding worth investigating)`
            : `Skia's own draw cost was ${fmtMs(t.skiaDrawCpu?.p50 ?? null, 2)} ms p50, common to both ` +
              `architectures and NOT counted in this comparison`;
        verdictItemsHtml.push(
            `<li><strong>${resLabel}:</strong> ${comparison} ` +
            `(interop overhead p50 ${fmtMs(aCpu, 2)} ms, mean ${fmtMs(t.interopOverheadCpu.mean, 4)} ms ` +
            `over ${t.interopOverheadCpu.samples} samples - the mean can reveal a real sub-clamp-` +
            `resolution number on Chrome/Edge that the percentile can't - vs ${dLabel} p50 ` +
            `${fmtMs(dCpu, 2)} ms${dLive ? `, mean ${fmtMs(dLive.uploadCpu.mean, 4)} ms` : ""}. ` +
            `${skiaCompare}). ` +
            `Budget (p95): ${budgetVerdict(t.interopOverheadCpu.p95, gpuP95)}.${correctnessNote}</li>`);
    }

    comparisonElement.innerHTML =
        `<h2>${label} - Option A vs Option D</h2>` +
        `<p>"Live" rows were measured THIS page load, interleaved frame-by-frame with Option A so ` +
        `both see the same power/thermal conditions - prefer these over the "published, different ` +
        `session" row, which is static reference data from ${OPTION_D_SOURCE_DOC} captured a different ` +
        `day under unknown power conditions. Both live rows measure interop overhead only, excluding ` +
        `each side's own Skia draw time (shown separately in the verdict list below). ` +
        `Budget: upload CPU &lt; ${OPTION_D_BUDGET_MS.uploadCpu} ms, upload GPU &lt; ${OPTION_D_BUDGET_MS.uploadGpu} ms.</p>` +
        `<table class="comparison-table"><thead><tr>` +
        `<th>Resolution</th><th>Source</th><th>CPU p50</th><th>CPU p95</th><th>GPU p50</th><th>GPU p95</th><th>Budget (p95)</th>` +
        `</tr></thead><tbody>${tableRowsHtml.join("")}</tbody></table>` +
        `<ul class="verdicts">${verdictItemsHtml.join("")}</ul>`;
}

// Exports EVERY resolution this browser profile has recorded so far (loadHistory()), not just the
// run that just finished - so running all 3 resolutions in one browser and clicking Export once
// hands back a single file covering the whole matrix for that browser. Overwrites the same
// filename on every click (no timestamp in the name) so repeated exports across a Chrome/Edge/
// Firefox run each land at one stable, predictable path in the Downloads folder instead of
// scattering timestamped one-resolution files that have to be hand-merged afterward.
function exportHistory() {
    const history = browserKey ? (loadHistory()[browserKey] || {}) : {};
    const runsCount = Object.keys(history).length;
    downloadJson({
        schemaVersion: 1,
        exportedUtc: new Date().toISOString(),
        browserKey,
        browser: navigator.userAgent,
        optionDSourceDoc: OPTION_D_SOURCE_DOC,
        optionDBudgetMs: OPTION_D_BUDGET_MS,
        optionDBaseline: browserKey ? OPTION_D_BASELINE[browserKey] : null,
        runs: history, // keyed by resolution, e.g. "3840x2160" - whatever's been run in this browser so far
    }, `webgl-optionA-${browserKey || "unknown-browser"}.json`);
    reportElement.textContent =
        `Exported ${runsCount} resolution(s) for ${browserKey || "this browser"} to your Downloads folder ` +
        `as webgl-optionA-${browserKey || "unknown-browser"}.json (same filename every export - it overwrites).`;
}

async function onRunClick() {
    if (running) return;
    running = true;
    runButton.disabled = true;
    exportButton.disabled = true;
    try {
        const report = await runBenchmark(`${width}x${height} — `);
        window.__optionABenchmarkReport = report;
        if (browserKey) saveHistoryEntry(browserKey, resKey, report);
        reportElement.textContent = `Done. correctness.pureGreenAfterSequence=${report.correctness.pureGreenAfterSequence} — click "Export" to download.`;
        console.log("[optionA] done:", JSON.stringify(report.timingsMilliseconds), "correctness:", report.correctness);
        renderComparisonTable();
        exportButton.disabled = false;
    } catch (error) {
        reportElement.textContent = error.stack || String(error);
        console.error("[optionA]", error);
    } finally {
        running = false;
        runButton.disabled = false;
    }
}

globalThis.optionABenchmark = {
    // Called from Index.razor.cs.OnAfterRenderAsync, i.e. after Blazor has actually rendered
    // Index.razor's markup - see the comment on the element-lookup variables above for why this
    // module can't just look these up (or wire click handlers) at top-level load time.
    init(dotNetInstance, resolvedContextUid, resolvedWidth, resolvedHeight, resolvedHasOptionD) {
        dotNetRef = dotNetInstance;
        contextUid = resolvedContextUid;
        width = resolvedWidth;
        height = resolvedHeight;
        hasOptionD = resolvedHasOptionD;
        resKey = `${width}x${height}`;
        browserKey = detectOptionDBrowserKey(navigator.userAgent);
        reportElement = document.getElementById("report");
        runButton = document.getElementById("run");
        exportButton = document.getElementById("export");
        comparisonElement = document.getElementById("comparison");
        runButton.addEventListener("click", onRunClick);
        exportButton.addEventListener("click", exportHistory);
        renderComparisonTable(); // shows any earlier resolutions' history immediately, before Run is clicked
        // Enabled whenever this browser already has at least one recorded run (from an earlier page
        // load/resolution), not just right after this page's own Run finishes - otherwise there'd be
        // no way to export a full 3-resolution history without re-running the last resolution first.
        const alreadyHasHistory = browserKey && Object.keys(loadHistory()[browserKey] || {}).length > 0;
        exportButton.disabled = !alreadyHasHistory;
        runButton.disabled = false;
    },
};
