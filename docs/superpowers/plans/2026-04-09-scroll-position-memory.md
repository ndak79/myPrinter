# Spec: Scroll Position Memory

**Date:** 2026-04-09  
**Feature:** Nhớ vị trí scroll khi chuyển giữa tab files, view modes, và quay lại tab cũ  
**Status:** Round 31 in progress — verifying Round 30 doc cleanup

---

## 1. Problem Statement

Hiện tại mỗi khi người dùng:
- Chuyển sang tab file khác rồi quay lại
- Toggle giữa page-view và sheet-view
- Thực hiện bất kỳ thao tác nào trigger `PreviewPanelModule.render()`

→ **Scroll position bị reset về 0** (đầu trang). Đối với PDF 151 trang, người dùng phải cuộn lại từ đầu mỗi lần chuyển tab — UX rất tệ.

---

## 2. Scope

### Containers cần nhớ scroll

| Container | Element ID | Module | Mô tả |
|---|---|---|---|
| **Preview Panel** | `#preview-panel` | `PreviewPanelModule` | Panel xem PDF chính — có 2 view modes |
| **Thumb Strip** | `#thumb-strip` | `ThumbStripModule` | Thumbnail panel bên trái |

> **Out of scope:** `#preview-modal` (zoom modal — stateless, không cần nhớ)

### Dimensions của state cần lưu

Preview Panel có **2 view modes độc lập** cho mỗi file:
- `page` mode — cuộn theo từng trang riêng lẻ  
- `sheet` mode — cuộn theo tờ giấy (front/back)

→ Cần lưu **`(fileId, viewMode)`** pair, không phải chỉ `fileId`.

Thumb Strip chỉ có 1 dimension: `fileId`.

---

## 3. Current Behavior — Scroll Reset Points

### 3.1 PreviewPanelModule

```
scrollTop = 0 tại:
  L3875 — page-view cache-hit path (tab switch back to cached file)
  L3927 — page-view full rebuild (first load or card mismatch)
  L3965 — sheet-view cache-hit path (tab switch)
  L4352 — sheet-view full rebuild (fingerprint mismatch)
```

### 3.2 ThumbStripModule

```
scrollIntoView(page 1) tại:
  L1569 — TabsModule.setActive() → rAF → firstThumb.scrollIntoView('start')
```

---

## 4. Desired Behavior

### Tab switch (file A → file B → file A)
- Khi rời file A: lưu `scrollTop` của preview panel (`page` và `sheet` mode riêng)
- Khi quay lại file A: restore đúng `scrollTop` cho view mode hiện tại
- Thumb strip: lưu/restore vị trí scroll của file A

### View mode toggle (page ↔ sheet, cùng file)
- Khi toggle sang mode khác: lưu `scrollTop` của mode cũ
- Khi toggle trở lại mode cũ: restore `scrollTop` đó
- Hai modes có scroll position **hoàn toàn độc lập**

### First load (file mới upload)
- Scroll về 0 như hiện tại — không thay đổi

### Same-file re-render (state change: select/deselect/rotate)
- Giữ nguyên scroll position — không reset, không restore từ saved value

### File bị xóa khỏi tab list
- Xóa scroll state tương ứng khỏi bộ nhớ (tự động via GC)

---

## 5. Design

### 5.1 Storage — per `fileEntry`

```js
// Trong AppState.createFileEntry():
_scrollPos: null,    // null = never rendered; { page, sheet, thumb } after first render
```

> `null` là sentinel để phân biệt "chưa từng render" vs "đã render, scroll = 0". Object được tạo lần đầu tại điểm full rebuild (Task 2d hoặc 3c), **không** phải tại `createFileEntry()`.

> **Lý do dùng `fileEntry` thay vì Map riêng:** State gắn liền với vòng đời file — khi file bị `removeFile()` thì tự động bị GC cùng fileEntry. Không cần cleanup riêng.

### 5.1b Module-level flags — `PreviewPanelModule._modeJustToggled`, `_sheetRenderGen` & `_nextSheetRenderGen`

```js
// Thêm vào PreviewPanelModule object (nơi các property khác như _viewMode, _container được khai báo):
_modeJustToggled: false,    // one-shot: set by ViewModeModule toggle, cleared by render cache-hit or full-rebuild
_sheetRenderGen: 0,         // generation counter: 0 = no pending async sheet full-rebuild
_nextSheetRenderGen: 0,     // monotonically increasing counter; each full-rebuild invocation gets a unique token
```

> `_modeJustToggled`: tín hiệu một lần — set trong Task 2f (toggle handler) và phải được clear ở TẤT CẢ các render paths — cả cache-hit (Task 2e, 3b) lẫn full-rebuild (Task 2d, 3c).

> `_sheetRenderGen` + `_nextSheetRenderGen`: thay thế plain boolean `_sheetRenderPending`. Lý do dùng token thay boolean:
> 1. **Overlapping async renders:** Nếu B bắt đầu async full-rebuild rồi user switch sang C (cũng async full-rebuild), B's abort guard sẽ clear boolean → C's rebuild chạy không được bảo vệ. Với token, mỗi invocation có ID riêng; abort guard chỉ clear khi ID còn khớp.
> 2. **Page-mode stale flag:** Boolean ở true trong khoảng thời gian sheet→page toggle xong nhưng old async abort chưa chạy. Trong window đó, page scroll save bị block sai. Với scoped guard (chỉ block khi `_viewMode === 'sheet'`), page-mode saves không bị ảnh hưởng.
>
> **Guard pattern cho save paths:** `this._viewMode === 'sheet' && this._sheetRenderGen !== 0` — "container hiện tại đang ở sheet mode VÀ có async rebuild đang pending" → không save.
> Khi `_viewMode === 'page'`: container reflects page content → save là hợp lệ bất kể `_sheetRenderGen`.

### 5.2 Save — khi nào lưu scroll

| Location | Trigger | Action |
|---|---|---|
| `render()` đầu hàm, trước L3827 (Task 2a) | Tab switch (cả page & sheet mode) | Save `_currentFileId`'s `_scrollPos[_viewMode]` |
| `render()` page-view mismatch branch trước `root.innerHTML=''` (Task 2c) | Rare: DOM card count mismatch | Save `_scrollPos.page` của incoming file |
| `_renderSheetView()` full rebuild trước `sheetRoot.innerHTML=''` (Task 4) | State-change rebuild của file đang xem | Save `_scrollPos.sheet` của file đó |
| ViewModeModule toggle handler, trong `if (file?.pdfDoc)` block (Task 2f) | Page↔Sheet toggle | Save `_scrollPos[_oldMode]` của active file, sau khi `_viewMode = newMode` đã sync |
| `setActive()` trước `activeFileIndex = idx` (Task 5a) | Tab switch | Save `_scrollPos.thumb` của outgoing file |

### 5.3 Restore — khi nào restore scroll

| Location | Trigger | Action |
|---|---|---|
| `render()` page cache-hit (L3875) | Tab switch về cached file | `scrollTop = fileEntry._scrollPos.page` |
| `render()` page full rebuild (L3927) | First load | `scrollTop = 0` (init) |
| `_renderSheetView()` cache-hit (L3965) | Tab switch về cached sheet file | `scrollTop = fileEntry._scrollPos.sheet` |
| `_renderSheetView()` full rebuild (L4352) | State-change rebuild | Restore `_scrollPos.sheet` |
| `setActive()` rAF block | Tab switch | Restore `_scrollPos.thumb` |

> **Same-file re-render:** cache-hit path không touch `scrollTop` nếu file đang được hiển thị — giữ nguyên vị trí user.

### 5.4 First Load vs Returning

```js
// Full rebuild (page hoặc sheet), lần đầu tiên:
if (fileEntry._scrollPos === null) {
    fileEntry._scrollPos = { page: 0, sheet: 0, thumb: 0 };
    this._container.scrollTop = 0;   // first time → top
} else {
    this._container.scrollTop = fileEntry._scrollPos[mode];  // returning → restore
}
```

### 5.5 _prevFileId pattern (key insight)

`_currentFileId` is updated at different lines depending on the path:

- **Page view:** L3842 — before the cache-hit check at L3875 (Task 2b captures `_prevFileId` before L3842).
- **Sheet view — cache-hit path:** L3961 — inside the cache-hit block (Task 3a captures `_prevFileId` before L3961); cache-hit block ends with `return` at L3967 so this capture is not available in the full-rebuild path.
- **Sheet view — full-rebuild path:** L3975 — Task 4a captures a separate `_prevFileId` before L3975, independent of Task 3a's capture.

To know "is this a tab-switch or a same-file re-render?" at each restore/save point, capture `_prevFileId` **before** the corresponding `_currentFileId` update:

```js
const _prevFileId = this._currentFileId;   // capture BEFORE L3842 (page) or L3961/L3975 (sheet)
this._currentFileId = fileEntry.id;         // L3842 / L3961 / L3975

// ... later at cache-hit or save guard:
if (_prevFileId !== fileEntry.id) {
    this._container.scrollTop = fileEntry._scrollPos?.page ?? 0;
}
// If same file: don't touch scrollTop
```

---

## 6. Detailed Implementation Plan

> **Note:** Context lines (unmarked) may abbreviate multi-line comments from actual `app.js` for brevity. Match on **code structure**, not comment text, when locating insertion points. Lines marked `← NEW` are additions; `← CHANGED` are modifications to existing lines.

---

### Task 1 — `AppState.createFileEntry()`: add `_scrollPos`

**Location:** Inside `createFileEntry()`, at the end of the returned object literal — after the `collate: true,` line.

```js
    createFileEntry(id, name, needsConversion) {
        return {
            id, name, needsConversion,
            pdfDoc:           null,
            totalPageCount:   0,
            selectedPages:    new Set(),
            singleSidedPages: new Set(),
            pageOrder:        [],
            pageRotations:    new Map(),
            blankAbsorbedBy:  new Map(),
            _togetherRotations:      new Set(),
            _originalOrientationMap: null,
            _pendingOrientationMap:  null,
            landscapeMode: 'together',
            copies:        1,
            collate:       true,
            _scrollPos: null,   // ← NEW: null = never rendered; { page, sheet, thumb } after first render
        };
    },
```

---

### Task 1b — `PreviewPanelModule` object: add module-level flags

**Location:** Inside the `PreviewPanelModule` object literal, after the `_sheetFingerprints` property declaration (last property before `init()`).

```js
    _sheetEls: new Map(),              // sheetIndex → .sheet-card el
    _isDeleting: false,
    _pageRoots:       new Map(),   // fileId → <div.preview-file-root> for page-view
    _sheetRoots:      new Map(),   // fileId → <div.preview-file-root> for sheet-view
    _sheetFingerprints: new Map(), // fileId → last-rendered sheet layout fingerprint string
    _modeJustToggled: false,       // ← NEW: one-shot flag — set by toggle handler, cleared by every render path
    _sheetRenderGen: 0,            // ← NEW: 0 = no pending async sheet rebuild; non-zero = token of active rebuild
    _nextSheetRenderGen: 0,        // ← NEW: monotonically increasing; each full-rebuild gets a unique token via ++

    init() {
        this._container = document.getElementById('preview-panel');
```

> **Why numeric initialization is critical:** `++this._nextSheetRenderGen` on an `undefined` property produces `NaN`. `NaN === NaN` is `false` — abort guards would never clear the token, and `NaN !== 0` is `true` — save guards would permanently block all sheet-mode scroll saves. The entire token system breaks without `0` initialization.

---

### Task 2 — `PreviewPanelModule.render()`: save & restore page-view scroll

**File:** `frontend/app.js`

#### 2a. Tab-switch save — at the top of `render()`, before the sheet-mode early return

Location: inside `render()`, before the `if (this._viewMode === 'sheet')` check, after the `blankAbsorbedBy` reset at the top of the function.

```js
    render(fileEntry) {
        // Invariant 3: reset blankAbsorbedBy before each rebuild
        if (fileEntry) fileEntry.blankAbsorbedBy = new Map();

        // §5.0: Teardown together-mode CCW90 when leaving sheet view.
        if (this._viewMode !== 'sheet') {
            for (const f of AppState.files) {
                if (f._togetherRotations?.size > 0) _teardownTogether(f);
            }
        }

        // Save outgoing file's scroll on tab-switch (both page and sheet mode).   // ← NEW
        // fileEntry null-guard: render() may be called with null activeFile.       // ← NEW
        // !_modeJustToggled guard: Task 2f already saved the correct pre-toggle    // ← NEW
        //   scroll; _viewMode has changed but container still shows old-mode DOM.  // ← NEW
        // sheet-pending guard: when _viewMode==='sheet' and _sheetRenderGen!==0,   // ← NEW
        //   container is mid-async-rebuild — saving would corrupt the sheet slot.  // ← NEW
        //   Guard is scoped to sheet mode only: page container is always valid.    // ← NEW
        const _sheetContainerInvalid = this._viewMode === 'sheet' && this._sheetRenderGen !== 0;   // ← NEW
        if (fileEntry && this._currentFileId && this._currentFileId !== fileEntry.id && this._container && !this._modeJustToggled && !_sheetContainerInvalid) {   // ← NEW
            const outgoing = AppState.files.find(f => f.id === this._currentFileId);   // ← NEW
            if (outgoing?._scrollPos) {   // ← NEW
                outgoing._scrollPos[this._viewMode] = this._container.scrollTop;   // ← NEW
            }   // ← NEW
        }   // ← NEW

        if (this._viewMode === 'sheet') {
            return this._renderSheetView(fileEntry);
        }
        if (!this._container || !fileEntry?.pdfDoc) return;
```

#### 2a2. Early-return flag clear — replacing the `if (!this._container || !fileEntry?.pdfDoc) return` line

Location: inside `render()`, the existing early-return guard after the sheet-mode branch, before the page-view logic begins.

```js
        if (this._viewMode === 'sheet') {
            return this._renderSheetView(fileEntry);
        }
        if (!this._container || !fileEntry?.pdfDoc) {   // ← CHANGED
            this._modeJustToggled = false;              // ← NEW: clear stale flag on early return
            return;                                     // ← CHANGED
        }
```

#### 2b. `_prevFileId` capture — before `this._currentFileId = fileEntry.id`

Location: inside `render()`, after the `for...of this._sheetRoots` loop that hides sheet roots, before `this._currentFileId = fileEntry.id`.

```js
        // Hide all roots; show only target file's page root
        for (const [fid, r] of this._pageRoots)    r.classList.toggle('preview-file-root--hidden', fid !== fileEntry.id);
        for (const r of this._sheetRoots.values()) r.classList.add('preview-file-root--hidden');

        const _prevFileId = this._currentFileId;   // ← NEW: capture before update
        this._currentFileId = fileEntry.id;

        const { root, isNew } = this._getOrCreatePageRoot(fileEntry);
```

#### 2c. Page-view mismatch branch save — before `root.innerHTML = ''`

Location: inside `render()`, inside the `isNew === false && domCardCount !== totalPageCount` branch, before `root.innerHTML = ''`.

```js
            // totalPageCount changed — fall through to rebuild this root
            // Guard: only save when this is a same-file state-change rebuild,   // ← NEW
            //   NOT a tab-switch (container still holds outgoing file's scroll). // ← NEW
            //   _prevFileId === fileEntry.id: true only for same-file mismatch. // ← NEW
            //   !_modeJustToggled: skip during mode-toggle context.             // ← NEW
            if (fileEntry._scrollPos && _prevFileId === fileEntry.id && !this._modeJustToggled) {   // ← NEW
                fileEntry._scrollPos.page = this._container.scrollTop;   // ← NEW
            }   // ← NEW
            root.innerHTML = '';
            for (const k of [...this._pageEls.keys()]) {
                if (k.startsWith(fileEntry.id + '-')) this._pageEls.delete(k);
            }
        }
        // First time or card count mismatch: build/rebuild the page root below
```

#### 2d. Page-view full rebuild scroll — replacing `this._container.scrollTop = 0`

Location: inside `render()`, after the page DOM rebuild loop, replacing the existing `this._container.scrollTop = 0` line before `requestAnimationFrame`.

```js
        // Scroll to top on first load; restore saved position on returning visits   // ← CHANGED (was: "Scroll to top, then render visible pages")
        if (fileEntry._scrollPos === null) {                                      // ← NEW
            fileEntry._scrollPos = { page: 0, sheet: 0, thumb: 0 };              // ← NEW
            this._container.scrollTop = 0;                                        // ← CHANGED
        } else {                                                                  // ← NEW
            this._container.scrollTop = fileEntry._scrollPos.page;               // ← NEW
        }                                                                         // ← NEW
        this._modeJustToggled = false;  // ← NEW: clear on full-rebuild (flag may be set from prior toggle)
        // rAF ensures DOM has been laid out before we measure visibility
        requestAnimationFrame(() => this._renderVisible());
    },
```

#### 2e. Page-view cache-hit scroll — replacing `this._container.scrollTop = 0`

Location: inside `render()`, inside the page cache-hit block (DOM valid, card count matches), replacing the existing `this._container.scrollTop = 0` line before `requestAnimationFrame`.

```js
                // DOM is valid and _pageEls is now correct — re-sync state only, no rebuild
                this._syncSelectionUI();
                if (_prevFileId !== fileEntry.id || this._modeJustToggled) {   // ← NEW
                    // Tab-switch OR mode-switch→page: restore incoming file's saved page scroll
                    this._container.scrollTop = fileEntry._scrollPos?.page ?? 0;   // ← CHANGED
                    this._modeJustToggled = false;   // ← NEW: clear after use
                }   // ← NEW
                // Same-file re-render (select/deselect): leave scrollTop untouched   // ← NEW
                requestAnimationFrame(() => this._renderVisible());
                return;
```

#### 2f. ViewModeModule toggle handler — replacing the entire `if (file) { ... }` block

Location: inside `ViewModeModule`'s button click handler, after the UI sync calls (`_syncModeBar()` etc.) and before the end of the handler. Replaces the existing `const file = AppState.activeFile; if (file) { ... }` block entirely. Part 1 is inserted after the existing same-mode guard (`if (newMode === AppState.viewMode) return`), before `AppState.viewMode = newMode`.

**Part 1 — hard gate, after the same-mode guard:**

```js
            const newMode = btn.dataset.view; // 'page' | 'sheet'
            if (newMode === AppState.viewMode) return;
            // Hard gate: if active file exists but pdfDoc not ready, drop the entire   // ← NEW
            // toggle click. Prevents AppState/UI/_viewMode from changing while the     // ← NEW
            // container shows stale DOM. Scoped to "file exists AND pdfDoc missing" —  // ← NEW
            // NOT "no file loaded" (no-file clicks are harmless: pre-select mode).     // ← NEW
            if (AppState.activeFile && !AppState.activeFile.pdfDoc) return;   // ← NEW
            AppState.viewMode = newMode;
            // Update button active state
            group.querySelectorAll('.view-toggle-btn').forEach(b => {
                b.classList.toggle('active', b.dataset.view === newMode);
            });
            // Show/hide landscape mode bar
            ViewModeModule._syncModeBar();
```

**Part 2 — replace the entire `const file = AppState.activeFile; if (file) { ... }` block:**

```js
            const file = AppState.activeFile;
            // Sync _viewMode unconditionally — regardless of whether a file is active.   // ← NEW
            // If left inside `if (file)`, a no-file preselect leaves _viewMode stale;   // ← NEW
            // first render after file upload would use the wrong path (R24A-1/R24B-1).  // ← NEW
            PreviewPanelModule._viewMode = newMode;   // ← CHANGED: moved outside if(file)
            if (file?.pdfDoc) {   // ← CHANGED
                // _viewMode is already newMode (synced above); container.scrollTop still   // ← NEW
                // reflects old-mode DOM. Save the OLD mode's scroll using _oldMode.       // ← NEW
                // Guard !_modeJustToggled: prior toggle still rendering; skip to avoid    // ← NEW
                // corrupting the slot that the prior Task 2f save already wrote correctly.// ← NEW
                // _sheetContainerInvalid: leaving sheet mode while mid-async rebuild;     // ← NEW
                // container scroll not yet restored — saving would corrupt sheet slot.    // ← NEW
                const _sheetContainerInvalid = (newMode === 'page') && PreviewPanelModule._sheetRenderGen !== 0;   // ← NEW
                const _oldMode = newMode === 'sheet' ? 'page' : 'sheet';   // ← NEW: mode we just LEFT
                if (!PreviewPanelModule._modeJustToggled && !_sheetContainerInvalid && file._scrollPos && PreviewPanelModule._container) {   // ← NEW
                    file._scrollPos[_oldMode] = PreviewPanelModule._container.scrollTop;   // ← NEW
                }   // ← NEW
                PreviewPanelModule._modeJustToggled = true;   // ← NEW: arm one-shot flag
                PreviewPanelModule.render(file);
            } else if (file) {   // ← NEW
                // file exists but pdfDoc not ready — hard gate above already blocked    // ← NEW
                // this path. Unreachable; kept for structural clarity.                  // ← NEW
                PreviewPanelModule.render(file);   // ← NEW: render() will early-return anyway
            }   // ← NEW
            // No file: _viewMode is synced above, no render needed.   // ← NEW
```

> **Important:** `_oldMode = newMode === 'sheet' ? 'page' : 'sheet'` correctly identifies the mode being LEFT, since `PreviewPanelModule._viewMode` is already `newMode` at save time. The `_sheetContainerInvalid` guard is scoped to `newMode === 'page'` (leaving sheet mode where mid-rebuild is possible); when leaving page mode, the page container is always valid.

---

### Task 3 — `_renderSheetView()`: restore sheet scroll

**File:** `frontend/app.js`

#### 3a0. Early-return flag clear — replacing the `if (!this._container || !fileEntry?.pdfDoc) return` line

Location: inside `_renderSheetView()`, the very first guard at the top of the function.

```js
    async _renderSheetView(fileEntry) {
        if (!this._container || !fileEntry?.pdfDoc) {   // ← CHANGED
            this._modeJustToggled = false;              // ← NEW: clear stale flag on early return
            return;                                     // ← CHANGED
        }
```

#### 3a. `_prevFileId` capture — before `this._currentFileId = fileEntry.id` in the cache-hit block

Location: inside `_renderSheetView()`, inside the sheet cache-hit block, before `this._currentFileId = fileEntry.id` at ~L3961.

```js
            this._renderQueue  = [];
            this._activeRenders = 0;
            for (const t of this._renderTasks.values()) { try { t.cancel(); } catch(_){} }
            this._renderTasks.clear();
            const _prevFileId = this._currentFileId;   // ← NEW: capture before update
            this._currentFileId = fileEntry.id;
            for (const r of this._pageRoots.values())   r.classList.add('preview-file-root--hidden');
```

#### 3b. Sheet cache-hit scroll — replacing `this._container.scrollTop = 0`

Location: inside `_renderSheetView()`, inside the cache-hit block, after showing the existing sheet root, replacing the existing `this._container.scrollTop = 0` before `requestAnimationFrame`.

```js
            _existingRoot.classList.remove('preview-file-root--hidden');
            if (_prevFileId !== fileEntry.id || this._modeJustToggled) {   // ← NEW
                // Tab-switch OR mode-switch→sheet: restore incoming file's saved sheet scroll
                this._container.scrollTop = fileEntry._scrollPos?.sheet ?? 0;   // ← CHANGED
                this._modeJustToggled = false;   // ← NEW: clear after use
            }   // ← NEW
            // Cache-hit: container is now valid (cached DOM shown, scroll restored).   // ← NEW
            // Clear any stale pending token from an older async full-rebuild in flight.// ← NEW
            this._sheetRenderGen = 0;   // ← NEW
            // Same-file re-render: leave scrollTop untouched (scroll already correct)  // ← NEW
            requestAnimationFrame(() => this._renderVisible());
            return;
        }
        // ── END SHEET CACHE-HIT ──
```

#### 3b2. Async abort guards — 5 early-returns after `await`, each with flag and token clear

Location: inside `_renderSheetView()`, at the two groups of ownership guards that exist after each `await`. Each replaces one existing single-line `return` guard with an expanded block.

**First pair — after the first `await`, guarding file-switch and mode-switch:**

```js
        // Guard: user may have switched file during async detection
        if (this._currentFileId !== fileEntry.id) {   // ← CHANGED
            this._modeJustToggled = false;                                    // ← NEW: clear stale flag on async abort
            if (this._sheetRenderGen === _myGen) this._sheetRenderGen = 0;   // ← NEW: clear only if this invocation owns the token
            return;                                                            // ← CHANGED
        }
        if (this._viewMode !== 'sheet') {   // ← CHANGED
            this._modeJustToggled = false;                                    // ← NEW
            if (this._sheetRenderGen === _myGen) this._sheetRenderGen = 0;   // ← NEW
            return;                                                            // ← CHANGED
        }
```

**Second trio — after the second `await`, inside the `if (landscapeMode === 'together')` block:**

```js
            if (this._currentFileId !== fileEntry.id) {   // ← CHANGED
                this._modeJustToggled = false;                                    // ← NEW
                if (this._sheetRenderGen === _myGen) this._sheetRenderGen = 0;   // ← NEW
                return;                                                            // ← CHANGED
            }
            if (this._viewMode !== 'sheet') {   // ← CHANGED
                this._modeJustToggled = false;                                    // ← NEW
                if (this._sheetRenderGen === _myGen) this._sheetRenderGen = 0;   // ← NEW
                return;                                                            // ← CHANGED
            }
            if (fileEntry.landscapeMode !== 'together') {   // ← CHANGED
                this._modeJustToggled = false;                                    // ← NEW
                if (this._sheetRenderGen === _myGen) this._sheetRenderGen = 0;   // ← NEW
                return;                                                            // ← CHANGED
            }
```

#### 3c-pre. Ownership + file-existence guard — before the shared commit path

Location: inside `_renderSheetView()`, after the `if (fileEntry.landscapeMode === 'together') { ... }` block closes at ~L4098, before the shared path begins at ~L4101 — specifically before `buildSheetLayout()` and before `fileEntry.blankAbsorbedBy = blankAbsorbedBy`. One insertion covers both `together` and `separate` landscape modes.

```js
        }
        // ── END TOGETHER MODE ────────────────────────────────────────────────────

        // Shared commit guard: abort before ANY state or DOM commit if either         // ← NEW
        // (1) a newer rebuild has taken over, OR                                     // ← NEW
        // (2) this file was deleted while the async rebuild was in flight.           // ← NEW
        // Without check (2), last-file deletion can still pass the `_currentFileId`  // ← NEW
        // and `_sheetRenderGen` guards if removal/clear did not schedule a newer     // ← NEW
        // render. A stale invocation could then overwrite blankAbsorbedBy, rebuild   // ← NEW
        // detached DOM, and re-add `_sheetFingerprints` for a file that no longer    // ← NEW
        // exists in `AppState.files`. After this point there are no more awaits →    // ← NEW
        // JS single-thread guarantees atomicity of all remaining commits.            // ← NEW
        const _fileStillExists = AppState.files.some(f => f.id === fileEntry.id);    // ← NEW
        if (this._sheetRenderGen !== _myGen || !_fileStillExists) {                  // ← NEW
            this._modeJustToggled = false;                                           // ← NEW
            if (this._sheetRenderGen === _myGen) this._sheetRenderGen = 0;           // ← NEW: clear only if this invocation still owns token
            return;                                                                  // ← NEW
        }   // ← NEW
        // Continue to shared commit: buildSheetLayout(), fileEntry.blankAbsorbedBy = ..., DOM build loop   // ← NEW

        const printMode = AppState.printMode;
        const { sheets, blankAbsorbedBy, deselectedPages } = buildSheetLayout(fileEntry, printMode, orientationMap, fileEntry.landscapeMode);
        fileEntry.blankAbsorbedBy = blankAbsorbedBy; // Invariant 9
```

#### 3c. Sheet full rebuild scroll — replacing `this._container.scrollTop = 0`

Location: inside `_renderSheetView()`, after the DOM build loop completes and `sheetRoot.appendChild(inner)` is called, replacing the existing `this._container.scrollTop = 0` and the fingerprint set, before `requestAnimationFrame`.

```js
        sheetRoot.appendChild(inner);

        // Store fingerprint so next render() for this file can skip rebuild on cache-hit
        this._sheetFingerprints.set(fileEntry.id, _fp);   // ← CHANGED (added note: must stay inside the owned commit block, after Task 3c-pre)

        // No re-check needed: Task 3c-pre already verified ownership synchronously.   // ← NEW
        // JS is single-threaded; no new invocation can intervene after that guard.    // ← NEW
        if (fileEntry._scrollPos === null) {                                           // ← NEW
            fileEntry._scrollPos = { page: 0, sheet: 0, thumb: 0 };                   // ← NEW
            this._container.scrollTop = 0;                                             // ← CHANGED
        } else {                                                                       // ← NEW
            // State-change rebuild: restore saved scroll (was saved in Task 4)        // ← NEW
            this._container.scrollTop = fileEntry._scrollPos.sheet;                   // ← NEW
        }                                                                              // ← NEW
        this._modeJustToggled = false;   // ← NEW: clear on full-rebuild
        this._sheetRenderGen = 0;        // ← NEW: we own the token; container now reflects correct sheet scroll
        requestAnimationFrame(() => this._renderVisible());
    },
```

---

### Task 4 — `_renderSheetView()`: save scroll before state-change rebuild

**Location:** Inside `_renderSheetView()`, in the full-rebuild path — after the cache-hit block returns, before `this._currentFileId = fileEntry.id` at ~L3975 and before `sheetRoot.innerHTML = ''` at ~L3983.

`_existingRoot` is already declared earlier via `const _existingRoot = this._sheetRoots.get(fileEntry.id)` (~L3954) and is in scope here.

#### 4a+4b. `_prevFileId` capture and scroll save — before `this._currentFileId = fileEntry.id`

Location: inside `_renderSheetView()`, full-rebuild path, immediately before `this._currentFileId = fileEntry.id` at ~L3975. The `_prevFileId` capture here is independent of Task 3a's capture (which is block-scoped inside the cache-hit block that ended with `return`).

```js
        this._renderQueue = [];
        this._activeRenders = 0;
        for (const t of this._renderTasks.values()) { try { t.cancel(); } catch(_){} }
        this._renderTasks.clear();
        const _prevFileId = this._currentFileId;   // ← NEW: capture BEFORE update; Task 3a's capture not available here (block-scoped in cache-hit)
        // Save current sheet scroll before rebuild — same-file state-change only.    // ← NEW
        // _existingRoot: skips first-time sheet render (no prior sheet DOM).          // ← NEW
        // _prevFileId === fileEntry.id: true only for same-file rebuilds; captured   // ← NEW
        //   BEFORE L3975 so it still holds the ID of the file the container was      // ← NEW
        //   showing. False for tab-switch (would save outgoing file's scroll into    // ← NEW
        //   incoming file's slot).                                                    // ← NEW
        // !_modeJustToggled: skips mode-toggle context (Task 2f saved correctly).    // ← NEW
        if (fileEntry._scrollPos && _existingRoot && _prevFileId === fileEntry.id && !this._modeJustToggled) {   // ← NEW
            fileEntry._scrollPos.sheet = this._container.scrollTop;   // ← NEW
        }   // ← NEW
        this._currentFileId = fileEntry.id;

        // Hide all roots; show only this file's sheet root
        for (const r of this._pageRoots.values())   r.classList.add('preview-file-root--hidden');
        for (const [fid, r] of this._sheetRoots)    r.classList.toggle('preview-file-root--hidden', fid !== fileEntry.id);

        const sheetRoot = this._getOrCreateSheetRoot(fileEntry);
        sheetRoot.classList.remove('preview-file-root--hidden');
        sheetRoot.innerHTML = ''; // layout must rebuild — fingerprint mismatch or first load
```

#### 4c. Fingerprint invalidation + invocation token — after `sheetRoot.innerHTML = ''`, before first `await`

Location: inside `_renderSheetView()`, full-rebuild path, immediately after `sheetRoot.innerHTML = ''` and after all `_sheetEls.clear()` cleanup calls (~L3993), before any `await`.

```js
        sheetRoot.innerHTML = ''; // layout must rebuild — fingerprint mismatch or first load

        // Clear stale _pageEls entries for this file IMMEDIATELY after innerHTML = ''.
        for (const k of [...this._pageEls.keys()]) {
            if (k.startsWith(fileEntry.id + '-')) this._pageEls.delete(k);
        }

        this._sheetEls.clear();

        // Reset blob pipeline
        this._blobQueue = [];
        this._activeBlobRenders = 0;

        // Invalidate the cached fingerprint — prevents a same-file reverted-state render   // ← NEW
        // from false-cache-hitting against the already-cleared root DOM.                    // ← NEW
        // _sheetFingerprints is only re-written on successful Task 3c completion.           // ← NEW
        this._sheetFingerprints.delete(fileEntry.id);   // ← NEW

        const _myGen = ++this._nextSheetRenderGen;   // ← NEW: unique token for this invocation
        this._sheetRenderGen = _myGen;               // ← NEW: container cleared, scroll not yet restored
```

---

### Task 5 — `TabsModule.setActive()`: save & restore thumb strip scroll

**File:** `frontend/app.js` | **Location:** `TabsModule.setActive()` (~L1527)

#### 5a+5b. Thumb save before index update, and restored rAF block

Location: inside `setActive()`, Task 5a goes before `AppState.activeFileIndex = idx` at ~L1529. Task 5b replaces the entire existing `requestAnimationFrame(() => { ... })` block at ~L1562.

```js
    setActive(idx) {
        if (idx < 0 || idx >= AppState.files.length) return;

        // Save outgoing file's thumb scroll before switching active index.   // ← NEW
        const outgoingFile = AppState.activeFile;   // ← NEW
        if (outgoingFile?._scrollPos && ThumbStripModule._container) {   // ← NEW
            outgoingFile._scrollPos.thumb = ThumbStripModule._container.scrollTop;   // ← NEW
        }   // ← NEW

        AppState.activeFileIndex = idx;

        // ... (rest of setActive body unchanged) ...

        this.render();
        ThumbStripModule.render();
        if (typeof PreviewPanelModule !== 'undefined') {
            PreviewPanelModule.render(AppState.activeFile);
        }
        PrintModule.updateButton();

        // Capture target file identity BEFORE scheduling rAF.   // ← NEW
        // AppState.activeFile may change again before the rAF runs (rapid tab clicks).  // ← NEW
        // Using live AppState.activeFile inside rAF causes wrong savedThumb, wrong      // ← NEW
        // _activeRoot, wrong querySelector target, wrong _setActiveHighlight call.      // ← NEW
        const _targetFile = AppState.files[idx];          // ← NEW
        const _targetFileId = _targetFile?.id;            // ← NEW

        requestAnimationFrame(() => {   // ← CHANGED: replaces old rAF block
            // Stale-callback guard: if user switched tabs again before this rAF ran,   // ← NEW
            // bail out. The newer setActive() call will schedule its own rAF.          // ← NEW
            if (AppState.activeFile?.id !== _targetFileId) return;   // ← NEW

            // Recompute live index: file may have been removed/reordered since         // ← NEW
            // setActive was called. _targetFileId is stable identity; idx (closure)   // ← NEW
            // may no longer match the file's tab position. Both _setActiveHighlight    // ← NEW
            // and querySelector use index-based DOM attributes → must use live index.  // ← NEW
            const _liveIdx = AppState.files.findIndex(f => f.id === _targetFileId);   // ← NEW
            if (_liveIdx < 0) return;  // ← NEW: file was deleted before rAF ran

            const _activeRoot = ThumbStripModule._fileRoots?.get(_targetFileId)   // ← NEW
                             ?? ThumbStripModule._container;                        // ← NEW
            const savedThumb = _targetFile?._scrollPos?.thumb ?? 0;   // ← NEW
            if (savedThumb > 0 && ThumbStripModule._container) {   // ← NEW
                // _setActiveHighlight first (may start smooth scroll),   // ← NEW
                // then scrollTop= cancels any in-progress smooth scroll. // ← NEW
                ThumbStripModule._setActiveHighlight(_liveIdx, 1);   // ← NEW
                ThumbStripModule._container.scrollTop = savedThumb;   // ← NEW
            } else {   // ← NEW
                const firstThumb = _activeRoot?.querySelector(   // ← NEW
                    `.thumb-item[data-file-index="${_liveIdx}"][data-page="1"]`   // ← NEW
                );   // ← NEW
                if (firstThumb) firstThumb.scrollIntoView({ behavior: 'auto', block: 'start' });   // ← NEW
                ThumbStripModule._setActiveHighlight(_liveIdx, 1);   // ← NEW
            }   // ← NEW
        });
    },
```

---

### Task 6 — Cleanup / deletion invalidation

`_scrollPos` vẫn nằm trong `fileEntry`, nên khi `AppState.removeFile()` xóa fileEntry khỏi `files[]`, `_scrollPos` tự bị GC. Nhưng với async sheet full-rebuild, như vậy **chưa đủ**: `PreviewPanelModule` còn giữ global ownership state (`_currentFileId`, `_sheetRenderGen`, `_modeJustToggled`) và `_renderSheetView()` commit path dùng captured `fileEntry` / `sheetRoot`, không phụ thuộc vào `files.find(...)`.

Nếu active/last file bị xóa khi sheet full-rebuild đang await, và ownership state không bị invalidate, resumed async path vẫn có thể pass các guard cũ rồi commit stale state vào detached root / `_sheetFingerprints`.

#### 6a. `PreviewPanelModule.removeFileRoot()` — invalidate ownership when removing the actively previewed file

Location: inside `removeFileRoot(fileId)`, near the end of the function after queue/cache cleanup.

```js
    removeFileRoot(fileId) {
        const pageRoot = this._pageRoots.get(fileId);
        if (pageRoot) { pageRoot.remove(); this._pageRoots.delete(fileId); }

        const sheetRoot = this._sheetRoots.get(fileId);
        if (sheetRoot) { sheetRoot.remove(); this._sheetRoots.delete(fileId); }
        this._sheetFingerprints.delete(fileId);

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

        // If the removed file currently owns the preview container, invalidate      // ← NEW
        // ownership immediately so any resumed async rebuild aborts safely.         // ← NEW
        if (this._currentFileId === fileId) {                                       // ← NEW
            this._currentFileId = null;                                             // ← NEW
            this._sheetRenderGen = 0;                                               // ← NEW
            this._modeJustToggled = false;                                          // ← NEW
        }                                                                           // ← NEW
    },
```

#### 6b. `PreviewPanelModule.clear()` — reset preview ownership state when all files are removed

Location: inside `clear()`, after the DOM/maps reset at the end of the function.

```js
    clear() {
        if (this._observer) { this._observer.disconnect(); this._observer = null; }
        if (this._container) {
            this._container.innerHTML = `
                <div class="preview-empty">
                    <span class="preview-empty-icon">🖨</span>
                    <span>Kéo file vào đây hoặc nhấn <strong>+ Thêm file</strong></span>
                </div>`;
        }
        // Reset per-file DOM roots (all files removed — maps now stale)
        this._pageRoots.clear();
        this._sheetRoots.clear();
        this._sheetFingerprints.clear();
        this._pageEls.clear();
        this._sheetEls.clear();
        this._currentFileId = null;    // ← NEW
        this._sheetRenderGen = 0;      // ← NEW
        this._modeJustToggled = false; // ← NEW
    },
```

---

## 7. Edge Cases

| Case | Behavior |
|---|---|
| First load của file | `_scrollPos === null` → init `{ page:0, sheet:0, thumb:0 }` → scroll = 0 |
| Tab switch: file chưa từng xem | `_scrollPos === null` → `?? 0` → scroll = 0 |
| Tab switch: file đã xem | Restore từ `_scrollPos[viewMode]` |
| Same-file re-render (select/deselect) | cache-hit → `_prevFileId === fileEntry.id` → scrollTop không thay đổi |
| View mode toggle page→sheet | Save page scroll (Task 2f); `_modeJustToggled = true`; cache-hit restores sheet scroll (0 nếu chưa xem) |
| View mode toggle sheet→page | Save sheet scroll (Task 2f); `_modeJustToggled = true`; cache-hit restores page scroll |
| State change trong sheet view (rotate, etc.) | Task 4 saves (nếu `_existingRoot`), Task 3c restores → stay in place |
| Mode-toggle→sheet lần đầu | `_existingRoot = null` → Task 4 skip (no wrong-scroll save) |
| File bị xóa mid-render | Không còn chỉ dựa vào `files.find(...)`. Task 6a/6b invalidate preview ownership state khi file active/last file bị xóa; Task 3c-pre còn check `AppState.files.some(...)` trước mọi commit shared-path. Async rebuild cũ abort an toàn, không được re-add `_sheetFingerprints` / stale DOM / scroll state cho file đã bị xóa. ✓ |
| Mode toggle với no file loaded | `PreviewPanelModule._viewMode` syncs unconditionally (moved outside `if(file)`); AppState/UI update; no render; `_modeJustToggled` NOT armed. First file render uses correct mode. ✓ |
| Mode toggle với active file nhưng `pdfDoc` chưa ready | Part 1 guard (`AppState.activeFile && !AppState.activeFile.pdfDoc`) returns before L5038 → no AppState/UI/`_viewMode` change; no save, no render, no flag arm. Container state unchanged. ✓ |
| Scroll > new content height | Browser clamps `scrollTop` tự động → safe |
| ThumbStrip: file chưa từng render | `_scrollPos === null` → `savedThumb = 0` → `scrollIntoView(page 1)` như cũ |

---

## 8. Files Changed

| File | Changes |
|---|---|
| `frontend/app.js` | Tasks 1–6, including deletion invalidation in `PreviewPanelModule.removeFileRoot()` / `clear()`, ~65 lines |

Không có file CSS thay đổi. Không có backend thay đổi.

---

## 9. Verification Checklist

- [ ] Load 3 PDF files (A=14 trang, B=11 trang, C=151 trang)
- [ ] Cuộn file C đến trang 80, switch sang file A, quay lại C → vẫn ở trang 80
- [ ] Click select/deselect page trong file C → scroll không nhảy
- [ ] Toggle page→sheet trong file C (ở trang 80) → sheet scroll = 0 (chưa từng xem)
- [ ] Cuộn sheet view xuống → toggle sheet→page → restore page position (trang 80)
- [ ] Deselect trang 5 trong sheet view → scroll giữ nguyên sau rebuild
- [ ] Toggle page→sheet lần 2 → restore sheet scroll (vị trí lần cuối xem sheet)
- [ ] Xóa file B → file A và C vẫn nhớ scroll đúng
- [ ] ThumbStrip: cuộn xuống thumb 100 → switch tab → quay lại → vẫn ở thumb 100
- [ ] Upload file mới → scroll về 0 (không restore gì) ✓
- [ ] Không có console errors trong suốt quá trình trên

---

## 10. Non-Goals

- **Persist scroll across app restarts** — in-memory only, không cần localStorage
- **Animate scroll restore** — dùng `scrollTop =` trực tiếp (không `behavior: 'smooth'`) để instant
- **Sync thumb scroll với preview scroll** — đây là behavior riêng của `_onScroll` handler, không liên quan

---

## 11. Review History

### Round 1 (2026-04-09) — Manual review by main agent

**C1 — View mode toggle saves to wrong slot:**  
`_viewMode` thay đổi tại L5048 TRƯỚC khi `render()` được gọi tại L5049. Nếu save trong `render()` với `this._viewMode`, sẽ lưu vào slot mode MỚI thay vì mode cũ. Fix: thêm Task 2f — save trong ViewModeModule toggle handler TRƯỚC dòng `_viewMode = newMode`.

**C2 — Page-view mismatch path thiếu save:**  
Khi `isNew === false` nhưng `domCardCount !== totalPageCount`, code gọi `root.innerHTML = ''` (L3881) mà không save scroll trước. Fix: thêm Task 2c — save `_scrollPos.page` trước `root.innerHTML = ''`.

### Round 2 (2026-04-09) — Manual review by main agent

**C3 — `_setActiveHighlight` smooth scroll fights thumb restore:**  
Task 5b cũ: set `scrollTop = savedThumb` trước, rồi `_setActiveHighlight(idx, 1)` chạy sau (vẫn trong cùng rAF). `_setActiveHighlight` dùng `scrollIntoView({ behavior: 'smooth' })` — nếu page 1 không visible ở savedThumb position, smooth scroll sẽ animate container về page 1 và xóa vị trí đã restore. Fix: đổi thứ tự — gọi `_setActiveHighlight` TRƯỚC, rồi `scrollTop =` CUỐI CÙNG — synchronous assignment hủy bỏ smooth scroll đang chạy, vị trí restore thắng.

**I1 — §5.1 code block mâu thuẫn với §5.4:**  
§5.1 hiển thị `_scrollPos: { page, sheet, thumb }` nhưng §5.4 và Task 1 dùng `null`. Fixed in final state.

**I2 — Task 5b snippet thiếu `_activeRoot` declaration và `data-file-index` selector:**  
Fixed in final state.

### Round 3 (2026-04-09) — Manual review by main agent

**C4 — Sheet-mode tab-switch never saved outgoing file's scroll:**  
Task 2a đặt sau L3830 bị bypass bởi early-return tại L3828 khi ở sheet mode. Fix: di chuyển Task 2a lên TRƯỚC L3827.

### Round 4 (2026-04-09) — Parallel Oracle review (2 agents)

**R4-A1 — Tasks 2b/3a restore stale data on same-file cache-hit re-renders:**  
Cache-hit bị trigger bởi cả tab-switch VÀ same-file state-change. Restore vô điều kiện khiến scroll nhảy khi user select/deselect page. Fix: capture `_prevFileId` trước khi update `_currentFileId`; chỉ restore khi `_prevFileId !== fileEntry.id`.

**R4-A2 — Task 4 saves wrong scroll on mode-toggle rebuild:**  
Toggle page→sheet: container vẫn mang page scroll khi Task 4 chạy → `sheet` slot bị ghi đè bằng page scroll. Fix: guard bằng `&& _existingRoot` — chỉ save khi sheet root đã tồn tại từ trước (state-change, không phải first-time sheet render).

**R4-A3 — Task 2e snippet gây duplicate render nếu implement literal:**  
Snippet cũ include cả `_viewMode = newMode; render(file)`. Fixed: final state chỉ show save block mới; note rõ không thêm lại các dòng hiện có.

**R4-B1 — Task 2a thiếu null guard cho `this._container`:**    
Task 2a chạy trước mọi container guard. Fix: thêm `&& this._container` vào condition.

### Round 5 (2026-04-09) — Parallel Oracle review (2 agents)

Không tìm thấy critical issue nào. Spec được xác nhận sẵn sàng implement.

### Round 6 (2026-04-09) — Parallel Oracle review (2 agents)

**R6-B1 — Sheet→page toggle không restore page scroll (CRITICAL):**  
Task 2e chỉ restore khi `_prevFileId !== fileEntry.id` (tab-switch). Nhưng khi toggle sheet→page, `_prevFileId === fileEntry.id` (cùng file) → condition false → `scrollTop` giữ nguyên giá trị sheet-mode → container hiển thị sai vị trí trong page-mode.

Root cause: shared `_container` cho cả page và sheet mode — scroll coordinate từ sheet không có nghĩa trong page layout.

Fix:
1. Task 2f thêm `PreviewPanelModule._modeJustToggled = true;` sau khi save scroll
2. Task 2e: condition mở rộng thành `_prevFileId !== fileEntry.id || this._modeJustToggled`; clear flag sau dùng
3. Task 3b: tương tự — `_prevFileId !== fileEntry.id || this._modeJustToggled`; clear flag

**R6: Tất cả checks khác confirmed correct** — Task 2a scope, Task 2f ordering, Task 3a scope, Task 4 async safety, Task 5b savedThumb=0 edge case, flow traces cho first-load/tab-switch/page→sheet đều đúng.

### Round 7 (2026-04-09) — Parallel Oracle review (2 agents)

**R7-1 — `_modeJustToggled` không được clear trên page full-rebuild path (Task 2d):**  
Nếu sheet→page toggle đi qua page full rebuild (edge case: page root chưa tồn tại), Task 2e không được gọi → flag không clear → tồn tại dai dẳng → lần cache-hit kế tiếp bắn nhầm restore.  
Fix: thêm `this._modeJustToggled = false;` vào cuối Task 2d.

**R7-2 — `_modeJustToggled` không được clear trên sheet full-rebuild path (Task 3c) ← THƯỜNG XUYÊN XẢY RA:**  
page→sheet toggle lần đầu **luôn** đi qua sheet full rebuild (không có existing root) → Task 3b không được gọi → flag không clear. Sau đó nếu user select rồi deselect cùng page → fingerprint giống cũ → sheet cache-hit → Task 3b: `_modeJustToggled` vẫn true → restore scroll về giá trị cũ → **visible scroll jump**.  
Fix: thêm `this._modeJustToggled = false;` vào cuối Task 3c.

**R7-3 — `_modeJustToggled` nên được init tường minh:**  
Không phải bug (undefined là falsy), nhưng nên thêm `_modeJustToggled: false` vào PreviewPanelModule object để rõ ràng về lifetime của flag. Thêm vào §5.1b.

**R7: Tất cả scenarios khác confirmed correct** — repeated toggles, tab-switch với stale flag, first-ever load, Task 2a + toggle interaction đều đúng.

### Round 8 (2026-04-09) — Parallel Oracle review (2 agents)

**R8-1 — Task 2c lưu scroll sai của outgoing file vào incoming file (CRITICAL):**  
Task 2c chạy khi page mismatch rebuild. `fileEntry` = incoming file B. `this._container.scrollTop` tại thời điểm này vẫn là scroll của outgoing file A (container shared, CSS class switch không thay đổi scrollTop). Task 2c ghi `B._scrollPos.page = A's scrollTop` → Task 2d restore B về vị trí sai.  
Fix: thêm guard `_prevFileId === fileEntry.id && !this._modeJustToggled` — chỉ save khi đây là same-file mismatch rebuild.

**R8-2 — Task 4 lưu scroll sai của outgoing file vào incoming file trên tab-switch (CRITICAL):**  
Tab-switch A→B trong sheet mode: B có existing sheet root → cache-hit fails (fingerprint mismatch) → Task 4 chạy. `this._container.scrollTop` vẫn là A's scroll → `B._scrollPos.sheet = A's scroll` → visible wrong restore.  
Fix: thêm guard `this._currentFileId === fileEntry.id` — chỉ save khi đây là same-file rebuild (container đang hiện file đó).

**R8: Tất cả paths khác confirmed correct** — Task 2a, 2e, 2f, 3b, 3c, 5a, 5b đều đúng. Q3 (ThumbStripModule async scroll race) là verification note cho implementation, không phải spec bug.

### Round 9 (2026-04-09) — Parallel Oracle review (2 agents)

**R9-1 — Task 4 thiếu guard `!this._modeJustToggled` (CRITICAL):**  
Scenario: File A ở sheet scroll 800 → toggle page → chọn thêm trang (fingerprint thay đổi) → toggle lại sheet. Task 4 chạy với `_existingRoot` = A's sheet root, `_currentFileId === A.id` → guard pass → saves `A._scrollPos.sheet = this._container.scrollTop`. Nhưng container lúc này đang ở page mode → `scrollTop` = A's page scroll (ví dụ 1200) → ghi đè A's sheet slot thành 1200 (WRONG). Task 3c restore về 1200 → **visible: sheet về sai vị trí**.

Fix: thêm `&& !this._modeJustToggled` vào Task 4's guard. Task 2f đã save đúng sheet scroll trước toggle; Task 4 không nên ghi đè trong mode-toggle context.

**Task 4 final guard:**
```js
if (fileEntry._scrollPos && _existingRoot && _prevFileId === fileEntry.id && !this._modeJustToggled) {
    fileEntry._scrollPos.sheet = this._container.scrollTop;
}
```

**R9: Tất cả 10 scenarios S1–S10 confirmed correct** sau khi áp dụng fix R9-1. Task 2c guard đúng. Tasks 2a, 2b, 2d, 2e, 2f, 3a, 3b, 3c, 5a, 5b đều đúng. Không còn bug nào khác được tìm thấy.

### Round 10 (2026-04-09) — Parallel Oracle review (2 agents) + manual code verification

**R10-1 — Task 2a thiếu null guard cho `fileEntry` (CRITICAL — crash):**  
`render()` được gọi tại nhiều nơi với `AppState.activeFile` mà không guard null. Nếu không có file active, `fileEntry = null`, Task 2a đọc `fileEntry.id` → TypeError crash.  
Fix: thêm `fileEntry &&` vào đầu điều kiện Task 2a.

**R10-2 — `_modeJustToggled` không clear khi `_renderSheetView` abort giữa chừng (async race):**  
`_renderSheetView` có 2 early-return guards sau `await` tại L4029 (`_currentFileId !== fileEntry.id`) và L4030 (`_viewMode !== 'sheet'`). Nếu user switch tab hoặc toggle lại trong khi async render đang chạy, function abort sớm mà không chạy Task 3c → flag không clear → stale flag tồn tại → render tiếp theo của file khác bị ảnh hưởng.  
Fix: thêm `this._modeJustToggled = false` vào mỗi guard trước `return`.

**R10-3 — Task 2a async race: save outgoing scroll khi container ở trạng thái trung gian:**  
Nếu `_renderSheetView(A)` đang await (chưa set scrollTop), user switch tab → render(B) → Task 2a saves `A._scrollPos['sheet'] = container.scrollTop` nhưng container.scrollTop lúc này là giá trị sai (chưa được restore bởi Task 3c).  
Root cause = R10-2: flag chưa clear nên `_viewMode` đã là 'sheet' nhưng scroll chưa đúng.  
Fix: việc clear flag ở L4029/L4030 (R10-2) giải quyết được vấn đề này — khi flag được clear đúng lúc, các invariants khác được giữ nguyên. Ngoài ra, Task 2f đã save scroll TRƯỚC khi toggle bắt đầu, nên A's scroll đã được save đúng; giá trị bị ghi đè bởi Task 2a là giá trị sai, nhưng lần render hoàn chỉnh tiếp theo sẽ overwrite lại đúng.

**R10: Tất cả 6 render paths flag-lifetime confirmed correct** (sau khi thêm clear ở L4029/L4030). Task 2c, Task 4 guards đúng. Task 2e restore expression đúng. Không có bug nào khác.

### Round 11 (2026-04-09) — Parallel Oracle review (2 agents) + manual code read

**R11-1 — `_modeJustToggled` leak qua sync early-returns ở L3830 và L3934 (CRITICAL):**  
Khi user toggle mode trong khi file đang convert (pdfDoc = null), `render()` trả về sớm ở L3830 (page path) hoặc L3934 (sheet path) mà không clear flag. Lần render thành công tiếp theo có thể là same-file re-render → cache-hit → Task 2e/3b: `_modeJustToggled=true` → restore fires → **visible scroll jump không mong muốn**.  
Verified: toggle button không disable khi pdfDoc=null (L5046: chỉ check `if (file)`, không check `file.pdfDoc`).

Fix approach 1 (spec chọn): chỉ arm flag trong Task 2f khi `AppState.activeFile?.pdfDoc` truthy → flag không bao giờ được set khi file chưa ready → L3830/L3934 early-return không thể leak flag.

Fix approach 2 (alternative, từ Agent 11B): thêm `this._modeJustToggled = false` vào L3830 và L3934 early-returns.

Spec dùng Approach 1 (guard ở nguồn — Task 2f) vì đơn giản hơn và không cần sửa thêm existing lines.  
Approach 2 thêm vào dự phòng: sửa L3830 và L3934 (Task 2a2 và Task 3a0).

**R11: Fixes 1–3 từ Round 10 confirmed correct.** Scenarios A–E verified đúng. Scenario F (async tab-switch mid-build) acceptable — A's sheet scroll sẽ về 0 sau lần quay lại đầu tiên (vì A chưa có completed sheet render), sau đó user scroll và lần sau mới được save đúng.

**Không còn bug nào khác được tìm thấy sau khi apply R11-1.**

### Round 12 (2026-04-09) — Parallel Oracle review (2 agents)

**R12-A1 — `_modeJustToggled` leak qua async early-returns tại L4063–4065 (CRITICAL):**  
Round 10 đã fix L4029 và L4030 (first await guard block), nhưng `_renderSheetView` có một **second await guard block** tại L4063–4065 (sau intrinsic-detection await). Ba returns này cũng không clear flag:
- L4063: `if (this._currentFileId !== fileEntry.id) return` — file switch sau intrinsic detection
- L4064: `if (this._viewMode !== 'sheet') return` — mode switch sau intrinsic detection  
- L4065: `if (fileEntry.landscapeMode !== 'together') return` — landscapeMode changed to 'separate'

Nếu render abort ở bất kỳ guard nào, `_modeJustToggled` tồn tại dai dẳng → lần render tiếp theo bị ảnh hưởng.  
Fix: thêm `this._modeJustToggled = false` vào cả 3 returns, mở rộng Task 3b2 trong spec.

**R12-B1 — Task 2a có thể corrupt scroll slot của outgoing file trong mode-toggle + immediate tab-switch (CRITICAL):**  
Scenario: File A ở page mode với `page=1200`, `sheet=800`. User toggle page→sheet: Task 2f saves `A._scrollPos.page = 1200`, sets `_modeJustToggled = true`, sets `_viewMode = 'sheet'`. User immediately switch sang file B **trước khi** A's sheet render hoàn thành. `render(B)` runs: Task 2a checks `_currentFileId !== B.id` → true → saves `A._scrollPos[this._viewMode] = container.scrollTop`. Nhưng `_viewMode` đã là `'sheet'` và `container.scrollTop` vẫn là giá trị page cũ (`1200`) → `A._scrollPos.sheet = 1200` ← SAI (đúng phải là `800`).  

Root cause: Task 2f saves đúng trước toggle, nhưng Task 2a overwrite slot sai sau khi `_viewMode` đã thay đổi.  
Fix: thêm `&& !this._modeJustToggled` vào điều kiện Task 2a — khi flag set, Task 2f đã save đúng giá trị; Task 2a phải skip để không ghi đè sai.

**R12: 2 bugs found, both fixed in spec. Firing Round 13.**

### Round 13 (2026-04-09) — Parallel Oracle review (2 agents)

**R13A: No bugs found.** R12-A1 and R12-B1 confirmed correct. Flag lifecycle exhaustive — no path where flag stays `true` indefinitely. Double-toggle scenario (Q6) traced correctly: A returns to correct sheet scroll after page→sheet→B→A sequence.

**R13-B1 — Task 2f double-toggle race corrupts opposite mode's scroll slot (CRITICAL):**  
Scenario: File A có `page=1200`, `sheet=800`. User toggle page→sheet: Task 2f saves `page=1200`, arms flag, `_viewMode='sheet'`, async sheet render starts. Container vẫn còn scroll `1200` (sheet render chưa xong, chưa restore). User toggle lại sheet→page: Task 2f chạy lần 2 với `_viewMode='sheet'` → saves `A._scrollPos.sheet = container.scrollTop = 1200` ← SAI (đúng phải là `800`). Sheet slot bị ghi đè bằng page scroll.

Root cause: Task 2f tin tưởng `_viewMode` mô tả đúng mode của container, nhưng trong async render, `_viewMode` đã thay đổi mà container chưa kịp cập nhật.  
Fix: guard Task 2f's save block bằng `!PreviewPanelModule._modeJustToggled` — khi flag=true, có nghĩa là một toggle trước đó vẫn đang in-flight và container chưa phản ánh `_viewMode` hiện tại → skip save để không corrupt slot.

**R13: 1 bug found, fix applied in spec. Firing Round 14.**

### Round 14 (2026-04-09) — Parallel Oracle review (2 agents)

**R14A: No bugs found.** R13-B1 fix confirmed correct. Single-toggle, post-render-toggle, and double-toggle race all traced correctly. No missed-save path introduced by the new `!_modeJustToggled` guard in Task 2f.

**R14-B1 — Task 4 same-file guard `this._currentFileId === fileEntry.id` is always true — guard is ineffective (CRITICAL):**  
Task 4 is located AFTER the full-rebuild path sets `this._currentFileId = fileEntry.id` at L3975. By the time Task 4 runs, `_currentFileId` already equals `fileEntry.id` — so the guard meant to distinguish "same-file rebuild" from "tab-switch" is always true, providing no protection.

Scenario: File B has `B._scrollPos.sheet = 200`. Switch from A (sheet scroll 800) to B. `_renderSheetView(B)` sees fingerprint mismatch → cache-hit fails → full-rebuild path → L3975 sets `_currentFileId = B.id` → Task 4 check: `_currentFileId === B.id` ← always true → saves `B._scrollPos.sheet = container.scrollTop = 800` (A's scroll, still in container) ← WRONG. Task 3c restores B to `800` instead of `200`.

Root cause: `_currentFileId` is updated at L3975 before Task 4 runs. The spec's guard uses the post-update value — identical for all cases.  
Fix: capture `_prevFileId` BEFORE L3975 in the full-rebuild path (separate from Task 3a's capture which is inside the cache-hit block and not available here), then use `_prevFileId === fileEntry.id` in Task 4's guard.

**R14: 1 bug found, fix applied in spec. Firing Round 15.**

### Round 15 (2026-04-09) — Parallel Oracle review (2 agents)

**R15A: No bugs found.** R14-B1 fix confirmed correct. `_prevFileId` scope safe (cache-hit block's `const _prevFileId` is block-scoped, no conflict with full-rebuild's `const _prevFileId`). Save coverage matrix for all 6 scenarios traced correct.

**R15-B1 — Task 2f corrupts incoming file's sheet scroll during async sheet full-rebuild after tab-switch + mode toggle (CRITICAL):**  
Scenario: File A at sheet scroll 800. Switch to B (sheet mode, fingerprint mismatch → async full rebuild starts). `_currentFileId = B.id` (L3975), `sheetRoot.innerHTML = ''` (L3983), async awaits begin. Container `scrollTop` still holds 800 (B's restore at Task 3c hasn't run yet). User immediately toggles sheet→page. Task 2f sees: `_modeJustToggled=false` (no prior toggle in flight), `_viewMode='sheet'`, `activeFile=B`, `container.scrollTop=800` → writes `B._scrollPos.sheet = 800` ← WRONG (belongs to A, not B). Later toggling back to sheet restores B to 800 instead of its correct value (0 or prior saved).

Root cause: Task 2f's `!_modeJustToggled` guard only blocks in-flight *mode-toggle* renders. It does NOT block in-flight *async sheet full-rebuild after tab-switch*, where container is in an intermediate state (cleared, not yet restored).

Fix: introduce new one-shot flag `_sheetRenderPending` (added to §5.1b). Set it true after `sheetRoot.innerHTML=''` and before the first `await` (Task 4c). Clear it at ALL exit paths of the async full-rebuild: Task 3c (success) and all 5 async abort guards (Task 3b2). Guard Task 2f's save block with `!PreviewPanelModule._sheetRenderPending` in addition to existing guards.

**R15: 1 bug found, fix applied in spec. Firing Round 16.**

### Round 16 (2026-04-09) — Parallel Oracle review (2 agents)

**R16A — Remaining bug: `_sheetRenderPending` over-blocks legitimate page saves:**  
After sheet→page toggle completes (page render done, container valid), old async sheet render is still in flight with `_sheetRenderPending=true`. If user scrolls in page mode and toggles page→sheet before old async aborts, Task 2f sees flag=true and skips saving the valid page scroll. This is a missed save — page position lost.

**R16B — Remaining bugs (3 related, same root cause):**
1. **A1:** Task 2a also has no pending guard — A→B async sheet rebuild + immediate C switch can corrupt B's scroll via Task 2a
2. **A3:** Plain boolean not ownership-safe — overlapping async renders: B aborts and clears boolean while C still pending → C's window unprotected
3. **A6/A7:** Boolean stays true in page mode (stale), blocking valid page saves

**Root cause (all):** `_sheetRenderPending` as a plain boolean cannot distinguish invocations, and guards every mode (not just sheet mode where container is actually invalid).

**Fix (unified):**
- Replace `_sheetRenderPending: false` with `_sheetRenderGen: 0` + `_nextSheetRenderGen: 0` in §5.1b
- Task 4c: capture `const _myGen = ++this._nextSheetRenderGen; this._sheetRenderGen = _myGen;` — each invocation owns a unique token
- Task 3c + Task 3b2: clear with `if (this._sheetRenderGen === _myGen) this._sheetRenderGen = 0;` — only clears if this invocation still owns it
- Save guards (Task 2a, Task 2f): `this._viewMode === 'sheet' && this._sheetRenderGen !== 0` — scoped to sheet mode only; page mode container always valid regardless of pending token

**R16: bugs found, fixes applied (token system replacing boolean). Firing Round 17.**

### Round 17 (2026-04-09) — Parallel Oracle review (2 agents)

**R17A: No bugs found.** Token ownership model verified correct: overlapping async renders cannot clear each other's tokens; page-mode guard correctly unblocks page saves; `_myGen` in scope at all exit points; no practical overflow risk; Task 2a B→C protection correct.

**R17-B1 — `_sheetRenderGen` not cleared on sheet cache-hit path (CRITICAL missed save):**  
Cache-hit path (L3955–3967) makes the container immediately valid — cached DOM shown, scroll restored synchronously — but it returns early before Task 4c and does NOT clear `_sheetRenderGen`. If an older async full-rebuild invocation left `_sheetRenderGen=1`:
1. C starts async full rebuild (`gen=1`)
2. User switches to B → sheet cache-hit: container valid immediately, DOM shown
3. User scrolls B (container valid, user sees correct content)
4. Before C's old async invocation reaches its abort guard, user switches away from B or toggles mode
5. Task 2a/2f: `_viewMode==='sheet' && _sheetRenderGen!==0` → `_sheetContainerInvalid=true` → **skip save** ← WRONG
6. Return to B: scroll restores to old value, not what user saw

Root cause: `_sheetRenderGen` semantics = "current visible sheet container invalid". Cache-hit makes container valid, so clearing the token here is correct and safe.  
Fix: add `this._sheetRenderGen = 0` at end of cache-hit return path (Task 3b), after scroll restore and flag clear.

**R17: 1 bug found, fix applied. Firing Round 18.**

### Round 18 (2026-04-09) — Parallel Oracle review (2 agents)

**R18-A/B (same bug found by both agents) — Same-file false cache-hit on cleared sheet root (CRITICAL):**  
`_sheetFingerprints` is only updated at Task 3c (success), NOT when `sheetRoot.innerHTML=''` is called at L3983. When a full-rebuild starts, the old fingerprint remains in the map. If the user then reverts the state change (fingerprint returns to old value) before the async rebuild completes, a second `_renderSheetView(A)` call sees the old fingerprint match → takes the cache-hit path → Task 3b clears `_sheetRenderGen=0` thinking the container is valid. But the container was already cleared by `innerHTML=''` — it is NOT valid. The in-flight old rebuild's Task 3c may then set `container.scrollTop` to A's saved scroll, which could be stale. Meanwhile, saves during this window operate on an invalid container.

Fix: add `this._sheetFingerprints.delete(fileEntry.id)` immediately after `sheetRoot.innerHTML=''` (and before first `await`) in Task 4c. This ensures no render can cache-hit against a cleared root. The second render call will see no fingerprint → enters full-rebuild → gets new `_myGen` → old rebuild's `_myGen` is orphaned (can't clear new token at abort guards).

**R18: 1 bug found, fix applied (fingerprint invalidation in Task 4c). Firing Round 19.**

### Round 19 (2026-04-09) — Parallel Oracle review (2 agents)

**R19-A1 — Task 3c missing `_myGen` ownership guard before scroll restore and fingerprint set (CRITICAL):**
Task 3c has no guard to check whether the current invocation still owns the render token before committing to DOM/scroll/fingerprint. Two concurrent same-file full-rebuilds (e.g., user triggers a state change mid-render, causing a second invocation) can both reach Task 3c. The later-completing invocation (with the lower/stale gen token) can overwrite the earlier one's (higher gen) scroll restore and `_sheetFingerprints.set()`, leaving the container showing the wrong scroll position and an incorrect fingerprint cached.

Root cause: Task 3c previously used `if (this._sheetRenderGen === _myGen) this._sheetRenderGen = 0;` to clear the token — but only AFTER already setting `scrollTop` and fingerprint. The ownership check must happen BEFORE any DOM mutations.

Fix: add `if (this._sheetRenderGen !== _myGen) { this._modeJustToggled = false; return; }` guard as the FIRST thing in Task 3c's commit block — before `scrollTop` assignment and before `_sheetFingerprints.set()`. The `_modeJustToggled = false` clear is safe to do in the aborting invocation because the flag is global (not per-invocation); the winning invocation will clear it too.

**R19-B: No bugs found.** Agent noted implementation is missing (expected — spec-only phase). Confirmed spec is structurally sound.

**R19: 1 spec bug found (R19-A1), fix applied (Task 3c ownership guard). Firing Round 20.**

### Round 20 (2026-04-09) — Parallel Oracle review (2 agents)

**R20B-1 — Task 3c ownership guard is too late; stale same-file rebuild can still corrupt DOM/maps/fingerprint (CRITICAL):**
The R19-A1 guard was inserted at the scroll-restore line (~L4352), but by that point a stale same-file invocation has already: (1) appended stale sheet page elements into `sheetRoot`, (2) updated `_sheetEls`/`_pageEls` maps, (3) potentially called `_sheetFingerprints.set(fileEntry.id, _fp)`. The guard fires after all DOM mutations — protecting only scroll and gen-clear, not the DOM itself.

Scenario: File A starts full rebuild #1 (gen=1). Before #1 finishes, user triggers state change → full rebuild #2 starts (gen=2). #2 completes first, rendering correct DOM. #1 later resumes, passes file/mode abort guards (same file, still sheet), appends stale DOM, updates maps, sets stale fingerprint — only THEN hits Task 3c and aborts scroll. But DOM is already corrupted.

Fix: Move ownership guard to **before DOM build** — immediately after the final Task 3b2 abort guard (L4065), before any `sheetRoot.appendChild`, `_sheetEls.set`, `_pageEls.set`, `_sheetFingerprints.set`. Because JS is single-threaded and there are no more `await`s after L4065, once the guard passes, the DOM commit phase runs atomically. Added as new Task 3c-pre in spec. Task 3c (scroll restore block) no longer needs its own ownership re-check — ownership is guaranteed by Task 3c-pre. Also explicitly called out that `_sheetFingerprints.set()` must be INSIDE the owned commit block.

**R20A-2 — Task 2f partially proceeds when `pdfDoc` is falsy for an already-rendered file (MEDIUM):**
Task 2f previously saved `_scrollPos[viewMode]` when `_scrollPos` exists (regardless of `pdfDoc`), but only armed `_modeJustToggled` when `pdfDoc` is truthy. If `pdfDoc` is temporarily falsy (e.g., mid-reconversion) but DOM caches exist: Task 2f saves (with container in wrong-mode coordinate), does NOT arm flag, `_viewMode` changes, render early-returns at Task 2a2/3a0. Later when pdfDoc returns and file re-renders via cache-hit: `_modeJustToggled=false` → restore skipped → container shows wrong-mode scroll in new mode.

Fix: Guard the ENTIRE Task 2f block with `if (!AppState.activeFile?.pdfDoc)` → skip both save and flag arm when pdfDoc unavailable. The `_viewMode` change and `render()` call (already present, not added by spec) still proceed — only the save/arm logic is gated.

**R20A-1 — Task 5b stale rAF closure on rapid tab switches (MEDIUM):**
Task 5b read `AppState.activeFile` inside the rAF callback (live at callback time), not at scheduling time. On rapid tab switches (setActive B then C before next paint), the first rAF runs with: `savedThumb` and `_activeRoot` from C (live activeFile), but `idx` and `_setActiveHighlight` target from B (closure). Results in wrong thumb highlighted, wrong root queried, mismatched scroll restoration.

Fix: Capture `const _targetFile = AppState.files[idx]` and `const _targetFileId = _targetFile?.id` BEFORE scheduling rAF. Inside rAF: add stale-callback guard `if (AppState.activeFile?.id !== _targetFileId) return;`. Use `_targetFile` and `_targetFileId` (not live `AppState.activeFile`) for `savedThumb`, `_activeRoot`, and `querySelector`.

**R20: 3 bugs found, all fixes applied. Firing Round 21.**

### Round 21 (2026-04-09) — Parallel Oracle review (2 agents)

**R21A-1 — Task 3c-pre placement ambiguous: guard inside `together` branch only would leave `separate` mode unguarded (CRITICAL):**
Task 3c-pre was anchored to "after Task 3b2 / L4065". But L4063–L4065 abort guards exist only INSIDE the `if (fileEntry.landscapeMode === 'together')` block. If an implementer places Task 3c-pre literally after L4065, it lives inside the together-branch — `landscapeMode === 'separate'` full-rebuilds never pass through the guard. Stale same-file async rebuilds in separate mode can still append stale DOM, mutate element maps, and write stale fingerprints.

Fix: Updated Task 3c-pre wording to explicitly state: "Covers BOTH landscapeMode==='together' AND 'separate' — do NOT place inside together-branch only." Correct placement: after the entire `if (landscapeMode === 'together') { ... }` block closes, before the first DOM/map mutation shared by both paths (before layout-construction loop / `sheetRoot.appendChild`).

**R21B-1 — Task 2f R20A-2 fix still allows `_viewMode` to change while container is stale (CRITICAL):**
R20A-2 wrapped save+flag-arm in `else (pdfDoc truthy)`, but the `if (!pdfDoc)` branch fell through to the existing `_viewMode = newMode` and `render()` lines. When pdfDoc is falsy: `_viewMode` still changes, `render()` early-returns at Task 2a2/3a0, container shows old-mode DOM at wrong-mode scroll. Later when pdfDoc returns and re-render fires via cache-hit: `_modeJustToggled=false` → no restore → wrong-mode scroll shown. If user switches tabs before re-render, Task 2a can write old-mode scroll into new-mode slot.

Fix: Changed Task 2f to `if (!AppState.activeFile?.pdfDoc) return;` as a hard early-exit BEFORE `_viewMode = newMode` and `render()`. Toggle click is dropped entirely when pdfDoc unavailable — mode state cannot change while container shows stale DOM. Save and `_modeJustToggled = true` (now unconditional, with existing guards) follow the pdfDoc check.

**R21B-2 — Task 5b still uses stale closure `idx` for `_setActiveHighlight` and `querySelector` after file deletion/reorder (MEDIUM):**
R20A-1 fixed stale file identity but not stale file index. The rAF callback used the closure `idx` (from `setActive(idx)` parameter) for `_setActiveHighlight(idx, 1)` and `.thumb-item[data-file-index="${idx}"]`. If a file is removed or reordered before the rAF runs, `_targetFileId` identity check passes (correct file is still active) but `idx` no longer matches the file's live tab/thumb index — highlighting wrong file or querying nonexistent DOM node.

Fix: Inside rAF (after stale-callback guard passes), compute `const _liveIdx = AppState.files.findIndex(f => f.id === _targetFileId); if (_liveIdx < 0) return;`. Use `_liveIdx` everywhere index-based behavior is needed (`_setActiveHighlight`, `data-file-index` selector).

**R21: 3 bugs found, all fixes applied. Firing Round 22.**

### Round 22 (2026-04-09) — Parallel Oracle review (2 agents)

**R22A-1/R22B-1 — Task 2f pdfDoc guard placed too late; `AppState.viewMode` + UI already mutated before return (CRITICAL — found by both agents):**
In actual code, `AppState.viewMode = newMode` fires at ~L5038 and UI sync (button active state, mode bar) runs at ~L5040–5044 — all BEFORE `PreviewPanelModule._viewMode = newMode` at L5048. The spec's `return` guard was inserted "before L5048", which means AppState and UI already switched to new mode. The preview container still shows old-mode DOM. App state says "sheet", panel shows page — permanently desynchronized until next full reload.

Fix: Moved the hard `if (!AppState.activeFile?.pdfDoc) return;` guard to BEFORE L5038 — before `AppState.viewMode = newMode` and before all UI sync. The scroll save + `_modeJustToggled = true` arm are placed after the pdfDoc guard (between the UI sync lines and `PreviewPanelModule._viewMode = newMode` at L5048). This means: click is fully dropped when pdfDoc null; when pdfDoc truthy, everything proceeds normally.

**R22A-2/R22B-2 — Task 3c-pre guard still allows stale `fileEntry.blankAbsorbedBy` assignment (CRITICAL — found by both agents, R22B verified with actual line numbers):**
R22B read actual app.js: `if (landscapeMode === 'together')` block closes at ~L4098. Shared path begins at ~L4101 with `buildSheetLayout()` at ~L4102 and `fileEntry.blankAbsorbedBy = blankAbsorbedBy` at ~L4103. This per-file state mutation is BEFORE the DOM build loop. A stale same-file async invocation that passes Task 3b2 abort guards can still write stale `blankAbsorbedBy` before hitting Task 3c-pre, corrupting blank-page absorption logic for all subsequent renders of that file — even though DOM stays correct.

Fix: Updated Task 3c-pre to specify exact placement: "after ~L4098 (together-block closes), before ~L4101 (shared path begins) — before `buildSheetLayout()` AND before `fileEntry.blankAbsorbedBy = ...`". One guard covers both together and separate modes (they merge at the shared path before L4101). Updated comment to enumerate all 4 things the guard prevents: blankAbsorbedBy write, DOM append, map updates, fingerprint write.

**R22: 2 bugs found, both fixes applied. Firing Round 23.**

### Round 23 (2026-04-09) — Parallel Oracle review (2 agents)

**R23A: No bugs found.** Confirmed R22 fixes correct. `buildSheetLayout()` reads live `fileEntry` state (not a snapshot), so a stale invocation computing `blankAbsorbedBy` after a state change would compute the NEW state anyway — narrowing the R22A-2/R22B-2 bug's impact but not eliminating it (fingerprint `_fp` was snapshotted at invocation start and could still diverge). No other spec gaps found. Task 2f pdfDoc guard ordering, Task 3c-pre placement, Task 5b live-index recompute all confirmed correct.

**R23B-1 — Task 2f hard gate `!activeFile?.pdfDoc` too broad — drops harmless no-file mode switches (MEDIUM):**
`!AppState.activeFile?.pdfDoc` is true in TWO cases: (1) active file exists but pdfDoc not ready — dangerous, must block; (2) no active file at all — harmless, current code allows it (existing UX: user pre-selects sheet mode before uploading). The spec's guard at "before L5038" drops BOTH cases, regressing the no-file behavior and removing an existing UX feature.

R23B also confirmed: no structural mismatches found in Tasks 2a/2b/3b2/3c-pre/4c/5a/5b. `_targetFile` stale-reference risk in Task 5b is not supported by actual code — file entries are mutated in place, never replaced.

Fix: Narrowed Task 2f guard to `if (AppState.activeFile && !AppState.activeFile.pdfDoc) return;`. The scroll save + flag arm block is wrapped in `if (AppState.activeFile?.pdfDoc)` to safely handle the no-active-file case (nothing to save, no flag to arm).

**R23: 1 bug found (R23B-1), fix applied. Firing Round 24.**

### Round 24 (2026-04-09) — Parallel Oracle review (2 agents)

**R24A-1/R24B-1 — No-file mode preselect not harmless: `PreviewPanelModule._viewMode` desynchronized (MEDIUM — found by both agents):**
Both agents read actual app.js and found: `PreviewPanelModule._viewMode = newMode` and `PreviewPanelModule.render(file)` are inside `if (file)` (~L5046). With no active file, only `AppState.viewMode` and UI change; `_viewMode` stays at its old value (`'page'` by default). When user later uploads/selects a file and `render(file)` is called, the preview renders using stale `_viewMode` → page view under sheet UI, or vice versa. Section 7's previous "no-file toggle is harmless" claim was incorrect.

Fix: Moved `PreviewPanelModule._viewMode = newMode` OUTSIDE `if (file)` — syncs unconditionally whenever mode changes. Task 2f completely rewritten as a two-part replacement of the entire `if (file) { ... }` block, with precise anchors and explicit handling of: (1) no-file case — `_viewMode` syncs, no render; (2) file+pdfDoc case — save old-mode scroll using `_oldMode = newMode==='sheet'?'page':'sheet'`, arm flag, render; (3) file+no-pdfDoc case — unreachable (Part 1 guard blocks it). Added edge case to Section 7.

**R24B-2 — Task 2f Part 2 anchor ambiguous against real code structure (MEDIUM):**
Between L5044 and L5048, real code contains `const file = AppState.activeFile; if (file) {`. Prior spec wording "insert between L5044 and L5048" didn't specify whether Part 2 went before the capture, between capture and `if`, or inside `if`. This ambiguity caused the incorrect analysis that `render(null)` and L5048–5049 always execute on no-file path.

Fix: Rewrote Task 2f to replace the ENTIRE `if (file) { ... }` block with precise new structure, eliminating ambiguity. `_oldMode` variable introduced to correctly reference the mode being LEFT (since `_viewMode` is now already `newMode` at save time). `_sheetContainerInvalid` guard re-scoped to `newMode === 'page'` (leaving sheet mode, where container may be mid-rebuild).

**R24: 2 bugs found (R24A-1/R24B-1, R24B-2), both fixed. Firing Round 25.**

### Round 25 (2026-04-09) — Parallel Oracle review (2 agents)

**R25A-1/R25B-1 — Task 2f still contained stale pre-R24 instruction block after the rewrite (MEDIUM — found by both agents):**
The Round 24 rewrite added the correct two-part structure (Part 1 guard + Part 2 full block replacement), but failed to delete the previous "Then, BEFORE `PreviewPanelModule._viewMode = newMode`… insert the scroll save + flag arm" block (lines 328–350 of the spec at that time). This stale block had conflicting instructions: referenced L5048 as an anchor (already removed by Part 2), used `PreviewPanelModule._viewMode` as the mode key (now wrong — `_viewMode` is already `newMode` at that point), and said "KHÔNG thêm lại" for lines that Part 2 had already replaced. An implementer following both blocks would introduce duplicated save logic and anchor confusion.

Fix: Deleted the stale block entirely (lines 328–350). Task 2f now contains only Part 1 + Part 2 and the explanatory note.

**R25A-2 — §5.2 save table row for toggle handler was stale and contradictory (LOW):**
The table said "trước `_viewMode = newMode` (Task 2e)" — wrong task reference (should be 2f), and wrong timing (save now happens AFTER `_viewMode = newMode`, using `_oldMode`). A future editor reading only the table could "fix" the implementation back to the old order, reintroducing the wrong-slot bug.

Fix: Updated §5.2 toggle row to: "trong `if (file?.pdfDoc)` block (Task 2f)", action "Save `_scrollPos[_oldMode]`, sau khi `_viewMode = newMode` đã sync".

**R25: 2 cleanup issues fixed. Firing Round 26.**

### Round 26 (2026-04-10) — Parallel Oracle review (2 agents)

**R26A-1 — Task 3b2 prose referenced deleted flag `_sheetRenderPending` (LOW — doc only):**
The section header and body still said "Tất cả đều xảy ra SAU khi `_sheetRenderPending` được set" and "cả `_modeJustToggled` lẫn `_sheetRenderPending` sẽ không được clear". This flag was replaced by the `_sheetRenderGen`/`_myGen` token system in Round 16. The code snippets were correct; only the prose was stale.
Fix: Rewrote Task 3b2 prose to reference `_sheetRenderGen` (set in Task 4c via `_myGen`) and clarified ownership-safe clear pattern.

**R26A-2/R26B-2 — Task 2f Part 2 inline comment said "before `_viewMode` changes" — wrong timing (LOW — doc only):**
The comment inside `if (file?.pdfDoc)` read "Save scroll of the OLD mode before `_viewMode` changes (just assigned above…)". But `PreviewPanelModule._viewMode = newMode` is assigned immediately above — `_viewMode` IS already `newMode` at this point. The comment described the old pre-R24 ordering and would confuse an implementer about why `_oldMode` is derived as `newMode==='sheet'?'page':'sheet'`.
Fix: Replaced comment with "\_viewMode is already newMode (synced above); container.scrollTop still reflects old-mode DOM. Save the OLD mode's scroll using `_oldMode` = the mode we just LEFT."

**R26A-3 — §7 edge case table missing "active file + pdfDoc not ready" toggle scenario (LOW — doc only):**
The table had a row for "no file loaded" but not for "file exists, pdfDoc=null". Part 1's hard gate blocks this path entirely (no AppState/UI/`_viewMode` change), but there was no row documenting this behavior for future readers.
Fix: Added row: "Mode toggle với active file nhưng `pdfDoc` chưa ready | Part 1 guard returns before L5038 → no AppState/UI/`_viewMode` change; no save, no render, no flag arm."

**R26B-1 — Task 4b comment claimed `_currentFileId` is already updated — contradicts its own guard (LOW — doc only):**
The Vietnamese comment said "vì tại thời điểm này `_currentFileId` đã === fileEntry.id (bị update ở L3975)" — implying the guard `_prevFileId === fileEntry.id` was working despite `_currentFileId` being equal. This is actually the point: `_prevFileId` is captured BEFORE L3975 precisely because `_currentFileId` updates at L3975. The comment obscured this by mixing the two variables.
Fix: Rewrote comment in English to clearly explain: "`_prevFileId` is captured BEFORE L3975, so it still holds the ID of the file the container was showing. The guard `_prevFileId === fileEntry.id` is true only for same-file state-change rebuilds, not tab-switches."

**R26B-3 — §5.5 `_prevFileId` summary incorrectly grouped L3961 and L3975 as a single update point (LOW — doc only):**
The original text "L3842 (page) / L3961 / L3975 (sheet)" implied both L3961 and L3975 are in the same path. In reality: L3961 is inside the cache-hit block (Task 3a captures there, ends with `return`); L3975 is in the full-rebuild path (Task 4a captures there, independent variable). Grouping them made it seem one capture would serve both paths.
Fix: Split §5.5 into two explicit cases — cache-hit path (L3961, Task 3a) and full-rebuild path (L3975, Task 4a) — with a note that cache-hit's `const _prevFileId` is block-scoped and not available in full-rebuild.

**R26: All 5 issues are doc/comment only (LOW severity). No logic bugs found. Firing Round 27.**

### Round 27 (2026-04-10) — Parallel Oracle review (2 agents)

**R27B: No bugs found.** Confirmed all Round 26 doc fixes correct. Traced all major scenarios (first load, tab-switch, mode toggle, double-toggle race, async abort race). No logic errors, race conditions, or contradictions found. R27A timed out (task expired); R27B clean result accepted.

**R27: No bugs found. Spec rewritten to final-state format (copy-paste-ready code blocks with surrounding context).**

### Round 28 (2026-04-10) — Parallel Oracle review (2 agents) — reviewing final-state format

**R28A-1/R28B-1 — §5.1b module-level flags have NO corresponding Task in §6 (CRITICAL — found by both agents):**
The `_modeJustToggled: false`, `_sheetRenderGen: 0`, and `_nextSheetRenderGen: 0` property declarations are described in §5.1b (Design section) but have no numbered Task in §6 with a copy-paste-ready code block showing surrounding context from the `PreviewPanelModule` object literal. An implementer following only §6 Tasks would never initialize these properties. Consequence: `++this._nextSheetRenderGen` on `undefined` produces `NaN`; `NaN === NaN` is `false` → abort guards never clear the token; `NaN !== 0` is `true` → save guards permanently block all sheet-mode scroll saves. The entire token system breaks.
Fix: Added new Task 1b in §6 with full surrounding context from `PreviewPanelModule` object (~L3742–3748), showing the three new properties after `_sheetFingerprints` and before `init()`. Added critical note about `NaN` consequence.

**R28B-2 — Task 2d unmarked changed comment (LOW — context accuracy):**
Spec line showed `// Scroll to top on first load; restore saved position on returning visits` without `← CHANGED` marker. Actual L3926 reads `// Scroll to top, then render visible pages`. Unmarked context that doesn't exist in code confuses copy-paste implementers.
Fix: Marked as `← CHANGED (was: "Scroll to top, then render visible pages")`.

**R28B-3 — Task 3c unmarked trailing comment on `_sheetFingerprints.set()` (LOW — context accuracy):**
Spec added `// must stay inside the owned commit block (after Task 3c-pre)` to existing line without marking as CHANGED. Actual L4350 has no trailing comment.
Fix: Marked as `← CHANGED (added note: ...)`.

**R28B-4 — Multiple context blocks abbreviate multi-line comments (LOW — context accuracy):**
Tasks 2a, 2c, 4c omit or simplify multi-line comments that exist in actual code. Structurally harmless but reduces copy-paste fidelity.
Fix: Added note at top of §6: "Context lines may abbreviate multi-line comments; match on code structure, not comment text."

**R28: 1 CRITICAL + 3 LOW. All fixes applied. Firing Round 29.**

### Round 29 (2026-04-10) — Review of deletion / reset mid-render invalidation

**R29A/R29B — Delete-mid-render analysis in Task 6 / §7 was incomplete (MEDIUM; one agent also reported a narrower LOW framing):**
Both reviews confirmed the same underlying bug: the spec's row `File bị xóa mid-render | files.find(...) → undefined → no-op ✓` only describes future lookups / event handlers, not the real async sheet full-rebuild commit path. In actual `frontend/app.js`, `UploadModule.removeFile()` calls `PreviewPanelModule.removeFileRoot(removedId)` and, when no files remain, `PreviewPanelModule.clear()` (`app.js:1321–1375`). But `removeFileRoot()` / `clear()` only remove roots, queues, maps, and caches (`app.js:4682–4729`) — they do **not** reset preview ownership state. Meanwhile `_renderSheetView()` uses captured `fileEntry` / `sheetRoot`, not `files.find(...)`; if the active or last file is deleted while a sheet full-rebuild is awaiting, the old `_currentFileId === fileEntry.id` and token can still let the resumed async path reach the shared commit unless the spec explicitly invalidates ownership or re-checks file existence.

**Confirmed failure mode:** last-file deletion while sheet full-rebuild is in flight can still re-add `_sheetFingerprints`, restore stale `scrollTop`, or rebuild detached DOM for a file already removed from `AppState.files`.

Fixes applied:
1. **Task 3c-pre** upgraded from pure ownership guard to **ownership + file-existence guard** using `AppState.files.some(f => f.id === fileEntry.id)` before any shared commit.
2. **Task 6** rewritten from "Cleanup (no code needed)" to **Cleanup / deletion invalidation** with two final-state tasks:
   - **Task 6a:** `PreviewPanelModule.removeFileRoot(fileId)` now clears `_currentFileId`, `_sheetRenderGen`, and `_modeJustToggled` when the removed file currently owns the preview container.
   - **Task 6b:** `PreviewPanelModule.clear()` now resets `_currentFileId`, `_sheetRenderGen`, and `_modeJustToggled` when all files are removed.
3. **§7 row `File bị xóa mid-render`** rewritten to reference Task 6a/6b + Task 3c-pre, not `files.find(...)`.

**R29: deletion invalidation fixes applied. Firing Round 30.**

### Round 30 (2026-04-10) — Review of Round 29 deletion invalidation fixes

**R30A/R30B — No remaining logic bugs in Round 29 fix set. One LOW doc-only issue remains:**
Both Oracle agents confirmed the Round 29 design is logically correct: Task 6a/6b are anchored to the real deletion/reset paths in `frontend/app.js`, and Task 3c-pre is correctly placed before the shared `buildSheetLayout()` / `blankAbsorbedBy` commit path. No remaining race, stale-commit, or ownership bugs were found.

Both agents found the same LOW documentation issue: **§8 Files Changed** was stale. It still said `frontend/app.js | Tasks 1–5, ~50 lines`, but Round 29 converted Task 6 from "no code needed" into real code changes in `PreviewPanelModule.removeFileRoot()` and `clear()`.

Fix applied: updated §8 to `Tasks 1–6, including deletion invalidation in PreviewPanelModule.removeFileRoot() / clear(), ~65 lines`.

**R30: no logic bugs; 1 LOW doc issue fixed. Firing Round 31.**
