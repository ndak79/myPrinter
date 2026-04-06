# Landscape Mode Toggle + Blank/Image Delete Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a landscape-mode toggle bar to sheet view (so portrait+landscape pages can print together or separately) and add an X delete button + colored border to user-inserted blank/image cards.

**Architecture:** All changes are in `frontend/app.js` (JS logic) and `frontend/styles.css` (CSS). No new files. `AppState` gets a new `landscapeMode` property. `buildSheetLayout()` gets a new `'together'` branch. `_renderSheetView()` injects the floating toggle bar. `makeFace()` gets the X button + border for `pageNum === 0` cards.

**Tech Stack:** Vanilla JS, CSS, PDF.js (already loaded)

---

## File Map

| File | Change |
|---|---|
| `frontend/app.js` line 32–33 | Add `landscapeMode: 'together'` to AppState |
| `frontend/app.js` line 152–221 | Add `'together'` branch inside duplex else-block of `buildSheetLayout()` |
| `frontend/app.js` line 3044 | Pass `AppState.landscapeMode` to `buildSheetLayout()` (already passes orientationMap) |
| `frontend/app.js` line 3020 | After `_container.innerHTML = ''`, inject floating mode bar |
| `frontend/app.js` line 3067–3089 | In `makeFace()` blank branch: add border style + X button for `pageNum === 0` |
| `frontend/styles.css` | Add `.sheet-view-modebar`, `.sheet-modebar-btn`, `.sheet-modebar-btn.active`, `.blank-delete-btn` |

---

### Task 1: Add `landscapeMode` to AppState

**Files:**
- Modify: `frontend/app.js` lines 32–33

- [ ] **Step 1: Edit AppState to add landscapeMode**

Find this block (lines 32–33):
```js
    printMode:             'duplex',   // 'duplex' | 'booklet'
    viewMode:              'page',     // 'page' | 'sheet'
```

Replace with:
```js
    printMode:             'duplex',   // 'duplex' | 'booklet'
    viewMode:              'page',     // 'page' | 'sheet'
    landscapeMode:         'together', // 'together' | 'separate'
```

- [ ] **Step 2: Verify with node --check**

Run: `node --check frontend/app.js`
Expected: no output (no errors)

---

### Task 2: Add `'together'` branch in `buildSheetLayout()`

**Files:**
- Modify: `frontend/app.js` lines 152–221 (inside duplex else-block)

**Current code at line 152:**
```js
    } else {
        // duplex: group consecutive same-orientation pages, pair within group
        // Rule: landscape pages never share a sheet with portrait pages
        ...
        if (!orientationMap) {
            // Fallback: no orientation data...
        } else {
            // Full orientation-aware grouping...
        }
    }
```

**Change:** The function signature needs a 4th param `landscapeMode`. Add `'together'` as an early branch inside the `else` (duplex) block, BEFORE the `if (!orientationMap)` check.

- [ ] **Step 1: Change function signature to accept landscapeMode**

Find:
```js
function buildSheetLayout(fileEntry, printMode, orientationMap = null) {
```
Replace with:
```js
function buildSheetLayout(fileEntry, printMode, orientationMap = null, landscapeMode = 'separate') {
```

- [ ] **Step 2: Add 'together' branch at line 152 inside duplex else-block**

Find the start of the duplex else block:
```js
    } else {
        // duplex: group consecutive same-orientation pages, pair within group
        // Rule: landscape pages never share a sheet with portrait pages
        // Rule: singleSided page → blank immediately after (same orientation group)
        // Rule: each orientation group must end on even count → pad blank if odd

        if (!orientationMap) {
```

Replace with:
```js
    } else {
        // duplex: group consecutive same-orientation pages, pair within group
        // Rule: landscape pages never share a sheet with portrait pages
        // Rule: singleSided page → blank immediately after (same orientation group)
        // Rule: each orientation group must end on even count → pad blank if odd

        if (orientationMap && landscapeMode === 'together') {
            // 'together' mode: ignore orientation grouping — pair pages freely in order
            // Landscape and portrait pages may share a sheet
            const logicalPages = [];
            for (const p of pages) {
                const isLS = p === 0 ? false : (orientationMap.get(p) ?? false);
                logicalPages.push({ pageNum: p, isLandscape: isLS });
                if (p !== 0 && p !== null && fileEntry.singleSidedPages.has(p)) {
                    logicalPages.push({ pageNum: null, isLandscape: isLS }); // blank after singleSided
                }
            }
            // Pad to even count
            if (logicalPages.length % 2 !== 0) {
                logicalPages.push({ pageNum: null, isLandscape: false });
            }
            let sheetIdx = 1;
            for (let j = 0; j < logicalPages.length; j += 2) {
                const f = logicalPages[j];
                const b = logicalPages[j + 1] ?? { pageNum: null, isLandscape: f.isLandscape };
                sheets.push({
                    sheetIndex: sheetIdx++,
                    front: f.pageNum,
                    back: b.pageNum,
                    isLandscape: f.isLandscape && b.isLandscape,
                    isSingleForced: f.pageNum !== null && b.pageNum === null && fileEntry.singleSidedPages.has(f.pageNum),
                });
            }
        } else if (!orientationMap) {
```

And find the existing `} else {` that starts the orientation-aware grouping:
```js
        } else {
            // Full orientation-aware grouping (matches backend ProcessMixedOrientation)
```
Leave it unchanged — only the `if (!orientationMap)` → `} else if (!orientationMap)` change needed above.

- [ ] **Step 3: Verify with node --check**

Run: `node --check frontend/app.js`
Expected: no output

---

### Task 3: Pass landscapeMode to buildSheetLayout in `_renderSheetView`

**Files:**
- Modify: `frontend/app.js` around line 3044–3045

- [ ] **Step 1: Find the buildSheetLayout call in _renderSheetView**

Find:
```js
        const printMode = AppState.printMode;
        const sheets = buildSheetLayout(fileEntry, printMode, orientationMap);
```

Replace with:
```js
        const printMode = AppState.printMode;
        const sheets = buildSheetLayout(fileEntry, printMode, orientationMap, AppState.landscapeMode);
```

- [ ] **Step 2: Verify with node --check**

Run: `node --check frontend/app.js`
Expected: no output

---

### Task 4: Inject floating mode bar in `_renderSheetView`

**Files:**
- Modify: `frontend/app.js` around line 3020 (after `this._container.innerHTML = ''`)

The bar must be injected BEFORE the `sheets.forEach(...)` loop, so it appears at the top of the container.

- [ ] **Step 1: Inject the mode bar after clearing the container**

Find:
```js
        this._container.innerHTML = '';

        // Detect page orientations for all pages in parallel (was sequential await)
```

Replace with:
```js
        this._container.innerHTML = '';

        // Landscape mode toggle bar (only visible in sheet view)
        const modeBar = document.createElement('div');
        modeBar.className = 'sheet-view-modebar';
        modeBar.innerHTML = `
            <span class="sheet-modebar-label">Trang ngang:</span>
            <button class="sheet-modebar-btn${AppState.landscapeMode === 'together' ? ' active' : ''}" data-lsmode="together">🔀 In cùng trang dọc</button>
            <button class="sheet-modebar-btn${AppState.landscapeMode === 'separate' ? ' active' : ''}" data-lsmode="separate">⬜ In tờ riêng</button>
        `;
        modeBar.addEventListener('click', e => {
            const btn = e.target.closest('[data-lsmode]');
            if (!btn) return;
            AppState.landscapeMode = btn.dataset.lsmode;
            PreviewPanelModule.render(AppState.activeFile);
        });
        this._container.appendChild(modeBar);

        // Detect page orientations for all pages in parallel (was sequential await)
```

- [ ] **Step 2: Verify with node --check**

Run: `node --check frontend/app.js`
Expected: no output

---

### Task 5: Add X button + border to blank-page-card for user blanks

**Files:**
- Modify: `frontend/app.js` lines 3076–3089 (inside `makeFace()`)

**Current code:**
```js
                if (pageNum === null || pageNum === undefined || pageNum === 0) {
                    // Blank page — render as a plain white sheet (no content)
                    // pageNum === 0 means user-inserted blank
                    const blank = document.createElement('div');
                    blank.className = 'blank-page-card';
                    blank.classList.toggle('landscape', isLandscapeHint);
                    // Right-click on user blank → context menu with page=0 for remove action
                    if (pageNum === 0) {
                        blank.addEventListener('contextmenu', (e) => {
                            e.preventDefault();
                            ContextMenu.show(e, 0, { isUserBlank: true });
                        });
                    }
                    face.appendChild(blank);
```

**Change:** Inside the `if (pageNum === 0)` block, add border style and the X button. The X handler needs to find the *correct* occurrence of `0` in `pageOrder` — we pass the `pageOrderIndex` via a closure variable captured in `makeFace`. We need to track which index into `fileEntry.pageOrder` we're at.

**Problem:** `makeFace` doesn't know the index. Solution: add an optional `pageOrderIndex` parameter to `makeFace`.

- [ ] **Step 1: Add pageOrderIndex param to makeFace**

Find:
```js
            const makeFace = (pageNum, faceLabel, isLandscapeHint = false) => {
```
Replace with:
```js
            const makeFace = (pageNum, faceLabel, isLandscapeHint = false, pageOrderIndex = -1) => {
```

- [ ] **Step 2: Add border + X button inside blank `if (pageNum === 0)` block**

Find:
```js
                    // Right-click on user blank → context menu with page=0 for remove action
                    if (pageNum === 0) {
                        blank.addEventListener('contextmenu', (e) => {
                            e.preventDefault();
                            ContextMenu.show(e, 0, { isUserBlank: true });
                        });
                    }
                    face.appendChild(blank);
```

Replace with:
```js
                    // Right-click on user blank → context menu with page=0 for remove action
                    if (pageNum === 0) {
                        blank.style.border = '2px solid #667eea';
                        blank.addEventListener('contextmenu', (e) => {
                            e.preventDefault();
                            ContextMenu.show(e, 0, { isUserBlank: true });
                        });
                        const xBtn = document.createElement('button');
                        xBtn.className = 'blank-delete-btn';
                        xBtn.textContent = '×';
                        xBtn.title = 'Xóa trang trắng';
                        xBtn.addEventListener('click', (e) => {
                            e.stopPropagation();
                            e.preventDefault();
                            const entry = AppState.files.find(f => f.id === fileEntry.id);
                            if (!entry) return;
                            // Remove the specific blank at pageOrderIndex, or first found
                            const idx = pageOrderIndex >= 0 ? pageOrderIndex : entry.pageOrder.indexOf(0);
                            if (idx >= 0 && entry.pageOrder[idx] === 0) {
                                entry.pageOrder.splice(idx, 1);
                                PreviewPanelModule.render(entry);
                            }
                        });
                        blank.appendChild(xBtn);
                    }
                    face.appendChild(blank);
```

- [ ] **Step 3: Pass pageOrderIndex when calling makeFace for front/back in duplex**

We need to know the `pageOrderIndex` of each page when building sheets. The `sheet.front` and `sheet.back` are page numbers. To find their index in `pageOrder`, we search `fileEntry.pageOrder`.

**However**, for blank pages (`pageNum === 0`) multiple blanks exist. We need to pass the index from the `logicalPages` that was built during layout. Since `buildSheetLayout` already computed the pairing, we can store `pageOrderIndex` in the sheet objects OR compute it at render time.

**Simplest approach at render time:** track a running index over `fileEntry.pageOrder` to match blanks in encounter order. But this is complex.

**Better approach:** Add `frontOrderIdx` and `backOrderIdx` to each sheet object from `buildSheetLayout`. This requires modifying `buildSheetLayout` to track indices.

**Even simpler (good enough for now):** When rendering, we scan `fileEntry.pageOrder` in order and assign indices. Since `pageOrder` drives sheet layout, each face corresponds to a position. We can compute it by building an index map before rendering.

**Implementation:** Before the `sheets.forEach`, build a helper that gives us the next occurrence of `0` in `pageOrder`:

```js
        // Build a queue of blank-page indices so each X button removes the right one
        const blankIndexQueue = [];
        fileEntry.pageOrder.forEach((p, idx) => { if (p === 0) blankIndexQueue.push(idx); });
        let blankQueuePos = 0;
```

Then in `makeFace`, when `pageNum === 0`, pop from this queue:
```js
        const makeFace = (pageNum, faceLabel, isLandscapeHint = false) => {
            // ...
            if (pageNum === 0) {
                const blankIdx = blankIndexQueue[blankQueuePos++] ?? -1;
                // use blankIdx in X handler
```

This approach keeps `makeFace` signature unchanged and uses closure correctly.

**Revert the pageOrderIndex param change from Step 1** — use the queue approach instead.

Revert makeFace signature back to:
```js
            const makeFace = (pageNum, faceLabel, isLandscapeHint = false) => {
```

- [ ] **Step 4: Add blankIndexQueue before sheets.forEach**

Find:
```js
        sheets.forEach(sheet => {
            const sheetCard = document.createElement('div');
```

Replace with:
```js
        // Build ordered queue of blank-page indices in pageOrder for X-button delete
        const blankIndexQueue = [];
        fileEntry.pageOrder.forEach((p, idx) => { if (p === 0) blankIndexQueue.push(idx); });
        let blankQueuePos = 0;

        sheets.forEach(sheet => {
            const sheetCard = document.createElement('div');
```

- [ ] **Step 5: Use blankQueuePos in the blank branch of makeFace**

In the `if (pageNum === 0)` block (just updated in Step 2), find:
```js
                        xBtn.addEventListener('click', (e) => {
                            e.stopPropagation();
                            e.preventDefault();
                            const entry = AppState.files.find(f => f.id === fileEntry.id);
                            if (!entry) return;
                            // Remove the specific blank at pageOrderIndex, or first found
                            const idx = pageOrderIndex >= 0 ? pageOrderIndex : entry.pageOrder.indexOf(0);
                            if (idx >= 0 && entry.pageOrder[idx] === 0) {
                                entry.pageOrder.splice(idx, 1);
                                PreviewPanelModule.render(entry);
                            }
                        });
```

And update the entire blank `if (pageNum === 0)` block to use the queue:

```js
                    if (pageNum === 0) {
                        blank.style.border = '2px solid #667eea';
                        blank.addEventListener('contextmenu', (e) => {
                            e.preventDefault();
                            ContextMenu.show(e, 0, { isUserBlank: true });
                        });
                        const blankIdx = blankIndexQueue[blankQueuePos++] ?? -1;
                        const xBtn = document.createElement('button');
                        xBtn.className = 'blank-delete-btn';
                        xBtn.textContent = '×';
                        xBtn.title = 'Xóa trang trắng';
                        xBtn.addEventListener('click', (e) => {
                            e.stopPropagation();
                            e.preventDefault();
                            const entry = AppState.files.find(f => f.id === fileEntry.id);
                            if (!entry) return;
                            const idx = blankIdx >= 0 ? blankIdx : entry.pageOrder.indexOf(0);
                            if (idx >= 0 && entry.pageOrder[idx] === 0) {
                                entry.pageOrder.splice(idx, 1);
                                PreviewPanelModule.render(entry);
                            }
                        });
                        blank.appendChild(xBtn);
                    }
```

- [ ] **Step 6: Verify with node --check**

Run: `node --check frontend/app.js`
Expected: no output

---

### Task 6: Add CSS for mode bar and delete button

**Files:**
- Modify: `frontend/styles.css` (append to end, or find appropriate section)

- [ ] **Step 1: Add CSS rules**

Append to `frontend/styles.css`:
```css
/* ── Sheet view: landscape mode toggle bar ──────────────────────── */
.sheet-view-modebar {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 8px 16px;
    background: var(--bg-secondary, #1e293b);
    border-bottom: 1px solid var(--border-color, #334155);
    position: sticky;
    top: 0;
    z-index: 10;
    flex-shrink: 0;
}

.sheet-modebar-label {
    font-size: 12px;
    color: var(--text-muted, #94a3b8);
    margin-right: 4px;
}

.sheet-modebar-btn {
    padding: 4px 12px;
    border-radius: 6px;
    border: 1px solid var(--border-color, #334155);
    background: transparent;
    color: var(--text-secondary, #cbd5e1);
    font-size: 12px;
    cursor: pointer;
    transition: background 0.15s, border-color 0.15s, color 0.15s;
}

.sheet-modebar-btn:hover {
    background: var(--bg-hover, #334155);
    color: var(--text-primary, #f1f5f9);
}

.sheet-modebar-btn.active {
    background: #667eea;
    border-color: #667eea;
    color: #fff;
}

/* ── Blank page card: X delete button ───────────────────────────── */
.blank-page-card {
    position: relative;
}

.blank-delete-btn {
    position: absolute;
    top: 4px;
    right: 4px;
    width: 18px;
    height: 18px;
    border-radius: 50%;
    border: none;
    background: rgba(239, 68, 68, 0.85);
    color: #fff;
    font-size: 14px;
    line-height: 1;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0;
    z-index: 5;
    opacity: 0;
    transition: opacity 0.15s;
}

.blank-page-card:hover .blank-delete-btn {
    opacity: 1;
}
```

- [ ] **Step 2: Verify CSS is valid** (visual check — no automated tool available)

---

### Task 7: Final verification

- [ ] **Step 1: Run node --check**

Run: `node --check frontend/app.js`
Expected: no output (no errors)

- [ ] **Step 2: Manual smoke test checklist**

1. Open app in browser
2. Load a PDF with landscape pages
3. Switch to Sheet view
4. Verify mode bar appears at top: "🔀 In cùng trang dọc" | "⬜ In tờ riêng"
5. Click "🔀 In cùng trang dọc" → portrait and landscape pages pair freely → fewer sheets
6. Click "⬜ In tờ riêng" → landscape pages separate into own sheets → more sheets
7. Right-click on a sheet view page → insert blank page
8. Blank page card should have blue (#667eea) border
9. Hover blank page card → X button appears top-right
10. Click X → blank removed, sheet view re-renders without it
