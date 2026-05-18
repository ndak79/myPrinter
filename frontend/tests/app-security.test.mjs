import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const __dirname = dirname(fileURLToPath(import.meta.url));
const appJs = readFileSync(resolve(__dirname, '..', 'app.js'), 'utf8');

class FakeElement {
  constructor() {
    this.children = [];
    this.innerHTML = '';
    this.className = '';
    this.classList = {
      contains: () => false,
      add: () => {},
    };
  }

  appendChild(child) {
    this.children.push(child);
    this.lastChild = child;
    return child;
  }

  querySelectorAll() {
    return [];
  }

  addEventListener() {}
}

const toastContainer = new FakeElement();

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
    createElement() { return new FakeElement(); },
    querySelectorAll() { return []; },
    getElementById(id) { return id === 'toast-container' ? toastContainer : null; },
  },
  localStorage: {
    getItem() { return null; },
    setItem() {},
  },
  console,
  URLSearchParams,
  setTimeout() {},
  clearTimeout,
});

vm.runInContext(appJs, context);

const escaped = vm.runInContext(
  "escapeHtml(`<img src=x onerror=alert(1)> & \"'`)",
  context,
);

assert.equal(
  escaped,
  '&lt;img src=x onerror=alert(1)&gt; &amp; &quot;&#39;',
);

vm.runInContext(
  "ToastModule.show(`<img src=x onerror=alert(1)>`, 'error', 1000)",
  context,
);

assert.match(toastContainer.lastChild.innerHTML, /&lt;img src=x onerror=alert\(1\)&gt;/);
assert.doesNotMatch(toastContainer.lastChild.innerHTML, /<img src=x/);
