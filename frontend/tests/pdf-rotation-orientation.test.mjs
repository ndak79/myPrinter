import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const __dirname = dirname(fileURLToPath(import.meta.url));
const appJs = readFileSync(resolve(__dirname, '..', 'app.js'), 'utf8');

const context = vm.createContext({
  window: {
    location: { search: '' },
    addEventListener() {},
    VI_STRINGS: {},
    EN_STRINGS: {},
  },
  document: {
    title: '',
    addEventListener() {},
    createElement() {
      return {
        classList: { add() {}, contains() { return false; } },
        appendChild() {},
        querySelector() { return null; },
        querySelectorAll() { return []; },
      };
    },
    querySelectorAll() { return []; },
    getElementById() { return null; },
  },
  localStorage: {
    getItem() { return null; },
    setItem() {},
  },
  console,
  URLSearchParams,
  setTimeout() {},
  clearTimeout() {},
});

vm.runInContext(appJs, context);

const result = vm.runInContext(`
(() => {
  const calls = [];
  const page = {
    rotate: 90,
    getViewport({ scale, rotation }) {
      calls.push(rotation);
      return rotation % 180 === 0
        ? { width: 842 * scale, height: 595 * scale }
        : { width: 595 * scale, height: 842 * scale };
    },
  };

  return {
    intrinsic: RotationHelper.isLandscape(page),
    userRotated: RotationHelper.isLandscape(page, 'CW90'),
    calls,
  };
})()
`, context);

assert.equal(result.intrinsic, false, 'PDF /Rotate=90 must make a raw landscape box display as portrait');
assert.equal(result.userRotated, true, 'user CW90 rotation should add to the PDF intrinsic rotation');
assert.equal(JSON.stringify(result.calls), JSON.stringify([90, 180]));

const inkResult = vm.runInContext(`
(() => {
  function makeImage(width, height, verticalBands) {
    const data = new Uint8ClampedArray(width * height * 4);
    for (let i = 0; i < data.length; i += 4) {
      data[i] = 255;
      data[i + 1] = 255;
      data[i + 2] = 255;
      data[i + 3] = 255;
    }

    for (let band = 0; band < 8; band++) {
      const offset = 4 + band * 6;
      for (let a = 0; a < 3; a++) {
        for (let b = 5; b < 55; b++) {
          const x = verticalBands ? offset + a : b;
          const y = verticalBands ? b : offset + a;
          const idx = (y * width + x) * 4;
          data[idx] = 0;
          data[idx + 1] = 0;
          data[idx + 2] = 0;
        }
      }
    }
    return { data };
  }

  return {
    sideways: RotationHelper.inkLandscapeScore(makeImage(80, 80, true), 80, 80),
    upright: RotationHelper.inkLandscapeScore(makeImage(80, 80, false), 80, 80),
  };
})()
`, context);

assert.equal(inkResult.sideways, 1, 'sideways text bands should be treated as visual landscape');
assert.equal(inkResult.upright, -1, 'upright text bands should remain portrait');

const asyncResult = await vm.runInContext(`
(async () => {
  function makeSidewaysImage(width, height) {
    const data = new Uint8ClampedArray(width * height * 4);
    for (let i = 0; i < data.length; i += 4) {
      data[i] = 255;
      data[i + 1] = 255;
      data[i + 2] = 255;
      data[i + 3] = 255;
    }
    for (let band = 0; band < 8; band++) {
      const x0 = 4 + band * 6;
      for (let x = x0; x < x0 + 3; x++) {
        for (let y = 5; y < 55; y++) {
          const idx = (y * width + x) * 4;
          data[idx] = 0;
          data[idx + 1] = 0;
          data[idx + 2] = 0;
        }
      }
    }
    return { data };
  }

  globalThis.OffscreenCanvas = class {
    constructor(width, height) {
      this.width = width;
      this.height = height;
      this._image = makeSidewaysImage(width, height);
    }
    getContext() {
      return {
        getImageData: () => this._image,
      };
    }
  };

  const page = {
    rotate: 0,
    getViewport({ scale, rotation }) {
      return rotation % 180 === 0
        ? { width: 612 * scale, height: 792 * scale }
        : { width: 792 * scale, height: 612 * scale };
    },
    render() {
      return { promise: Promise.resolve() };
    },
  };

  return RotationHelper.detectLandscape(page);
})()
`, context);

assert.equal(asyncResult, true, 'portrait page boxes with sideways rendered ink should be visual landscape');

const profileResult = await vm.runInContext(`
(async () => {
  const visualCalls = [];
  const originalDetect = RotationHelper.detectVisualLandscape;
  RotationHelper.detectVisualLandscape = async (_page, _rotation) => {
    visualCalls.push(_page.pageNumber);
    return true;
  };

  const pdfDoc = {
    async getPage(pageNumber) {
      return {
        pageNumber,
        rotate: 0,
        cleaned: false,
        getViewport({ scale, rotation }) {
          return rotation % 180 === 0
            ? { width: 612 * scale, height: 792 * scale }
            : { width: 792 * scale, height: 612 * scale };
        },
        cleanup() { this.cleaned = true; },
      };
    },
  };

  try {
    const profile = await OrientationProfileModule.build(pdfDoc, 300, new Map());
    return {
      action: profile.action,
      rotation: profile.rotation,
      allLandscape: profile.allLandscape,
      visualCalls,
      autoRotationFirst: profile.pageRotations.get(1),
      autoRotationLast: profile.pageRotations.get(300),
    };
  } finally {
    RotationHelper.detectVisualLandscape = originalDetect;
  }
})()
`, context);

assert.equal(profileResult.action, 'auto-apply', 'uniform sideways-landscape portrait boxes should auto-apply profile rotation');
assert.equal(profileResult.rotation, 'CW90');
assert.equal(profileResult.allLandscape, true);
assert.equal(profileResult.autoRotationFirst, 'CW90');
assert.equal(profileResult.autoRotationLast, 'CW90');
assert.ok(
  profileResult.visualCalls.length <= 8,
  `orientation profile must sample at most 8 pages, got ${profileResult.visualCalls.length}`,
);
assert.equal(
  JSON.stringify(profileResult.visualCalls),
  JSON.stringify([1, 2, 43, 86, 129, 172, 215, 300]),
  'large documents should use stable representative sampling',
);

const mixedProfileResult = await vm.runInContext(`
(async () => {
  const visualByPage = new Map([[1, false], [2, true], [10, true]]);
  const originalDetect = RotationHelper.detectVisualLandscape;
  RotationHelper.detectVisualLandscape = async (page) => visualByPage.get(page.pageNumber) ?? true;

  const pdfDoc = {
    async getPage(pageNumber) {
      return {
        pageNumber,
        rotate: 0,
        getViewport({ scale, rotation }) {
          return rotation % 180 === 0
            ? { width: 612 * scale, height: 792 * scale }
            : { width: 792 * scale, height: 612 * scale };
        },
        cleanup() {},
      };
    },
  };

  try {
    const userRotations = new Map();
    const profile = await OrientationProfileModule.build(pdfDoc, 10, userRotations);
    return {
      action: profile.action,
      rotations: profile.pageRotations.size,
      userRotations: userRotations.size,
    };
  } finally {
    RotationHelper.detectVisualLandscape = originalDetect;
  }
})()
`, context);

assert.equal(mixedProfileResult.action, 'none', 'mixed visual orientation must not auto-rotate per page');
assert.equal(mixedProfileResult.rotations, 0, 'mixed visual orientation must not create hidden auto rotations');
assert.equal(mixedProfileResult.userRotations, 0, 'orientation profile must not mutate user pageRotations');

const sheetRenderStart = appJs.indexOf('async _renderSheetView(fileEntry)');
const sheetRenderEnd = appJs.indexOf('\n    // Render a page to blob URL', sheetRenderStart);
const sheetRenderBlock = sheetRenderStart >= 0 && sheetRenderEnd > sheetRenderStart
  ? appJs.slice(sheetRenderStart, sheetRenderEnd)
  : '';
assert.ok(sheetRenderBlock.length > 0, 'test must locate _renderSheetView');
assert.doesNotMatch(
  sheetRenderBlock,
  /detectVisualLandscape\(/,
  'sheet preview must not visually inspect every page; visual detection belongs in bounded OrientationProfileModule sampling',
);
assert.doesNotMatch(
  sheetRenderBlock,
  /pageRotations\.set\(p,\s*'CW90'\)/,
  'sheet preview must not mutate user pageRotations for auto-detected scanned landscape pages',
);

const startPrintBlock = appJs.match(/async _startPrint\(\)\s*\{[\s\S]*?\n    async _continuePrint/)?.[0] ?? '';
assert.ok(startPrintBlock.length > 0, 'test must locate _startPrint');
const duplexSideStart = startPrintBlock.indexOf('// Compute duplexSide');
const duplexSideEnd = startPrintBlock.indexOf('// Build request-local pageRotations', duplexSideStart);
const duplexSideBlock = duplexSideStart >= 0 && duplexSideEnd > duplexSideStart
  ? startPrintBlock.slice(duplexSideStart, duplexSideEnd)
  : '';
assert.ok(duplexSideBlock.length > 0, 'test must locate duplexSide block');
assert.match(duplexSideBlock, /_isPrintLandscapePage\(file, p\)/);
assert.match(startPrintBlock, /_ensureOrientationProfile\(file\)/, 'print path must build one orientation profile before deciding duplexSide and rotations');
assert.doesNotMatch(
  duplexSideBlock,
  /file\.landscapeMode === 'together'/,
  'duplex side must be based on printable page orientation, not the sheet-preview landscapeMode',
);

assert.doesNotMatch(
  appJs,
  /getViewport\(\{\s*scale:\s*1\s*,\s*rotation:\s*0\s*\}\)/,
  'orientation detection must not force rotation: 0 because that ignores PDF /Rotate metadata',
);
