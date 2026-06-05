import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const __dirname = dirname(fileURLToPath(import.meta.url));
const appJs = readFileSync(resolve(__dirname, '..', 'app.js'), 'utf8');

class FakeClassList {
  constructor() {
    this.values = new Set();
  }

  add(value) {
    this.values.add(value);
  }

  contains(value) {
    return this.values.has(value);
  }
}

class FakeElement {
  constructor() {
    this.children = [];
    this.innerHTML = '';
    this.className = '';
    this.classList = new FakeClassList();
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

  remove() {
    this.removed = true;
  }
}

const toastContainer = new FakeElement();
const timers = [];

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
    getElementById(id) {
      if (id === 'toast-container') return toastContainer;
      return null;
    },
  },
  localStorage: {
    getItem() { return null; },
    setItem() {},
  },
  console,
  URLSearchParams,
  setTimeout(fn, duration) {
    timers.push({ fn, duration });
    return timers.length;
  },
  clearTimeout() {},
});

vm.runInContext(appJs, context);

const persistentHandle = vm.runInContext(
  "ToastModule.showPersistent('Dang chuyen doi test.docx...', 'info')",
  context,
);

assert.equal(typeof persistentHandle.dismiss, 'function');
assert.equal(toastContainer.children.length, 1);
assert.equal(timers.length, 0, 'persistent conversion toast must not auto-dismiss on a timer');
assert.match(toastContainer.lastChild.innerHTML, /toast-countdown-persistent/);

persistentHandle.dismiss();
assert.equal(toastContainer.lastChild.classList.contains('dismissing'), true);

const uploadStart = appJs.indexOf('async _upload(file)');
const uploadEnd = appJs.indexOf('removeFile(index)', uploadStart);
const uploadBlock = uploadStart >= 0 && uploadEnd > uploadStart ? appJs.slice(uploadStart, uploadEnd) : '';
const showIndex = uploadBlock.indexOf("ToastModule.showPersistent(I18nModule.t('toast.converting')(entry.name)");
const convertIndex = uploadBlock.indexOf("await fetch(`${API_BASE}/convert?fileId=${entry.id}`");
const previewLoadIndex = uploadBlock.indexOf('await PreviewModule.renderEntry(entry)');
const previewPanelIndex = uploadBlock.indexOf('PreviewPanelModule.render(AppState.activeFile)');
const dismissIndex = uploadBlock.lastIndexOf('convertingToast?.dismiss()');

assert.ok(showIndex >= 0, 'DOC/DOCX conversion must use a persistent toast handle');
assert.ok(convertIndex > showIndex, 'conversion request must happen after showing the persistent toast');
assert.ok(previewLoadIndex > convertIndex, 'PDF preview document must load before dismissing conversion toast');
assert.ok(previewPanelIndex > previewLoadIndex, 'visible preview panel must render before dismissing conversion toast');
assert.ok(dismissIndex > previewPanelIndex, 'conversion toast must dismiss only after preview is available on screen');
