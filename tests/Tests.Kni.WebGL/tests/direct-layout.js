// What Sample.Kni.WebGL draws in its SkiaDirectCanvas mode (samples/Sample.Kni.WebGL/Game1.cs), as pixel
// probes. Keep the sizes and colors in step with Game1.

const SQUARE = 128;
const GAP = 16;

const colors = {
  background: [20, 30, 200],
  before: [30, 180, 40],
  skiaStrip: [210, 50, 90],
  after: [240, 220, 10],
  a: [40, 90, 160],
  b: [220, 40, 40],
  c: [40, 200, 80],
  d: [250, 160, 20],
  e: [120, 40, 200],
};

// Layers: SpriteBatch (before) under Skia under SpriteBatch (after), like tests/Shared/DirectDrawLayering.cs.
// Each probe is covered by a known set of layers, so a vertical flip or a draw out of order fails one.
const layersProbes = [
  ["before only", SQUARE / 8, SQUARE / 8, colors.before],
  ["Skia over before", SQUARE * 3 / 8, SQUARE / 16, colors.skiaStrip],
  ["Skia alone, at the top", SQUARE * 5 / 8, SQUARE / 16, colors.skiaStrip],
  ["after over Skia", SQUARE * 5 / 8, SQUARE * 3 / 16, colors.after],
  ["background where a flipped Skia strip would be", SQUARE * 5 / 8, SQUARE * 15 / 16, colors.background],
  ["background", SQUARE / 8, SQUARE * 7 / 8, colors.background],
];

// KNI, Skia, KNI, Skia, KNI in turn, each over part of the last.
const interleavingProbes = [
  ["Skia B over KNI A", 16, 112, colors.b],
  ["KNI A alone", 112, 112, colors.a],
  ["KNI C over Skia B", 16, 16, colors.c],
  ["KNI C over KNI A", 112, 16, colors.c],
  ["Skia D over Skia B", 40, 88, colors.d],
  ["Skia D over KNI C", 88, 40, colors.d],
  ["KNI E over Skia D", 64, 64, colors.e],
  ["Skia D alone", 88, 88, colors.d],
];

const sceneCell = (width, height) => Math.floor(Math.min(width, height) / 2);

// The squares sit in a row below the Scene's cells: the back buffer's layers, the interleaving, then the
// layers in a plain render target and in a 4x multisampled one, both drawn onto the back buffer.
function regionOrigin(index, width, height) {
  return { x: GAP + index * (SQUARE + GAP), y: sceneCell(width, height) + GAP };
}

const regions = [
  { name: "back buffer layers", probes: layersProbes },
  { name: "back buffer interleaving", probes: interleavingProbes },
  { name: "render target layers", probes: layersProbes },
  { name: "multisampled render target layers", probes: layersProbes, msaa: true },
];

// Every probe as an absolute canvas pixel (top-left origin): [label, x, y, expected rgb].
function probesFor(regionIndex, width, height) {
  const origin = regionOrigin(regionIndex, width, height);
  return regions[regionIndex].probes.map(([label, x, y, color]) => [label, origin.x + x, origin.y + y, color]);
}

// Reads canvas pixels once, inside a requestAnimationFrame after the sample's own draw, so every probe
// comes from the same frame. Runs in the page (Playwright's evaluate or BiDi's script.evaluate).
const readPixelsInPage = `(points) => new Promise(resolve => requestAnimationFrame(() => {
  const element = document.getElementById("theCanvas");
  const gl = element.getContext("webgl2");
  const pixels = points.map(([x, y]) => {
    const pixel = new Uint8Array(4);
    // readPixels counts rows from the bottom.
    gl.readPixels(x, element.height - 1 - y, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);
    return Array.from(pixel);
  });
  resolve({ width: element.width, height: element.height, pixels });
}))`;

const TOLERANCE = 3;

function mismatches(probes, pixels) {
  const errors = [];
  probes.forEach(([label, x, y, expected], i) => {
    const actual = pixels[i];
    const off = expected.some((channel, c) => Math.abs(channel - actual[c]) > TOLERANCE) || actual[3] < 252;
    if (off)
      errors.push(`${label} at (${x}, ${y}): expected ${expected}, got ${actual.slice(0, 3)} alpha ${actual[3]}`);
  });
  return errors;
}

module.exports = { SQUARE, GAP, colors, regions, regionOrigin, probesFor, readPixelsInPage, mismatches, sceneCell };
