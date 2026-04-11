# Implementation Plan: Booklet Landscape Mode
**Date:** 2026-04-11
**Spec:** `docs/superpowers/specs/2026-04-11-booklet-landscape-mode.md` (Round 5 reviewed, CLEAN)
**Scope:** `frontend/app.js` only — 2 changes, 1 file
**Plan status:** Round 8 reviewed (Oracle — 8A CLEAN, 8B 4 issues, all fixed)

---

## Overview

Add `landscapeMode` (`together` / `separate`) support to booklet print mode. Preview already works (spec §2.2). Two print functions need changes:

1. `_startPrint()` — guard `duplexSide` + request-local `pageRotationsForPrint` fallback
2. `_resumePrintQueue()` — same two fixes, using captured `mode` from `modeCode`

No backend changes. No HTML changes.

---

## Pre-Implementation Checklist

- [ ] Read spec §5.1 and §5.2 fully before touching code
- [ ] Run `gitnexus_impact({target: "_startPrint", direction: "upstream"})` — if HIGH/CRITICAL risk, **stop and warn user before proceeding**
- [ ] Run `gitnexus_impact({target: "_resumePrintQueue", direction: "upstream"})` — if HIGH/CRITICAL risk, **stop and warn user before proceeding**

---

## Task 1 — `_startPrint()`: guard `duplexSide` + request-local fallback

**File:** `frontend/app.js`
**Location:** ~line 2561–2598

### 1a. Guard `duplexSide` (code clarity fix — REQUIRED)

**Current code (lines 2561–2579):**
```javascript
// Compute duplexSide for together mode (spec §5.8)
let duplexSide = null;  // default: null = no override → backend uses printer default
// ('LongEdge' is NEVER sent explicitly — null preserves existing behavior for all non-together cases)
if (file.landscapeMode === 'together' && file._originalOrientationMap != null) {
    let allLandscape = true;
    // Check only pages actually being printed (Bug B8 fix):
    // previously looped 1..totalPageCount, so unselected portrait pages in a mixed
    // doc would keep allLandscape=false even when only landscape pages are selected.
    const pagesToCheck = (sel.size > 0 && sel.size < file.totalPageCount)
        ? [...sel]                                                        // partial selection → check only selected
        : Array.from({ length: file.totalPageCount }, (_, i) => i + 1); // all selected → check all
    for (const p of pagesToCheck) {
        if (file._originalOrientationMap.get(p) !== true) {
            allLandscape = false;
            break;
        }
    }
    if (allLandscape) duplexSide = 'ShortEdge';
}
```

**Replace `if` condition** — add `mode !== 'booklet'` guard:
```javascript
if (mode !== 'booklet'
    && file.landscapeMode === 'together'
    && file._originalOrientationMap != null) {
```

Everything inside the `if` block (allLandscape check, pagesToCheck, loop, `duplexSide = 'ShortEdge'`) stays **unchanged**.

### 1b. Add request-local `pageRotationsForPrint` fallback

**Insert after the closing `}` of the duplexSide block (after line 2579), before `const body = {`:**

```javascript
// Build request-local pageRotations — fallback for booklet+together when
// _originalOrientationMap is null (page view, or sheet view before first-render commit).
// MUST NOT mutate file.pageRotations — request-local only.
let pageRotationsForPrint = new Map(file.pageRotations);
if (mode === 'booklet'
    && file.landscapeMode === 'together'
    && file._originalOrientationMap == null
    && file.pdfDoc != null) {
    // Snapshot pdfDoc reference before first await — rest of UI can mutate live
    // file.* while the async loop yields to the event loop.
    const pdfDoc = file.pdfDoc;
    // Only check pages actually being printed — mirrors duplexSide pagesToCheck logic.
    const pagesToPrint = (sel.size > 0 && sel.size < file.totalPageCount)
        ? [...sel]
        : Array.from({ length: file.totalPageCount }, (_, idx) => idx + 1);
    // Sequential loop (not Promise.all) — avoids firing all pdfDoc.getPage() at once
    // on large documents; simpler and safer in a print path.
    for (const p of pagesToPrint) {
        try {
            const page = await pdfDoc.getPage(p);
            const vp = page.getViewport({ scale: 1, rotation: 0 });
            if (vp.width > vp.height && !pageRotationsForPrint.has(p)) {
                pageRotationsForPrint.set(p, 'CCW90');
            }
        } catch (err) {
            // getPage failed — treat as portrait (no CCW90 injected for this page).
            // Log so the failure is diagnosable; do not abort the whole print.
            console.warn(`[booklet-together] getPage(${p}) failed, skipping rotation:`, err);
        }
    }
}
```

### 1c. Update `pageRotations` in request body

**Current (line 2593–2595):**
```javascript
pageRotations:    file.pageRotations.size > 0
    ? Array.from(file.pageRotations.entries()).map(([pageNumber, rotation]) => ({ pageNumber, rotation }))
    : null,
```

**Replace with:**
```javascript
pageRotations:    pageRotationsForPrint.size > 0
    ? Array.from(pageRotationsForPrint.entries()).map(([pageNumber, rotation]) => ({ pageNumber, rotation }))
    : null,
```

Only the variable name changes (`file.pageRotations` → `pageRotationsForPrint`). The serialization logic is identical.

### 1d. No code change: `manualFlipDir` remains correct

`manualFlipDir: duplexSide` (line 2597) is already correct — when `mode === 'booklet'`, `duplexSide` is `null` (guarded in 1a), so `manualFlipDir` will also be `null`. Backend ignores both fields for booklet. No code change needed. ✓

---

## Task 2 — `_resumePrintQueue()`: same guard + same fallback

**File:** `frontend/app.js`
**Location:** ~line 2772–2801

### 2a. Guard `duplexSide`

**Current code (lines 2772–2782):**
```javascript
let duplexSide = null;
if (file.landscapeMode === 'together' && file._originalOrientationMap != null) {
    let allLandscape = true;
    const pagesToCheck = (sel.size > 0 && sel.size < file.totalPageCount)
        ? [...sel]
        : Array.from({ length: file.totalPageCount }, (_, k) => k + 1);
    for (const p of pagesToCheck) {
        if (file._originalOrientationMap.get(p) !== true) { allLandscape = false; break; }
    }
    if (allLandscape) duplexSide = 'ShortEdge';
}
```

**Replace `if` condition** — add guard (same as Task 1a):
```javascript
if (mode !== 'booklet'
    && file.landscapeMode === 'together'
    && file._originalOrientationMap != null) {
```

**Note:** `_resumePrintQueue` already has `const mode = modeCode === 1 ? 'booklet' : 'duplex'` at line 2755. Use that captured `mode` — do NOT read `AppState.printMode` (user may have changed UI mode while paused for manual flip).

### 2b. Add request-local `pageRotationsForPrint` fallback

**Insert after closing `}` of duplexSide block (after line 2782), before `const body = {` (line 2784):**

```javascript
// Request-local pageRotations fallback — same pattern as _startPrint.
// Uses captured `mode` (from modeCode, line 2755), NOT AppState.printMode.
let pageRotationsForPrint = new Map(file.pageRotations);
if (mode === 'booklet'
    && file.landscapeMode === 'together'
    && file._originalOrientationMap == null
    && file.pdfDoc != null) {
    // Snapshot pdfDoc reference before first await.
    const pdfDoc = file.pdfDoc;
    // Only check pages actually being printed — mirrors duplexSide pagesToCheck logic.
    const pagesToPrint = (sel.size > 0 && sel.size < file.totalPageCount)
        ? [...sel]
        : Array.from({ length: file.totalPageCount }, (_, idx) => idx + 1);
    // Sequential loop — avoids firing all pdfDoc.getPage() at once on large documents.
    for (const p of pagesToPrint) {
        try {
            const page = await pdfDoc.getPage(p);
            const vp = page.getViewport({ scale: 1, rotation: 0 });
            if (vp.width > vp.height && !pageRotationsForPrint.has(p)) {
                pageRotationsForPrint.set(p, 'CCW90');
            }
        } catch (err) {
            console.warn(`[booklet-together] getPage(${p}) failed, skipping rotation:`, err);
        }
    }
}
```

### 2c. Update `pageRotations` in request body

**Current (lines 2796–2798):**
```javascript
pageRotations:    file.pageRotations.size > 0
    ? Array.from(file.pageRotations.entries()).map(([pageNumber, rotation]) => ({ pageNumber, rotation }))
    : null,
```

**Replace with:**
```javascript
pageRotations:    pageRotationsForPrint.size > 0
    ? Array.from(pageRotationsForPrint.entries()).map(([pageNumber, rotation]) => ({ pageNumber, rotation }))
    : null,
```

---

## Post-Implementation Verification

### Automated checks
- [ ] Open browser DevTools → Console: no JS errors on load
- [ ] Syntax check: `node --check frontend/app.js` → exits 0 (no syntax errors)
- [ ] Browser smoke test: open app, upload PDF → no console errors

### Manual smoke tests

**Test A — Booklet + separate (unchanged behavior)**
1. Fresh upload — do NOT manually rotate any pages; verify `AppState.activeFile.pageRotations.size === 0` before printing
2. Select Booklet mode, landscapeMode = `separate`
3. Print → DevTools Network: `duplexSide: null`, `pageRotations: null`
4. Expected: backend receives `duplexSide: null`, `pageRotations: null`

**Test B — Booklet + together (sheet view, committed map — B3 coverage)**
1. Fresh upload a **mixed** portrait/landscape PDF; verify `AppState.activeFile.pageRotations.size === 0`; select Booklet + together mode → switch to Sheet view
2. Wait for sheet view to fully render; then **verify in console: `AppState.activeFile._originalOrientationMap != null`** — this confirms the committed-map path (not the fallback). If still null, wait longer or use a smaller PDF.
3. Print → DevTools Network: `pageRotations` contains `CCW90` for landscape pages only, `duplexSide: null`
4. Expected: printed booklet has landscape pages portrait-sized
5. Note: proves B3 (mixed L+P committed-map path). B5 guard (`duplexSide: null` when booklet+together+committed+all-landscape) proven by Test K.

**Test C — Booklet + together (page view — fallback path)**
1. Upload PDF with landscape pages
2. Select Booklet + together mode → stay in Page view (never switch to Sheet view)
3. Before printing, snapshot map: `const snap = JSON.stringify([...AppState.activeFile.pageRotations.entries()].sort((a,b) => a[0]-b[0]))`
4. Print → DevTools Network: `pageRotations` contains `CCW90` for intrinsic-landscape pages
5. After print, verify: `JSON.stringify([...AppState.activeFile.pageRotations.entries()].sort((a,b) => a[0]-b[0])) === snap`
6. Expected: map identical before and after (content-level check, not just `.size`)

**Test D — Booklet + together (all-landscape — no duplexSide, output coverage)**
1. Fresh upload of all-landscape PDF (slides); verify `AppState.activeFile.pageRotations.size === 0` before printing; **stay in Page view** (so `_originalOrientationMap` remains null — duplexSide guard is irrelevant here, but output is verified)
2. Select Booklet + together mode
3. Print → DevTools Network: `duplexSide: null` (NOT `'ShortEdge'`)
4. Expected: backend receives `pageRotations` all CCW90, `duplexSide: null`
5. Note: this test proves B4 output only (fallback path, map null). Guard correctness (B5) is proven by Test K (committed map + all-landscape + booklet → duplexSide null). Test F proves _resumePrintQueue fallback, not the B6 committed-map guard (the guard is not exercised when map is null).

**Test E — Duplex + together (all-landscape — duplexSide still works)**
1. Upload all-landscape PDF; select Duplex + together mode → **switch to Sheet view and verify `AppState.activeFile._originalOrientationMap != null`** (committed, so the duplexSide block can fire)
2. Print → DevTools Network: `duplexSide: 'ShortEdge'`
3. Expected: guard `mode !== 'booklet'` does NOT block duplex path

**Test F — `_resumePrintQueue` fallback path**
1. Upload 2 landscape PDFs named distinctly (e.g. `file1.pdf`, `file2.pdf`); select Booklet mode (defaults to together)
2. **Use a non-duplex printer** (`IsDuplex === false`) — flip modal only appears on manual-duplex path
3. **Keep app in Page view only — do NOT switch to Sheet view for either file**
4. Before starting, verify: `AppState.files.find(f => f.name === 'file2.pdf')._originalOrientationMap === null`
5. Start print → first `/api/print` call belongs to `file1.pdf` and should return `jobState.waitingForFlip: true`; flip modal appears (modal appearance is sufficient proxy — optionally confirm `AppState.currentJob?.waitingForFlip === true` in console)
6. Complete flip → `file2.pdf` prints via `_resumePrintQueue`
7. DevTools: `file2.pdf` request has `pageRotations` with CCW90 entries, `duplexSide: null`
8. **This verifies Task 2b fallback branch ran** (file 2 never went through sheet view → `_originalOrientationMap` stayed null)

**Test G — Partial page selection + fallback (page view)**
1. Fresh upload of mixed portrait/landscape PDF (≥8 pages, at least 2 landscape, at least 1 portrait); select Booklet mode (defaults to together), stay in Page view; verify `AppState.activeFile.pageRotations.size === 0`
2. Select exactly **4 pages** (multiple of 4 — avoids booklet padding ambiguity): include **at least 1 landscape AND at least 1 portrait** from the selection, AND leave at least 1 landscape page unselected — this proves portrait pages are not rotated and unselected landscape pages are skipped
3. Print → DevTools Network: `pageRotations` contains `CCW90` for the **selected landscape pages only** (selected portrait pages have no CCW90; unselected landscape pages have no CCW90); `duplexSide: null`
4. Expected: fallback only iterates `pagesToPrint` (selected pages). The `vp.width > vp.height` check skips portrait pages; the selected-only loop skips unselected pages. Frontend enforces both constraints.
5. Verify: `AppState.activeFile.pageRotations.size === 0` still (no file mutation)

**Test H — Fallback + failed print → no file mutation**
1. Fresh upload of landscape PDF; select Booklet mode (defaults to together), stay in Page view
2. Before print, snapshot: `const snap = JSON.stringify([...AppState.activeFile.pageRotations.entries()].sort((a,b) => a[0]-b[0]))`
3. Disconnect backend (stop server or block network)
4. Attempt print → expect error toast
5. Verify: `JSON.stringify([...AppState.activeFile.pageRotations.entries()].sort((a,b) => a[0]-b[0])) === snap`
6. Expected: `file.pageRotations` fully unchanged after failed print

**Test I — Booklet + together, all-portrait → no CCW90 (spec B2)**
1. Fresh upload of all-portrait PDF; select Booklet mode (defaults to together), stay in Page view; verify `AppState.activeFile._originalOrientationMap === null` and `AppState.activeFile.pageRotations.size === 0` before printing
2. Print → DevTools Network: `pageRotations: null`, `duplexSide: null`
3. Expected: no rotations injected — all pages already portrait

**Test J — Sheet view before first-render commit → fallback may trigger (spec B7 edge)**
1. Upload a large mixed portrait/landscape PDF (enough pages to make first render slow)
2. Select Booklet mode (defaults to together) → switch to Sheet view
3. Use CPU throttle in DevTools to slow rendering; before clicking Print, check `AppState.activeFile._originalOrientationMap === null`
4. Click Print — if `_originalOrientationMap` is still null at request time, DevTools Network will show `pageRotations` with CCW90 (fallback ran)
5. **Note: this test is best-effort / racey.** `_startPrint()` awaits `ConfirmPrintModal.show()` before building the request body; `_renderSheetView` may commit `_originalOrientationMap` during that gap. Seeing null at click time does not guarantee fallback ran.
6. **Reliable fallback coverage comes from Tests C, F, I** (page view = guaranteed null map). Test J is exploratory only — omit from required test suite if unreliable in your environment.

**Test K — Booklet + together (all-landscape, sheet view committed — prove B5 `duplexSide` guard)**
1. Upload an **all-landscape** PDF; select Booklet + together mode → switch to Sheet view
2. Wait; **verify in console: `AppState.activeFile._originalOrientationMap != null`** (committed) — this is the condition where `duplexSide = 'ShortEdge'` would be computed if guard were absent
3. Print → DevTools Network: `duplexSide: null` (NOT `'ShortEdge'`)
4. Expected: guard `mode !== 'booklet'` blocks `duplexSide = 'ShortEdge'` even when `_originalOrientationMap != null` and all pages are landscape
5. Note: this is the only path where the B5 guard can actually fail

**Test L — Manual rotation precedence (fallback does not override manual rotation)**
1. Fresh upload of mixed portrait/landscape PDF (≥2 landscape pages); stay in Page view; verify `AppState.activeFile.pageRotations.size === 0`
2. Select Booklet + together mode
3. Manually rotate **one** of the landscape pages via context menu (e.g. rotate CW90) — this sets a manual rotation in `file.pageRotations`; verify `AppState.activeFile.pageRotations.size === 1`
4. Print → DevTools Network: inspect `pageRotations` array
5. Expected: the manually-rotated landscape page retains its manual rotation (NOT overridden with `CCW90`); other landscape pages receive `CCW90`
6. This verifies `!pageRotationsForPrint.has(p)` guard in the fallback correctly preserves manual rotations (spec B12)

**Test N — Committed map + partial selection → request body contains committed rotations (frontend scope only)**
1. Upload a **mixed** portrait/landscape PDF (≥8 pages, at least 2 landscape, at least 1 portrait); select Booklet + together mode → switch to Sheet view
2. Wait; **verify in console: `AppState.activeFile._originalOrientationMap != null`** (committed) AND `AppState.activeFile.pageRotations.size > 0` (sheet view has committed CCW90 into the map)
3. Select exactly **4 pages** (multiple of 4): include at least 1 landscape page AND leave at least 1 landscape page unselected
4. Print → DevTools Network: `pageRotations` array contains CCW90 for **all** committed landscape pages (not filtered to selected pages only — frontend sends the full committed map as-is); `duplexSide: null`
5. Expected: request body mirrors `file.pageRotations` as-is (committed path — no fallback injection, `pageRotationsForPrint` is just a copy). Backend `RemapRotations()` drops entries outside the selected subset, but this is not observable in the Network request body.
6. Note: this test proves the booklet committed-map path sends existing rotations correctly (frontend behavior). Backend scoping via `RemapRotations()` is a separate backend concern not verifiable via DevTools. `pagesToCheck` selection-scoping logic in the duplexSide block is **not exercised here** (blocked by `mode !== 'booklet'` guard). That branch is covered by Test E (Duplex + together).

**Test O — `pagesToCheck` partial-selection scoping in duplexSide block — `_startPrint()` (Duplex + together)**
1. Upload a **mixed** portrait/landscape PDF (≥8 pages, **at least 4 landscape**, at least 1 portrait); select **Duplex + together** mode → switch to Sheet view
2. Wait; **verify in console: `AppState.activeFile._originalOrientationMap != null`** (committed)
3. Select exactly **4 pages** (multiple of 4): select ONLY landscape pages (all-landscape subset), but leave at least 1 **portrait** page unselected
4. Print → DevTools Network: `duplexSide: 'ShortEdge'` — because `pagesToCheck` iterates only the selected (all-landscape) subset → `allLandscape = true`
5. Expected: if code erroneously checked ALL pages (including the unselected portrait), `duplexSide` would be `null`. Getting `'ShortEdge'` proves the partial-selection branch uses only selected pages.
6. Note: verifies `_startPrint()` only. `_resumePrintQueue()` same branch is covered by Test Q.

**Test P — `_resumePrintQueue()` Duplex + together, all-landscape committed → `duplexSide: 'ShortEdge'`**
1. Upload 2 PDFs: `file1.pdf` (any), `file2.pdf` (all-landscape); select **Duplex + together** mode; **use a non-duplex printer** (`IsDuplex === false`)
2. Switch to Sheet view for **file2** specifically; wait; **verify: `AppState.files.find(f => f.name === 'file2.pdf')._originalOrientationMap != null`** (committed all-landscape)
3. Start print → file1 prints first → flip modal appears
4. Complete flip → file2 prints via `_resumePrintQueue`
5. DevTools: file2 request has `duplexSide: 'ShortEdge'`
6. Expected: `_resumePrintQueue()` duplexSide block (non-booklet committed all-landscape path) fires correctly — mirrors Test E for resume path

**Test Q — `_resumePrintQueue()` Duplex + together, partial selection all-landscape, unselected portrait → `duplexSide: 'ShortEdge'`**
1. Upload 2 PDFs: `file1.pdf` (any), `file2.pdf` (mixed portrait/landscape, ≥8 pages, **at least 4 landscape**, at least 1 portrait); select **Duplex + together** mode; **use a non-duplex printer** (`IsDuplex === false`)
2. Switch to Sheet view for **file2**; wait; **verify: `AppState.files.find(f => f.name === 'file2.pdf')._originalOrientationMap != null`** (committed)
3. Select exactly **4 pages** from file2 (multiple of 4): select ONLY landscape pages (all-landscape subset), leave at least 1 **portrait** page unselected
4. Start print → file1 prints → flip modal appears
5. Complete flip → file2 prints via `_resumePrintQueue`
6. DevTools: file2 request has `duplexSide: 'ShortEdge'` — `pagesToCheck` iterates only selected (all-landscape) subset → `allLandscape = true`
7. Expected: if code erroneously checked ALL pages (including the unselected portrait), `duplexSide` would be `null`. Getting `'ShortEdge'` proves `pagesToCheck` uses selected pages only — mirrors Test O for resume path.

**Test R — `_resumePrintQueue()` Booklet + together + partial selection, page view fallback**
1. Upload 2 PDFs: `file1.pdf` (any), `file2.pdf` (mixed portrait/landscape, ≥8 pages, at least 2 landscape, at least 1 portrait); select **Booklet + together** mode; **use a non-duplex printer** (`IsDuplex === false`)
2. **Keep both files in Page view only — do NOT switch to Sheet view**; verify: `AppState.files.find(f => f.name === 'file2.pdf')._originalOrientationMap === null`
3. Select exactly **4 pages** from file2 (multiple of 4): include **at least 1 landscape AND at least 1 portrait** from the selection, AND leave at least 1 landscape page unselected — this proves portrait pages are not rotated and unselected landscape pages are skipped
4. Start print → file1 prints → flip modal appears
5. Complete flip → file2 prints via `_resumePrintQueue`
6. DevTools: file2 request has `pageRotations` with CCW90 for **selected landscape pages only** (selected portrait pages have no CCW90; unselected landscape pages have no CCW90); `duplexSide: null`
7. Verify after print: `AppState.files.find(f => f.name === 'file2.pdf').pageRotations.size === 0` (no file mutation)
8. Expected: fallback partial-selection path in `_resumePrintQueue()` iterates only selected pages — mirrors Test G for resume path

**Test S — `_resumePrintQueue()` Booklet + together, all-landscape committed → `duplexSide: null` (B6 guard)**
1. Upload 2 PDFs: `file1.pdf` (any), `file2.pdf` (all-landscape); select **Booklet + together** mode; **use a non-duplex printer** (`IsDuplex === false`)
2. Switch to Sheet view for **file2**; wait; **verify: `AppState.files.find(f => f.name === 'file2.pdf')._originalOrientationMap != null`** (committed) — this is the condition where `duplexSide = 'ShortEdge'` would fire if guard were absent
3. Start print → file1 prints → flip modal appears
4. Complete flip → file2 prints via `_resumePrintQueue`
5. DevTools: file2 request has `duplexSide: null` (NOT `'ShortEdge'`)
6. Expected: `mode !== 'booklet'` guard in `_resumePrintQueue()` blocks `duplexSide = 'ShortEdge'` even when `_originalOrientationMap != null` and all pages are landscape
7. Note: this is the B6 resume-path equivalent of Test K (`_startPrint()`). Covers the committed-map booklet guard in `_resumePrintQueue()`.

**Test T — `_resumePrintQueue()` Booklet + together, committed-map + mixed orientation → correct `pageRotations` sent**
1. Upload 2 PDFs: `file1.pdf` (any), `file2.pdf` (mixed portrait/landscape, at least 1 landscape); select **Booklet + together** mode; **use a non-duplex printer** (`IsDuplex === false`)
2. Switch to Sheet view for **file2**; wait; **verify: `AppState.files.find(f => f.name === 'file2.pdf')._originalOrientationMap != null`** (committed) AND `AppState.files.find(f => f.name === 'file2.pdf').pageRotations.size > 0` (sheet view has committed CCW90 for landscape pages)
3. Start print → file1 prints → flip modal appears
4. Complete flip → file2 prints via `_resumePrintQueue`
5. DevTools: file2 request has `pageRotations` array with CCW90 for committed landscape pages; `duplexSide: null`
6. Expected: `pageRotationsForPrint = new Map(file.pageRotations)` in `_resumePrintQueue()` correctly clones and serializes the committed rotation map. This is the resume-path equivalent of Test B (`_startPrint()` committed mixed path).
7. Note: this test covers full-selection resume committed serialization. Partial-selection committed-map resume path is covered by Test U.

**Test U — `_resumePrintQueue()` Booklet + together, committed-map + partial selection → full committed rotations sent**
1. Upload 2 PDFs: `file1.pdf` (any), `file2.pdf` (mixed portrait/landscape, ≥8 pages, at least 2 landscape, at least 1 portrait); select **Booklet + together** mode; **use a non-duplex printer** (`IsDuplex === false`)
2. Switch to Sheet view for **file2**; wait; **verify: `AppState.files.find(f => f.name === 'file2.pdf')._originalOrientationMap != null`** (committed) AND `AppState.files.find(f => f.name === 'file2.pdf').pageRotations.size > 0` (committed CCW90 in map)
3. Select exactly **4 pages** from file2 (multiple of 4): include at least 1 landscape AND leave at least 1 landscape unselected
4. Start print → file1 prints → flip modal appears
5. Complete flip → file2 prints via `_resumePrintQueue`
6. DevTools: file2 request has `pageRotations` array with CCW90 for **all** committed landscape pages (not filtered to selected pages only — frontend sends full committed map, backend `RemapRotations()` scopes); `duplexSide: null`
7. Expected: committed path in `_resumePrintQueue()` sends `file.pageRotations` as-is (cloned via `new Map(file.pageRotations)`) regardless of selection. This is the resume-path equivalent of Test N. A bug that filters committed rotations to only selected pages would pass Test T (full selection) but fail here.

**Test M — `_resumePrintQueue` uses captured mode, not live `AppState.printMode`**
1. Upload 2 PDFs: `file1.pdf` (any), `file2.pdf` (**all-landscape**); select **Booklet + together** mode; use a non-duplex printer (`IsDuplex === false`)
2. Switch to Sheet view for **file2**; wait; **verify: `AppState.files.find(f => f.name === 'file2.pdf')._originalOrientationMap != null`** (committed all-landscape) — this is the state where live `AppState.printMode === 'duplex'` would cause `duplexSide = 'ShortEdge'` if captured mode were not used
3. Start print → file1 prints → flip modal appears
4. **While flip modal is open**, switch UI mode to Duplex: `AppState.printMode = 'duplex'` in DevTools console
5. Complete flip → file2 prints via `_resumePrintQueue`
6. DevTools: file2 request has **`duplexSide: null`** (NOT `'ShortEdge'`) AND `mode: 1`
7. Expected: if `_resumePrintQueue` read live `AppState.printMode` (= `'duplex'`), the duplexSide block would fire → `duplexSide: 'ShortEdge'`. Getting `null` proves captured `modeCode=1` (booklet) is used — guard `mode !== 'booklet'` blocks ShortEdge.
8. Note: `duplexSide` is now the **primary discriminator** (observable branch output). `mode: 1` in request body is secondary sanity check only (always equals modeCode regardless of which variable the logic reads).

---

## Invariants to Verify

Covers spec §8 invariants **affected by this change** (B1–B8, B12, B17, plus internal branches). Spec §8 also defines B9–B11, B13–B16 — those are pre-existing invariants unaffected by this change and intentionally excluded from this matrix.

| # | Condition | Check | Test |
|---|-----------|-------|------|
| B1 | Separate mode — no CCW90 | `pageRotations: null` in request | Test A |
| B2 | Together mode, all-portrait → no CCW90 | `pageRotations: null`, `duplexSide: null` | Test I |
| B3 | Together mode, mixed L+P, committed map | CCW90 for L pages only, `duplexSide: null` | Test B |
| B4 | All-landscape booklet+together (output) | `duplexSide: null`, `pageRotations` all CCW90 | Test D |
| B5 | `duplexSide` guard — `_startPrint()` | `duplexSide: null` when `mode === 'booklet'` with committed map + all-landscape | Test K |
| B6 | `duplexSide` guard — `_resumePrintQueue()` booklet committed all-landscape | `duplexSide: null` in resumed booklet+together+committed+all-landscape | Test S |
| B6-fallback | `_resumePrintQueue()` fallback path | CCW90 in resumed booklet request, `duplexSide: null` | Test F |
| B6-committed | Duplex guard doesn't block non-booklet — `_startPrint()` | `duplexSide: 'ShortEdge'` for duplex+together with committed map, all-landscape | Test E |
| B6-committed-resume | Duplex guard doesn't block non-booklet — `_resumePrintQueue()` | `duplexSide: 'ShortEdge'` for resumed duplex+together with committed map, all-landscape | Test P |
| B7 | Fallback from page view | CCW90 in request; `file.pageRotations` content unchanged | Test C |
| B7-edge | Fallback from sheet view before commit | CCW90 in request if fallback ran (best-effort / racey — see Test J note) | Test J (exploratory) |
| B8 | Fallback + print fail | `file.pageRotations` content unchanged after failed request (B17: `_togetherRotations` and `_originalOrientationMap` are untouched by fallback — not manually verifiable, but guaranteed by design: fallback only writes to `pageRotationsForPrint`, a local var) | Test H |
| B12 | Manual rotation preserved by fallback | `!has(p)` guard keeps manual rotation; other L pages get CCW90 | Test L |
| B3-partial | Committed map + partial selection → request sends full committed rotations (frontend scope) | `pageRotations` contains committed CCW90 (all L pages), `duplexSide: null`; backend `RemapRotations()` scoping is not observable in Network | Test N |
| pagesToCheck-start | `pagesToCheck` partial-selection branch — `_startPrint()` | `duplexSide: 'ShortEdge'` when selected-only subset is all-landscape (unselected contains portrait) | Test O |
| pagesToCheck-resume | `pagesToCheck` partial-selection branch — `_resumePrintQueue()` | `duplexSide: 'ShortEdge'` for resumed duplex with all-landscape selected subset (unselected portrait) | Test Q |
| fallback-start-partial | `_startPrint()` fallback partial-selection scoping | CCW90 for selected landscape pages only (selected portrait = no CCW90, unselected landscape = no CCW90); `file.pageRotations` unchanged | Test G |
| fallback-resume-partial | `_resumePrintQueue()` fallback partial-selection scoping | CCW90 for selected landscape pages only (selected portrait = no CCW90, unselected landscape = no CCW90); no file mutation | Test R |
| B3-resume | `_resumePrintQueue()` booklet committed-map mixed orientation, full selection → correct serialization | `pageRotations` contains committed CCW90 for landscape pages; `duplexSide: null` | Test T |
| B3-resume-partial | `_resumePrintQueue()` booklet committed-map + partial selection → full committed rotations sent (frontend scope) | `pageRotations` contains committed CCW90 (all L pages, not filtered to selected); `duplexSide: null` | Test U |
| — | `_resumePrintQueue` uses captured mode | `duplexSide: null` (NOT `'ShortEdge'`) when live `AppState.printMode` switched to `'duplex'` during flip pause, but file2 is all-landscape committed — proves captured `modeCode=1` used, not live mode | Test M (`duplexSide` primary; `mode: 1` secondary) |

---

## Rollback

Both changes are additive. To rollback:
1. In `_startPrint()` and `_resumePrintQueue()`: remove `mode !== 'booklet' &&` from the `duplexSide` guard
2. In `_startPrint()` and `_resumePrintQueue()`: delete the entire `pageRotationsForPrint` block
3. In `_startPrint()` body and `_resumePrintQueue()` body: revert `pageRotationsForPrint` → `file.pageRotations`

No backend changes, no DB migrations, no HTML changes.

---

## Summary

| Task | File | Lines affected | Risk |
|------|------|---------------|------|
| 1a: guard duplexSide | `app.js` | ~2564 (1 line change) | LOW |
| 1b: pageRotationsForPrint block | `app.js` | ~2580–2603 (insert ~22 lines) | LOW |
| 1c: body.pageRotations rename | `app.js` | ~2593 (1 word change) | LOW |
| 2a: guard duplexSide | `app.js` | ~2773 (1 line change) | LOW |
| 2b: pageRotationsForPrint block | `app.js` | ~2783–2806 (insert ~22 lines) | LOW |
| 2c: body.pageRotations rename | `app.js` | ~2796 (1 word change) | LOW |

Total: ~46 lines inserted/modified in 1 file. Backend untouched.

**Tests:** A (separate), B (sheet view mixed B3), C (page view fallback), D (all-landscape output), E (duplex _startPrint duplexSide all-selected), F (_resumePrintQueue booklet fallback full-selection), G (partial selection fallback _startPrint), H (failed print no mutation), I (all-portrait no CCW90), J (sheet-view before commit — exploratory), K (B5 guard _startPrint: all-landscape+committed), L (manual rotation precedence B12), M (captured mode during flip pause), N (booklet committed-map partial → frontend serialization), O (pagesToCheck _startPrint: all-landscape subset → ShortEdge), P (_resumePrintQueue duplex all-landscape committed → ShortEdge), Q (_resumePrintQueue pagesToCheck all-landscape subset → ShortEdge), R (_resumePrintQueue booklet fallback partial-selection), S (B6 guard _resumePrintQueue: booklet all-landscape committed → duplexSide null), T (_resumePrintQueue booklet committed-map mixed full-selection → correct pageRotations), U (_resumePrintQueue booklet committed-map partial selection → full committed rotations sent).
