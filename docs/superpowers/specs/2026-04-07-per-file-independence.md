# Spec: Per-File Independence — Independent Tabs, Thumbstrip, landscapeMode, Copies + Landscape Badge

**Date:** 2026-04-07
**Status:** Draft (Round 1 review pending)
**Scope:** `frontend/app.js`, `frontend/index.html`, `frontend/styles.css`

---

## 1. Problem Statement

Currently the application has partial multi-file support: files share a single thumbstrip (all files rendered in one scrollable list with name dividers), `landscapeMode` is a single global property on `AppState`, and `copies`/`collate` are global state in `CopiesModule` (and their HTML widget is absent from `index.html`).

This spec makes each file tab fully independent:
- **Thumbstrip** shows only the active file's pages (not all files concatenated).
- **`landscapeMode`** becomes a per-file field, proxied from `AppState.landscapeMode` so existing code requires no change.
- **`copies`** and **`collate`** become per-file fields, with `CopiesModule` re-wired to proxy through the active file; the copies widget HTML is added to `index.html`.
- **Drag-to-reorder tabs** lets the user change the print order of files.
- **All-landscape badge** on the file tab shows a visual icon when `_originalOrientationMap` indicates the file is entirely landscape, with a tooltip.

---

## 2. Scope

### In scope
- §3 — `createFileEntry`: add `landscapeMode`, `copies`, `collate` fields
- §4 — `AppState.landscapeMode` proxy getter/setter
- §5 — `CopiesModule` proxy re-wire + copies HTML widget in `index.html`
- §6 — `ThumbStripModule.render()`: render only active file
- §7 — `TabsModule`: drag-to-reorder + landscape badge
- §8 — `_startPrint`: use per-file `copies`/`collate` + per-file `landscapeMode`
- §9 — Modebar sync on tab switch
- §10 — Copies UI sync on tab switch
- §11 — CSS for landscape badge

### Out of scope
- Per-printer session (file list isolated per printer)
- Per-file print mode (duplex / booklet stays global)
- Per-file page range (global)

---

## 3. Data Model — `createFileEntry` New Fields

Add three new fields to the return object of `createFileEntry` (currently lines 59–72 of `app.js`), appended after `_originalOrientationMap`:

```javascript
landscapeMode: 'together',   // 'separate' | 'together' — per-file, default matches current global default
copies:        1,            // int 1–99 — per-file copy count
collate:       true,         // bool — per-file collate setting
```

**Invariant:** `fileEntry.landscapeMode` is always `'separate'` or `'together'`. Never `null` or `undefined`.

---

## 4. `AppState.landscapeMode` — Proxy Getter/Setter

Convert the plain property `landscapeMode: 'together'` (line 34) into a getter/setter pair that proxies through `activeFile`:

```javascript
get landscapeMode() {
    return this.activeFile?.landscapeMode ?? 'together';
},
set landscapeMode(value) {
    if (this.activeFile) this.activeFile.landscapeMode = value;
},
```

**Why this works without touching downstream code:**
All 9 existing read sites (`_renderSheetView` lines 3313, 3363, 3392, 3415; `_startPrint` line 2260; modebar handler lines 4269, 4277, 4296; `onPrintModeChange` line 4296) read `AppState.landscapeMode` — they will transparently get the active file's value. The single write site (modebar handler line 4275 `AppState.landscapeMode = newMode`) will transparently write to the active file.

**Edge case — no active file:** The getter returns `'together'` as fallback (matching the original default). The setter silently no-ops when `activeFile` is null. This is safe because `landscapeMode` is only meaningful when a file is loaded.

**`_teardownTogether` is unaffected:** It does not read `AppState.landscapeMode`; it only touches `fileEntry` fields.

**Tab switch:** When `TabsModule.setActive(idx)` sets `AppState.activeFileIndex = idx`, the next read of `AppState.landscapeMode` automatically returns the new file's `landscapeMode`. The modebar button highlight is synced separately in §9.

---

## 5. `CopiesModule` — Per-File Proxy Re-wire

### 5.1 Remove backing fields from `CopiesModule`

Remove `_copies: 1` and `_collate: true` from the `CopiesModule` object literal. Replace the getters `get copies()` and `get collate()` with proxies:

```javascript
get copies()  { return AppState.activeFile?.copies  ?? 1;    },
get collate() { return AppState.activeFile?.collate ?? true;  },
```

### 5.2 Update mutators

The `click` handlers on `copies-dec` / `copies-inc` and the `change` handler on `collate-check` currently write to `this._copies` / `this._collate`. Replace with writes to the active file:

```javascript
// dec handler:
const f = AppState.activeFile;
if (f && f.copies > 1) { f.copies--; this._update(); }

// inc handler:
const f = AppState.activeFile;
if (f && f.copies < 99) { f.copies++; this._update(); }

// collate change handler:
const f = AppState.activeFile;
if (f) f.collate = e.target.checked;
```

**Guard:** all three handlers check `AppState.activeFile` is non-null before writing. If no file is loaded, button clicks are silently ignored.

### 5.3 `reset()` method

`CopiesModule.reset()` currently sets `this._copies = 1; this._collate = true`. Replace with:

```javascript
reset() {
    const f = AppState.activeFile;
    if (f) { f.copies = 1; f.collate = true; }
    this._update();
},
```

### 5.4 History restore (line 2112)

`HistoryModule` restore currently writes `CopiesModule._copies = item.copies`. Since `_copies` no longer exists, replace with a new `CopiesModule.setCopies(n)` helper:

```javascript
// Add to CopiesModule:
setCopies(n) {
    const f = AppState.activeFile;
    if (f) { f.copies = Math.min(99, Math.max(1, n)); this._update(); }
},
```

History restore becomes: `CopiesModule.setCopies(item.copies);` (and remove the `CopiesModule._update()` call on the next line — it is now inside `setCopies`).

### 5.5 `_update()` — unchanged logic, new sync method

`_update()` reads `this.copies` (via getter → `activeFile.copies`) and `#copies-display`, `#collate-label`. No change to the method body needed — the getter already proxies correctly.

Add a public `sync()` method that other code can call when the active file changes:

```javascript
sync() { this._update(); },
```

### 5.6 Copies widget HTML — add to `index.html`

The copies widget DOM elements (`#copies-dec`, `#copies-inc`, `#copies-display`, `#collate-check`, `#collate-label`) are referenced in JS but **absent from `index.html`**. Add the widget inside the `#file-tabs` bar, to the right of `#file-tab-add-btn`:

```html
<!-- inside #file-tabs, after #file-tab-add-btn -->
<div class="copies-widget" id="copies-widget">
  <span class="copies-label">Số bản:</span>
  <div class="copies-control">
    <button class="copies-btn" id="copies-dec" title="Giảm số bản">−</button>
    <span class="copies-val" id="copies-display">1</span>
    <button class="copies-btn" id="copies-inc" title="Tăng số bản">+</button>
  </div>
  <label class="collate-label" id="collate-label" style="display:none">
    <input type="checkbox" id="collate-check" checked> Ghép bộ
  </label>
</div>
```

The existing CSS classes (`.copies-row`, `.copies-btn`, `.copies-val`, `.copies-label`, `.copies-control`, `.collate-label`) already exist in `styles.css`. Add a new `.copies-widget` rule to `styles.css` that positions it correctly in the tab bar context:

```css
.copies-widget {
  display:     flex;
  align-items: center;
  gap:         6px;
  margin-left: auto;   /* push to right side of tab bar */
  padding:     0 8px;
  flex-shrink: 0;
}
```

---

## 6. `ThumbStripModule.render()` — Active File Only

### 6.1 Change the render loop

Currently `ThumbStripModule.render()` iterates `AppState.files.forEach(...)` and creates a `div.thumb-file-divider` + page thumbs for every file.

Replace the entire loop body with a single-file render:

```javascript
render() {
    // ... existing cancel-tasks + clear-container block unchanged ...

    if (AppState.files.length === 0) {
        // ... existing empty-state HTML unchanged ...
        return;
    }

    const f = AppState.activeFile;   // NEW: only the active file
    if (!f) return;

    // No file-name divider — single file only
    for (let p = 1; p <= f.totalPageCount; p++) {
        const item = document.createElement('div');
        item.className         = 'thumb-item';
        item.dataset.fileId    = f.id;
        item.dataset.page      = p;
        item.dataset.fileIndex = AppState.activeFileIndex;
        item.setAttribute('tabindex', '0');
        item.setAttribute('role', 'button');
        item.setAttribute('aria-label', `Trang ${p}`);  // no file name — unambiguous

        const img   = document.createElement('img');
        img.className = 'thumb-img';
        img.alt       = '';
        img.draggable = false;

        const label = document.createElement('div');
        label.className   = 'thumb-item-label';
        label.textContent = p;

        item.appendChild(img);
        item.appendChild(label);

        if (p === 1) item.classList.add('active');   // first page of active file

        item.addEventListener('click', () => this._onThumbClick(AppState.activeFileIndex, p));
        item.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            ContextMenu.show(e, p);
        });

        this._container.appendChild(item);
    }

    requestAnimationFrame(() => this._renderVisible());
},
```

### 6.2 Remove `thumb-file-divider` from `_syncSelectionHighlights`

`_syncSelectionHighlights` currently queries ALL `.thumb-item` elements and looks up `fileId` from `data-file-id`. Since the thumbstrip now only shows one file, all visible thumbs belong to `AppState.activeFile`. The method continues to work correctly because it still resolves `fileId → entry` via `AppState.files.find(...)`. **No change required to `_syncSelectionHighlights`.**

### 6.3 Keyboard navigation `tabindex` scroll target

`TabsModule.setActive()` currently scrolls to:
```javascript
ThumbStripModule._container?.querySelector(
    `.thumb-item[data-file-index="${idx}"][data-page="1"]`
)
```
After the change, only the active file's thumbs exist in the DOM, so the selector `.thumb-item[data-page="1"]` (without `data-file-index`) would also work. Keep the existing selector — it still matches because `data-file-index` is set to `AppState.activeFileIndex` for all rendered thumbs.

---

## 7. `TabsModule` — Drag-to-Reorder + Landscape Badge

### 7.1 Drag-to-reorder tabs

Use HTML5 drag-and-drop on the tab `div` elements.

**State:** A module-level variable `_dragSourceIdx = null` tracks the index being dragged.

**On the tab div, add:**
- `draggable="true"` attribute
- `dragstart` → `_dragSourceIdx = idx; e.dataTransfer.effectAllowed = 'move';`
- `dragover` → `e.preventDefault(); e.dataTransfer.dropEffect = 'move'; tab.classList.add('drag-over');`
- `dragleave` → `tab.classList.remove('drag-over');`
- `drop` → if `_dragSourceIdx !== null && _dragSourceIdx !== idx`, call `_reorderFiles(_dragSourceIdx, idx)`; `tab.classList.remove('drag-over'); _dragSourceIdx = null;`
- `dragend` → `_dragSourceIdx = null; document.querySelectorAll('.file-tab').forEach(t => t.classList.remove('drag-over'));`

**`_reorderFiles(fromIdx, toIdx)` method:**
```javascript
_reorderFiles(fromIdx, toIdx) {
    const files = AppState.files;
    const [moved] = files.splice(fromIdx, 1);
    files.splice(toIdx, 0, moved);

    // Keep active file pointing to the same file object
    const activeFile = AppState.activeFile;  // read before index changes
    AppState.activeFileIndex = files.indexOf(activeFile);

    this.render();
    ThumbStripModule.render();
    PreviewPanelModule.render(AppState.activeFile);
},
```

**Why `files.indexOf(activeFile)`:** After splicing, the object reference is the same; `indexOf` finds it in O(n). This correctly updates `activeFileIndex` even when the active tab itself is dragged.

### 7.2 All-landscape badge on tab

When a file's `_originalOrientationMap` is non-null and every page maps to `true` (all landscape), show a landscape badge icon on the tab.

**Badge element added inside `TabsModule.render()` tab construction:**
```javascript
// Compute isAllLandscape
const isAllLandscape = (
    file._originalOrientationMap != null &&
    file.totalPageCount > 0 &&
    (() => {
        for (let p = 1; p <= file.totalPageCount; p++) {
            if (file._originalOrientationMap.get(p) !== true) return false;
        }
        return true;
    })()
);

if (isAllLandscape) {
    const badge = document.createElement('span');
    badge.className = 'file-tab-landscape-badge';
    badge.textContent = '🌄';   // landscape icon — see §11 for alternatives
    badge.title = 'File này toàn trang ngang — tự động lật theo cạnh ngắn khi in 2 mặt';
    tab.insertBefore(badge, name);   // badge appears before the file name
}
```

**Badge visibility rule:**
- `_originalOrientationMap` is `null` until the file is first rendered in together mode. Before that, no badge is shown.
- The badge appears automatically after the first together-mode sheet render of the file.
- `TabsModule.render()` is called from `TabsModule.setActive()` and after print/upload events, so the badge updates naturally.

**Re-render trigger after snapshot:** When `_renderSheetView` finishes the snapshot step [3] and sets `fileEntry._originalOrientationMap`, it does NOT explicitly call `TabsModule.render()`. The badge will appear the next time `TabsModule.render()` is called (e.g., user switches tab or uploads another file). To show the badge immediately after detection, add one call at the end of the together-mode block in `_renderSheetView`:

```javascript
// At end of together-mode block (after step [5] cache invalidation):
TabsModule.render();   // update landscape badge if just detected
```

This call is idempotent and cheap (only rebuilds the tab DOM, not PDF rendering).

---

## 8. `_startPrint` — Use Per-File copies/collate and landscapeMode

### 8.1 copies and collate
Currently reads `CopiesModule.copies` and `CopiesModule.collate` (global). Replace with per-file reads inside the loop:

```javascript
// Before:
copies:  CopiesModule.copies,
collate: CopiesModule.collate,

// After:
copies:  file.copies,
collate: file.collate,
```

Apply at both occurrences in the loop body: the POST body (line 2277–2278) and the `HistoryModule.add()` call (lines 2327–2328).

### 8.2 landscapeMode
`_startPrint` reads `AppState.landscapeMode` at line 2260 to decide `duplexSide`. Via the §4 proxy, this now returns `AppState.activeFile.landscapeMode` — but inside the print loop, the file being printed (`file`) may differ from `AppState.activeFile` (user might have switched tabs mid-loop, or files are printed in sequence).

Replace the `AppState.landscapeMode` read in the `duplexSide` computation with a direct read from the loop variable:

```javascript
// Before:
if (AppState.landscapeMode === 'together' && file._originalOrientationMap != null) {

// After:
if (file.landscapeMode === 'together' && file._originalOrientationMap != null) {
```

This ensures the correct landscape mode is used for each file being printed, regardless of which tab is active.

---

## 9. Modebar Sync on Tab Switch

When `TabsModule.setActive(idx)` switches tabs, the modebar buttons must reflect the new file's `landscapeMode`.

Add modebar sync in `TabsModule.setActive()`, after `AppState.activeFileIndex = idx`:

```javascript
// Sync modebar to new active file's landscapeMode
const modeBar = document.getElementById('sheet-view-modebar');
if (modeBar) {
    const lsMode = AppState.activeFile?.landscapeMode ?? 'together';
    modeBar.querySelectorAll('.sheet-modebar-btn').forEach(b => {
        b.classList.toggle('active', b.dataset.lsmode === lsMode);
    });
}
```

This must run BEFORE `PreviewPanelModule.render(AppState.activeFile)` so that the button highlight is correct when the preview renders.

**Modebar teardown on tab switch:** The existing modebar click handler calls `_teardownTogether(f)` for all files when switching away from `'together'`. This logic is unaffected — it reads `AppState.landscapeMode` (now proxy → active file's mode) before setting the new mode.

---

## 10. Copies UI Sync on Tab Switch

When `TabsModule.setActive(idx)` switches tabs, the copies widget must reflect the new file's `copies`/`collate`.

Add `CopiesModule.sync()` call in `TabsModule.setActive()`, after modebar sync:

```javascript
CopiesModule.sync();
```

`CopiesModule.sync()` calls `this._update()`, which reads `this.copies` (→ proxy → `activeFile.copies`) and updates `#copies-display` and `#collate-label` visibility.

---

## 11. CSS — Landscape Badge Styling

Add to `styles.css`:

```css
/* Landscape tab badge */
.file-tab-landscape-badge {
    font-size:      14px;
    line-height:    1;
    flex-shrink:    0;
    cursor:         default;
    filter:         saturate(1.2);
    transition:     transform 0.15s ease;
}
.file-tab-landscape-badge:hover {
    transform: scale(1.25);
}
```

**Icon choice:** `🌄` (sunrise over mountains — landscape orientation metaphor). Alternative: `↔️` (left-right arrow, indicating wide/horizontal). The icon is a Unicode emoji, consistent with the project's icon system (§5 of the HTML exploration — no icon library used). Final icon is `🌄` unless overridden during review.

**Tooltip:** Set via `title` attribute on the `span` element. No custom tooltip CSS needed — browser native tooltip suffices.

---

## 12. Edge Cases

### 12.1 File with no together-mode render yet
`_originalOrientationMap` is `null` until the file is rendered in together mode at least once. Badge is not shown. This is correct — we cannot determine all-landscape status until orientation detection runs. If the user prints before ever viewing sheet+together, `file.landscapeMode === 'together'` but `_originalOrientationMap == null`, so `duplexSide` stays `null` (no override). Existing behavior.

### 12.2 Switching to separate mode clears snapshot
`_teardownTogether` sets `_originalOrientationMap = null`. After teardown, badge disappears on next `TabsModule.render()` call. If the user switches back to together mode and re-renders, the snapshot is rebuilt and badge reappears. **This is correct** — the snapshot must be retaken because switching to separate and back to together may involve user rotations in between.

### 12.3 Drag-reorder with activeFile
When the active tab is dragged, `_reorderFiles` uses `files.indexOf(activeFile)` to recompute `activeFileIndex`. This correctly handles both cases: dragging the active tab and dragging a non-active tab.

### 12.4 `collate` checkbox state on tab switch
`CopiesModule.sync()` calls `_update()` which updates `#copies-display` and `#collate-label` visibility. The `#collate-check` checkbox `checked` state is NOT updated by `_update()`. Add to `sync()`:

```javascript
sync() {
    const chk = document.getElementById('collate-check');
    if (chk) chk.checked = AppState.activeFile?.collate ?? true;
    this._update();
},
```

### 12.5 `_syncSelectionHighlights` after thumbstrip single-file change
`_syncSelectionHighlights` queries ALL `.thumb-item` elements in the container and resolves each by `data-file-id`. Since the container now only holds the active file's thumbs, this is effectively scoped to one file. No correctness issue.

### 12.6 `HistoryModule` restore
`HistoryModule.restore(item)` currently writes `CopiesModule._copies = item.copies`. After §5.4, replace with `CopiesModule.setCopies(item.copies)`. The `_update()` call on the next line is removed (now inside `setCopies`).

### 12.7 `SummaryModule` and `ConfirmDialog`
Both read `CopiesModule?.copies` (lines 2544, 2715). After the proxy re-wire, `CopiesModule.copies` returns `activeFile.copies`. These calls continue to work without change.

---

## 13. Implementation Tasks

| Task | Files | Description |
|------|-------|-------------|
| T1 | `app.js` | Add `landscapeMode`, `copies`, `collate` to `createFileEntry` |
| T2 | `app.js` | Convert `AppState.landscapeMode` to getter/setter proxy |
| T3 | `app.js` | Re-wire `CopiesModule` to use per-file fields; add `setCopies()` + `sync()` |
| T4 | `index.html` | Add copies widget HTML inside `#file-tabs` |
| T5 | `styles.css` | Add `.copies-widget` layout + `.file-tab-landscape-badge` CSS |
| T6 | `app.js` | `ThumbStripModule.render()` — single-file only, remove divider |
| T7 | `app.js` | `TabsModule`: drag-to-reorder + landscape badge + modebar/copies sync on setActive |
| T8 | `app.js` | `_startPrint`: use `file.copies`, `file.collate`, `file.landscapeMode` |
| T9 | `app.js` | `_renderSheetView`: call `TabsModule.render()` after snapshot to trigger badge |

---

## 14. Non-Goals (explicit)

- No per-file print mode (duplex / booklet). `AppState.printMode` stays global.
- No per-printer file list isolation.
- No persistence across browser reloads.
- No animated drag placeholder (drag-over highlight is sufficient).
- No custom tooltip component — browser `title` attribute used for landscape badge.
