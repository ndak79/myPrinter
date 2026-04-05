# Group 1 — Thumbnail & Preview — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enhance the thumbnail grid with orientation badges, hover preview popup, drag-to-reorder pages, and per-page rotation. Two features (drag-reorder and rotation) require backend changes.

**Architecture:** Frontend: `PreviewModule` (orientation badge, hover preview cache), new `DragReorderModule`, `HoverPreviewModule`; extended `ContextMenu` (rotation submenu); extended `AppState` (`pageOrder`, `pageRotations`). Backend: `PrintRequest` gets `PageOrder: int[]?` and `PageRotations: List<PageRotation>?`; `PrintAlgorithmService` applies both before building phases.

**Tech Stack:** Vanilla JS (pointer events for drag), CSS transforms for rotation preview, PdfSharp for backend rotation, .NET 8.

**Implement in this order:** 1 (badge) → O (pop — already done in Group 5) → J (hover preview) → B (keyboard — already done in Group 2) → I (drag-reorder) → U (per-page rotation)

---

## File Map

| File | Changes |
|------|---------|
| `frontend/index.html` | Add `#hover-preview` div; add rotation items to `#page-context-menu` |
| `frontend/styles.css` | `.orientation-badge`; `#hover-preview`; `.drag-ghost`; `.drop-placeholder`; rotation context menu |
| `frontend/app.js` | `PreviewModule._createPlaceholder()` — orientation badge; new `HoverPreviewModule`; new `DragReorderModule`; extend `ContextMenu._handleAction()`; extend `AppState`; extend `PrintModule._startPrint()` payload |
| `backend/Models/PrintModels.cs` | Add `PageOrder`, `PageRotations`, `PageRotation`, `RotationDirection` |
| `backend/Services/PrintAlgorithmService.cs` | Apply `PageOrder` reordering and `PageRotations` before building phases |
| `backend/Services/WordInteropService.cs` | Extend `CreateRotatedPdfSubset()` to accept per-page rotation map |

---

## Task 1: Orientation Badge on Thumbnails (1)

**Files:**
- Modify: `frontend/styles.css` — add `.orientation-badge`
- Modify: `frontend/app.js` — `PreviewModule._renderCanvas()` adds badge after render

- [ ] **Step 1: Add orientation badge CSS**

In `frontend/styles.css`, after `.sidebar-preview-grid .page-number` block, add:

```css
/* ── Orientation Badge on Thumbnails (1) ───────────────────── */
.orientation-badge {
    position: absolute;
    top: 5px;
    left: 5px;
    background: rgba(0, 0, 0, 0.65);
    color: rgba(255, 255, 255, 0.9);
    font-size: 9px;
    font-weight: 600;
    padding: 2px 5px;
    border-radius: 3px;
    line-height: 1.3;
    pointer-events: none;
    letter-spacing: 0.02em;
    z-index: 2;
}

.orientation-badge.landscape {
    background: rgba(102, 126, 234, 0.75);
}

[data-theme="light"] .orientation-badge {
    background: rgba(30, 41, 59, 0.65);
}
```

- [ ] **Step 2: Add orientation badge in PreviewModule._renderCanvas()**

In `frontend/app.js`, in `PreviewModule._renderCanvas(thumb, pageNum)` (around line 366), after `await page.render(...)`:

```js
async _renderCanvas(thumb, pageNum) {
    try {
        const page     = await AppState.currentPdfDoc.getPage(pageNum);
        const viewport = page.getViewport({ scale: 0.6 });
        const canvas   = document.createElement('canvas');
        const ctx      = canvas.getContext('2d');
        canvas.width  = viewport.width;
        canvas.height = viewport.height;
        canvas.style.cssText = 'width:100%!important;height:auto!important;display:block;border-radius:6px;';
        await page.render({ canvasContext: ctx, viewport }).promise;
        thumb.insertBefore(canvas, thumb.firstChild);
        thumb.dataset.rendered = '1';

        // Add orientation badge (1)
        const isLandscape = viewport.width > viewport.height;
        const badge = document.createElement('div');
        badge.className = `orientation-badge${isLandscape ? ' landscape' : ''}`;
        badge.textContent = isLandscape ? '▭ Ngang' : '▯ Dọc';
        thumb.appendChild(badge);
    } catch (err) {
        console.error(`Error rendering page ${pageNum}:`, err);
    }
},
```

- [ ] **Step 3: Verify orientation badges**

Upload a PDF with both portrait and landscape pages (or any PDF). Each thumbnail should show a small "▯ Dọc" (dark) or "▭ Ngang" (blue) badge in top-left corner.

- [ ] **Step 4: Commit**

```bash
git add frontend/styles.css frontend/app.js
git commit -m "feat(ui): orientation badge on sidebar thumbnails (1)"
```

---

## Task 2: Hover Preview Popup (J)

**Files:**
- Modify: `frontend/index.html` — add `#hover-preview` container
- Modify: `frontend/styles.css` — add `#hover-preview` styles
- Modify: `frontend/app.js` — add `HoverPreviewModule`; call `HoverPreviewModule.attach()` from `PreviewModule._createPlaceholder()`

- [ ] **Step 1: Add hover preview HTML to index.html**

In `frontend/index.html`, before `</body>`, add:

```html
<!-- Hover Preview Popup (J) -->
<div id="hover-preview" hidden aria-label="Xem trước trang" role="tooltip">
    <canvas id="hover-preview-canvas" width="280" height="360"></canvas>
</div>
```

- [ ] **Step 2: Add hover preview CSS to styles.css**

```css
/* ── Hover Preview Popup (J) ───────────────────────────────── */
#hover-preview {
    position: fixed;
    z-index: 8000;
    width: 280px;
    background: var(--bg-card);
    backdrop-filter: blur(10px);
    border: 1px solid var(--border-color);
    border-radius: 10px;
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4);
    overflow: hidden;
    pointer-events: none;
    opacity: 0;
    transform: scale(0.96);
    transition: opacity 120ms ease, transform 120ms ease;
}

#hover-preview.visible {
    opacity: 1;
    transform: scale(1);
}

#hover-preview canvas {
    display: block;
    width: 100%;
    height: auto;
}
```

- [ ] **Step 3: Add HoverPreviewModule to app.js**

In `frontend/app.js`, add before the BOOTSTRAP section:

```js
// ═══════════════════════════════════════════════════════════════════
// HoverPreviewModule — Hover popup with larger page preview (J)
// ═══════════════════════════════════════════════════════════════════
const HoverPreviewModule = {
    SHOW_DELAY:    150, // ms before showing
    PREVIEW_W:     280,
    PREVIEW_H:     360,
    _timer:        null,
    _activeThumb:  null,
    _cache:        new Map(), // pageNum → offscreen canvas

    _preview()  { return document.getElementById('hover-preview'); },
    _pCanvas()  { return document.getElementById('hover-preview-canvas'); },

    attach(thumb, pageNum) {
        thumb.addEventListener('mouseenter', () => {
            clearTimeout(this._timer);
            this._timer = setTimeout(() => this._show(thumb, pageNum), this.SHOW_DELAY);
        });
        thumb.addEventListener('mouseleave', () => {
            clearTimeout(this._timer);
            this._hide();
        });
        // Keyboard
        thumb.addEventListener('focus', () => this._show(thumb, pageNum));
        thumb.addEventListener('blur',  () => this._hide());
    },

    async _show(thumb, pageNum) {
        if (!AppState.currentPdfDoc) return;
        const preview = this._preview();
        const pCanvas = this._pCanvas();
        if (!preview || !pCanvas) return;

        // Render or use cached
        if (!this._cache.has(pageNum)) {
            try {
                const page     = await AppState.currentPdfDoc.getPage(pageNum);
                const viewport = page.getViewport({ scale: 1.0 });
                const scale    = Math.min(this.PREVIEW_W / viewport.width, this.PREVIEW_H / viewport.height);
                const vp2      = page.getViewport({ scale });
                const off      = document.createElement('canvas');
                off.width      = vp2.width;
                off.height     = vp2.height;
                await page.render({ canvasContext: off.getContext('2d'), viewport: vp2 }).promise;
                this._cache.set(pageNum, off);
            } catch { return; }
        }

        const cached = this._cache.get(pageNum);
        pCanvas.width  = cached.width;
        pCanvas.height = cached.height;
        pCanvas.getContext('2d').drawImage(cached, 0, 0);

        // Position
        const rect = thumb.getBoundingClientRect();
        this._position(preview, rect);

        preview.hidden = false;
        this._activeThumb = thumb;
        requestAnimationFrame(() => preview.classList.add('visible'));
    },

    _hide() {
        const preview = this._preview();
        if (!preview) return;
        preview.classList.remove('visible');
        this._activeThumb = null;
        // Hide after transition
        setTimeout(() => { if (!preview.classList.contains('visible')) preview.hidden = true; }, 130);
    },

    _position(preview, anchorRect) {
        const W = this.PREVIEW_W + 20; // approx width + gap
        const H = this.PREVIEW_H + 20;
        let left = anchorRect.right + 12;
        let top  = anchorRect.top + (anchorRect.height / 2) - (H / 2);

        // Flip left if would overflow right
        if (left + W > window.innerWidth) left = anchorRect.left - W - 4;
        // Clamp vertically
        top = Math.max(8, Math.min(top, window.innerHeight - H - 8));

        preview.style.left = Math.round(left) + 'px';
        preview.style.top  = Math.round(top)  + 'px';
    },

    clearCache() { this._cache.clear(); },
};
```

- [ ] **Step 4: Call HoverPreviewModule.attach() in PreviewModule._createPlaceholder()**

In `frontend/app.js`, in `PreviewModule._createPlaceholder(pageNum)`, at the end before `return div`:

```js
HoverPreviewModule.attach(div, pageNum);
```

Also clear cache on new file load. In `PreviewModule.render()`, after `this._observer?.disconnect()`:

```js
HoverPreviewModule.clearCache();
```

- [ ] **Step 5: Initialize HoverPreviewModule (no separate init needed — attaches per-thumb)**

Add to BOOTSTRAP for future compat:
```js
// HoverPreviewModule attaches per-thumb in PreviewModule._createPlaceholder()
```

- [ ] **Step 6: Verify hover preview**

Upload a PDF. Hover over a sidebar thumbnail for 150ms — a larger (280px wide) preview should appear to the right (or left if near right edge). Move mouse away — preview fades out. Second hover on same page: instant (cached).

- [ ] **Step 7: Commit**

```bash
git add frontend/index.html frontend/styles.css frontend/app.js
git commit -m "feat(ui): hover preview popup for thumbnails (J)"
```

---

## Task 3: Drag-to-Reorder Pages (I) — Backend

**Files:**
- Modify: `backend/Models/PrintModels.cs` — add `PageOrder` field to `PrintRequest`
- Modify: `backend/Services/PrintAlgorithmService.cs` — apply `PageOrder` before building page phases

- [ ] **Step 1: Read PrintModels.cs to find PrintRequest class**

Read `D:\Pro\myPrinter\backend\Models\PrintModels.cs` to find the `PrintRequest` class definition.

- [ ] **Step 2: Add PageOrder to PrintRequest in PrintModels.cs**

In `backend/Models/PrintModels.cs`, find `public class PrintRequest`. Add after the `Collate` property:

```csharp
/// <summary>
/// Optional explicit page order. If provided, pages are printed in this order
/// rather than the natural document order. Each value is a 1-based page number.
/// Example: [3, 1, 2] prints page 3 first, then 1, then 2.
/// </summary>
public int[]? PageOrder { get; set; }
```

- [ ] **Step 3: Apply PageOrder in PrintAlgorithmService**

In `backend/Services/PrintAlgorithmService.cs`, find the method(s) that build the page list from `PrintRequest` (look for where `pageRange` is parsed and `selectedPages` list is built — typically in `CreateNormalDuplexJob`, `CreateSimplexJob`, `CreateBookletJob`).

After the selected pages list is built (a `List<int>` of 1-based page numbers), add reordering:

```csharp
// Apply custom page order if provided (I)
if (request.PageOrder != null && request.PageOrder.Length > 0)
{
    // Reorder: keep only pages in both selectedPages and PageOrder, in the specified order
    var selectedSet = new HashSet<int>(selectedPages);
    var reordered   = request.PageOrder.Where(p => selectedSet.Contains(p)).ToList();
    // Add any selected pages not in PageOrder at the end (safety)
    foreach (var p in selectedPages.Where(p => !reordered.Contains(p)))
        reordered.Add(p);
    selectedPages = reordered;
}
```

This must be added in all three job creation methods that use `selectedPages`.

- [ ] **Step 4: Build backend**

```bash
dotnet build D:\Pro\myPrinter\backend\PrinterApp.csproj
```

Expected: 0 errors.

- [ ] **Step 5: Commit backend**

```bash
git add backend/Models/PrintModels.cs backend/Services/PrintAlgorithmService.cs
git commit -m "feat(backend): add PageOrder support for drag-to-reorder (I)"
```

---

## Task 4: Drag-to-Reorder Pages (I) — Frontend

**Files:**
- Modify: `frontend/styles.css` — add `.drag-ghost`, `.drop-placeholder`
- Modify: `frontend/app.js` — add `DragReorderModule`; extend `AppState` with `pageOrder`; update `PrintModule._startPrint()` payload; update `PreviewModule` to respect order

- [ ] **Step 1: Add drag CSS to styles.css**

```css
/* ── Drag-to-Reorder (I) ────────────────────────────────────── */
.page-thumbnail.dragging {
    opacity: 0.4;
    transform: scale(0.96);
}

.drag-ghost {
    position: fixed;
    pointer-events: none;
    z-index: 9998;
    opacity: 0.85;
    box-shadow: 0 8px 24px rgba(0,0,0,0.4);
    border-radius: 8px;
    border: 2px solid #667eea;
    background: var(--bg-card);
    transition: none;
    transform: rotate(2deg);
}

.drop-placeholder {
    width: 100%;
    min-height: 60px;
    border: 2px dashed #667eea;
    border-radius: 8px;
    background: rgba(102, 126, 234, 0.08);
    pointer-events: none;
    margin: 2px 0;
}
```

- [ ] **Step 2: Add pageOrder to AppState**

In `frontend/app.js`, in `AppState`, add:

```js
pageOrder: [], // 1-based page numbers in display/print order; empty = natural order
```

And in `AppState.reset()`:
```js
this.pageOrder = [];
```

- [ ] **Step 3: Initialize pageOrder when PDF loads**

In `frontend/app.js`, in `PreviewModule.render()`, after `AppState.totalPageCount = ...`:

```js
AppState.pageOrder = Array.from({ length: AppState.totalPageCount }, (_, i) => i + 1);
```

- [ ] **Step 4: Add DragReorderModule to app.js**

In `frontend/app.js`, add before BOOTSTRAP:

```js
// ═══════════════════════════════════════════════════════════════════
// DragReorderModule — Pointer-event drag to reorder page thumbnails (I)
// ═══════════════════════════════════════════════════════════════════
const DragReorderModule = {
    _ghost:       null,
    _dragging:    null,
    _placeholder: null,
    _startY:      0,
    _dragPageNum: null,

    init() {
        const grid = document.getElementById('sidebar-preview-grid');
        if (!grid) return;
        grid.addEventListener('pointerdown', e => this._onDown(e));
    },

    _onDown(e) {
        const thumb = e.target.closest('.page-thumbnail');
        if (!thumb) return;
        // Only left button, not on the hover-preview canvas
        if (e.button !== 0) return;

        this._dragging    = thumb;
        this._dragPageNum = parseInt(thumb.dataset.pageNumber);
        this._startY      = e.clientY;

        // Create ghost
        const rect  = thumb.getBoundingClientRect();
        this._ghost = thumb.cloneNode(true);
        this._ghost.className = 'drag-ghost';
        this._ghost.style.cssText = `
            width: ${rect.width}px;
            height: ${rect.height}px;
            left: ${rect.left}px;
            top:  ${rect.top}px;
        `;
        document.body.appendChild(this._ghost);

        thumb.classList.add('dragging');

        document.addEventListener('pointermove', this._onMove = e => this._move(e));
        document.addEventListener('pointerup',   this._onUp   = e => this._drop(e));
        e.preventDefault();
    },

    _move(e) {
        if (!this._ghost) return;
        const dy = e.clientY - this._startY;
        const rect = this._dragging.getBoundingClientRect();
        this._ghost.style.top = (rect.top + dy) + 'px';

        // Find drop target
        const grid   = document.getElementById('sidebar-preview-grid');
        const thumbs = Array.from(grid.querySelectorAll('.page-thumbnail:not(.dragging)'));
        const y      = e.clientY;

        // Remove old placeholder
        this._placeholder?.remove();
        this._placeholder = null;

        // Find insertion point
        let insertBefore = null;
        for (const t of thumbs) {
            const r = t.getBoundingClientRect();
            if (y < r.top + r.height / 2) { insertBefore = t; break; }
        }

        this._placeholder = document.createElement('div');
        this._placeholder.className = 'drop-placeholder';
        if (insertBefore) {
            grid.insertBefore(this._placeholder, insertBefore);
        } else {
            grid.appendChild(this._placeholder);
        }
    },

    _drop(e) {
        document.removeEventListener('pointermove', this._onMove);
        document.removeEventListener('pointerup',   this._onUp);

        if (this._ghost)       { this._ghost.remove();       this._ghost = null; }
        if (this._dragging)    this._dragging.classList.remove('dragging');

        if (this._placeholder) {
            // Reorder AppState.pageOrder
            const grid    = document.getElementById('sidebar-preview-grid');
            const thumbs  = Array.from(grid.querySelectorAll('.page-thumbnail'));
            const dragIdx = thumbs.indexOf(this._dragging);

            // Compute new order from DOM after inserting dragging before placeholder
            const newOrder = [];
            let placed = false;
            for (const t of grid.childNodes) {
                if (t === this._placeholder) {
                    if (!placed) { newOrder.push(this._dragPageNum); placed = true; }
                } else if (t.classList?.contains('page-thumbnail') && t !== this._dragging) {
                    newOrder.push(parseInt(t.dataset.pageNumber));
                }
            }
            if (!placed) newOrder.push(this._dragPageNum);

            AppState.pageOrder = newOrder;

            // Re-render grid in new order
            this._placeholder.remove();
            this._placeholder = null;
            this._reRenderGrid(newOrder);

            showToast('Đã đổi thứ tự trang', 'info');
        }

        this._dragging    = null;
        this._dragPageNum = null;
        this._placeholder = null;
    },

    _reRenderGrid(order) {
        const grid   = document.getElementById('sidebar-preview-grid');
        const thumbs = Array.from(grid.querySelectorAll('.page-thumbnail'));
        const byPage = new Map(thumbs.map(t => [parseInt(t.dataset.pageNumber), t]));
        // Reorder DOM
        order.forEach(pageNum => {
            const t = byPage.get(pageNum);
            if (t) grid.appendChild(t);
        });
        PreviewModule.updateThumbnails();
    },
};
```

- [ ] **Step 5: Initialize DragReorderModule in BOOTSTRAP**

```js
DragReorderModule.init();
```

- [ ] **Step 6: Include pageOrder in print payload**

In `frontend/app.js`, in `PrintModule._startPrint()`, in the `body` object (around line 786), add:

```js
const body = {
    fileId:           AppState.uploadedFile.id,
    printerName:      AppState.selectedPrinter.name,
    mode:             mode === 'normal' ? 0 : (mode === 'booklet' ? 1 : 2),
    pageRange,
    singleSidedPages: AppState.singleSidedPages.size > 0 ? Array.from(AppState.singleSidedPages) : null,
    copies:           CopiesModule.copies,
    collate:          CopiesModule.collate,
    // Drag-reorder (I)
    pageOrder:        AppState.pageOrder.length > 0 ? AppState.pageOrder : null,
};
```

- [ ] **Step 7: Verify drag-to-reorder**

Upload a multi-page PDF. Hold mousedown on a thumbnail and drag up/down — a ghost should follow the cursor, a blue placeholder shows drop position. Release to drop — thumbnail moves, page number doesn't change (reflects original page), but order in `AppState.pageOrder` changes. Print — pages should print in new order.

- [ ] **Step 8: Commit frontend**

```bash
git add frontend/styles.css frontend/app.js
git commit -m "feat(ui): drag-to-reorder page thumbnails (I)"
```

---

## Task 5: Per-page Rotation (U) — Backend

**Files:**
- Modify: `backend/Models/PrintModels.cs` — add `PageRotation`, `RotationDirection`, add `PageRotations` to `PrintRequest`
- Modify: `backend/Services/PrintAlgorithmService.cs` — apply rotations before building phases
- Modify: `backend/Services/WordInteropService.cs` — extend `CreateRotatedPdfSubset()` with rotation

- [ ] **Step 1: Add PageRotation model to PrintModels.cs**

In `backend/Models/PrintModels.cs`, add after `PrintRequest`:

```csharp
public enum RotationDirection
{
    None           = 0,
    CW90           = 90,   // Clockwise 90°
    CCW90          = 270,  // Counter-clockwise 90°
    Rotate180      = 180,
    FlipHorizontal = -1,   // Mirror left-right
    FlipVertical   = -2,   // Mirror top-bottom
}

public class PageRotation
{
    /// <summary>1-based page number</summary>
    public int PageNumber { get; set; }
    public RotationDirection Rotation { get; set; }
}
```

And add to `PrintRequest`:

```csharp
/// <summary>
/// Per-page rotations/flips applied before printing.
/// Null or empty means no rotations.
/// </summary>
public List<PageRotation>? PageRotations { get; set; }
```

- [ ] **Step 2: Apply per-page rotations in PrintAlgorithmService**

In `backend/Services/PrintAlgorithmService.cs`, after `PageOrder` reordering (added in Task 3), add:

```csharp
// Apply per-page rotations (U)
if (request.PageRotations != null && request.PageRotations.Count > 0)
{
    var rotationMap = request.PageRotations.ToDictionary(r => r.PageNumber, r => r.Rotation);
    // Pass rotationMap to PDF processing — applies PdfSharp rotation per page
    // This is done in CreateRotatedPdfSubset via WordInteropService
    // Store in job state for use during phase building
    job.PageRotations = rotationMap;
}
```

Note: `PrintJobState` needs a `PageRotations` property. Add to `PrintJobState` class:
```csharp
public Dictionary<int, RotationDirection>? PageRotations { get; set; }
```

- [ ] **Step 3: Extend WordInteropService to apply rotations**

In `backend/Services/WordInteropService.cs`, find `CreateRotatedPdfSubset()` method. Add an overload or parameter to accept `Dictionary<int, RotationDirection>? rotations`:

```csharp
// Inside the method, after selecting each page from the PDF:
if (rotations != null && rotations.TryGetValue(originalPageNum, out var rotation))
{
    switch (rotation)
    {
        case RotationDirection.CW90:
            page.Rotate = 90;
            break;
        case RotationDirection.CCW90:
            page.Rotate = 270;
            break;
        case RotationDirection.Rotate180:
            page.Rotate = 180;
            break;
        case RotationDirection.FlipHorizontal:
        case RotationDirection.FlipVertical:
            // PdfSharp doesn't support flip natively; use content stream transform
            // For now, apply 180° as fallback + note in comment
            page.Rotate = 180;
            break;
    }
}
```

**Note on FlipH/FlipV:** PdfSharp's `Page.Rotate` only supports 90/180/270. True flip requires content stream manipulation. For the MVP, FlipH and FlipV both map to 180° rotation as a best approximation. Document this limitation in a code comment.

- [ ] **Step 4: Build and test backend**

```bash
dotnet build D:\Pro\myPrinter\backend\PrinterApp.csproj
dotnet test  D:\Pro\myPrinter\backend.Tests
```

Expected: build succeeds, all 82 tests pass.

- [ ] **Step 5: Commit backend rotation**

```bash
git add backend/Models/PrintModels.cs backend/Services/PrintAlgorithmService.cs backend/Services/WordInteropService.cs
git commit -m "feat(backend): per-page rotation support (U)"
```

---

## Task 6: Per-page Rotation (U) — Frontend

**Files:**
- Modify: `frontend/index.html` — add rotation items to `#page-context-menu`
- Modify: `frontend/styles.css` — add rotation submenu styles
- Modify: `frontend/app.js` — extend `AppState.pageRotations`; extend `ContextMenu._handleAction()`; apply CSS transform on thumbnails; include in print payload

- [ ] **Step 1: Add pageRotations to AppState**

In `frontend/app.js`, in `AppState`:

```js
pageRotations: new Map(), // key: pageNum (1-based), value: rotation string
```

And in `AppState.reset()`:
```js
this.pageRotations = new Map();
```

- [ ] **Step 2: Add rotation context menu items to index.html**

In `frontend/index.html`, in `#page-context-menu`, after the last `.context-menu-separator`, add:

```html
<div class="context-menu-separator"></div>
<div class="context-menu-header-sub">Xoay trang</div>
<div class="context-menu-item" data-action="rotate-cw90">
    <span class="context-menu-icon">↻</span>
    <span>Xoay phải 90°</span>
</div>
<div class="context-menu-item" data-action="rotate-ccw90">
    <span class="context-menu-icon">↺</span>
    <span>Xoay trái 90°</span>
</div>
<div class="context-menu-item" data-action="rotate-fliph">
    <span class="context-menu-icon">↔</span>
    <span>Lật ngang</span>
</div>
<div class="context-menu-item" data-action="rotate-flipv">
    <span class="context-menu-icon">↕</span>
    <span>Lật dọc</span>
</div>
<div class="context-menu-item" data-action="rotate-180">
    <span class="context-menu-icon">🔄</span>
    <span>Xoay 180°</span>
</div>
<div class="context-menu-item" data-action="rotate-reset">
    <span class="context-menu-icon">↩</span>
    <span>Reset về gốc</span>
</div>
```

- [ ] **Step 3: Add rotation CSS**

In `frontend/styles.css`, add:

```css
/* ── Per-page Rotation (U) ─────────────────────────────────── */
.context-menu-header-sub {
    font-size: 0.7rem;
    color: var(--text-muted);
    padding: 0.25rem 0.75rem;
    text-transform: uppercase;
    letter-spacing: 0.05em;
}

.page-thumbnail[data-rotation="CW90"]  canvas { transform: rotate(90deg);  transform-origin: center; }
.page-thumbnail[data-rotation="CCW90"] canvas { transform: rotate(-90deg); transform-origin: center; }
.page-thumbnail[data-rotation="Rotate180"] canvas { transform: rotate(180deg); transform-origin: center; }
.page-thumbnail[data-rotation="FlipHorizontal"] canvas { transform: scaleX(-1); }
.page-thumbnail[data-rotation="FlipVertical"]   canvas { transform: scaleY(-1); }
```

- [ ] **Step 4: Handle rotation actions in ContextMenu._handleAction()**

In `frontend/app.js`, in `ContextMenu._handleAction(action)`, add to the switch statement:

```js
case 'rotate-cw90':
    if (n !== null) { AppState.pageRotations.set(n, 'CW90'); this._applyRotation(n); } break;
case 'rotate-ccw90':
    if (n !== null) { AppState.pageRotations.set(n, 'CCW90'); this._applyRotation(n); } break;
case 'rotate-fliph':
    if (n !== null) { AppState.pageRotations.set(n, 'FlipHorizontal'); this._applyRotation(n); } break;
case 'rotate-flipv':
    if (n !== null) { AppState.pageRotations.set(n, 'FlipVertical'); this._applyRotation(n); } break;
case 'rotate-180':
    if (n !== null) { AppState.pageRotations.set(n, 'Rotate180'); this._applyRotation(n); } break;
case 'rotate-reset':
    if (n !== null) { AppState.pageRotations.delete(n); this._applyRotation(n); } break;
```

Add `_applyRotation(pageNum)` method to `ContextMenu`:

```js
_applyRotation(pageNum) {
    const thumb = document.querySelector(`.page-thumbnail[data-page-number="${pageNum}"]`);
    if (!thumb) return;
    const rotation = AppState.pageRotations.get(pageNum);
    if (rotation) {
        thumb.dataset.rotation = rotation;
        showToast(`Trang ${pageNum}: đã xoay ${rotation}`, 'info');
    } else {
        delete thumb.dataset.rotation;
        showToast(`Trang ${pageNum}: đã reset về gốc`, 'info');
    }
},
```

- [ ] **Step 5: Include pageRotations in print payload**

In `frontend/app.js`, in `PrintModule._startPrint()`, extend the `body` object:

```js
// Per-page rotations (U)
pageRotations: AppState.pageRotations.size > 0
    ? Array.from(AppState.pageRotations.entries()).map(([pageNumber, rotation]) => ({ pageNumber, rotation }))
    : null,
```

- [ ] **Step 6: Verify rotation**

Upload a PDF. Right-click a thumbnail → "Xoay phải 90°" — the canvas in that thumbnail rotates 90° clockwise immediately. Right-click again → "Reset về gốc" — returns to normal. The rotation is included in the print payload when printing.

- [ ] **Step 7: Commit**

```bash
git add frontend/index.html frontend/styles.css frontend/app.js
git commit -m "feat(ui): per-page rotation via context menu (U)"
```

---

## Task 7: Final Verification

- [ ] **Step 1: Run full backend tests**

```bash
dotnet test D:\Pro\myPrinter\backend.Tests
```

Expected: 82 tests pass (existing tests unaffected by PageOrder/PageRotations which default to null).

- [ ] **Step 2: Verify all Group 1 features**

- [ ] Orientation badge (▯ Dọc / ▭ Ngang) appears on every thumbnail after render
- [ ] Hover over thumbnail → popup preview appears after 150ms; disappears on mouse out; fast on second hover (cached)
- [ ] Drag thumbnail up/down → ghost follows cursor, placeholder shows drop target, releasing reorders thumbnails
- [ ] Right-click → rotation submenu → thumbnail canvas rotates visually immediately
- [ ] Print payload includes `pageOrder` and `pageRotations` fields when applicable

- [ ] **Step 3: Confirm no regressions**

- Upload, preview, select pages, choose mode, print — all work end-to-end
- ZoomModal, ContextMenu existing actions (1 mặt / 2 mặt) still work
- Keyboard shortcuts (Ctrl+P, Ctrl+O, A, Esc) still work

- [ ] **Step 4: Final commit log**

```bash
git log --oneline -10
```

Should show 6 commits from this plan (2 backend + 4 frontend features).
