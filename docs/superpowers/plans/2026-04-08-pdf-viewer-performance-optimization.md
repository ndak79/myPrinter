# PDF Viewer Performance Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make switching between loaded PDF files feel near-instant and reduce first-render latency for scan/image-heavy PDFs without rewriting the viewer architecture.

**Architecture:** Keep per-file DOM alive (separate roots for page-view and sheet-view) instead of destroying on every switch. Classify `render()` calls into 3 types so only the right amount of work is done per call. Reduce first-render stalls by enabling PDF.js auto-fetch and removing unnecessary JPEG re-encoding. Preserve the current Vanilla JS + PDF.js module structure.

**Tech Stack:** Vanilla JavaScript, PDF.js v5, OffscreenCanvas, existing `LRUBlobCache`, existing `CanvasPool`

---

## Root Cause Summary

### Problem 1 — File switching re-renders everything
`TabsModule.setActive()` calls `ThumbStripModule.render()` and `PreviewPanelModule.render()`.
Both start with `this._container.innerHTML = ''` — destroying and recreating all DOM even for previously-viewed files.
Even with the LRU pixel cache intact, every switch still: creates N DOM nodes, attaches event listeners, measures layout, and re-enqueues renders.

### Problem 2 — Scan PDF slow on first render
`_renderBlobPage()` pipeline for sheet/thumb: `getPage → render → OffscreenCanvas → convertToBlob(JPEG) → createObjectURL`
For scan PDFs (large JPEG/JBIG2 images per page), the re-encode step is expensive.
Also: `disableAutoFetch: true` prevents PDF.js from warming data chunks proactively, adding latency to the very first `getPage()` call.

---

## Critical Design Decisions

### D1: Two DOM roots per file, per view mode

`PreviewPanelModule.render()` routes to either `_renderPage` path or `_renderSheetView` path depending on `_viewMode`.
Sheet view builds a fundamentally different DOM structure (`sheet-view-inner > sheets-column + ejected-column`), not a flat card list.

**Therefore:** each file needs two separate roots:
- `pageRoot` — holds flat `.preview-page-card` elements; only created once per file; never rebuilt unless `pageOrder.length` changes
- `sheetRoot` — holds the sheet layout; always rebuilt when layout-affecting state changes (selection, blanks, rotation). This is unavoidable — layout depends on `selectedPages`, `blankAbsorbedBy`, `pageOrder`

### D2: Three classes of `render()` callers

`PreviewPanelModule.render()` is called from 22+ sites. Each call must be understood as one of 3 types:

| Type | Example trigger | Correct behavior after refactor |
|---|---|---|
| **Switch** | `TabsModule.setActive()` | Hide old root, show new root. Zero DOM work if already rendered and `pageOrder.length` unchanged. |
| **State-sync** | Page selection toggle in page-view only | Call `_syncSelectionUI()` — no DOM rebuild. Already handled by `onStateChanged()`. |
| **Layout-rebuild** | Sheet toggle, blank add/remove, rotation in sheet-view, page card click in sheet-view | Full `_renderSheetView()` rebuild — unavoidable, scoped to active file's `sheetRoot`. |

**Important:** Sheet-view page card click listeners (inside `_renderSheetView()`) call `PreviewPanelModule.render(entry)` directly (B3 fix, ~line 4085). This is a **Layout-rebuild** call — it must continue calling `render()` full, not `_syncSelectionUI()`. Do not change these listeners.

### D3: Invariant 3 reset must happen before any early-return guard

`render()` has a required invariant: `fileEntry.blankAbsorbedBy = new Map()` must be reset at the top of every `render()` call (Invariant 3, line ~3768). This reset must occur **before** any `isNew === false` early-return guard. If the guard fires before the reset, `blankAbsorbedBy` will be stale and cause R7/B7 bugs in blank page absorption logic.

### D4: Page root early-return must validate `pageOrder.length`

The `isNew === false` early-return (file already has a pageRoot) is only valid if the number of DOM cards matches `fileEntry.pageOrder.length`. If a blank page was inserted (`pageOrder.push(0)`), the existing root is missing a card. The guard must check this:

```js
const domCardCount = root.querySelectorAll('.preview-page-card').length;
if (domCardCount !== fileEntry.pageOrder.length) {
    // pageOrder changed — must rebuild this root
    root.innerHTML = '';
    this._pageEls.forEach((_, k) => { if (k.startsWith(fileEntry.id + '-')) this._pageEls.delete(k); });
    // fall through to card creation loop
} else {
    this._syncSelectionUI();
    this._container.scrollTop = 0;
    requestAnimationFrame(() => this._renderVisible());
    return;
}
```

### D5: Never measure DOM on hidden roots

`_unmountOffScreen()` writes `el.style.minHeight = el.offsetHeight + 'px'` before zeroing canvas.
Elements inside `display: none` return `offsetHeight = 0` → minHeight = 0px → layout collapses when shown again.
**Rule:** `_unmountOffScreen()` must only run on the currently visible root. Always guard with `if (!activeRoot.contains(el)) return;`.

### D6: File removal must reset render counters

When `removeFileRoot()` cancels in-flight render tasks, the `_activeBlobRenders` and `_activeRenders` counters may become stale (cancelled tasks' `.finally()` callbacks may not fire, leaving counters above zero permanently). After cancelling tasks for the removed file, recalculate the counter from surviving tasks:

```js
this._activeRenders     = this._renderTasks.size;
this._activeBlobRenders = 0; // blob tasks are not tracked by key, safe to reset
```

**Important for ThumbStripModule:** `_renderThumb()` tasks are tracked by `_activeRenders` counter but NOT stored in `_renderTasks` Map (they use a bare `await` path). Therefore `ThumbStripModule.removeFileRoot()` must set `this._activeRenders = 0` (not `this._renderTasks.size`) to avoid over-spawning.

### D7: File removal must capture `fileId` before `AppState.removeFile()`

`AppState.removeFile(index)` splices the file out of the array. Always capture `removedId = AppState.files[index]?.id` before calling it.

### D8: JPEG blob re-encode is NOT in page-view hot path

`_renderPage()` (page-view) uses `CanvasPool` + `drawImage` directly — **no blob encoding**.
`_renderBlobPage()` (JPEG encode) is only called by `_renderSheetPage()` and `ThumbStripModule._renderThumb()`.
Task 6 targets sheet/thumb paths only, not page-view.

### D9: CSS `display: flex` instead of `display: contents` for file roots

`display: contents` has known edge cases in WebView2 (Chromium) with accessibility tree and certain `getBoundingClientRect` scenarios. Use `display: flex; flex-direction: column; gap: 20px` on the file root instead, and remove `gap` from `.preview-panel` to avoid double-gap. This is functionally equivalent and more reliable.

### D10: `data-file-index` on cached thumb items must be refreshed after drag-to-reorder

`ThumbStripModule` thumb items store `data-file-index` at creation time (line 4612). `_setActiveHighlight()` queries by `[data-file-index="N"]` at call time. After `TabsModule._reorderFiles()`, the active file moves to a new index — but cached thumb roots still hold items with the OLD index. On the early-return cache-hit path, `_setActiveHighlight()` will find no match → no active highlight.

**Fix:** On the `isNew === false` early-return path in `ThumbStripModule.render()`, refresh `data-file-index` before returning:
```js
const currentIdx = AppState.activeFileIndex;
root.querySelectorAll('.thumb-item').forEach(el => {
    el.dataset.fileIndex = currentIdx;
});
```

### D11: `disableAutoFetch` affects two separate functions

There are two `pdfjsLib.getDocument(...)` call sites:
- `PreviewModule.render(fileId)` — legacy path (~line 1576)
- `PreviewModule.renderEntry(entry)` — multi-file path (~line 1602)

Both must have `disableAutoFetch: true` removed.

### D12: `_renderVisible()` in ThumbStripModule must be scoped to the active file root

After Task 4 DOM persistence, `this._container` holds ALL file roots (hidden + visible). `_renderVisible()` currently does:
```js
this._container.querySelectorAll('.thumb-item:not(.rendered)').forEach(...)
```
This will find `.thumb-item` elements inside hidden roots (display:none). Those elements return `getBoundingClientRect()` as all-zeros, and depending on the lookahead formula they may pass the visibility check. The selector must be scoped to visible roots only.

**Correct selector:** scope the querySelectorAll to `activeThumbRoot` (see Task 4 Step 5).

### D13: `PreviewPanelModule.clear()` and `ThumbStripModule.render()` (empty state) must reset Maps

`clear()` (called when all files are removed) does `this._container.innerHTML = ...`. After Tasks 1–4, `_pageRoots`, `_sheetRoots`, and `_fileRoots` Maps still hold references to now-detached DOM nodes. When a new file is uploaded after `clear()`, `_getOrCreatePageRoot()` finds the stale root in the Map and reuses a detached orphan — the `appendChild` call does not re-attach it correctly.

**Fix:** `PreviewPanelModule.clear()` must also call:
```js
this._pageRoots.clear();
this._sheetRoots.clear();
this._pageEls.clear();
this._sheetEls.clear();
```

`ThumbStripModule.render()` early-exit for `files.length === 0` must also reset:
```js
this._fileRoots?.clear();
```

### D14: `_renderSheetView()` is async — `_pageEls` entries for old sheet cards must be cleared immediately

`_renderSheetView()` starts with `sheetRoot.innerHTML = ''` (clearing the DOM) then does async `await Promise.all(...)` for orientation detection. During those awaits, scroll events can fire and `_renderVisible()` / `_onScroll()` iterate `_pageEls`. After `sheetRoot.innerHTML = ''`, the old sheet cards stored in `_pageEls` are detached — `getBoundingClientRect()` returns all-zeros on detached elements, which may cause `_enqueueBlob()` to be called on ghost entries.

**Fix:** Immediately after `sheetRoot.innerHTML = ''` in `_renderSheetView()`, also clear `_pageEls` entries for this file:
```js
for (const k of [...this._pageEls.keys()]) {
    if (k.startsWith(fileEntry.id + '-')) this._pageEls.delete(k);
}
```

### D15: `CanvasPool` holds `HTMLCanvasElement`; `LRUBlobCache` holds `OffscreenCanvas` — do not conflate

`CanvasPool.release()` calls `canvas.getContext('2d')` expecting an `HTMLCanvasElement`. `_renderBlobPage()` creates `new OffscreenCanvas(...)` and stores it in `LRUBlobCache`. When `LRUBlobCache` evicts an entry it calls `CanvasPool.release(entry.canvas)` — passing an OffscreenCanvas into a pool meant for HTMLCanvasElement. This is a pre-existing bug **not introduced by this plan**. Do not attempt to fix it in this optimization pass.

**Consequence for Task 6:** The `ImageBitmap` / `transferToImageBitmap()` fast-path described in earlier drafts is invalid. `CanvasPool.acquire()` returns `document.createElement('canvas')` — an HTMLCanvasElement — which does NOT have `transferToImageBitmap()`. Any attempt to call it will throw `TypeError` and silently fall through to the existing `drawImage` path. Task 6 Step 2 is therefore removed — it would be dead code.

### D16: `data-file-index` on cached thumb items becomes stale after drag-to-reorder

`ThumbStripModule` thumb items store `data-file-index` at creation time. `_setActiveHighlight()` queries by `[data-file-index="N"]` at call time. After `TabsModule._reorderFiles()`, the active file moves to a new index — but cached thumb roots still hold items with the old index. On the early-return cache-hit path, `_setActiveHighlight()` finds no match → no active highlight.

**Fix:** On the `isNew === false` early-return path in `ThumbStripModule.render()`, refresh `data-file-index` before returning (see Task 4 Step 3).

---

## File Map

**Primary file to modify**
- `frontend/app.js`
  - `PreviewPanelModule` — add `_pageRoots`, `_sheetRoots`, helpers, hide/show logic, cleanup method, render counter reset
  - `ThumbStripModule` — add `_fileRoots`, helpers, hide/show logic, cleanup method, scoped `_renderVisible()`
  - `PreviewPanelModule._unmountOffScreen()` — guard: active root only
  - `ThumbStripModule._unmountOffScreen()` — guard: active root only
  - `PreviewPanelModule.clear()` — reset Maps
  - `UploadModule.removeFile()` — capture `removedId`, call `removeFileRoot()` on both modules before `AppState.removeFile()`
  - `PreviewModule.render()` (~line 1524) — remove `disableAutoFetch: true`
  - `PreviewModule.renderEntry()` (~line 1550) — remove `disableAutoFetch: true`, add page-1 warmup

**Secondary file to modify**
- `frontend/styles.css`
  - Adjust `.preview-panel` gap (move to file roots)
  - Add `.preview-file-root`, `.preview-file-root--hidden`, `.thumb-file-root`, `.thumb-file-root--hidden`

---

## Task 1: Persist page-view DOM per file (PreviewPanelModule)

**Files:**
- Modify: `frontend/app.js` — `PreviewPanelModule` object
- Modify: `frontend/styles.css`

- [ ] **Step 1: Add `_pageRoots` and `_sheetRoots` Maps to `PreviewPanelModule`**

Inside the `PreviewPanelModule` object literal, alongside existing fields, add:

```js
_pageRoots:  new Map(),   // fileId → <div.preview-file-root> for page-view
_sheetRoots: new Map(),   // fileId → <div.preview-file-root> for sheet-view
```

Keep all existing fields (`_renderTasks`, `_cache`, `_pageEls`, `_renderQueue`, `_sheetEls`, etc.) unchanged.

- [ ] **Step 2: Add `_getOrCreatePageRoot(fileEntry)` helper**

```js
_getOrCreatePageRoot(fileEntry) {
    let root = this._pageRoots.get(fileEntry.id);
    if (root) return { root, isNew: false };
    root = document.createElement('div');
    root.className = 'preview-file-root';
    root.dataset.fileId = fileEntry.id;
    this._pageRoots.set(fileEntry.id, root);
    this._container.appendChild(root);
    return { root, isNew: true };
},
```

- [ ] **Step 3: Replace destructive reset in page-view path of `render()` with hide/show**

The page-view path of `render()` currently spans lines ~3782–3836. Replace the exact block between `if (!this._container || !fileEntry?.pdfDoc) return;` and the card creation loop with the following. **All lines shown must appear exactly as written — do not omit any.**

Existing lines to replace (lines ~3784–3792):
```js
// Cancel queue and in-flight tasks
this._renderQueue = [];
this._activeRenders = 0;
for (const t of this._renderTasks.values()) { try { t.cancel(); } catch(_){} }
this._renderTasks.clear();
this._currentFileId = fileEntry.id;

this._pageEls.clear();
this._container.innerHTML = '';
```

Replace the above 8 lines with:

```js
// Cancel queue and in-flight tasks (still needed — even on cache-hit, old tasks must stop)
this._renderQueue = [];
this._activeRenders = 0;  // ← MUST be reset; stale count blocks _drainQueue()
for (const t of this._renderTasks.values()) { try { t.cancel(); } catch(_){} }
this._renderTasks.clear();

// Hide all roots; show only target file's page root
for (const [fid, r] of this._pageRoots)    r.classList.toggle('preview-file-root--hidden', fid !== fileEntry.id);
for (const r of this._sheetRoots.values()) r.classList.add('preview-file-root--hidden');

this._currentFileId = fileEntry.id;

const { root, isNew } = this._getOrCreatePageRoot(fileEntry);

if (!isNew) {
    // Validate that DOM card count matches totalPageCount.
    // Compare against totalPageCount (NOT pageOrder.length):
    //   - page-view cards are created for real pages 1..totalPageCount only
    //   - blank pages (pageOrder entries === 0) have NO page-view card
    //   - totalPageCount is immutable for a loaded PDF
    //   - pageOrder.length > totalPageCount after blank insertion, causing a
    //     false mismatch and unnecessary rebuild if compared against pageOrder.length
    const domCardCount = root.querySelectorAll('.preview-page-card').length;
    if (domCardCount === fileEntry.totalPageCount) {
        // DOM card structure is valid. However, _pageEls may contain stale sheet-view
        // card references if the user previously switched to sheet-view and back
        // (sheet-view D14 fix clears _pageEls for this file; sheet cards overwrite
        // page cards with the same keys). Repopulate _pageEls from the DOM if needed.
        const firstKey = `${fileEntry.id}-1`;
        const firstEl  = this._pageEls.get(firstKey);
        if (!firstEl || firstEl.querySelector('img.sheet-page-img')) {
            // _pageEls is empty for this file OR contains stale sheet cards —
            // rebuild the map from the actual page-view DOM cards in pageRoot.
            for (const k of [...this._pageEls.keys()]) {
                if (k.startsWith(fileEntry.id + '-')) this._pageEls.delete(k);
            }
            root.querySelectorAll('.preview-page-card').forEach(card => {
                const p = parseInt(card.dataset.page);
                if (p) this._pageEls.set(`${fileEntry.id}-${p}`, card);
            });
        }
        // DOM is valid and _pageEls is now correct — re-sync state only, no rebuild
        this._syncSelectionUI();
        this._container.scrollTop = 0;
        requestAnimationFrame(() => this._renderVisible());
        return;
    }
    // totalPageCount changed (shouldn't happen for loaded PDFs, but be safe) —
    // fall through to rebuild this root
    root.innerHTML = '';
    for (const k of [...this._pageEls.keys()]) {
        if (k.startsWith(fileEntry.id + '-')) this._pageEls.delete(k);
    }
}
// First time or card count mismatch: build/rebuild the page root below
```

- [ ] **Step 4: Scope card creation to the page root**

In the card creation loop that follows, replace:

```js
this._container.appendChild(card);
```

with:

```js
root.appendChild(card);
```

Remove the old `this._pageEls.clear()` line — it is now handled inside the guard above.

- [ ] **Step 5: Guard `_unmountOffScreen()` to the active root only**

At the top of `_unmountOffScreen()`, add:

```js
const activeRoot = this._pageRoots.get(this._currentFileId)
                ?? this._sheetRoots.get(this._currentFileId);
if (!activeRoot) return;
```

Inside the `_pageEls.forEach`, add before any `offsetHeight` or `getBoundingClientRect` call:

```js
if (!activeRoot.contains(el)) return; // never measure hidden roots — offsetHeight returns 0 there
```

- [ ] **Step 6: Update `PreviewPanelModule.clear()` to reset Maps**

Find `clear()` (~line 4535). After `this._container.innerHTML = ...`, add:

```js
// Reset per-file DOM roots (all files removed — maps now stale)
this._pageRoots.clear();
this._sheetRoots.clear();
this._pageEls.clear();
this._sheetEls.clear();
```

- [ ] **Step 7: Update CSS — move gap from `.preview-panel` to file roots**

In `frontend/styles.css`, change `.preview-panel`:

```css
.preview-panel {
  /* remove: gap: 20px; */
  gap: 0; /* gap now lives on file roots */
}
```

Add new rules:

```css
/* Per-file page-view root */
.preview-file-root {
  display: flex;
  flex-direction: column;
  gap: 20px;
}
.preview-file-root--hidden {
  display: none;
}
```

- [ ] **Step 8: Verify**

- Load 2 PDFs, switch A→B→A repeatedly
- Previously rendered pages in A reappear instantly (no rebuild flash)
- Page selection (green/blue borders) correct after every switch
- Rotation badges correct after every switch
- Insert a blank page into file A, switch away, switch back — blank card is present
- Remove all files → upload new file → no orphan DOM, no crash

---

## Task 2: Route sheet-view through per-file sheet root

**Files:**
- Modify: `frontend/app.js` — `_renderSheetView()` and new `_getOrCreateSheetRoot()` helper

- [ ] **Step 1: Add `_getOrCreateSheetRoot(fileEntry)` helper**

```js
_getOrCreateSheetRoot(fileEntry) {
    let root = this._sheetRoots.get(fileEntry.id);
    if (!root) {
        root = document.createElement('div');
        root.className = 'preview-file-root preview-file-root--hidden';
        root.dataset.fileId = fileEntry.id;
        root.dataset.viewMode = 'sheet';
        this._sheetRoots.set(fileEntry.id, root);
        this._container.appendChild(root);
    }
    return root;
},
```

- [ ] **Step 2: Replace ONLY the DOM-destruction block in `_renderSheetView()` — preserve the cancel block above it**

`_renderSheetView()` starts with two distinct blocks. **Block A (lines ~3848–3852) MUST be preserved unchanged:**
```js
this._renderQueue = [];
this._activeRenders = 0;
for (const t of this._renderTasks.values()) { try { t.cancel(); } catch(_){} }
this._renderTasks.clear();
this._currentFileId = fileEntry.id;
```

**Block B (lines ~3853–3859) is what gets replaced.** Replace these exact lines:
```js
this._pageEls.clear();
this._sheetEls.clear();
this._container.innerHTML = '';

// NEW: reset blob pipeline to prevent stale renders draining into new cycle
this._blobQueue = [];
this._activeBlobRenders = 0;
```

With:
```js
// Hide all roots; show only this file's sheet root
for (const r of this._pageRoots.values())   r.classList.add('preview-file-root--hidden');
for (const [fid, r] of this._sheetRoots)    r.classList.toggle('preview-file-root--hidden', fid !== fileEntry.id);

const sheetRoot = this._getOrCreateSheetRoot(fileEntry);
sheetRoot.classList.remove('preview-file-root--hidden');
sheetRoot.innerHTML = ''; // sheet layout always rebuilds — depends on selection/blank/rotation state

// Clear stale _pageEls entries for this file IMMEDIATELY after innerHTML = ''.
// _renderSheetView is async — scroll events during the upcoming await may iterate
// _pageEls and call getBoundingClientRect() on now-detached elements (returns zeros),
// which would cause phantom _enqueueBlob() calls. (per D14)
for (const k of [...this._pageEls.keys()]) {
    if (k.startsWith(fileEntry.id + '-')) this._pageEls.delete(k);
}

this._sheetEls.clear();

// Reset blob pipeline (was previously resetting this._container globally)
this._blobQueue = [];
this._activeBlobRenders = 0;
```

**Block A remains immediately before Block B and must not be touched.**

- [ ] **Step 3: Append sheet inner layout into `sheetRoot`, not `this._container`**

Find the final append in `_renderSheetView()`:

```js
this._container.appendChild(inner);
```

Replace with:

```js
sheetRoot.appendChild(inner);
```

- [ ] **Step 4: Also update `this._container.scrollTop = 0` after the `inner` append**

Line ~4209: `this._container.scrollTop = 0;` — this is correct, stays as-is (scroll is on the outer container, not on the root).

- [ ] **Step 4.5: Add `_viewMode` guard at BOTH async await points in `_renderSheetView()`**

`_renderSheetView()` is async and has two `await` points. The existing guards only check `_currentFileId` (file switch), but NOT `_viewMode` (view mode switch). If the user switches page→sheet→page rapidly, the page-view `render()` runs during the sheet-view await, hides the sheet root, repopulates `_pageEls` with page cards — and then the sheet render resumes, passes the file-ID guard, and **overwrites `_pageEls` with sheet cards again**, re-breaking the page-view.

Find the existing guard after the **first** await in `_renderSheetView()` (~line 3891):
```js
if (this._currentFileId !== fileEntry.id) return;
```
Replace with:
```js
if (this._currentFileId !== fileEntry.id) return;
if (this._viewMode !== 'sheet') return;  // view mode changed back to page — abort
```

Find the existing guard after the **second** await (~line 3924, inside the together-mode block):
```js
if (this._currentFileId !== fileEntry.id) return;
if (fileEntry.landscapeMode !== 'together') return;
```
Replace with:
```js
if (this._currentFileId !== fileEntry.id) return;
if (this._viewMode !== 'sheet') return;  // view mode changed — abort
if (fileEntry.landscapeMode !== 'together') return;
```

These two guards ensure that a sheet render that was started, then overtaken by a page-view switch, always aborts rather than polluting `_pageEls` with stale sheet cards.

- [ ] **Step 5: Verify**

- Page-view → sheet-view toggle: correct sheet layout, no page-view DOM visible
- Sheet-view → page-view toggle: page-view DOM shown, sheet root hidden
- **Rapid page→sheet→page toggle (within 200ms): page-view renders correctly, no stale sheet entries in `_pageEls`**
- Switch files while in sheet-view: correct file's sheet layout shown
- Page click in sheet-view still triggers full rebuild (ejected-column updates)
- Blank page insert/remove still works correctly in sheet-view

---

## Task 3: File removal cleanup

**Files:**
- Modify: `frontend/app.js` — `PreviewPanelModule`, `ThumbStripModule`, `UploadModule.removeFile()`

- [ ] **Step 1: Add `removeFileRoot(fileId)` to `PreviewPanelModule`**

```js
removeFileRoot(fileId) {
    const pageRoot = this._pageRoots.get(fileId);
    if (pageRoot) { pageRoot.remove(); this._pageRoots.delete(fileId); }

    const sheetRoot = this._sheetRoots.get(fileId);
    if (sheetRoot) { sheetRoot.remove(); this._sheetRoots.delete(fileId); }

    // Remove _pageEls entries for this file
    for (const key of [...this._pageEls.keys()]) {
        if (key.startsWith(fileId + '-')) this._pageEls.delete(key);
    }

    // Cancel in-flight render tasks and recalculate active render counter
    for (const [taskKey, task] of [...this._renderTasks.entries()]) {
        if (taskKey.startsWith(fileId + '-')) {  // '+'-' prevents prefix collision (e.g. id='f1' matching 'f10-...')
            try { task.cancel(); } catch(_) {}
            this._renderTasks.delete(taskKey);
        }
    }
    // Recalculate counters — cancelled tasks may not decrement via .finally()
    this._activeRenders     = this._renderTasks.size;
    this._activeBlobRenders = 0; // blob tasks not tracked by key; safe to reset

    // Drain queues
    this._blobQueue   = (this._blobQueue   ?? []).filter(j => j.fileId !== fileId);
    this._renderQueue = this._renderQueue.filter(j => j.fileId !== fileId);

    // Evict pixel cache for this file
    this._cache.deleteByPrefix(fileId + '-');
},
```

- [ ] **Step 2: Add `removeFileRoot(fileId)` to `ThumbStripModule`**

```js
removeFileRoot(fileId) {
    const root = this._fileRoots?.get(fileId);
    if (root) { root.remove(); this._fileRoots.delete(fileId); }

    for (const [key, task] of [...this._renderTasks.entries()]) {
        if (key.startsWith(fileId + '-')) {  // '+'-' prevents prefix collision (e.g. id='f1' matching 'f10-...')
            try { task.cancel(); } catch(_) {}
            this._renderTasks.delete(key);
        }
    }
    // NOTE: _renderThumb tasks run as bare awaits (not in _renderTasks Map).
    // Setting _activeRenders = 0 is correct — their .finally() decrements are
    // benign on detached nodes and will not over-decrement below 0 because
    // _drainQueue checks _activeRenders < _MAX_CONCURRENT before spawning.
    this._activeRenders = 0;

    this._renderQueue = this._renderQueue.filter(j => j.fileId !== fileId);
    this._cache.deleteByPrefix(fileId + '-');
},
```

- [ ] **Step 3: Call both cleanup methods from `UploadModule.removeFile()` — before `AppState.removeFile()`**

Find `UploadModule.removeFile(index)` (~line 1321). At the very top of the function body, before `AppState.removeFile(index)`:

```js
// Capture id BEFORE AppState.removeFile() splices the array
const removedId = AppState.files[index]?.id;
if (removedId) {
    PreviewPanelModule.removeFileRoot(removedId);
    ThumbStripModule.removeFileRoot(removedId);
}
```

Let the existing `AppState.removeFile(index)` call and all subsequent logic proceed unchanged.

- [ ] **Step 4: Verify**

- Load 3 files, remove the middle one
- DevTools Elements: no orphan `.preview-file-root[data-file-id="<removed>"]`
- `PreviewPanelModule._pageRoots.size` is 2 (add temporary `console.log` to confirm)
- Remaining 2 files switch correctly; no console errors
- Render queue does not stall after removal

---

## Task 4: Persist thumb-strip DOM per file (ThumbStripModule)

**Files:**
- Modify: `frontend/app.js` — `ThumbStripModule` object

- [ ] **Step 1: Add `_fileRoots: new Map()` to `ThumbStripModule`**

```js
_fileRoots: new Map(),   // fileId → <div.thumb-file-root>
```

- [ ] **Step 2: Add `_getOrCreateThumbRoot(fileEntry)` helper**

```js
_getOrCreateThumbRoot(fileEntry) {
    let root = this._fileRoots.get(fileEntry.id);
    if (root) return { root, isNew: false };
    root = document.createElement('div');
    root.className = 'thumb-file-root';
    root.dataset.fileId = fileEntry.id;
    this._fileRoots.set(fileEntry.id, root);
    this._container.appendChild(root);
    return { root, isNew: true };
},
```

- [ ] **Step 3: Replace `innerHTML = ''` in `ThumbStripModule.render()` with hide/show**

Replace the existing destructive reset at the top of `render()` (and the cancel/clear block before the `files.length === 0` guard):

```js
render() {
    if (!this._container) return;

    // Cancel all in-flight renders and queue
    for (const t of this._renderTasks.values()) { try { t.cancel(); } catch(_){} }
    this._renderTasks.clear();
    this._renderQueue = [];
    this._activeRenders = 0;

    if (AppState.files.length === 0) {
        // All files removed — reset Maps to prevent stale DOM on next upload
        this._fileRoots?.clear();
        this._container.innerHTML = `
            <div class="preview-empty" style="padding:16px;text-align:center">
                <span class="preview-empty-icon">📄</span>
                <span style="font-size:12px">Chưa có file</span>
            </div>`;
        return;
    }

    const f = AppState.activeFile;
    if (!f) return;

    // Hide all file roots except the active one
    for (const [fid, r] of this._fileRoots) {
        r.classList.toggle('thumb-file-root--hidden', fid !== f.id);
    }
    const { root, isNew } = this._getOrCreateThumbRoot(f);
    if (!isNew) {
        // Already built — refresh data-file-index (may have changed after drag-to-reorder)
        // then re-sync selection highlights and re-render visible thumbs
        const currentIdx = AppState.activeFileIndex;
        root.querySelectorAll('.thumb-item').forEach(el => {
            el.dataset.fileIndex = currentIdx;
        });
        this._syncSelectionHighlights();
        requestAnimationFrame(() => this._renderVisible());
        return;
    }
    // else: first time for this file — fall through to item creation loop below
```

Note: preserve the existing item creation loop (lines 4607–4636) unchanged, but replace `this._container.appendChild(item)` with `root.appendChild(item)` (see Step 4).

- [ ] **Step 4: Append thumb items into `root`, not `this._container`**

In the thumb item creation loop (currently line ~4635), replace:

```js
this._container.appendChild(item);
```

with:

```js
root.appendChild(item);
```

- [ ] **Step 5: Scope `_renderVisible()` to the active file root only**

Replace the current selector in `_renderVisible()`:

```js
this._container.querySelectorAll('.thumb-item:not(.rendered)').forEach(el => {
```

With:

```js
// Scope to visible root only — hidden roots' items return getBoundingClientRect() as zeros
const activeFileId = AppState.activeFile?.id;
const activeThumbRoot = activeFileId ? this._fileRoots?.get(activeFileId) : null;
const searchRoot = activeThumbRoot ?? this._container;
searchRoot.querySelectorAll('.thumb-item:not(.rendered)').forEach(el => {
```

This ensures only items inside the active (visible) file root are considered for rendering.

- [ ] **Step 5.5: Scope `_syncSelectionHighlights()` to the active file root**

`_syncSelectionHighlights()` currently does `this._container.querySelectorAll('.thumb-item')` which, after Task 4, iterates ALL thumb items across ALL file roots (hidden and visible). This causes unnecessary `RotationHelper.updateBadge()` DOM writes on hidden roots and O(N_all_pages_all_files) work on every selection change.

Replace the selector line inside `_syncSelectionHighlights()`:

```js
// BEFORE:
this._container.querySelectorAll('.thumb-item').forEach(el => {

// AFTER:
const _activeId   = AppState.activeFile?.id;
const _activeRoot = _activeId ? (this._fileRoots?.get(_activeId) ?? this._container) : this._container;
_activeRoot.querySelectorAll('.thumb-item').forEach(el => {
```

Everything inside the `.forEach` body remains unchanged. This scopes the sync to the active (visible) root only.

- [ ] **Step 6: Guard `_unmountOffScreen()` to the active file root**

Replace the current `_unmountOffScreen()` body:

```js
_unmountOffScreen() {
    if (!this._container) return;
    const f = AppState.activeFile;
    if (!f) return;
    const activeRoot = this._fileRoots?.get(f.id) ?? this._container;

    const cRect = activeRoot.getBoundingClientRect();
    const buffer = cRect.height * 4; // keep 4 screens of thumbs

    activeRoot.querySelectorAll('.thumb-item.rendered').forEach(el => {
        const eRect = el.getBoundingClientRect();
        const isFar = eRect.bottom < cRect.top - buffer || eRect.top > cRect.bottom + buffer;
        if (isFar) {
            const img = el.querySelector('img.thumb-img');
            if (img && img.src) {
                el.style.minHeight = `${el.offsetHeight}px`; // measured on visible element — safe
                img.src = ''; // release decoded bitmap memory
                el.classList.remove('rendered');
            }
        }
    });
},
```

Key changes:
- `cRect` is now measured from `activeRoot` (not `this._container`) — avoids measuring the outer container that may span all files
- `querySelectorAll` scoped to `activeRoot` — hidden roots skipped entirely
- `el.offsetHeight` is only measured on elements inside the visible root — safe

- [ ] **Step 6.5: Scope `_setActiveHighlight()` to the active file root**

`_setActiveHighlight()` currently queries `this._container` (all roots):
```js
const target = this._container.querySelector(
    `.thumb-item[data-file-index="${fileIndex}"][data-page="${pageNum}"]`
);
```

After Task 4, `this._container` holds multiple `.thumb-file-root` children (hidden + visible). `querySelector` does NOT skip `display:none` descendants — it returns the first match in DOM order. After drag-to-reorder, a hidden root retains stale `data-file-index` values that collide with the current active index, causing `querySelector` to match the wrong (invisible) element. The active thumb gets no highlight.

Replace the full `_setActiveHighlight` body:
```js
_setActiveHighlight(fileIndex, pageNum) {
    const activeId   = AppState.activeFile?.id;
    const searchRoot = (activeId && this._fileRoots?.get(activeId)) || this._container;
    searchRoot.querySelectorAll('.thumb-item.active')
        .forEach(el => el.classList.remove('active'));
    const target = searchRoot.querySelector(
        `.thumb-item[data-file-index="${fileIndex}"][data-page="${pageNum}"]`
    );
    target?.classList.add('active');
    target?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
},
```

- [ ] **Step 6.6: Fix `TabsModule.setActive()` rAF thumb query — scope to active root**

`TabsModule.setActive()` (~line 1556) contains a `requestAnimationFrame` callback that queries `ThumbStripModule._container` for the first thumb of the newly active file. After Task 4, this unscoped query can match a stale `data-file-index` on a hidden root, causing `scrollIntoView` to operate on a `display:none` element (no-op) — the thumb strip never scrolls to page 1 of the new file after a reorder.

Find in `TabsModule.setActive()` the `requestAnimationFrame` block that queries for `thumb-item[data-file-index`:
```js
requestAnimationFrame(() => {
    const firstThumb = ThumbStripModule._container?.querySelector(
        `.thumb-item[data-file-index="${idx}"][data-page="1"]`
    );
    if (firstThumb) {
        firstThumb.scrollIntoView({ behavior: 'auto', block: 'start' });
        ThumbStripModule._setActiveHighlight(idx, 1);
    }
});
```

Replace with:
```js
requestAnimationFrame(() => {
    const _activeRoot = ThumbStripModule._fileRoots?.get(AppState.activeFile?.id)
                     ?? ThumbStripModule._container;
    const firstThumb = _activeRoot?.querySelector(
        `.thumb-item[data-file-index="${idx}"][data-page="1"]`
    );
    if (firstThumb) {
        firstThumb.scrollIntoView({ behavior: 'auto', block: 'start' });
        ThumbStripModule._setActiveHighlight(idx, 1);
    }
});
```

**Note**: `_fileRoots` is introduced by Task 4 Step 1. Steps 6.5 and 6.6 MUST be done after Step 1. If `_fileRoots` is not yet populated for the active file (first time viewing), the `?? this._container` fallback is safe.

- [ ] **Step 7: Add CSS for thumb file roots**

In `frontend/styles.css`:

```css
.thumb-file-root {
  display: flex;
  flex-direction: column;
  gap: 8px; /* spacing between thumb items within a file */
}
.thumb-file-root--hidden {
  display: none;
}
```

**Do NOT change `.thumb-panel-inner { gap: 4px; }`** — that existing gap becomes the between-file-roots spacing, which is the correct visual separation between file groups. Only add the two rules above; nothing else.

- [ ] **Step 8: Verify**

- Switch between 2 files: thumbs for the previously viewed file reappear instantly (no rebuild)
- Active thumb highlight follows preview scroll correctly per file
- Selection colors (green/blue) correct per file after every switch
- No duplicate thumb items after repeated switching
- DevTools: `ThumbStripModule._fileRoots.size` equals number of files ever viewed

---

## Task 5: Reduce first-render latency — PDF.js load options

**Files:**
- Modify: `frontend/app.js`
  - `PreviewModule.render()` (~line 1524)
  - `PreviewModule.renderEntry()` (~line 1550)

- [ ] **Step 1: Remove `disableAutoFetch: true` from `PreviewModule.render()`**

In the `pdfjsLib.getDocument({...})` call inside `PreviewModule.render(fileId)` (~line 1576):

```js
// Remove this line:
disableAutoFetch: true,   // Only fetch pages when needed  ← DELETE
```

- [ ] **Step 2: Remove `disableAutoFetch: true` from `PreviewModule.renderEntry()`**

In the `pdfjsLib.getDocument({...})` call inside `PreviewModule.renderEntry(entry)` (~line 1602):

```js
// Remove this line:
disableAutoFetch: true,   // Only fetch pages when needed  ← DELETE
```

Risk note: with multiple large scan files open simultaneously, PDF.js will proactively download all of them. If DevTools shows excessive concurrent network activity or memory spikes with 3+ large files, restore `disableAutoFetch: true` on non-active files only.

- [ ] **Step 3: Add page-1 background warmup in `renderEntry()`**

After `entry.pdfDoc = await loadTask.promise` in `renderEntry(entry)`:

```js
// Warm page 1 in background — pre-populates PDF.js internal page cache
// Use setTimeout (not queueMicrotask) to yield to active file's own render first
setTimeout(async () => {
    try {
        const page = await entry.pdfDoc?.getPage(1);
        if (page) page.cleanup();
    } catch (_) {}
}, 100);
```

- [ ] **Step 4: Verify**

- Open a scan PDF (50+ pages, image-heavy) as the second file, then switch to it
- First page should appear visibly faster than before
- No errors or excessive memory in DevTools with 2–3 large files open simultaneously

---

## Task 6: Reduce JPEG re-encode cost in sheet/thumb rendering

**Files:**
- Modify: `frontend/app.js` — `_renderBlobPage()` only

Context (per D8): `_renderPage()` (page-view) uses `CanvasPool + drawImage` — no blob encode. The JPEG re-encode cost is in `_renderBlobPage()` only, called by `_renderSheetPage()` and `ThumbStripModule._renderThumb()`.

**Note (per D15):** The `ImageBitmap` / `transferToImageBitmap()` fast-path is NOT applicable to `_renderPage()`. `CanvasPool.acquire()` returns an `HTMLCanvasElement`, which does not have `transferToImageBitmap()`. Any such call would throw `TypeError` and silently fall through. Do NOT add it.

- [ ] **Step 1: Try WebP instead of JPEG in `_renderBlobPage()`**

Find in `_renderBlobPage()` (~line 4339):

```js
const blob = await off.convertToBlob({ type: 'image/jpeg', quality: 0.88 });
```

Replace with:

```js
const blob = await off.convertToBlob({ type: 'image/webp', quality: 0.82 });
```

WebP encode is often faster than JPEG for scan content (fewer artifacts at lower quality settings). Test with real scan PDFs. If encode is slower or file size is larger than JPEG, revert to JPEG. Quality range 0.75–0.85 is the right experimentation range.

- [ ] **Step 2: Verify no visual regression**

- Sheet thumbnails look correct at WebP quality (no obvious artifacts at quality 0.82)
- Thumb strip images look correct
- Memory usage in DevTools Memory tab is not higher than baseline after viewing 20+ pages
- If WebP is measurably slower than JPEG on this machine, revert Step 1

---

## Task 7: CSS containment (do last, only after Tasks 1–6 verified stable)

**Files:**
- Modify: `frontend/styles.css`

- [ ] **Step 1: Add `contain: layout paint` to card elements**

```css
.preview-page-card { contain: layout paint; }
.thumb-item        { contain: layout paint; }
```

`contain: layout paint` is safe: it does not affect `getBoundingClientRect` accuracy on visible elements. It prevents paint from propagating out of each card.

- [ ] **Step 2: Do NOT add `content-visibility: auto` to any card or item**

`_renderVisible()` and `_unmountOffScreen()` call `getBoundingClientRect()` on every page card and thumb item. `content-visibility: auto` causes the browser to skip layout for off-screen items — `getBoundingClientRect` returns zero for unrendered items — permanently breaking lazy rendering.

Do not apply `content-visibility` to `.preview-page-card`, `.thumb-item`, or any element whose position is measured by the viewer's visibility logic.

- [ ] **Step 3: Verify no broken visibility detection**

- Scroll through a 50-page PDF: all pages render as they enter the viewport
- No pages stuck in unrendered placeholder state after scrolling past them

---

## What Not To Do

- Do **not** apply `content-visibility: auto` to `.preview-page-card` or `.thumb-item`
- Do **not** use `display: contents` on file root elements — use `display: flex` instead (per D9)
- Do **not** increase LRU cache size before completing Tasks 1–4 — DOM rebuild is the bottleneck
- Do **not** rewrite to React or replace PDF.js
- Do **not** add IndexedDB persistence before verifying in-memory improvements
- Do **not** start Task 7 before Tasks 1–6 are verified stable
- Do **not** change sheet-view page card click listeners — they must continue calling `render(entry)` full (B3 fix)
- Do **not** set `ThumbStripModule._activeRenders = this._renderTasks.size` in `removeFileRoot` — use `= 0` instead (per D6)
- Do **not** query `this._container.querySelectorAll('.thumb-item')` inside `_renderVisible()` after Task 4 — always scope to active root (per D12)
- Do **not** add `transferToImageBitmap()` fast-path to `_renderPage()` — `CanvasPool.acquire()` returns HTMLCanvasElement which lacks this method (per D15)
- Do **not** attempt to fix the `CanvasPool` / `OffscreenCanvas` mismatch in `LRUBlobCache` eviction — pre-existing bug, out of scope
- Do **not** try to cancel in-flight `_renderThumb()` tasks on file switch — they run as bare `async` (no cancellation token) and their `.finally()` counter decrement can go negative; the effect is bounded (at most `_MAX_CONCURRENT` over-fire on the next drain, then self-corrects) and is pre-existing behavior not introduced by this plan
- Do **not** compare `domCardCount` against `fileEntry.pageOrder.length` in the page-view early-return guard — use `fileEntry.totalPageCount` instead (blanks are not in page-view, so `pageOrder.length > totalPageCount` after blank insertion would cause false rebuilds)
- Do **not** query `this._container.querySelector('.thumb-item[data-file-index...]')` in `_setActiveHighlight()` after Task 4 — always scope to active root via `this._fileRoots.get(activeId)` (unscoped queries match stale `data-file-index` on hidden roots after reorder)
- Do **not** skip the `_viewMode !== 'sheet'` guard in `_renderSheetView()` — without it, an async sheet render that was overtaken by a page-view switch will overwrite `_pageEls` with stale sheet cards, breaking selection sync, scroll tracking, and lazy rendering in the page-view that is now visible

---

## Expected Outcome Per Task

| Task | What it fixes | Expected improvement |
|---|---|---|
| 1 | Page-view DOM persistence + `clear()` Map reset | File switch: ~0ms DOM work for already-viewed files |
| 2 | Sheet-view DOM routing | No cross-view DOM pollution, sheet root scoped correctly |
| 3 | File removal cleanup | No memory leak, no orphan DOM, no stalled render counters |
| 4 | Thumb DOM persistence + scoped `_renderVisible()` | Thumb strip instant on file switch; no phantom renders from hidden roots |
| 5 | PDF.js auto-fetch + warmup | First render of scan PDFs noticeably faster |
| 6 | WebP encode in `_renderBlobPage()` | Sheet/thumb encode faster (no page-view change) |
| 7 | CSS containment | Incremental browser paint savings |

---

## Verification Checklist

- [ ] Switching between 2 already-loaded files: no DOM rebuild, near-instant
- [ ] Thumbs reappear immediately on switch back to a previously viewed file
- [ ] Page selection and rotation state correct after every switch
- [ ] Sheet-view layout rebuilds only when selection/blank/rotation changes — not on mere file switch
- [ ] Inserting a blank page in A, switching to B, switching back to A: blank card is present
- [ ] Removing a file: no orphan DOM, no stalled render queues, remaining files switch correctly
- [ ] Removing all files then uploading a new one: no crash, no stale root reuse
- [ ] Scan PDF first page appears faster than before Task 5
- [ ] No memory leak after open → switch → remove multiple files
- [ ] `_renderVisible()` still works correctly — no pages stuck in unrendered state
- [ ] `getBoundingClientRect` measurements not broken by any CSS change
- [ ] `_isDeleting` flag on `PreviewPanelModule` still prevents double blank-delete clicks
- [ ] ThumbStrip: no phantom renders triggered for hidden file roots after Task 4
- [ ] Drag-to-reorder files: active thumb highlight correctly follows the dragged file after reorder (data-file-index refreshed)
- [ ] **Page→sheet→page on the same file**: after switching back, page selection badges update correctly and scrolling enqueues lazy renders (no stale `_pageEls` from sheet-view)
- [ ] **Rapid page→sheet→page toggle (within 200ms)**: page-view is stable, no leftover sheet cards in `_pageEls`
- [ ] **After drag-to-reorder**: thumb strip scrolls to page 1 of the active file (`scrollIntoView` works on a visible element, not a hidden one)
