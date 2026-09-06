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
    if (!values.length) return { samples: 0, p50: null, p95: null, p99: null, max: null };
    return {
        samples: values.length,
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

    const frameCpu = [], innerCpu = [], gpuTimes = [];
    for (let frame = 0; frame < MEASURED_FRAMES; frame++) {
        await new Promise(requestAnimationFrame);
        const gpuQueryStarted = contextBridge.gpuQueryBegin(contextUid);
        const frameStart = performance.now();
        const innerElapsedMs = dotNetRef.invokeMethod("RunFrame", width, height);
        frameCpu.push(performance.now() - frameStart);
        innerCpu.push(innerElapsedMs);
        if (gpuQueryStarted) contextBridge.gpuQueryEnd(contextUid);
        gpuTimes.push(...contextBridge.gpuQueryPoll(contextUid));
        reportElement.textContent = `${progressPrefix}Measured ${frame + 1}/${MEASURED_FRAMES}`;
    }
    // Drain any GPU queries still in flight (results lag a few frames behind on some drivers).
    for (let drain = 0; drain < 10; drain++) {
        await new Promise(requestAnimationFrame);
        gpuTimes.push(...contextBridge.gpuQueryPoll(contextUid));
    }

    const info = contextBridge.getContextInfo(contextUid);
    return {
        schemaVersion: 1,
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
        correctness: { pureGreenAfterSequence, sampledPixelRgba },
        timingsMilliseconds: {
            // Whole invokeMethod round trip as timed from JS - directly comparable in spirit to
            // Benchmarks.WebGL's "uploadCpu" column (a JS-timed wall-clock cost around the work).
            frameCpu: stats(frameCpu),
            // C#-side Stopwatch around steps 1-3 only, excluding the JS<->WASM call boundary.
            innerCpu: stats(innerCpu),
            // EXT_disjoint_timer_query_webgl2 - Chromium only. null (not zero samples) on browsers
            // that don't expose the extension, e.g. Firefox - see docs/webgl/performance-results.md.
            uploadGpu: info.gpuTimerAvailable ? stats(gpuTimes) : null,
        },
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
        const optionD = OPTION_D_BASELINE[browserKey][key];
        const optionA = history[key];
        const isCurrent = key === resKey;
        const resLabel = key + (isCurrent ? " (this page)" : "");

        tableRowsHtml.push(
            `<tr class="option-d${isCurrent ? " current" : ""}">` +
            `<td>${resLabel}</td><td>Option D</td>` +
            `<td>${fmtMs(optionD.uploadCpuP50)}</td><td>${fmtMs(optionD.uploadCpuP95)}</td>` +
            `<td>${fmtMs(optionD.uploadGpuP50)}</td><td>${fmtMs(optionD.uploadGpuP95)}</td>` +
            `<td>${budgetVerdict(optionD.uploadCpuP95, optionD.uploadGpuP95)}</td></tr>`);

        if (!optionA) {
            tableRowsHtml.push(
                `<tr class="option-a${isCurrent ? " current" : ""}">` +
                `<td>${resLabel}</td><td>Option A</td>` +
                `<td colspan="5">not run yet${isCurrent ? " - click \"Run this resolution\"" : " (open ?w=&h= for this resolution)"}</td></tr>`);
            verdictItemsHtml.push(`<li><strong>${resLabel}:</strong> Option A not run yet.</li>`);
            continue;
        }

        const t = optionA.timingsMilliseconds;
        const gpuP50 = t.uploadGpu?.p50 ?? null;
        const gpuP95 = t.uploadGpu?.p95 ?? null;
        tableRowsHtml.push(
            `<tr class="option-a${isCurrent ? " current" : ""}">` +
            `<td>${resLabel}</td><td>Option A</td>` +
            `<td>${fmtMs(t.frameCpu.p50)}</td><td>${fmtMs(t.frameCpu.p95)}</td>` +
            `<td>${fmtMs(gpuP50)}</td><td>${fmtMs(gpuP95)}</td>` +
            `<td>${budgetVerdict(t.frameCpu.p95, gpuP95)}</td></tr>`);

        const aCpu = t.frameCpu.p50, dCpu = optionD.uploadCpuP50;
        const comparison = dCpu <= 0
            ? `Option A adds ~${aCpu.toFixed(2)} ms (Option D measured ~0 ms here)`
            : (aCpu <= dCpu
                ? `Option A is ${(dCpu / aCpu).toFixed(1)}x FASTER than Option D`
                : `Option A is ${(aCpu / dCpu).toFixed(1)}x SLOWER than Option D`);
        const correctnessNote = optionA.correctness.pureGreenAfterSequence
            ? ""
            : " [WARNING: correctness check FAILED on this run - do not trust this row]";
        verdictItemsHtml.push(
            `<li><strong>${resLabel}:</strong> ${comparison} ` +
            `(frame p50 ${fmtMs(aCpu, 2)} ms vs upload p50 ${fmtMs(dCpu, 2)} ms). ` +
            `Budget (p95): ${budgetVerdict(t.frameCpu.p95, gpuP95)}.${correctnessNote}</li>`);
    }

    comparisonElement.innerHTML =
        `<h2>${label} - Option A vs Option D</h2>` +
        `<p>Option D is already-published reference data (${OPTION_D_SOURCE_DOC}), not re-measured ` +
        `here. Budget: upload CPU &lt; ${OPTION_D_BUDGET_MS.uploadCpu} ms, upload GPU &lt; ${OPTION_D_BUDGET_MS.uploadGpu} ms.</p>` +
        `<table class="comparison-table"><thead><tr>` +
        `<th>Resolution</th><th>Source</th><th>CPU p50</th><th>CPU p95</th><th>GPU p50</th><th>GPU p95</th><th>Budget (p95)</th>` +
        `</tr></thead><tbody>${tableRowsHtml.join("")}</tbody></table>` +
        `<ul class="verdicts">${verdictItemsHtml.join("")}</ul>`;
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
    init(dotNetInstance, resolvedContextUid, resolvedWidth, resolvedHeight) {
        dotNetRef = dotNetInstance;
        contextUid = resolvedContextUid;
        width = resolvedWidth;
        height = resolvedHeight;
        resKey = `${width}x${height}`;
        browserKey = detectOptionDBrowserKey(navigator.userAgent);
        reportElement = document.getElementById("report");
        runButton = document.getElementById("run");
        exportButton = document.getElementById("export");
        comparisonElement = document.getElementById("comparison");
        runButton.addEventListener("click", onRunClick);
        exportButton.addEventListener("click", () => {
            downloadJson([window.__optionABenchmarkReport], `webgl-optionA-${width}x${height}-${new Date().toISOString().replace(/[:.]/g, "-")}.json`);
        });
        renderComparisonTable(); // shows any earlier resolutions' history immediately, before Run is clicked
        runButton.disabled = false;
    },
};
