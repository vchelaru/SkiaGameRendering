// Option D's already-published baseline, embedded as fixed reference data for the on-page
// comparison table - NOT re-measured by this benchmark. Copied by hand from
// docs/webgl/performance-results.md (measured 2026-09-05, Ryzen 7 260 / RTX 5060 Laptop GPU) - the
// texSubImage2D row specifically, because that is KNI's actual production upload path (see
// Game1.cs's WebGlUploadMode.DirectCanvasTexSubImage2D; the other 4 paths that doc also measured -
// texImage2D/imageBitmap/offscreenBitmap/readPixels - are diagnostic/negative-baseline only).
// If docs/webgl/performance-results.md is ever re-measured on different hardware, update this file
// to match - there is no automated link between the two.
export const OPTION_D_SOURCE_DOC = "docs/webgl/performance-results.md (texSubImage2D path)";

export const OPTION_D_BUDGET_MS = { uploadCpu: 0.5, uploadGpu: 1.0 };

export const OPTION_D_BASELINE = {
    chrome: {
        "1920x1080": { uploadCpuP50: 0.0, uploadCpuP95: 0.1, uploadGpuP50: 0.052, uploadGpuP95: 0.227 },
        "2560x1440": { uploadCpuP50: 0.1, uploadCpuP95: 0.2, uploadGpuP50: 0.068, uploadGpuP95: 0.108 },
        "3840x2160": { uploadCpuP50: 0.0, uploadCpuP95: 0.1, uploadGpuP50: 0.075, uploadGpuP95: 0.079 },
    },
    edge: {
        "1920x1080": { uploadCpuP50: 0.1, uploadCpuP95: 0.2, uploadGpuP50: 0.112, uploadGpuP95: 0.243 },
        "2560x1440": { uploadCpuP50: 0.0, uploadCpuP95: 0.1, uploadGpuP50: 0.035, uploadGpuP95: 0.037 },
        "3840x2160": { uploadCpuP50: 0.1, uploadCpuP95: 0.2, uploadGpuP50: 0.075, uploadGpuP95: 0.079 },
    },
    // Firefox does not expose EXT_disjoint_timer_query_webgl2 by default - uploadGpu is genuinely
    // unmeasured (null), not zero, same convention this benchmark uses for its own uploadGpu column.
    firefox: {
        "1920x1080": { uploadCpuP50: 35.0, uploadCpuP95: 39.0, uploadGpuP50: null, uploadGpuP95: null },
        "2560x1440": { uploadCpuP50: 64.0, uploadCpuP95: 79.0, uploadGpuP50: null, uploadGpuP95: null },
        "3840x2160": { uploadCpuP50: 122.0, uploadCpuP95: 128.0, uploadGpuP50: null, uploadGpuP95: null },
    },
};

// Same detection categories docs/webgl/performance-results.md's table uses (Chrome/Edge/Firefox).
// Order matters: Edge's UA string also contains "Chrome/", so Edg/ must be checked first.
export function detectOptionDBrowserKey(userAgent) {
    if (/Firefox\//.test(userAgent)) return "firefox";
    if (/Edg\//.test(userAgent)) return "edge";
    if (/Chrome\//.test(userAgent)) return "chrome";
    return null;
}

export function optionDBrowserLabel(browserKey) {
    switch (browserKey) {
        case "chrome": return "Chrome";
        case "edge": return "Edge";
        case "firefox": return "Firefox";
        default: return "Unknown browser";
    }
}
