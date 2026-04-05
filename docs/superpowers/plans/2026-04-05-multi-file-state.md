# Multi-File AppState Refactor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor AppState from single-file to multi-file array so users can upload multiple files and manage them independently, each with their own page selections, rotations, and print order.

**Architecture:** `AppState.files[]` replaces `AppState.uploadedFile`. Each file entry is a `FileEntry` object carrying its own `pdfDoc`, `selectedPages`, `singleSidedPages`, `pageOrder`, `pageRotations`, `totalPageCount`. `AppState.activeFileIndex` tracks which file is currently active. All modules that read `AppState.uploadedFile / currentPdfDoc / selectedPages / etc.` are updated to read from `AppState.activeFile` getter instead. UploadModule appends rather than replaces.

**Tech Stack:** Vanilla JS (no framework), existing module pattern

---

## File Map

| File | Change |
|------|--------|
| `frontend/app.js:21-49` | Replace AppState single-file fields with `files[]` array + `activeFileIndex` + `activeFile` getter |
| `frontend/app.js` UploadModule `_upload()` | Append to `AppState.files`, set `activeFileIndex`, update tab UI |
| `frontend/app.js` UploadModule `_remove(fileIndex)` | Remove specific file from array, switch active to adjacent |
| `frontend/app.js` PreviewModule `render()` | Read from `AppState.activeFile.pdfDoc` etc. |
| `frontend/app.js` PrintPreviewModule | Per-file cache maps (`_thumbCache`, `_mainCache` keyed by `fileId+pageNum`) |
| `frontend/app.js` PageSelectModule | Read/write `AppState.activeFile.selectedPages` |
| `frontend/app.js` PrintModule | Iterate all files to build print job |
| `frontend/index.html` | Add `#file-tabs` container; remove single `#file-info` element |

---

## Data Model

```javascript
// FileEntry — one per uploaded file
{
    id:              'abc123',        // fileId from backend
    name:            'report.pdf',   // original filename
    needsConversion: false,
    pdfDoc:          null,           // PDFDocumentProxy once loaded
    totalPageCount:  0,
    selectedPages:   new Set(),
    singleSidedPages: new Set(),
    pageOrder:       [],             // 1-based, empty = natural
    pageRotations:   new Map(),      // Map<pageNum, 'cw90'|'ccw90'|'180'>
}
```

---

## Task 1: Refactor AppState to multi-file

**Files:**
- Modify: `frontend/app.js:21-49`

- [ ] **Step 1: Replace AppState definition**

Replace the entire `AppState` object (lines 21-49) with:

```javascript
const AppState = {
    selectedPrinter:       null,
    currentJob:            null,
    isUserTypingPageRange: false,

    // Multi-file
    files:           [],   // FileEntry[]
    activeFileIndex: -1,   // index into files[], -1 = no file loaded

    // Getter — always use this instead of direct field access
    get activeFile() {
        return this.files[this.activeFileIndex] ?? null;
    },

    // Legacy shims — keep these so untouched code still works during migration
    get uploadedFile()    { return this.activeFile ? { id: this.activeFile.id, name: this.activeFile.name, needsConversion: this.activeFile.needsConversion } : null; },
    get currentPdfDoc()   { return this.activeFile?.pdfDoc ?? null; },
    set currentPdfDoc(v)  { if (this.activeFile) this.activeFile.pdfDoc = v; },
    get selectedPages()   { return this.activeFile?.selectedPages ?? new Set(); },
    set selectedPages(v)  { if (this.activeFile) this.activeFile.selectedPages = v; },
    get singleSidedPages(){ return this.activeFile?.singleSidedPages ?? new Set(); },
    set singleSidedPages(v){ if (this.activeFile) this.activeFile.singleSidedPages = v; },
    get totalPageCount()  { return this.activeFile?.totalPageCount ?? 0; },
    set totalPageCount(v) { if (this.activeFile) this.activeFile.totalPageCount = v; },
    get pageOrder()       { return this.activeFile?.pageOrder ?? []; },
    set pageOrder(v)      { if (this.activeFile) this.activeFile.pageOrder = v; },
    get pageRotations()   { return this.activeFile?.pageRotations ?? new Map(); },
    set pageRotations(v)  { if (this.activeFile) this.activeFile.pageRotations = v; },

    // Create a new FileEntry
    createFileEntry(id, name, needsConversion) {
        return {
            id, name, needsConversion,
            pdfDoc:           null,
            totalPageCount:   0,
            selectedPages:    new Set(),
            singleSidedPages: new Set(),
            pageOrder:        [],
            pageRotations:    new Map(),
        };
    },

    // Add a file and make it active
    addFile(entry) {
        this.files.push(entry);
        this.activeFileIndex = this.files.length - 1;
    },

    // Remove a file by index; switches active to nearest remaining
    removeFile(index) {
        this.files.splice(index, 1);
        if (this.files.length === 0) {
            this.activeFileIndex = -1;
        } else {
            this.activeFileIndex = Math.min(index, this.files.length - 1);
        }
    },

    // Switch active file
    setActiveFile(index) {
        if (index >= 0 && index < this.files.length) {
            this.activeFileIndex = index;
        }
    },

    selectAllPages() {
        const f = this.activeFile;
        if (!f) return;
        f.selectedPages = new Set();
        for (let i = 1; i <= f.totalPageCount; i++) f.selectedPages.add(i);
    },

    // Full reset (used when all files removed)
    reset() {
        this.files           = [];
        this.activeFileIndex = -1;
        this.currentJob      = null;
        this.isUserTypingPageRange = false;
    },
};
```

- [ ] **Step 2: Build check — open app in browser**

Open `frontend/index.html` in browser (via WebView2 or direct). Open DevTools console. Verify no JS errors on load. Type `AppState.activeFile` in console — should return `null`.

- [ ] **Step 3: Commit**

```bash
git add frontend/app.js
git commit -m "refactor(state): multi-file AppState with legacy shims for backward compat"
```

---

## Task 2: UploadModule — append files instead of replace

**Files:**
- Modify: `frontend/app.js` UploadModule `_upload()` ~line 800-864

- [ ] **Step 1: Update _upload to append and call TabsModule**

Find `_upload(file)` in UploadModule. Replace the section that sets `AppState.uploadedFile` (around line 841) and the UI show/hide block (lines 848-856):

```javascript
// OLD:
// AppState.uploadedFile = { id: result.fileId, name: result.originalFileName, needsConversion: ext !== '.pdf' };
// document.getElementById('file-name').textContent = ...
// document.getElementById('upload-area').classList.add('hidden');
// document.getElementById('file-info').classList.remove('hidden');

// NEW:
const entry = AppState.createFileEntry(result.fileId, result.originalFileName, ext !== '.pdf');

if (entry.needsConversion) {
    showToast('Đang chuyển đổi sang PDF...', 'info');
    await fetch(`${API_BASE}/convert?fileId=${entry.id}`, { method: 'POST' });
}

AppState.addFile(entry);
TabsModule.render();               // render/update the file tabs UI
TabsModule.setActive(AppState.activeFileIndex);

await PreviewModule.render(entry.id);
PrintModule.updateButton();
showToast(`Đã tải file ${entry.name}`, 'success');
StepIndicatorModule.update();
SRModule.announce(`Đã tải file ${entry.name}, ${AppState.totalPageCount} trang`);
```

Remove the old `needsConversion` fetch block (previously around line 843-846) since it's now inline above.

- [ ] **Step 2: Update _remove to remove specific file by index**

Replace `_remove()` with:

```javascript
_remove(index) {
    const f = AppState.files[index];
    if (!f) return;

    // Destroy pdf.js document to free memory
    f.pdfDoc?.destroy();

    AppState.removeFile(index);
    TabsModule.render();

    if (AppState.activeFile) {
        // Switch preview to the new active file
        PreviewModule.render(AppState.activeFile.id);
    } else {
        // No files left — reset UI to upload state
        PrintPreviewModule.clear();
    }

    PrintModule.updateButton();
    StepIndicatorModule.update();
    if (AppState.files.length === 0) {
        document.getElementById('page-range-section')?.classList.add('hidden');
        const pi = document.getElementById('page-range-input');
        if (pi) pi.value = '';
        document.getElementById('file-input').value = '';
    }
},
```

- [ ] **Step 3: Allow multiple file input selection**

In `UploadModule.init()`, find the `<input type="file">` event listener. Update to handle multiple files dropped/selected one-by-one:

```javascript
// For drag-drop: iterate all dropped files
area.addEventListener('drop', async e => {
    e.preventDefault();
    area.classList.remove('drag-over');
    for (const file of e.dataTransfer.files) {
        await this._upload(file);
    }
});

// For file input: allow multiple selection
input.addEventListener('change', async e => {
    for (const file of e.target.files) {
        await this._upload(file);
    }
    e.target.value = ''; // reset so same file can be re-added
});
```

Also add `multiple` attribute to the file input in `index.html`:
```html
<input type="file" id="file-input" multiple accept=".pdf,.doc,.docx,.jpg,.jpeg,.png">
```

- [ ] **Step 4: Test uploading 2 files**

Upload file A → verify 1 tab appears. Upload file B → verify 2 tabs appear, tab B is active, preview shows B's pages. No console errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/app.js frontend/index.html
git commit -m "feat(upload): support multi-file append, _remove by index, multi-drop"
```

---

## Task 3: TabsModule — new module managing file tabs UI

**Files:**
- Modify: `frontend/app.js` — add `TabsModule` object before `DOMContentLoaded`
- Modify: `frontend/index.html` — add `#file-tabs` container

- [ ] **Step 1: Add #file-tabs container to index.html**

In `frontend/index.html`, find the main layout. After the top settings bar (Row 1), add:

```html
<!-- Row 2: File tabs -->
<div id="file-tabs" class="file-tabs-bar" role="tablist" aria-label="Các file đang mở">
  <!-- Tabs rendered by TabsModule.render() -->
</div>
```

- [ ] **Step 2: Add TabsModule to app.js**

Add this module before the `DOMContentLoaded` block:

```javascript
// ═══════════════════════════════════════════════════════════════════
// TabsModule — Manages file tab strip (Row 2 of header)
// ═══════════════════════════════════════════════════════════════════
const TabsModule = {
    _container: null,

    init() {
        this._container = document.getElementById('file-tabs');
        this.render();
    },

    render() {
        if (!this._container) return;
        this._container.innerHTML = '';

        AppState.files.forEach((f, i) => {
            const tab = document.createElement('button');
            tab.className = 'file-tab' + (i === AppState.activeFileIndex ? ' active' : '');
            tab.setAttribute('role', 'tab');
            tab.setAttribute('aria-selected', i === AppState.activeFileIndex ? 'true' : 'false');
            tab.title = f.name;

            const icon = document.createElement('span');
            icon.className = 'file-tab-icon';
            icon.textContent = '📄';

            const label = document.createElement('span');
            label.className = 'file-tab-label';
            // Truncate long filenames
            label.textContent = f.name.length > 20 ? f.name.slice(0, 18) + '…' : f.name;

            const close = document.createElement('button');
            close.className = 'file-tab-close';
            close.textContent = '×';
            close.setAttribute('aria-label', `Đóng ${f.name}`);
            close.addEventListener('click', e => {
                e.stopPropagation();
                UploadModule._remove(i);
            });

            tab.appendChild(icon);
            tab.appendChild(label);
            tab.appendChild(close);

            tab.addEventListener('click', () => this.setActive(i));
            this._container.appendChild(tab);
        });

        // [+] Add file button
        const addBtn = document.createElement('button');
        addBtn.className = 'file-tab-add';
        addBtn.textContent = '+ Thêm file';
        addBtn.setAttribute('aria-label', 'Thêm file mới');
        addBtn.addEventListener('click', () => {
            document.getElementById('file-input')?.click();
        });
        this._container.appendChild(addBtn);
    },

    setActive(index) {
        if (index === AppState.activeFileIndex && AppState.activeFile?.pdfDoc) return;
        AppState.setActiveFile(index);
        this.render(); // re-render tabs to update active state

        // Switch preview and thumbnail to this file, jump to page 1
        const f = AppState.activeFile;
        if (f?.pdfDoc) {
            PrintPreviewModule.jumpToFile(f);
        } else if (f) {
            PreviewModule.render(f.id);
        }

        PageSelectModule.updateDisplay();
        PrintModule.updateButton();
    },
},
```

- [ ] **Step 3: Add TabsModule.init() in DOMContentLoaded**

Find the `DOMContentLoaded` event handler at the bottom of app.js. Add `TabsModule.init();` alongside other module inits.

- [ ] **Step 4: Commit**

```bash
git add frontend/app.js frontend/index.html
git commit -m "feat(tabs): TabsModule renders file tabs, handles active switching"
```

---

## Task 4: PrintPreviewModule — per-file cache + jumpToFile

**Files:**
- Modify: `frontend/app.js` `PrintPreviewModule` (~line 56)

- [ ] **Step 1: Key caches by fileId+pageNum**

The current `_thumbCache` and `_mainCache` use `pageNum` as key. With multiple files, page 1 of file A and page 1 of file B would collide. Update the key to `${fileId}-${pageNum}`.

At the top of `PrintPreviewModule`, change:
```javascript
_thumbCache: new Map(),   // 'fileId-pageNum' → offscreen canvas
_mainCache:  new Map(),   // 'fileId-pageNum' → offscreen canvas
```

Update all `_thumbCache.get(pageNum)` / `_thumbCache.set(pageNum, ...)` to use:
```javascript
const key = `${AppState.activeFile?.id}-${pageNum}`;
this._thumbCache.get(key)
this._thumbCache.set(key, off)
```

Do the same for `_mainCache`.

- [ ] **Step 2: Add jumpToFile(fileEntry) method**

Add this method to `PrintPreviewModule`:

```javascript
jumpToFile(fileEntry) {
    // Update the pdfDoc reference for the module's rendering pipeline
    // (rendering reads from AppState.currentPdfDoc which now proxies to activeFile.pdfDoc)

    // Clear only the main view DOM and scroll to top — keep caches for other files
    const mainContainer = document.getElementById('preview-main-canvas-container')
        ?? document.querySelector('.preview-main-canvas-container');
    if (mainContainer) mainContainer.scrollTop = 0;

    const thumbContainer = document.querySelector('.preview-thumb-grid');
    if (thumbContainer) thumbContainer.scrollTop = 0;

    // Re-render the entire preview for the new active file
    this._activePage = 1;
    this._render(fileEntry.pdfDoc);
},
```

Where `_render(pdfDoc)` is the existing method that builds placeholder DOM elements and sets up IntersectionObservers. If the existing `open()` method calls `_render()`, refactor so `_render()` accepts a pdfDoc parameter (defaulting to `AppState.currentPdfDoc`) to allow calling it mid-session.

- [ ] **Step 3: Test switching tabs jumps preview**

Upload 2 PDFs. Open Print Preview. Click tab 1 → thumbnails show file 1's pages, preview scrolled to top. Click tab 2 → thumbnails show file 2's pages, preview scrolled to top.

- [ ] **Step 4: Commit**

```bash
git add frontend/app.js
git commit -m "feat(preview): per-file cache keys, jumpToFile scrolls to page 1"
```

---

## Task 5: PrintModule — print all files in tab order

**Files:**
- Modify: `frontend/app.js` PrintModule (find the print API call)

- [ ] **Step 1: Update print payload to include all files**

Find where `POST /api/print` is called in PrintModule. The current payload sends a single `fileId`. Update to send all files in order:

```javascript
// Build per-file print specs
const fileSpecs = AppState.files
    .filter(f => f.selectedPages.size > 0)
    .map(f => ({
        fileId:          f.id,
        pages:           [...f.selectedPages].sort((a, b) => a - b),
        pageOrder:       f.pageOrder,
        pageRotations:   Object.fromEntries(f.pageRotations),
        singleSidedPages: [...f.singleSidedPages],
    }));

const payload = {
    printer:   AppState.selectedPrinter,
    printMode: /* existing mode selection */,
    files:     fileSpecs,
};
```

- [ ] **Step 2: Update backend to accept files array (or keep single-file and loop)**

**Simplest approach:** Keep the backend single-file endpoint and call it once per file sequentially from the frontend:

```javascript
for (const spec of fileSpecs) {
    await fetch(`${API_BASE}/print`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            printer:         AppState.selectedPrinter,
            printMode:       currentMode,
            fileId:          spec.fileId,
            pages:           spec.pages,
            pageOrder:       spec.pageOrder,
            pageRotations:   spec.pageRotations,
            singleSidedPages: spec.singleSidedPages,
        }),
    });
}
```

This avoids any backend changes for now.

- [ ] **Step 3: Update PrintModule.updateButton() to check any file has pages selected**

```javascript
updateButton() {
    const btn = document.getElementById('print-btn')
             ?? document.getElementById('print-preview-btn');
    if (!btn) return;
    const hasPrinter = !!AppState.selectedPrinter;
    const hasPages   = AppState.files.some(f => f.selectedPages.size > 0);
    btn.disabled     = !(hasPrinter && hasPages);
},
```

- [ ] **Step 4: Test multi-file print**

Upload 2 files. Select pages from each. Click Print. Verify both jobs are sent to the printer (check network tab in DevTools for 2 POST /api/print requests).

- [ ] **Step 5: Commit**

```bash
git add frontend/app.js
git commit -m "feat(print): send all files in tab order, updateButton checks any file"
```

---

## Verification Checklist

After all tasks complete:

- [ ] Upload 3 PDFs — 3 tabs appear, each with correct filename
- [ ] Click each tab — preview & thumbnails switch to that file, scrolled to page 1
- [ ] Remove middle tab — remaining tabs reindex, adjacent tab becomes active
- [ ] `AppState.activeFile` returns correct FileEntry in console
- [ ] Page rotations on file A are preserved when switching to B and back
- [ ] Print with 2 files: 2 separate print jobs sent
- [ ] No JS errors in console throughout
