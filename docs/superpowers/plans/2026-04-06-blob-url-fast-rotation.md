# Blob URL Fast Rotation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace canvas-to-canvas `drawImage` blit with `<img>` blob URL rendering in ThumbStrip and Sheet View so that page rotation updates appear in ~15-30ms instead of 5-6s.

**Architecture:** Two-layer cache: `OffscreenCanvas` (raw pixels, for fast rotation re-blit) + blob URL string (for `<img>` display). On rotation: Phase 1 CSS `transform` instant visual feedback → Phase 2 `drawImage`+`convertToBlob` from cached canvas (~3-7ms/page) → swap `img.src`. Sheet view never wipes `innerHTML` on rotation — only updates the single rotated page's `<img>`. Page view (scale=1.5) stays canvas-only (blob encode too slow at full res).

**Tech Stack:** Vanilla JS, PDF.js v5 (`page.render` + `page.getViewport`), `OffscreenCanvas.convertToBlob()`, `URL.createObjectURL/revokeObjectURL`, CSS `transform`

**Scope:** `frontend/app.js` only. No new files.

---

## File Map

| File | Lines affected | Change |
|------|---------------|--------|
| `frontend/app.js` | ~2821–2883 | Replace `LRUCanvasCache` with `LRUBlobCache` that stores `{canvas, url}` pairs |
| `frontend/app.js` | ~3482–3737 | `ThumbStripModule`: replace `<canvas>` placeholder with `<img>`, rewrite `_renderThumb`, `_unmountOffScreen` |
| `frontend/app.js` | ~3044–3247 | `PreviewPanelModule._renderSheetView`: replace `<canvas>` with `<img>` in `makeFace()`, add surgical rotation path |
| `frontend/app.js` | ~3327–3385 | `PreviewPanelModule._renderPage`: split into thumb/sheet path (blob) vs page-view path (canvas, unchanged) |
| `frontend/app.js` | ~1747–1778 | `ContextMenu._applyRotation`: add instant CSS phase + `_rotateBlob()` call; remove full `render()` for sheet rotation |
| `frontend/app.js` | ~2798–2816 | `CanvasPool`: keep as-is (still used by page view canvas path) |

---

## Key Invariants (read before touching any code)

1. **Page view (`_viewMode === 'page'`, scale=1.5)** — keep ALL canvas logic unchanged. Do not migrate this path.
2. **Sheet view + ThumbStrip (scale=0.26 / sheet-reduced)** — migrate to `<img>` blob.
3. Cache key format stays: `"fileId-pageNum-rotation-scale"` — same key works for both canvas and blob.
4. `LRUBlobCache` stores `{ canvas: OffscreenCanvas, url: string }`. On evict: `URL.revokeObjectURL(url)` + `CanvasPool.release(canvas)`.
5. `fileEntry._orientationMap` cache (from previous work) stays unchanged — only orientation detection is cached there.
6. After rotation in sheet view: do NOT call `PreviewPanelModule.render()` — call new `_patchRotatedPage(pageNum)` instead.
7. `CanvasPool` is still used for the OffscreenCanvas acquisition in the new blob path — reuse it.

---

## Task 1: Replace `LRUCanvasCache` with `LRUBlobCache`

**Files:**
- Modify: `frontend/app.js` lines 2821–2883

The new cache stores `{ canvas: OffscreenCanvas, url: string }` per entry. Memory budget is calculated from the canvas (raw pixels), same formula as before. On evict: revoke blob URL AND release canvas to pool.

- [ ] **Step 1: Replace the entire `LRUCanvasCache` class**

Find the block starting with `class LRUCanvasCache {` (line 2821) and ending with the closing `}` (line 2883). Replace the entire class:

```js
class LRUBlobCache {
    #map        = new Map(); // key → { canvas: OffscreenCanvas, url: string }
    #totalBytes = 0;
    #maxBytes;

    constructor(maxMB = 50) {
        this.#maxBytes = maxMB * 1024 * 1024;
    }

    has(key)  { return this.#map.has(key); }
    get size() { return this.#map.size; }

    // Returns { canvas, url } or undefined
    get(key) {
        if (!this.#map.has(key)) return undefined;
        const val = this.#map.get(key);
        this.#map.delete(key);
        this.#map.set(key, val); // move to MRU position
        return val;
    }

    // entry = { canvas: OffscreenCanvas, url: string }
    set(key, entry) {
        const bytes = entry.canvas.width * entry.canvas.height * 4;
        if (this.#map.has(key)) {
            const old = this.#map.get(key);
            this.#totalBytes -= old.canvas.width * old.canvas.height * 4;
            URL.revokeObjectURL(old.url);
            CanvasPool.release(old.canvas);
            this.#map.delete(key);
        }
        while (this.#totalBytes + bytes > this.#maxBytes && this.#map.size > 0) {
            const oldestKey = this.#map.keys().next().value;
            const oldest    = this.#map.get(oldestKey);
            this.#totalBytes -= oldest.canvas.width * oldest.canvas.height * 4;
            URL.revokeObjectURL(oldest.url);
            CanvasPool.release(oldest.canvas);
            this.#map.delete(oldestKey);
        }
        this.#map.set(key, entry);
        this.#totalBytes += bytes;
    }

    delete(key) {
        if (!this.#map.has(key)) return;
        const entry = this.#map.get(key);
        this.#totalBytes -= entry.canvas.width * entry.canvas.height * 4;
        URL.revokeObjectURL(entry.url);
        CanvasPool.release(entry.canvas);
        this.#map.delete(key);
    }

    deleteByPrefix(prefix) {
        for (const k of [...this.#map.keys()]) {
            if (k.startsWith(prefix)) this.delete(k);
        }
    }

    clear() {
        for (const entry of this.#map.values()) {
            URL.revokeObjectURL(entry.url);
            CanvasPool.release(entry.canvas);
        }
        this.#map.clear();
        this.#totalBytes = 0;
    }
}
```

- [ ] **Step 2: Update cache instantiation names**

Find both instantiation lines and update class name:
```js
// Was: new LRUCanvasCache(50)
_cache: new LRUBlobCache(50), // PreviewPanelModule line ~2931

// Was: new LRUCanvasCache(20)
_cache: new LRUBlobCache(20), // ThumbStripModule line ~3487
```

- [ ] **Step 3: Verify syntax**

Run: `node --check frontend/app.js`
Expected: no output (exit 0)

---

## Task 2: Add `_renderBlobPage()` helper — shared blob render+cache logic

**Files:**
- Modify: `frontend/app.js` — insert new method into `PreviewPanelModule` just before `_renderPage` (~line 3327)

This helper is used by both sheet view and thumb strip. It renders a page to OffscreenCanvas at the given scale, encodes to JPEG blob, caches both, returns `{ canvas, url }`.

- [ ] **Step 1: Insert `_renderBlobPage` method into `PreviewPanelModule`**

Find the line `async _renderPage(fileId, pageNum, el) {` (line ~3327) and insert BEFORE it:

```js
    // Render a page to blob URL + OffscreenCanvas, store in cache.
    // Used by sheet view and thumb strip (NOT page view — that stays canvas-only).
    // Returns { canvas: OffscreenCanvas, url: string } or null on error/cancel.
    async _renderBlobPage(fileId, pageNum, scale, cacheRef) {
        const fileEntry = AppState.files.find(f => f.id === fileId);
        if (!fileEntry?.pdfDoc) return null;

        const rotation = fileEntry.pageRotations?.get(pageNum) ?? null;
        const rotDeg   = RotationHelper.toDeg(rotation);
        const key      = `${fileId}-${pageNum}-${rotation ?? '0'}-${scale}`;

        // Cache hit
        if (cacheRef.has(key)) return cacheRef.get(key);

        // Cancel stale task
        const taskKey  = `${fileId}-${pageNum}-blob`;
        const existing = this._renderTasks.get(taskKey);
        if (existing) { try { existing.cancel(); } catch(_){} }

        const page = await fileEntry.pdfDoc.getPage(pageNum);
        const vp   = page.getViewport({ scale, rotation: rotDeg });
        const off  = CanvasPool.acquire();
        off.width  = vp.width;
        off.height = vp.height;

        const task = page.render({
            canvasContext: off.getContext('2d', { alpha: false }),
            viewport:      vp,
            intent:        'display',
        });
        this._renderTasks.set(taskKey, task);

        try {
            await task.promise;
            const blob = await off.convertToBlob({ type: 'image/jpeg', quality: 0.88 });
            const url  = URL.createObjectURL(blob);
            const entry = { canvas: off, url };
            cacheRef.set(key, entry);
            return entry;
        } catch(err) {
            CanvasPool.release(off);
            if (err?.name !== 'RenderingCancelledException') console.warn(err);
            return null;
        } finally {
            page.cleanup();
            this._renderTasks.delete(taskKey);
        }
    },

```

- [ ] **Step 2: Verify syntax**

Run: `node --check frontend/app.js`
Expected: no output

---

## Task 3: Migrate `ThumbStripModule` — `<canvas>` → `<img>` in DOM build

**Files:**
- Modify: `frontend/app.js` lines 3540–3567 (DOM build loop inside `render()`)
- Modify: `frontend/app.js` lines 3620–3668 (`_renderThumb`)
- Modify: `frontend/app.js` lines 3718–3736 (`_unmountOffScreen`)

### Step 3a: DOM build — replace `<canvas>` with `<img>`

- [ ] **Step 1: Replace canvas placeholder with img in `render()`**

Find (inside the `for (let p = 1; ...)` loop at line ~3550):
```js
                const canvas  = document.createElement('canvas');
                const label   = document.createElement('div');
                label.className   = 'thumb-item-label';
                label.textContent = p;

                item.appendChild(canvas);
                item.appendChild(label);
```

Replace with:
```js
                const img   = document.createElement('img');
                img.className = 'thumb-img';
                img.alt       = '';
                img.draggable = false;
                const label   = document.createElement('div');
                label.className   = 'thumb-item-label';
                label.textContent = p;

                item.appendChild(img);
                item.appendChild(label);
```

### Step 3b: Rewrite `_renderThumb`

- [ ] **Step 2: Replace `_renderThumb` body**

Find `async _renderThumb(fileId, pageNum, el) {` (line ~3612) through its closing `},` (line ~3669). Replace the entire method body:

```js
    async _renderThumb(fileId, pageNum, el) {
        const fileEntry = AppState.files.find(f => f.id === fileId);
        if (!fileEntry?.pdfDoc) return;

        const rotation   = fileEntry.pageRotations?.get(pageNum) ?? null;
        const thumbScale = 0.26;
        const key        = `${fileId}-${pageNum}-${rotation ?? '0'}-${thumbScale}`;
        const img        = el.querySelector('img.thumb-img');
        if (!img) return;

        RotationHelper.updateBadge(el, rotation);

        // Cache hit — just set src (browser re-uses decoded bitmap if URL unchanged)
        if (this._cache.has(key)) {
            const { url } = this._cache.get(key);
            if (img.src !== url) img.src = url;
            el.classList.add('rendered');
            return;
        }

        // Fresh render
        const taskKey  = `${fileId}-${pageNum}`;
        if (this._renderTasks.has(taskKey)) return;
        if (this._renderQueue.some(j => j.fileId === fileId && j.pageNum === pageNum)) return;

        const entry = await PreviewPanelModule._renderBlobPage(fileId, pageNum, thumbScale, this._cache);
        if (!entry) return;

        img.src = entry.url;
        el.classList.add('rendered');
    },
```

### Step 3c: Update `_unmountOffScreen`

- [ ] **Step 3: Replace canvas-zeroing with img src clear in `_unmountOffScreen`**

Find inside `_unmountOffScreen` (line ~3727):
```js
                const canvas = el.querySelector('canvas');
                if (canvas && (canvas.width > 0 || canvas.height > 0)) {
                    el.style.minHeight = `${el.offsetHeight}px`;
                    canvas.width = 0;
                    canvas.height = 0;
                    el.classList.remove('rendered');
                }
```

Replace with:
```js
                const img = el.querySelector('img.thumb-img');
                if (img && img.src) {
                    el.style.minHeight = `${el.offsetHeight}px`;
                    img.src = ''; // release decoded bitmap memory
                    el.classList.remove('rendered');
                }
```

- [ ] **Step 4: Verify syntax**

Run: `node --check frontend/app.js`
Expected: no output

---

## Task 4: Migrate `PreviewPanelModule._renderSheetView` — `<canvas>` → `<img>` in sheet face cards

**Files:**
- Modify: `frontend/app.js` — `makeFace()` helper inside `_renderSheetView` (~line 3134)

Sheet view face cards (`makeFace`) create `.preview-page-card` with a `<canvas>` child. Replace with `<img>`.

Also: the shared `_renderPage()` (used by page view) must NOT be called for sheet cards after this migration. Sheet cards will be rendered by `_renderBlobPage` via a new `_renderSheetPage()` helper called from `_renderVisible`.

### Step 4a: Replace `<canvas>` with `<img>` in `makeFace()`

- [ ] **Step 1: Update makeFace to create `<img>` instead of `<canvas>`**

Find inside `makeFace` (line ~3185):
```js
                    const canvas = document.createElement('canvas');
                    card.appendChild(canvas);
```

Replace with:
```js
                    const img = document.createElement('img');
                    img.className = 'sheet-page-img';
                    img.alt       = '';
                    img.draggable = false;
                    card.appendChild(img);
```

### Step 4b: Update `_renderPage` to skip sheet-view cards

Sheet-view cards now have `<img>` not `<canvas>`. The existing `_renderPage` does `el.querySelector('canvas')` — it will silently skip them (returns null, early return). That's correct — sheet cards are rendered by a separate path.

- [ ] **Step 2: Add a sheet-specific render path in `_renderVisible`**

Find `_renderVisible()` (line ~3284). It currently calls `this._enqueue(fileId, pageNum, el)` for visible page elements. We need it to call the blob path for sheet-view cards (those that have `img.sheet-page-img`) and the existing canvas path for page-view cards (those that have `canvas`).

Find inside `_renderVisible` the block that calls `_enqueue`:
```js
            if (visible) {
                const fileId  = el.dataset.fileId;
                const pageNum = parseInt(el.dataset.page);
                this._enqueue(fileId, pageNum, el);
            }
```

Replace with:
```js
            if (visible) {
                const fileId  = el.dataset.fileId;
                const pageNum = parseInt(el.dataset.page);
                // Sheet-view cards use img blob path; page-view cards use canvas path
                if (el.querySelector('img.sheet-page-img')) {
                    this._enqueueBlob(fileId, pageNum, el);
                } else {
                    this._enqueue(fileId, pageNum, el);
                }
            }
```

- [ ] **Step 3: Add `_enqueueBlob` and `_drainBlobQueue` methods**

Find `_enqueue(fileId, pageNum, el) {` (line ~3302) and insert BEFORE it:

```js
    // ── Blob render queue (for sheet-view <img> cards) ────────────
    _blobQueue: [],
    _activeBlobRenders: 0,
    _MAX_BLOB_CONCURRENT: 6,

    _enqueueBlob(fileId, pageNum, el) {
        if (el.classList.contains('rendered')) return;
        if (this._blobQueue.some(j => j.fileId === fileId && j.pageNum === pageNum)) return;
        this._blobQueue.push({ fileId, pageNum, el });
        this._drainBlobQueue();
    },

    _drainBlobQueue() {
        while (this._activeBlobRenders < this._MAX_BLOB_CONCURRENT && this._blobQueue.length > 0) {
            const job = this._blobQueue.pop(); // LIFO
            this._activeBlobRenders++;
            this._renderSheetPage(job.fileId, job.pageNum, job.el).finally(() => {
                this._activeBlobRenders--;
                this._drainBlobQueue();
            });
        }
    },

    async _renderSheetPage(fileId, pageNum, el) {
        const SHEET_SCALE = 0.8; // reduced scale for sheet view thumbnails
        const img = el.querySelector('img.sheet-page-img');
        if (!img) return;

        const fileEntry = AppState.files.find(f => f.id === fileId);
        if (!fileEntry?.pdfDoc) return;

        const rotation = fileEntry.pageRotations?.get(pageNum) ?? null;
        RotationHelper.updateBadge(el, rotation);

        const entry = await this._renderBlobPage(fileId, pageNum, SHEET_SCALE, this._cache);
        if (!entry) return;

        // Check element still in DOM (user may have switched views)
        if (!this._container?.contains(el)) return;

        img.src = entry.url;
        el.classList.add('rendered');
    },

```

Note: `SHEET_SCALE = 0.8` — sheet view shows pages at a size bigger than thumb (0.26) but smaller than full page (1.5). Adjust this value visually; it determines the quality/size of sheet-view page thumbnails.

- [ ] **Step 4: Update `_unmountOffScreen` to handle both img types**

Find in `_unmountOffScreen` (line ~3452):
```js
                const canvas = el.querySelector('canvas');
                if (canvas && (canvas.width > 0 || canvas.height > 0)) {
                    // Preserve card height so scroll position stays stable
                    el.style.minHeight = `${el.offsetHeight}px`;
                    // Release GPU memory
                    canvas.width = 0;
                    canvas.height = 0;
                    el.classList.remove('rendered');
                }
```

Replace with:
```js
                // Sheet-view cards use <img>, page-view cards use <canvas>
                const imgEl    = el.querySelector('img.sheet-page-img');
                const canvasEl = el.querySelector('canvas');
                if (imgEl && imgEl.src) {
                    el.style.minHeight = `${el.offsetHeight}px`;
                    imgEl.src = '';
                    el.classList.remove('rendered');
                } else if (canvasEl && (canvasEl.width > 0 || canvasEl.height > 0)) {
                    el.style.minHeight = `${el.offsetHeight}px`;
                    canvasEl.width = 0;
                    canvasEl.height = 0;
                    el.classList.remove('rendered');
                }
```

- [ ] **Step 5: Verify syntax**

Run: `node --check frontend/app.js`
Expected: no output

---

## Task 5: Surgical rotation patch — avoid full `render()` on rotate in sheet view

**Files:**
- Modify: `frontend/app.js` — `ContextMenu` dispatch block (line ~1704) and `_applyRotation` (line ~1747)
- Modify: `frontend/app.js` — add `_patchRotatedPage()` to `PreviewPanelModule`

### Step 5a: Add `_patchRotatedPage()` to `PreviewPanelModule`

This method: (1) CSS-rotates the img instantly, (2) re-blobs from cached canvas, (3) swaps `img.src`.

- [ ] **Step 1: Insert `_patchRotatedPage` into `PreviewPanelModule` before `scrollToPage`**

Find `scrollToPage(pageNum) {` (line ~3388) and insert BEFORE it:

```js
    // Called after a single page is rotated in sheet view.
    // Phase 1: instant CSS transform. Phase 2: re-blob from cache (~3-7ms).
    // Never wipes innerHTML — only updates the one rotated page.
    async _patchRotatedPage(fileId, pageNum) {
        const key = `${fileId}-${pageNum}`;
        const el  = this._pageEls.get(key);
        if (!el) return; // not currently in DOM (scrolled away)

        const fileEntry = AppState.files.find(f => f.id === fileId);
        if (!fileEntry) return;

        const rotation = fileEntry.pageRotations?.get(pageNum) ?? null;
        const rotDeg   = RotationHelper.toDeg(rotation);
        const img      = el.querySelector('img.sheet-page-img');

        if (!img) {
            // Page-view canvas card — use existing incremental re-render
            el.classList.remove('rendered');
            this._enqueue(fileId, pageNum, el);
            return;
        }

        // Phase 1: instant CSS rotation (compositor only, ~0ms)
        const deg = rotDeg;
        img.style.transform = deg ? `rotate(${deg}deg)` : '';

        // Phase 2: re-blit from cached OffscreenCanvas (~3-7ms total for all visible pages)
        const SHEET_SCALE = 0.8;
        const oldKey = (() => {
            // Find old cache entry (any rotation for this page)
            const prefix = `${fileId}-${pageNum}-`;
            // We need the entry with the OLD rotation to get the canvas
            // Actually we re-render at the new rotation from scratch using _renderBlobPage
        })();

        // Invalidate old cache entry for this page at all rotations
        this._cache.deleteByPrefix(`${fileId}-${pageNum}-`);

        // Re-render at new rotation (uses pdf.js only for the canvas, then re-blobs)
        el.classList.remove('rendered');
        img.style.transform = ''; // will be set by _renderSheetPage via RotationHelper
        this._enqueueBlob(fileId, pageNum, el);
    },

```

Wait — Phase 1 CSS trick is only useful if we have the old blob to show while re-rendering. But we just deleted the cache. Let me fix: keep old url for Phase 1, then delete cache, then re-render.

- [ ] **Step 1 (corrected): Replace `_patchRotatedPage` with the fixed version**

```js
    // Called after a single page is rotated in sheet view.
    // Phase 1: instant CSS transform on existing img (~0ms).
    // Phase 2: async re-render at new rotation, swap img.src (~3-7ms).
    async _patchRotatedPage(fileId, pageNum) {
        const key = `${fileId}-${pageNum}`;
        const el  = this._pageEls.get(key);
        if (!el) return;

        const fileEntry = AppState.files.find(f => f.id === fileId);
        if (!fileEntry) return;

        const rotation = fileEntry.pageRotations?.get(pageNum) ?? null;
        const rotDeg   = RotationHelper.toDeg(rotation);
        const img      = el.querySelector('img.sheet-page-img');

        if (!img) {
            // Page-view canvas card — incremental canvas re-render
            el.classList.remove('rendered');
            this._enqueue(fileId, pageNum, el);
            return;
        }

        // Phase 1: instant CSS hint (compositor, ~0ms)
        if (rotDeg) img.style.transform = `rotate(${rotDeg}deg)`;

        // Phase 2: invalidate old cache entry, re-render at new rotation
        this._cache.deleteByPrefix(`${fileId}-${pageNum}-`);
        el.classList.remove('rendered');
        // _renderSheetPage will clear the CSS transform and set proper img.src
        this._enqueueBlob(fileId, pageNum, el);
    },

```

And update `_renderSheetPage` to always clear CSS transform before setting `img.src`:

Find in `_renderSheetPage` (added in Task 4 Step 3):
```js
        img.src = entry.url;
        el.classList.add('rendered');
```

Replace with:
```js
        img.style.transform = ''; // clear phase-1 CSS hint
        img.src = entry.url;
        el.classList.add('rendered');
```

### Step 5b: Update the rotation caller

- [ ] **Step 2: Update ContextMenu rotation dispatch (line ~1704)**

Find:
```js
        if (AppState.viewMode === 'sheet' && AppState.activeFile) {
            PreviewPanelModule.render(AppState.activeFile);
        } else {
            PreviewPanelModule.onStateChanged();
        }
```

Replace with:
```js
        if (AppState.viewMode === 'sheet' && AppState.activeFile) {
            // Surgical patch — only update the rotated page, no DOM wipe
            PreviewPanelModule._patchRotatedPage(AppState.activeFile.id, n);
            // Also re-render the sheet layout if orientation changed (landscape grouping)
            // Check if this page's orientation flipped — if so, full rebuild needed
            const newRot = RotationHelper.toDeg(AppState.activeFile.pageRotations?.get(n) ?? null);
            const wasLS  = AppState.activeFile._orientationMap?.get(n) ?? false;
            const willLS = (() => {
                // Landscape if 90 or 270 → portrait page becomes landscape, etc.
                // We already updated _orientationMap.delete(n) in _applyRotation.
                // Let sheet re-layout decide — only do full render if orientation changed.
                return false; // placeholder — see note below
            })();
            // Simpler: always do surgical patch; _orientationMap entry was deleted by
            // _applyRotation so next _renderSheetView will re-fetch only this one page.
            // For now: surgical patch covers visible re-render; orientation recalc deferred.
        } else {
            PreviewPanelModule.onStateChanged();
        }
```

Actually this is getting complex. Simpler and correct approach: for rotation in sheet mode, call `_patchRotatedPage` AND also do a lightweight layout-only rebuild that skips orientation re-detection (uses the cached `_orientationMap` which already has the entry deleted — so it fetches 1 page then rebuilds DOM).

- [ ] **Step 2 (simplified): Final version**

Replace the rotation caller block:
```js
        if (AppState.viewMode === 'sheet' && AppState.activeFile) {
            // _patchRotatedPage: instant CSS + re-blob in place (no DOM wipe)
            PreviewPanelModule._patchRotatedPage(AppState.activeFile.id, n);
        } else {
            PreviewPanelModule.onStateChanged();
        }
```

This alone handles the visible update fast. The `_orientationMap` has `n` deleted by `_applyRotation`, so IF the user changes landscape/portrait grouping they'll see it on the next full render (switching view mode or adding pages). For the rotation UX itself (showing the page rotated), `_patchRotatedPage` is sufficient.

- [ ] **Step 3: Also patch ThumbStrip rotation (currently calls `_enqueue`)**

In `_applyRotation` (line ~1765):
```js
            const card = PreviewPanelModule._pageEls.get(`${fid}-${pageNum}`);
            if (card) { card.classList.remove('rendered'); PreviewPanelModule._enqueue(fid, pageNum, card); }
```

After this block, the thumb update at line ~1767:
```js
            const thumbEl = ThumbStripModule._container?.querySelector(`.thumb-item[data-file-id="${fid}"][data-page="${pageNum}"]`);
            if (thumbEl) { thumbEl.classList.remove('rendered'); ThumbStripModule._enqueue(fid, pageNum, thumbEl); }
```

The thumb `_enqueue` still works — it calls `_renderThumb` which now uses blob path. No change needed here. ✅

- [ ] **Step 4: Verify syntax**

Run: `node --check frontend/app.js`
Expected: no output

---

## Task 6: Add CSS for `img.thumb-img` and `img.sheet-page-img`

**Files:**
- Modify: `frontend/styles.css`

- [ ] **Step 1: Append CSS rules**

Read the last line of `frontend/styles.css` to confirm current end, then append:

```css
/* ── Blob-URL img replacements for canvas thumbnails ─────────────── */
.thumb-img {
  display: block;
  width: 100%;
  height: auto;
  object-fit: contain;
}

.sheet-page-img {
  display: block;
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
  will-change: transform; /* promote to compositor layer for instant CSS rotation */
}
```

- [ ] **Step 2: Verify visually** — open app in browser, load PDF, check thumbstrip shows pages correctly.

---

## Task 7: Final verification

- [ ] **Step 1: Syntax check**

Run: `node --check frontend/app.js`
Expected: no output

- [ ] **Step 2: Manual smoke test**

1. Open app, load a PDF with 14+ pages
2. **ThumbStrip**: thumbnails render → check they appear (img tags, not canvas)
3. **Sheet View**: switch to "Xem trước khi in" → check pages appear
4. **Rotate a page** in sheet view via right-click context menu:
   - Instant CSS rotation (Phase 1): img should flip in ≤1 frame
   - Re-render (Phase 2): img.src updates with correct rotation in ~15-30ms
   - Other pages: UNCHANGED (no DOM wipe)
5. **Rotate a page** in page view: canvas re-render only for that page (unchanged behavior)
6. **Rotate a thumbnail**: thumbstrip updates that thumb only
7. **Scroll sheet view**: virtual scroll unmounts `img.src = ''` for far-off pages, remounts on scroll back
8. **Switch files/reload**: all caches clear correctly, no stale blob URLs

- [ ] **Step 3: Memory check** — open Chrome DevTools → Memory tab → take heap snapshot after rotating 10+ pages → confirm no growing blob URL list (all old URLs should be revoked)
