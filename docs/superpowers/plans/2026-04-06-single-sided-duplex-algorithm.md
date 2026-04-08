# Single-Sided / Duplex Mixed Printing Algorithm — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the finalized single-sided/duplex mixed printing algorithm so that the SheetView preview matches what the backend actually prints, handling all edge cases (orientation mix, user blanks, drag reorder, batch toggle).

**Architecture:** The algorithm lives in two places: (1) `frontend/app.js` — `buildSheetLayout()` duplex branch, all mutation helpers, and the print request builder; (2) `backend/Services/` — `PrintAlgorithmService.cs` and `WordInteropService.cs` to support blank page markers. The frontend spec is fully defined in `docs/superpowers/specs/2026-04-06-single-sided-duplex-algorithm.md`.

**Tech Stack:** Vanilla JavaScript (ES6, no bundler), C# .NET 8, PdfSharp for PDF manipulation.

---

## File Map

| File | What changes |
|------|--------------|
| `frontend/app.js:34` | `landscapeMode: 'separate'` (was `'together'`) |
| `frontend/app.js:59–69` | `createFileEntry` — add `blankAbsorbedBy: new Map()` |
| `frontend/app.js:116–249` | `buildSheetLayout` — full rewrite of duplex branch |
| `frontend/app.js:~500` | PrintPreviewModule click handler — use `togglePageSelection` |
| `frontend/app.js:~1343` | Range-input handler — cleanup `singleSidedPages` before replacing `selectedPages` |
| `frontend/app.js:~1362` | `PageSelectModule.toggle` — use `togglePageSelection` |
| `frontend/app.js:~1551` | PrintPreviewModule thumbnail click — use `togglePageSelection` |
| `frontend/app.js:~2086` | `_startPrint` — use `buildEffectivePageOrder` |
| `frontend/app.js:~2807–2817` | `DragReorderModule._drop` — call `PreviewPanelModule.render()` after `pageOrder` update |
| `frontend/app.js:~2830` | `DragReorderModule._reRenderGrid` — use render index as Map key |
| `frontend/app.js:~3028–3032` | `PreviewPanelModule.render` — return Promise + reset `blankAbsorbedBy` + coerce `landscapeMode` |
| `frontend/app.js:~3091` | `PreviewPanelModule._renderSheetView` — assign `blankAbsorbedBy` from `buildSheetLayout` return |
| `frontend/app.js:~3186–3200` | X-button handler — race condition lock + stable blank index |
| `frontend/app.js:~3220` | Sheet view card click — use `togglePageSelection` |
| `frontend/app.js:~3596` | `onStateChanged` — route via `this.render()` not `_renderSheetView()` |
| `frontend/app.js` (new) | `togglePageSelection` helper function |
| `frontend/app.js` (new) | `setSingleSided` / `unsetSingleSided` functions |
| `frontend/app.js` (new) | `buildEffectivePageOrder` function |
| `frontend/app.js` (new) | `lookAheadOrientation` helper function |
| `backend/Services/PrintAlgorithmService.cs:522` | `ApplyPageOrder` — keep `0` as blank marker |
| `backend/Services/WordInteropService.cs:677–683` | `CreatePdfSubset` — handle `pageNum == 0` as blank page |

---

## Task 1: `landscapeMode` default + `blankAbsorbedBy` in `createFileEntry`

**Files:**
- Modify: `frontend/app.js:34`
- Modify: `frontend/app.js:59–69`

Two tiny one-line fixes. No tests needed — verified by Task 3 (sheet layout tests).

- [ ] **Step 1: Fix `landscapeMode` default**

  In `AppState` (line 34), change:
  ```javascript
  landscapeMode:         'together', // 'together' | 'separate'
  ```
  to:
  ```javascript
  landscapeMode:         'separate', // 'separate' | 'together' ('together' not backend-supported — see §4.8)
  ```

- [ ] **Step 2: Add `blankAbsorbedBy` to `createFileEntry`**

  In `createFileEntry` (lines 59–69), change:
  ```javascript
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
  ```
  to:
  ```javascript
  createFileEntry(id, name, needsConversion) {
      return {
          id, name, needsConversion,
          pdfDoc:           null,
          totalPageCount:   0,
          selectedPages:    new Set(),
          singleSidedPages: new Set(),
          pageOrder:        [],
          pageRotations:    new Map(),
          blankAbsorbedBy:  new Map(), // populated by buildSheetLayout; reset each render cycle
      };
  },
  ```

- [ ] **Step 3: Commit**
  ```
  git add frontend/app.js
  git commit -m "fix: set landscapeMode default to 'separate'; add blankAbsorbedBy to createFileEntry"
  ```

---

## Task 2: Add helper functions (before `buildSheetLayout`)

**Files:**
- Modify: `frontend/app.js` — insert after the `buildSheetLayout` function or near it (between lines 110–116)

Add all new helper functions as top-level functions (not inside any module, same scope as `buildSheetLayout`).

- [ ] **Step 1: Add `lookAheadOrientation`**

  Insert this function immediately before `buildSheetLayout` (around line 115):
  ```javascript
  // ─── lookAheadOrientation ──────────────────────────────────────────
  // Determine effective orientation for a leading blank page (no group yet).
  // Scans forward past the blank to find the first real page, returns its orientation.
  function lookAheadOrientation(pages, blankIdx, orientationMap) {
      for (let i = blankIdx + 1; i < pages.length; i++) {
          if (pages[i] !== 0) return orientationMap.get(pages[i]) ?? false;
      }
      return false; // fallback: portrait
  }
  ```

- [ ] **Step 2: Add `togglePageSelection`**

  Insert after `lookAheadOrientation`:
  ```javascript
  // ─── togglePageSelection ──────────────────────────────────────────
  // Centralized toggle for page selection. Handles singleSidedPages cleanup (R8).
  // CONTRACT: Caller MUST call rebuildSheetView(entry) after this returns.
  function togglePageSelection(entry, pageNum) {
      if (entry.selectedPages.has(pageNum)) {
          entry.selectedPages.delete(pageNum);
          entry.singleSidedPages.delete(pageNum); // R8: deselect clears SS status
      } else {
          entry.selectedPages.add(pageNum);
          // Do NOT auto-add to singleSidedPages — user must toggle explicitly
      }
  }
  ```

- [ ] **Step 3: Add `setSingleSided` and `unsetSingleSided`**

  Insert after `togglePageSelection`:
  ```javascript
  // ─── setSingleSided ───────────────────────────────────────────────
  // Mark pageNum as single-sided. Invariant 5: blank (0) cannot be SS.
  function setSingleSided(fileEntry, pageNum) {
      if (pageNum === 0) return;
      fileEntry.singleSidedPages.add(pageNum);
      PreviewPanelModule.render(fileEntry); // Invariant 7
  }

  // ─── unsetSingleSided ─────────────────────────────────────────────
  // Remove single-sided status from one or more pages. If a page had absorbed
  // a user blank (R6), removes that blank from pageOrder first (R7).
  // pageNums: array of page numbers to unset.
  function unsetSingleSided(fileEntry, pageNums) {
      // Phase 1: collect blank indices to remove (before splicing anything)
      const blankIndicesToRemove = [];
      for (const pageNum of pageNums) {
          if (fileEntry.blankAbsorbedBy.has(pageNum)) {
              // Forward scan: find blank (0) after pageNum in raw pageOrder,
              // skipping deselected pages that may lie in between.
              const rawIdx = fileEntry.pageOrder.indexOf(pageNum);
              if (rawIdx >= 0) {
                  for (let k = rawIdx + 1; k < fileEntry.pageOrder.length; k++) {
                      const v = fileEntry.pageOrder[k];
                      if (v === 0) {
                          blankIndicesToRemove.push(k);
                          break;
                      }
                      if (fileEntry.selectedPages.has(v)) {
                          break; // selected page encountered — blank not reachable
                      }
                      // deselected page — skip and continue forward
                  }
              }
          }
          fileEntry.singleSidedPages.delete(pageNum);
      }

      // Phase 2: splice in DESCENDING order to avoid index shift (Invariant 4)
      blankIndicesToRemove.sort((a, b) => b - a);
      for (const idx of blankIndicesToRemove) {
          fileEntry.pageOrder.splice(idx, 1);
      }

      PreviewPanelModule.render(fileEntry); // Invariant 7
  }
  ```

- [ ] **Step 4: Add `buildEffectivePageOrder`**

  Insert after `unsetSingleSided`:
  ```javascript
  // ─── buildEffectivePageOrder ──────────────────────────────────────
  // Derive the page order to send to backend — strips absorbed blanks so backend
  // does not double-blank (SS page already gets a system blank from ProcessMixedOrientation).
  // REQUIREMENT: fileEntry.blankAbsorbedBy must be populated (Invariant 7 guarantees this).
  function buildEffectivePageOrder(fileEntry) {
      // Derive pages[] — same filtered view buildSheetLayout uses (deselected pages excluded).
      // MUST use pages[] instead of raw pageOrder to correctly detect absorption adjacency
      // when a deselected page sits between a SS page and its absorbed blank.
      let pages = fileEntry.pageOrder.filter(
          p => p === 0 || fileEntry.selectedPages.has(p)
      );

      // Fallback: if pageOrder is empty but selectedPages is not (rare edge case),
      // derive from selectedPages to avoid mismatch between preview and print.
      if (!pages.length && fileEntry.selectedPages.size > 0) {
          pages = [...fileEntry.selectedPages].sort((a, b) => a - b);
      }

      // Strip blanks that were absorbed by SS pages; keep standalone blanks.
      return pages.filter((p, i) => {
          if (p !== 0) return true;          // non-blank: always keep
          const prevPage = pages[i - 1];     // predecessor in filtered view
          return !fileEntry.blankAbsorbedBy.has(prevPage); // strip if absorbed
      });
  }
  ```

- [ ] **Step 5: Verify no syntax errors**

  Open the browser console (or run a quick lint):
  ```powershell
  node -e "require('fs').readFileSync('frontend/app.js','utf8')" 2>&1
  ```
  Expected: no output (no syntax errors). If there are errors, fix before continuing.

- [ ] **Step 6: Commit**
  ```
  git add frontend/app.js
  git commit -m "feat: add lookAheadOrientation, togglePageSelection, setSingleSided, unsetSingleSided, buildEffectivePageOrder helpers"
  ```

---

## Task 3: Rewrite `buildSheetLayout` duplex branch

**Files:**
- Modify: `frontend/app.js:153–248` (the `else { // duplex }` block)

This is the core algorithm. Replace the entire existing duplex branch (from `} else {` on line 153 through `}` on line 247) with the new implementation. Keep the simplex and booklet branches untouched. Keep the `return sheets` on line 248.

- [ ] **Step 1: Replace the duplex branch**

  The section to replace starts at line 153 (`} else {`) and ends at line 246 (the `}` closing the else). The new duplex content (replacing lines 153–247, but keeping `return { sheets, blankAbsorbedBy }` pattern in mind — see note below):

  **Important:** The old function returns `sheets`. The new duplex branch must still fit inside `buildSheetLayout` and the function will now return `{ sheets, blankAbsorbedBy }` from the duplex branch. Add a wrapper so the overall function always returns the right shape. See Step 2.

  Replace the duplex branch:
  ```javascript
      } else {
          // ── Duplex: spec §4.1 algorithm ──────────────────────────────────
          // Invariant 8: landscapeMode must be 'separate' (coerced upstream in render())
          const blankAbsorbedBy = new Map(); // Map<absorbingPageNum, 0>

          // ── Bước 1: Group pages by orientation ──────────────────────────
          const groups = [];
          let currentGroup = { isLandscape: null, pages: [] };

          for (let gi = 0; gi < pages.length; gi++) {
              const p = pages[gi];
              let effectiveOrientation;
              if (p === 0) {
                  // Blank inherits orientation of current group;
                  // if no group started yet, look ahead to first real page.
                  effectiveOrientation = currentGroup.isLandscape !== null
                      ? currentGroup.isLandscape
                      : lookAheadOrientation(pages, gi, orientationMap ?? new Map());
              } else {
                  effectiveOrientation = orientationMap ? (orientationMap.get(p) ?? false) : false;
              }

              if (currentGroup.isLandscape === null) {
                  currentGroup.isLandscape = effectiveOrientation;
              }
              if (effectiveOrientation !== currentGroup.isLandscape) {
                  groups.push(currentGroup);
                  currentGroup = { isLandscape: effectiveOrientation, pages: [] };
              }
              currentGroup.pages.push({ pageNum: p });
          }
          groups.push(currentGroup);

          // ── Bước 2: Process each group, handle single-sided + blank absorption ──
          const logicalPages = []; // { pageNum: N|null|0, isLandscape: bool }

          for (const group of groups) {
              const groupLogical = [];
              let i = 0;
              while (i < group.pages.length) {
                  const entry   = group.pages[i];
                  const p       = entry.pageNum;
                  const next    = group.pages[i + 1]?.pageNum; // undefined if last

                  if (fileEntry.singleSidedPages.has(p)) {
                      // R2: close current sheet if in odd position
                      if (groupLogical.length % 2 === 1) {
                          groupLogical.push({ pageNum: null, isLandscape: group.isLandscape });
                      }
                      groupLogical.push({ pageNum: p, isLandscape: group.isLandscape });

                      if (next === 0) {
                          // R6: absorb the blank immediately after as back of SS sheet
                          groupLogical.push({ pageNum: 0, isLandscape: group.isLandscape });
                          blankAbsorbedBy.set(p, 0);
                          i += 2; // skip the blank
                      } else {
                          // No blank → auto-blank back
                          groupLogical.push({ pageNum: null, isLandscape: group.isLandscape });
                          i += 1;
                      }
                  } else {
                      groupLogical.push({ pageNum: p, isLandscape: group.isLandscape });
                      i += 1;
                  }
              }

              // R4: pad each orientation group to even count independently
              if (groupLogical.length % 2 === 1) {
                  groupLogical.push({ pageNum: null, isLandscape: group.isLandscape });
              }
              logicalPages.push(...groupLogical);
          }

          // ── Bước 3: Pair logical pages into sheets ────────────────────
          let sheetIdx = 1;
          for (let j = 0; j < logicalPages.length; j += 2) {
              const f = logicalPages[j];
              const b = logicalPages[j + 1];

              if (!b) {
                  // Should never happen — Bước 2 ensures even count per group
                  console.error(`[buildSheetLayout] BUG: odd logicalPages at j=${j}. Bước 2 padding failed.`);
                  break;
              }

              const isSingleForced = f.pageNum !== null
                  && f.pageNum !== 0
                  && (b.pageNum === null || b.pageNum === 0)
                  && fileEntry.singleSidedPages.has(f.pageNum);

              sheets.push({
                  sheetIndex:      sheetIdx++,
                  front:           f.pageNum,
                  back:            b.pageNum,
                  isLandscape:     f.isLandscape,
                  isSingleForced:  isSingleForced,
                  backIsUserBlank: b.pageNum === 0,
              });
          }

          return { sheets, blankAbsorbedBy };
      }
  ```

- [ ] **Step 2: Update `buildSheetLayout` return and callers**

  The old function returned `sheets`. The duplex branch now returns `{ sheets, blankAbsorbedBy }`. We need:
  1. The simplex/booklet branches to return `{ sheets, blankAbsorbedBy: new Map() }` as well, OR
  2. Wrap the whole function to normalize the return shape.

  **Chosen approach: wrap at the end.** Change the last line of `buildSheetLayout`:

  Old:
  ```javascript
      return sheets;
  }
  ```

  New — restructure so simplex/booklet also return `{ sheets, blankAbsorbedBy }`:

  At the top of `buildSheetLayout` (after `const sheets = []`), add:
  ```javascript
  let blankAbsorbedBy = new Map(); // will be replaced by duplex branch
  ```

  Then in the simplex and booklet branches, they don't touch `blankAbsorbedBy`.
  In the duplex branch: remove `const blankAbsorbedBy = new Map()` from the top of duplex (now declared above). Keep `return { sheets, blankAbsorbedBy }` at the end of the duplex `else` block.

  Change the final `return sheets` line of the function to:
  ```javascript
      return { sheets, blankAbsorbedBy };
  }
  ```

  This makes all three branches return `{ sheets, blankAbsorbedBy }`.

- [ ] **Step 3: Update `_renderSheetView` to consume new return shape**

  In `_renderSheetView` (line 3136), change:
  ```javascript
  const sheets = buildSheetLayout(fileEntry, printMode, orientationMap, AppState.landscapeMode);
  ```
  to:
  ```javascript
  const { sheets, blankAbsorbedBy } = buildSheetLayout(fileEntry, printMode, orientationMap, AppState.landscapeMode);
  fileEntry.blankAbsorbedBy = blankAbsorbedBy; // Invariant 9
  ```

- [ ] **Step 4: Verify in browser**

  Open the app in browser. Load a PDF. Switch to Sheet View. Verify:
  - Pages appear correctly grouped in sheets
  - No console errors about "odd logicalPages"
  - Single-sided pages (if any) show correct layout

- [ ] **Step 5: Commit**
  ```
  git add frontend/app.js
  git commit -m "feat: rewrite buildSheetLayout duplex branch per spec §4.1 algorithm"
  ```

---

## Task 4: Update `PreviewPanelModule.render` — return Promise + reset `blankAbsorbedBy` + coerce `landscapeMode`

**Files:**
- Modify: `frontend/app.js:3028–3032`

- [ ] **Step 1: Replace `render` method**

  Current (lines 3028–3032):
  ```javascript
  render(fileEntry) {
      if (this._viewMode === 'sheet') {
          this._renderSheetView(fileEntry);
          return;
      }
  ```

  Replace with:
  ```javascript
  render(fileEntry) {
      // Invariant 3: reset blankAbsorbedBy before each rebuild
      if (fileEntry) fileEntry.blankAbsorbedBy = new Map();

      // Invariant 8: coerce unsupported landscapeMode at entry
      if (fileEntry && fileEntry.landscapeMode === 'together') {
          console.warn('[render] landscapeMode="together" unsupported — forcing "separate"');
          fileEntry.landscapeMode = 'separate';
      }

      if (this._viewMode === 'sheet') {
          return this._renderSheetView(fileEntry); // Invariant 10: return Promise
      }
  ```

- [ ] **Step 2: Verify `onStateChanged` routes through `render`**

  In `onStateChanged` (line 3596), current:
  ```javascript
  this._renderSheetView(AppState.activeFile);
  ```

  Change to:
  ```javascript
  this.render(AppState.activeFile);
  ```

  This ensures the `blankAbsorbedBy` reset and `landscapeMode` coerce always run.

- [ ] **Step 3: Commit**
  ```
  git add frontend/app.js
  git commit -m "fix: render() resets blankAbsorbedBy, coerces landscapeMode, returns Promise; onStateChanged routes via render()"
  ```

---

## Task 5: X-button race condition fix

**Files:**
- Modify: `frontend/app.js:3141–3200` (X-button creation inside `_renderSheetView`)

- [ ] **Step 1: Add `_isDeleting` flag to `PreviewPanelModule`**

  Find the `PreviewPanelModule` object definition (around line 2900). Add:
  ```javascript
  _isDeleting: false,
  ```
  to its property list.

- [ ] **Step 2: Replace X-button handler in `_renderSheetView`**

  Find the section (lines ~3185–3201):
  ```javascript
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
  ```

  Replace with:
  ```javascript
  const renderPos = blankQueuePos++;
  const xBtn = document.createElement('button');
  xBtn.className = 'blank-delete-btn';
  xBtn.textContent = '×';
  xBtn.title = 'Xóa trang trắng';
  // Store stable render position on the DOM node (survives closure)
  blank.dataset.blankRenderPos = renderPos;
  xBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      e.preventDefault();
      if (PreviewPanelModule._isDeleting) return; // drop second click
      PreviewPanelModule._isDeleting = true;

      const entry = AppState.files.find(f => f.id === fileEntry.id);
      if (!entry) {
          PreviewPanelModule._isDeleting = false;
          return;
      }

      // Re-derive index at click time — avoid stale closure
      const rPos = parseInt(blank.dataset.blankRenderPos);
      const allBlanks = entry.pageOrder
          .map((p, i) => p === 0 ? i : -1)
          .filter(i => i >= 0);
      const currentIdx = allBlanks[rPos];

      try {
          if (currentIdx >= 0 && entry.pageOrder[currentIdx] === 0) {
              entry.pageOrder.splice(currentIdx, 1);
              const result = PreviewPanelModule.render(entry);
              if (result && typeof result.finally === 'function') {
                  result.finally(() => { PreviewPanelModule._isDeleting = false; });
              } else {
                  PreviewPanelModule._isDeleting = false;
              }
          } else {
              PreviewPanelModule._isDeleting = false;
          }
      } catch (ex) {
          PreviewPanelModule._isDeleting = false;
          throw ex;
      }
  });
  ```

  Also update line 3141 — old code used `blankIndexQueue[blankQueuePos++]`, now `blankQueuePos` is incremented inside the new block. Remove the old `const blankIdx = blankIndexQueue[blankQueuePos++] ?? -1;` line (it no longer exists).

  Note: `blankIndexQueue` (line 3139–3140) can be removed as well — we no longer use a pre-built queue of raw indices. Instead we re-scan at click time. Remove these two lines:
  ```javascript
  const blankIndexQueue = [];
  fileEntry.pageOrder.forEach((p, idx) => { if (p === 0) blankIndexQueue.push(idx); });
  ```

- [ ] **Step 3: Verify double-click protection**

  In browser: add two blank pages, go to SheetView, rapidly double-click the X button. Verify only one blank is deleted and no console error occurs.

- [ ] **Step 4: Commit**
  ```
  git add frontend/app.js
  git commit -m "fix: X-button race condition — add _isDeleting lock and re-derive blank index at click time"
  ```

---

## Task 6: Drag reorder — `_reRenderGrid` Map key + SheetView rebuild

**Files:**
- Modify: `frontend/app.js:2826–2836` (`_reRenderGrid`)
- Modify: `frontend/app.js:2807–2817` (`_drop`)

- [ ] **Step 1: Fix `_reRenderGrid` Map key collision**

  Current (lines 2830–2836):
  ```javascript
  const byPage = new Map(thumbs.map(t => [parseInt(t.dataset.pageNumber), t]));
  // Reorder DOM
  order.forEach(pageNum => {
      const t = byPage.get(pageNum);
      if (t) grid.appendChild(t);
  });
  ```

  Replace with:
  ```javascript
  // Use render index as key — pageNum 0 (blank) may appear multiple times
  const byRenderIdx = new Map(thumbs.map((t, i) => [i, t]));
  // Reorder DOM matching `order` array positions
  order.forEach((pageNum, orderIdx) => {
      // Find the thumb that was at position orderIdx before reorder
      // We need to match by pageNum stored on the element, handling duplicates
      // by consuming each matched element only once.
      const t = thumbs.find(el => parseInt(el.dataset.pageNumber) === pageNum && !el._used);
      if (t) {
          t._used = true;
          grid.appendChild(t);
      }
  });
  // Cleanup _used flag
  thumbs.forEach(t => delete t._used);
  ```

- [ ] **Step 2: Add SheetView rebuild after drop**

  In `_drop` (after line 2812 `this._reRenderGrid(newOrder)`), add:
  ```javascript
  // Rebuild SheetView if active — pageOrder changed (Invariant 7)
  if (AppState.viewMode === 'sheet' && AppState.activeFile) {
      PreviewPanelModule.render(AppState.activeFile);
  }
  ```

- [ ] **Step 3: Verify**

  In browser: load PDF with multiple pages including a blank (add blank from context menu). Drag reorder pages. Verify SheetView updates live and no console errors.

- [ ] **Step 4: Commit**
  ```
  git add frontend/app.js
  git commit -m "fix: _reRenderGrid uses render-index key to avoid blank Map collision; _drop rebuilds SheetView after reorder"
  ```

---

## Task 7: Use `togglePageSelection` at all 6 deselect paths

**Files:**
- Modify: `frontend/app.js` at lines ~500, ~1343, ~1362, ~1551, ~3063, ~3220

The goal: wherever the code currently does `entry.selectedPages.delete(pageNum)` or `AppState.selectedPages.delete(pageNum)` for a page toggle, replace with `togglePageSelection(entry, pageNum)` so `singleSidedPages` cleanup always happens (R8).

- [ ] **Step 1: Path 1 — PrintPreviewModule thumbnail click (line ~500)**

  Current:
  ```javascript
  if (AppState.selectedPages.has(pageNum)) AppState.selectedPages.delete(pageNum);
  else AppState.selectedPages.add(pageNum);
  this._syncAll();
  PrintModule.updateButton();
  ```

  Replace toggle lines with:
  ```javascript
  togglePageSelection(AppState.activeFile, pageNum);
  this._syncAll();
  PrintModule.updateButton();
  ```

- [ ] **Step 2: Path 2 — `PageSelectModule.toggle` (line ~1362)**

  Current:
  ```javascript
  if (AppState.selectedPages.has(pageNum)) AppState.selectedPages.delete(pageNum);
  else AppState.selectedPages.add(pageNum);
  ```

  Replace with:
  ```javascript
  togglePageSelection(AppState.activeFile, pageNum);
  ```

- [ ] **Step 3: Path 3 — Range input handler (line ~1343)**

  The range input replaces `selectedPages` wholesale. After it sets `AppState.selectedPages = parsed`, pages that were SS but are no longer selected need cleanup. Add singleSidedPages cleanup before the assignment:

  Current:
  ```javascript
  AppState.selectedPages = parsed;
  ```

  Replace with:
  ```javascript
  // R8 cleanup: remove SS status for pages no longer selected
  if (AppState.activeFile) {
      for (const p of AppState.activeFile.singleSidedPages) {
          if (!parsed.has(p)) AppState.activeFile.singleSidedPages.delete(p);
      }
  }
  AppState.selectedPages = parsed;
  ```

- [ ] **Step 4: Path 4 — PrintPreviewModule zoom-modal thumbnail click (line ~1551)**

  Current:
  ```javascript
  if (AppState.selectedPages.has(n)) AppState.selectedPages.delete(n);
  else AppState.selectedPages.add(n);
  ```

  Replace with:
  ```javascript
  togglePageSelection(AppState.activeFile, n);
  ```

- [ ] **Step 5: Path 5 — `PreviewPanelModule._renderSheetView` page card click (line ~3063)**

  Current:
  ```javascript
  if (entry.selectedPages.has(pageNum)) entry.selectedPages.delete(pageNum);
  else entry.selectedPages.add(pageNum);
  ```

  Replace with:
  ```javascript
  togglePageSelection(entry, pageNum);
  ```

- [ ] **Step 6: Path 6 — Sheet view page card click (line ~3220)**

  Current:
  ```javascript
  if (entry.selectedPages.has(pn)) entry.selectedPages.delete(pn);
  else entry.selectedPages.add(pn);
  ```

  Replace with:
  ```javascript
  togglePageSelection(entry, pn);
  ```

- [ ] **Step 7: Verify deselect clears singleSidedPages**

  In browser: mark page 2 as single-sided. Deselect page 2 (click on it). Re-select page 2. Verify page 2 is NOT single-sided after re-select (no SS badge).

- [ ] **Step 8: Commit**
  ```
  git add frontend/app.js
  git commit -m "fix: use togglePageSelection at all 6 deselect paths for R8 singleSidedPages cleanup"
  ```

---

## Task 8: Update `_startPrint` to use `buildEffectivePageOrder`

**Files:**
- Modify: `frontend/app.js:2086`

- [ ] **Step 1: Replace raw `pageOrder` with `buildEffectivePageOrder`**

  Current (line 2086):
  ```javascript
  pageOrder:        file.pageOrder.length > 0 ? file.pageOrder : null,
  ```

  Replace with:
  ```javascript
  pageOrder:        (() => {
      const effective = buildEffectivePageOrder(file);
      return effective.length > 0 ? effective : null;
  })(),
  ```

- [ ] **Step 2: Verify correct data sent to backend**

  In browser: load PDF, mark page 2 as single-sided, add a blank after page 2 (from context menu). In Sheet View, verify the blank is shown as absorbed (back of sheet 2). Then initiate print and check browser DevTools Network tab → POST /api/print → body `pageOrder`. Verify the absorbed blank (0) is NOT in `pageOrder`, but standalone blanks ARE.

- [ ] **Step 3: Commit**
  ```
  git add frontend/app.js
  git commit -m "fix: _startPrint uses buildEffectivePageOrder to strip absorbed blanks before sending to backend"
  ```

---

## Task 9: Backend — `ApplyPageOrder` keep blank marker (C2a)

**Files:**
- Modify: `backend/Services/PrintAlgorithmService.cs:522`

- [ ] **Step 1: Update `ApplyPageOrder` to keep `0`**

  Current (line 522):
  ```csharp
  var reordered = pageOrder.Where(p => selectedSet.Contains(p)).ToList();
  ```

  Replace with:
  ```csharp
  // Keep 0 as a blank page marker in addition to selected pages
  var reordered = pageOrder.Where(p => p == 0 || selectedSet.Contains(p)).ToList();
  ```

- [ ] **Step 2: Verify existing tests still pass**

  ```powershell
  cd backend
  dotnet test
  ```
  Expected: all tests pass (or same failures as before this change).

- [ ] **Step 3: Commit**
  ```
  git add backend/Services/PrintAlgorithmService.cs
  git commit -m "fix: ApplyPageOrder keeps 0 as blank marker (C2a)"
  ```

---

## Task 10: Backend — `CreatePdfSubset` handle blank page marker (C2b)

**Files:**
- Modify: `backend/Services/WordInteropService.cs:677–683`

- [ ] **Step 1: Update `CreatePdfSubset` to handle `pageNum == 0`**

  Current (lines 677–684):
  ```csharp
  foreach (var pageNum in pageNumbers)
  {
      if (pageNum >= 1 && pageNum <= sourceDoc.PageCount)
      {
          Console.WriteLine($"[CreatePdfSubset] Adding page {pageNum} (Index {pageNum - 1}) to subset");
          targetDoc.AddPage(sourceDoc.Pages[pageNum - 1]);
      }
  }
  ```

  Replace with:
  ```csharp
  foreach (var pageNum in pageNumbers)
  {
      if (pageNum == 0)
      {
          // User-inserted blank page marker — add blank page with orientation from prev page
          PdfPage? template = targetDoc.PageCount > 0
              ? targetDoc.Pages[targetDoc.PageCount - 1]
              : null;

          if (template != null)
          {
              bool isLandscape = template.Width.Point > template.Height.Point;
              CreateNonSkippableBlankPage(targetDoc, template, isLandscape);
              Console.WriteLine($"[CreatePdfSubset] Added blank page (orientation from previous page, isLandscape={isLandscape})");
          }
          else
          {
              // Blank is first page — no template; create A4 portrait blank manually
              var blankPage = targetDoc.AddPage();
              blankPage.Width  = XUnit.FromPoint(595.28);
              blankPage.Height = XUnit.FromPoint(841.89);
              using var gfx = XGraphics.FromPdfPage(blankPage);
              gfx.DrawRectangle(XBrushes.White, 0, 0, 0.01, 0.01); // non-skippable
              Console.WriteLine($"[CreatePdfSubset] Added blank page (first page, no template — A4 portrait)");
          }
      }
      else if (pageNum >= 1 && pageNum <= sourceDoc.PageCount)
      {
          Console.WriteLine($"[CreatePdfSubset] Adding page {pageNum} (Index {pageNum - 1}) to subset");
          targetDoc.AddPage(sourceDoc.Pages[pageNum - 1]);
      }
      // else: pageNum out of range — skip (existing behavior)
  }
  ```

  **Note:** `CreateNonSkippableBlankPage` already exists in `WordInteropService.cs` at ~line 24. Use it as-is; its exact signature can be found in that file.

- [ ] **Step 2: Verify `CreateNonSkippableBlankPage` signature**

  Search for the method in `WordInteropService.cs`:
  ```powershell
  Select-String -Path "backend\Services\WordInteropService.cs" -Pattern "CreateNonSkippableBlankPage"
  ```
  Confirm the signature matches what we're calling: `CreateNonSkippableBlankPage(targetDoc, template, isLandscape)`.

- [ ] **Step 3: Build**

  ```powershell
  cd backend
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**
  ```
  git add backend/Services/WordInteropService.cs
  git commit -m "fix: CreatePdfSubset handles pageNum==0 as blank page marker (C2b)"
  ```

---

## Task 11: Wire up `setSingleSided` / `unsetSingleSided` to existing toggle UI

**Files:**
- Modify: `frontend/app.js` — wherever the context menu / right-click actions toggle single-sided

- [ ] **Step 1: Find current single-sided toggle code**

  Search for where `singleSidedPages.add` and `singleSidedPages.delete` are called (outside of the new helper functions):
  ```powershell
  Select-String -Path "frontend\app.js" -Pattern "singleSidedPages\.(add|delete)"
  ```

- [ ] **Step 2: Replace with `setSingleSided` / `unsetSingleSided`**

  For each occurrence outside of the new helpers:
  - `singleSidedPages.add(pageNum)` + any following `rebuildSheetView` or `render()` → replace with `setSingleSided(fileEntry, pageNum)`
  - `singleSidedPages.delete(pageNum)` + any following `rebuildSheetView` or `render()` → replace with `unsetSingleSided(fileEntry, [pageNum])`

  If the existing code calls `PreviewPanelModule.render()` after the add/delete, remove the duplicate call (the helpers already call it).

- [ ] **Step 3: Verify single-sided toggle in browser**

  In browser: right-click a page → "In 1 mặt". Verify:
  - Sheet view shows page on its own sheet with blank back
  - Right-click same page → "Bỏ in 1 mặt". Verify page returns to duplex pairing
  - If a blank was absorbed: verify blank is removed from pageOrder when unsetting SS

- [ ] **Step 4: Commit**
  ```
  git add frontend/app.js
  git commit -m "feat: wire setSingleSided/unsetSingleSided to context menu SS toggle"
  ```

---

## Task 12: End-to-end verification

- [ ] **Step 1: Run backend build + tests**
  ```powershell
  cd backend
  dotnet build
  dotnet test
  ```
  Expected: build OK, tests pass (or same pre-existing failures).

- [ ] **Step 2: Verify E2E-1 — Basic SS, no blanks**

  Load a 4-page PDF. Mark page 2 as single-sided. Switch to Sheet View. Verify:
  - Sheet 1: pages [1 | blank]
  - Sheet 2: pages [2 | blank] (forced single)
  - Sheet 3: pages [3 | 4]

- [ ] **Step 3: Verify E2E-2 — SS with absorbed user blank**

  Load 3-page PDF. Mark page 2 as single-sided. Add a blank page after page 2 (context menu → "Thêm trang trắng sau"). Switch to Sheet View. Verify:
  - Sheet 1: pages [1 | blank]
  - Sheet 2: pages [2 | user-blank] (absorbed, blue border)
  - Sheet 3: pages [3 | blank]
  
  Check Network tab: `pageOrder` sent to backend does NOT include `0` for the absorbed blank.

- [ ] **Step 4: Verify E2E-3 — Landscape mix**

  Load PDF with portrait + landscape pages (or rotate a page). Verify landscape and portrait pages do not share a sheet.

- [ ] **Step 5: Verify E2E-5 — Consecutive SS pages**

  Mark pages 2 and 3 as single-sided. Verify:
  - Sheet 1: [1 | blank]
  - Sheet 2: [2 | blank]
  - Sheet 3: [3 | blank]
  - Sheet 4: [4 | blank]

- [ ] **Step 6: Verify drag reorder**

  Drag a page to a new position while in Sheet View. Verify Sheet View updates immediately without errors.

- [ ] **Step 7: Verify deselect + re-select clears SS**

  Mark page 2 as SS. Deselect page 2. Re-select page 2. Verify: page 2 is NOT SS after re-select (no SS badge, pairs normally in duplex).

- [ ] **Step 8: Final commit**
  ```
  git add -A
  git commit -m "chore: complete single-sided/duplex mixed printing algorithm implementation"
  ```

---

## Appendix: Key Invariants (for implementer reference)

| # | Invariant | Where enforced |
|---|-----------|----------------|
| 3 | `blankAbsorbedBy` reset at start of each `rebuildSheetView` | Task 4: `render()` |
| 4 | Blank splice order DESCENDING | Task 2: `unsetSingleSided` |
| 5 | `0` never in `singleSidedPages` | Task 2: `setSingleSided` |
| 7 | All mutations call `rebuildSheetView` | Tasks 2, 6, 7, 11 |
| 8 | `landscapeMode` coerced to `'separate'` at entry | Task 4: `render()` |
| 9 | `buildSheetLayout` duplex returns `{ sheets, blankAbsorbedBy }` | Task 3 |
| 10 | `render()` returns Promise in sheet mode | Task 4 |
| 11 | No duplicate non-zero pageNums in `pageOrder` | (not enforced — Invariant 11 is doc-only) |

## Appendix: Spec Reference

Full algorithm spec: `docs/superpowers/specs/2026-04-06-single-sided-duplex-algorithm.md`

Status: **Final — Round 7 verified, all 10 test cases PASS, ready for implementation.**
