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

assert.doesNotMatch(
  appJs,
  /getViewport\(\{\s*scale:\s*1\s*,\s*rotation:\s*0\s*\}\)/,
  'orientation detection must not force rotation: 0 because that ignores PDF /Rotate metadata',
);
