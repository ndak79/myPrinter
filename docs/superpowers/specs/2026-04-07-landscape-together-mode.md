# Spec: Landscape Together Mode
**Date:** 2026-04-07
**Status:** Final (Round 4 reviewed)
**Scope:** `frontend/app.js`, `backend/Models/PrintModels.cs`, `backend/Services/PrintAlgorithmService.cs`, `backend/Services/IWordInteropService.cs`, `backend/Services/WordInteropService.cs`, `backend/BackendStartup.cs`

---

## 1. Problem Statement

`landscapeMode = 'separate'` groups pages by orientation before pairing them into duplex
sheets. Landscape pages are always isolated into their own orientation group and can never
share a physical sheet with portrait pages. This is correct and must not change.

`landscapeMode = 'together'` is exposed in the UI (`data-lsmode="together"`) but is a
stub — `buildSheetLayout` rejects it with a `console.warn` and falls back to separate
logic. This spec implements it fully.

---

## 2. Core Principle

```
TOGETHER MODE = "rotate every landscape page CCW90, then treat all pages as portrait"

Landscape (297 x 210 mm) --[CCW90]--> (210 x 297 mm)  identical to A4 portrait
Portrait  (210 x 297 mm) --[no-op]--> (210 x 297 mm)  unchanged

After rotation:
  - All pages are portrait-sized (210 x 297 mm)
  - buildSheetLayout sees no orientation differences => one group => no splitting
  - Pages pair in their original document order
  - Sheet view is WYSIWYG: PDF.js renders CCW90 (rotation=270) which swaps
    landscape dims (297x210) to portrait (210x297), matching the printed output
```

---

## 3. Data Model

### 3.1 New fields on FileEntry

Added alongside the existing `pageRotations` field at FileEntry construction:

```javascript
_togetherRotations:     new Set(),  // Set<pageNum> — pages auto-rotated by together mode
_originalOrientationMap: null,      // Map<pageNum, bool> | null — pre-injection snapshot
```

`_togetherRotations` tracks only pages whose CCW90 was injected by together mode, never
pages the user rotated manually.

`_originalOrientationMap` is a frozen snapshot taken after the first orientation
detection pass and before any CCW90 injection. It is used by `_startPrint()` to determine
whether the document is all-landscape. It is `null` until the first together-mode render.

### 3.2 `AppState.landscapeMode` default

Change the default at `app.js` line ~34 from `'separate'` to `'together'`, reconciling
the mismatch between the code default and the HTML active-button marker
(`data-lsmode="together"` is already marked `active` in `index.html`):

```javascript
// Before:
landscapeMode: 'separate',  // 'separate' | 'together' ('together' not backend-supported — see §4.8)

// After:
landscapeMode: 'together',  // 'separate' | 'together'
```

Remove the `(§4.8)` comment — together mode is now fully supported.

### 3.3 New POST body fields in `_startPrint()`

```
duplexSide:    'LongEdge' | 'ShortEdge'
manualFlipDir: 'LongEdge' | 'ShortEdge'   (maps to PrintRequest.ManualFlipDir on backend)
```

Defaults to `'LongEdge'` for all existing cases. `'ShortEdge'` is sent only when
together mode is active and the entire document is landscape (see §5.8).

---

## 4. New Helper: `_teardownTogether(fileEntry)`

A standalone helper function. Restores the fileEntry to the state it was in before
together mode was activated. Safe to call multiple times (idempotent when
`_togetherRotations` is already empty).

```
_teardownTogether(fileEntry):
    for each p in fileEntry._togetherRotations:
        fileEntry.pageRotations.delete(p)        // remove auto-injected CCW90
        fileEntry._orientationMap?.delete(p)     // force re-detect at original orientation
    fileEntry._togetherRotations.clear()
    fileEntry._originalOrientationMap = null
```

User-set rotations (pages not in `_togetherRotations`) are never touched.

---

## 5. Frontend Changes (`app.js`)

### 5.0 Teardown on view-mode switch

When the user switches from sheet view to page view while together mode is active,
`_renderSheetView` is never called, so step [0]'s defensive teardown never fires.
The page-view renderer reads `fileEntry.pageRotations` directly and would show stale
CCW90 injections as `-90°` badges the user never requested.

Add this teardown call in `PreviewPanelModule.render()`, after the `blankAbsorbedBy`
reset and **before** the viewMode dispatch:

```javascript
// In PreviewPanelModule.render(), before the viewMode dispatch:
if (this._viewMode !== 'sheet' && fileEntry?._togetherRotations?.size > 0) {
    _teardownTogether(fileEntry);
}
```

This catches all entry points (view-mode toggle, file switch with non-sheet view, etc.)
and is self-contained within `render()`.

### 5.1 Remove warn-guard in `buildSheetLayout`

Remove this block entirely:

```javascript
if (landscapeMode !== 'separate') {
    console.warn('[buildSheetLayout] landscapeMode must be "separate"; ...');
}
```

No branch is needed inside `buildSheetLayout`. When together mode is active, the
orientation pre-processing in `_renderSheetView` ensures `orientationMap` maps every
page to `false` before `buildSheetLayout` is called, so Bước-1 naturally produces one
group.

### 5.2 `_renderSheetView` — execution sequence for together mode

The existing orientation cache-fill loop must run **before** injection so that the
original landscape/portrait identity of each page is known. The full sequence when
`AppState.landscapeMode === 'together'`:

```
[0] DEFENSIVE TEARDOWN (at top of _renderSheetView, before all other logic):
    if AppState.landscapeMode !== 'together' AND fileEntry._togetherRotations?.size > 0:
        _teardownTogether(fileEntry)
        // Uses optional-chain (?.) to guard against undefined _togetherRotations
        // (e.g. legacy FileEntry objects). Consistent with guards in §5.5–5.7.

[1] CLEANUP (existing, unchanged):
    Clear _renderTasks, _renderQueue, _blobQueue, _activeBlobRenders, DOM container.
    Note: _blobQueue and _activeBlobRenders must be reset to [] and 0 here to prevent
    stale blob renders from the previous mode from draining after the mode switch.

[2] ORIENTATION DETECTION (existing cache-fill loop, unchanged):
    For each page p missing from fileEntry._orientationMap:
        fetch pdfDoc.getPage(p) with current pageRotations applied
        orientationMap.set(p, vp.width > vp.height)
    // orientationMap now contains ORIGINAL orientations (no CCW90 injected yet)

[2b] ASYNC GUARD (existing, STAYS HERE — not moved to step [6]):
    if _currentFileId !== fileEntry.id: return
    // This guard protects steps [3]–[4] from mutating a stale fileEntry.
    // Step [6] adds a SECOND guard after step [5]'s await.

[3] SNAPSHOT (together mode only, runs after [2b]):
    if fileEntry._originalOrientationMap == null:    // == catches both null and undefined

        // Detect INTRINSIC orientation (without any user rotations) for each page.
        // This is needed because step [2]'s cache-fill applies current pageRotations
        // (including user CW90/CCW90/Rotate180), which would misrepresent a user-rotated
        // portrait page as landscape. The allLandscape check in _startPrint must reflect
        // the PDF's inherent page dimensions, not user-chosen rotations.
        //
        // Implementation: fetch each page with rotation=0 (ignoring pageRotations).
        const intrinsicMap = new Map()
        for each p in [1 .. fileEntry.totalPageCount]:
            page = await pdfDoc.getPage(p)
            vp = page.getViewport({ scale: 1, rotation: 0 })   // NO rotation
            intrinsicMap.set(p, vp.width > vp.height)

        fileEntry._originalOrientationMap = intrinsicMap

    // Must be taken before [4] so it is not contaminated by CCW90 injection.
    // The null-guard prevents re-renders from overwriting the snapshot.

[4] STEP A — INJECT CCW90 (together mode only):
    for each p in [1 .. fileEntry.totalPageCount]:
        if fileEntry._originalOrientationMap.get(p) === true:  // intrinsically landscape
            if NOT fileEntry.pageRotations.has(p):             // not manually rotated
                fileEntry.pageRotations.set(p, 'CCW90')
                fileEntry._togetherRotations.add(p)

[5] STEP B — INVALIDATE STALE CACHE (together mode only):
    for each p in fileEntry._togetherRotations:
        fileEntry._orientationMap.delete(p)
    // After deletion, all pages in _togetherRotations are missing from orientationMap.
    // Now set them directly (synchronous — result is deterministic: CCW90 always
    // makes landscape → portrait, no async fetch needed):
    for each p in fileEntry._togetherRotations:
        fileEntry._orientationMap.set(p, false)  // CCW90 makes landscape pages portrait-sized

[6] ASYNC GUARD (NEW — same pattern as [2b], protects against file switch during
    step [3]'s await in the intrinsic detection loop):
    if _currentFileId !== fileEntry.id: return

[7] BUILD SHEETS:
    buildSheetLayout(fileEntry, printMode, orientationMap, AppState.landscapeMode)
    // orientationMap now maps every page to false => one group => original order
```

### 5.3 `_renderSheetView` — cleanup block

The cleanup block (where `_renderTasks` is cleared and the DOM is emptied) must also
reset the blob pipeline:

```
this._blobQueue       = []
this._activeBlobRenders = 0
```

This prevents stale blob render callbacks from draining into the new render cycle after
a mode switch.

### 5.4 Modebar click handler — teardown on mode switch

When the user switches away from together mode, teardown must run on the active file
before the new mode is set:

```
on modebar click:
    newMode = btn.dataset.lsmode

    if AppState.landscapeMode === 'together' AND newMode !== 'together':
        if AppState.activeFile: _teardownTogether(AppState.activeFile)

    AppState.landscapeMode = newMode
    update active class on buttons
    PreviewPanelModule.render(AppState.activeFile)
```

The defensive teardown in `_renderSheetView` (§5.2 step [0]) covers non-active files
that may have stale state from a previous together-mode render.

### 5.5 Rotation badge suppression in `_renderSheetPage`

Pages auto-rotated by together mode show a '-90°' badge that the user never requested.
Suppress the badge for pages in `_togetherRotations`:

```
rotation      = fileEntry.pageRotations?.get(pageNum) ?? null
badgeRotation = fileEntry._togetherRotations?.has(pageNum) ? null : rotation
RotationHelper.updateBadge(el, badgeRotation)
```

User-manually-rotated pages (not in `_togetherRotations`) retain their badge normally.

### 5.6 Rotation badge suppression in `_syncSelectionUI`

`_syncSelectionUI` (~line 3806) is a second badge-update path that runs on every
selection change. It also calls `RotationHelper.updateBadge` unconditionally with
`pageRotations.get(pageNum)`, which would re-add the auto-suppressed `-90°` badge
on every selection event. Apply the same suppression guard here:

```javascript
// Before (existing):
const rotation = activeFile.pageRotations?.get(pageNum) ?? null;
RotationHelper.updateBadge(card, rotation);

// After (together-mode guard added):
const rotation     = activeFile.pageRotations?.get(pageNum) ?? null;
const badgeRotation = activeFile._togetherRotations?.has(pageNum) ? null : rotation;
RotationHelper.updateBadge(card, badgeRotation);
```

This guard is identical in structure to §5.5. Both paths must be updated together.

### 5.5b Rotation badge suppression in `ThumbStripModule`

The left thumbnail strip also shows rotation badges. Two paths must be updated:

```javascript
// ThumbStripModule._renderThumb (line ~4032):
const badgeRotation = fileEntry._togetherRotations?.has(pageNum) ? null : rotation;
RotationHelper.updateBadge(el, badgeRotation);

// ThumbStripModule._syncSelectionHighlights (line ~4095):
const badgeRotation = entry._togetherRotations?.has(pageNum) ? null : rotation;
RotationHelper.updateBadge(el, badgeRotation);
```

Same pattern as §5.5/§5.6 — `?.` guard handles missing field gracefully.

### 5.5c Rotation badge suppression in `ZoomModal`

The zoom modal (`ZoomModal._buildPageContainer`, line ~1704) calls
`RotationHelper.updateBadge(div, rotation)` with the raw rotation. Apply the same guard:

```javascript
const badgeRotation = fileEntry._togetherRotations?.has(pageNum) ? null : rotation;
RotationHelper.updateBadge(div, badgeRotation);
```

### 5.6 Rotation badge suppression in `_syncSelectionUI`

`_syncSelectionUI` (~line 3806) is a second badge-update path that runs on every
selection change. It also calls `RotationHelper.updateBadge` unconditionally with
`pageRotations.get(pageNum)`, which would re-add the auto-suppressed `-90°` badge
on every selection event. Apply the same suppression guard here:

```javascript
// Before (existing):
const rotation = activeFile.pageRotations?.get(pageNum) ?? null;
RotationHelper.updateBadge(card, rotation);

// After (together-mode guard added):
const rotation     = activeFile.pageRotations?.get(pageNum) ?? null;
const badgeRotation = activeFile._togetherRotations?.has(pageNum) ? null : rotation;
RotationHelper.updateBadge(card, badgeRotation);
```

### 5.7 User-rotates a page while together mode is active — evict from `_togetherRotations`

**Why needed (I3 invariant):** When the user manually rotates page P that was already
auto-injected by together mode, P is currently in `_togetherRotations`. Without this fix,
`_teardownTogether` would delete P's rotation when the user switches modes — destroying
the manually applied rotation.

**Where to add this:** In `ContextMenu._applyRotation` (line ~1949 in `app.js`), after
the rotation is applied to `AppState.pageRotations` (lines ~1952/1955) and before the
cache-invalidation block (line ~1960). Use `const fileEntry = AppState.activeFile`
(already available in that scope at line ~1961).

```javascript
// After applying user's rotation to page P:
if (fileEntry._togetherRotations?.has(p)) {
    fileEntry._togetherRotations.delete(p);
    // P is now "owned" by the user — teardown won't touch it.
    // _orientationMap entry for P will be re-detected correctly on next render.
}
```

**All cases handled by this single addition:**

| Scenario | Effect |
|---|---|
| User sets P to CW90 (already auto-CCW90'd) | P leaves `_togetherRotations`; teardown skips P; CW90 preserved ✓ |
| User resets P to null (already auto-CCW90'd) | P leaves `_togetherRotations`; `pageRotations.delete(P)` removes it; next render re-injects CCW90 ✓ |
| User rotates P that was never auto-injected | `_togetherRotations` doesn't have P; no-op ✓ |

### 5.8 `_startPrint()` — duplexSide

```
for each file being printed:
    originalMap = file._originalOrientationMap
    duplexSide  = 'LongEdge'                        // default

    if AppState.landscapeMode === 'together' AND originalMap is not null:
        allLandscape = every p in [1..file.totalPageCount]:
            originalMap.get(p) === true
        if allLandscape:
            duplexSide = 'ShortEdge'

    POST body includes:
        duplexSide:    duplexSide           // 'LongEdge' | 'ShortEdge'
        manualFlipDir: duplexSide           // same value; maps to PrintRequest.ManualFlipDir on backend
```

`manualFlipDir` sent from frontend maps to `PrintRequest.ManualFlipDir` on the backend
via ASP.NET Core's default camelCase deserialization (`manualFlipDir` → `ManualFlipDir`).
It overrides the backend's first-page-heuristic for computing the manual duplex flip
instruction (see §6.6).

**Important:** The key name MUST be `manualFlipDir` (not `flipDirection`). ASP.NET Core
case-insensitive matching maps by property-name similarity, not by arbitrary renaming:
`flipDirection` would fail to bind to `ManualFlipDir` → `request.ManualFlipDir = null`
→ §6.6 override never activates.

If `_originalOrientationMap` is null (user prints without ever opening sheet view in
together mode), `duplexSide` defaults to `'LongEdge'` safely.

---

## 6. Backend Changes

### 6.1 `PrintModels.cs` — new fields on `PrintRequest`

```csharp
public string? DuplexSide    { get; set; }  // "LongEdge" | "ShortEdge" | null => LongEdge
public string? ManualFlipDir { get; set; }  // "LongEdge" | "ShortEdge" | null => auto-detect
```

**Note:** The property is named `ManualFlipDir` (not `FlipDirection`) to avoid reader
confusion with the `FlipDirection` enum type defined at `PrintModels.cs:132`.
Although C# would not produce a compiler error (the "Color Color" rule permits a
property and a type to share a name in the same namespace), the distinct name prevents
ambiguity for readers of the code.

### 6.2 `BackendStartup.cs` — thread `DuplexSide` and `ManualFlipDir` to job creation

The `/api/print` endpoint in `BackendStartup.cs` (line ~193) calls
`CreateNormalDuplexJob` with 8 positional arguments. The final call site after both
§6.3 and §6.6 changes is shown in §6.6 — two new named arguments are appended.
The intermediate version for §6.3 only:

```csharp
// BackendStartup.cs ~line 193 — intermediate (§6.3 only):
jobState = printAlgorithm.CreateNormalDuplexJob(
    filePath, request.PrinterName, printer.IsDuplex,
    request.PageRange, request.SingleSidedPages,
    request.Watermark, request.PageOrder, request.PageRotations,
    duplexSide: request.DuplexSide);   // NEW
// (manualFlipDir added in §6.6)
```

### 6.3 `PrintAlgorithmService.cs` — thread `DuplexSide` to print call

Add `string? duplexSide = null` to `CreateNormalDuplexJob`'s signature, then pass it
through `ExecutePrintJob`. `ExecutePrintJob` passes it to `_wordService.PrintPdf` **only
for the auto-duplex branch (line ~577)**. Manual duplex branches (lines ~608 and ~636)
are one-sided prints on simplex printers — they must NOT receive `duplexSide`.

**Signature chain (4 methods):**

```csharp
// 1. CreateNormalDuplexJob — add param at end
public PrintJobState CreateNormalDuplexJob(
    string pdfPath, string printerName, bool isDuplexPrinter,
    string? pageRange = null, int[]? singleSidedPages = null,
    WatermarkOptions? watermark = null, int[]? pageOrder = null,
    List<PageRotation>? pageRotations = null,
    string? duplexSide = null)   // NEW

// 2. Store on jobState for ExecutePrintJob to read (nullable — do NOT coalesce to "LongEdge")
jobState.DuplexSide = duplexSide;
// (Add DuplexSide string? property to PrintJobState — see §6.4)

// 3. ExecutePrintJob — reads jobState.DuplexSide, passes to PrintPdf (auto-duplex branch ONLY)
//    Line ~577 (auto-duplex):
_wordService.PrintPdf(jobState.TempPdfPath, jobState.PrinterName,
    pageRange: null, duplexSide: jobState.DuplexSide);
//    Lines ~608 and ~636 (manual duplex — simplex printer): NO duplexSide param

// 4. IWordInteropService.PrintPdf — add duplexSide param
void PrintPdf(string pdfPath, string printerName,
    string? pageRange = null, string? duplexSide = null);
```

### 6.4 `PrintModels.cs` — `DuplexSide` on `PrintJobState`

Add a **nullable** property to carry `duplexSide` through the job lifecycle:

```csharp
public string? DuplexSide { get; set; }  // null = use printer default (no override)
```

**Nullable is required.** If it were `string DuplexSide = "LongEdge"` (non-nullable with
default), `ExecutePrintJob` would always pass `"LongEdge"` to `PrintPdf` — including for
booklet mode and manual-duplex branches that don't set `DuplexSide`. This would cause
`PrintWithSumatra` to emit `-print-settings "duplexlong"` on every print, breaking
simplex printers and booklet mode.

With nullable, `jobState.DuplexSide = null` means no override → `PrintWithSumatra`
emits no `-print-settings` argument → existing behavior preserved.

### 6.5 `WordInteropService.cs` — thread `duplexSide` to `PrintWithSumatra`

```csharp
// WordInteropService.PrintPdf — add param:
public void PrintPdf(string pdfPath, string printerName,
    string? pageRange = null, string? duplexSide = null)

// TryShellPrint — add param:
private bool TryShellPrint(string filePath, string printerName,
    string? duplexSide = null)

// PrintWithSumatra — add param and use CONDITIONALLY:
private bool PrintWithSumatra(string sumatraPath, string pdfPath,
    string printerName, string? duplexSide = null)
{
    // Only emit -print-settings when duplexSide is explicitly set.
    // null → no arg → preserve existing printer behavior (no regression for simplex/booklet).
    // Correct SumatraPDF syntax: "duplexshort" and "duplexlong" (NOT "short"/"long").
    var settingsPart = duplexSide switch {
        "ShortEdge" => "-print-settings \"duplexshort\" ",
        "LongEdge"  => "-print-settings \"duplexlong\" ",
        _           => ""   // null or unknown → no override
    };
    var args = $"-print-to \"{printerName}\" {settingsPart}\"{pdfPath}\"";
    // ... rest unchanged
}
```

**Note:** `TryPowerShellPrint` (Strategy 1 fallback) and `PrintBySwappingDefaultPrinter`
(Strategy 2 fallback inside `TryShellPrint`) do NOT receive `duplexSide` — duplex-side
control via those paths is not supported. This is acceptable degraded behavior: when
SumatraPDF is not installed or its print fails, `duplexSide` is silently ignored.

### 6.6 `PrintAlgorithmService.cs` — manual duplex `flipDirection`

The current code at line 42 derives `flipDirection` from `pdfInfo.IsLandscape` (first
page of the original PDF). In together mode the output PDF is all-portrait after
CCW90 rotation, so `pdfInfo.IsLandscape` returns `false` even for an all-landscape
document → wrong `LongEdge` flip instruction.

Add `string? manualFlipDir = null` to `CreateNormalDuplexJob` as a second new
parameter alongside `duplexSide` (§6.3). Pass `request.ManualFlipDir` from
`BackendStartup.cs`. When provided, use it directly instead of the heuristic:

```csharp
// CreateNormalDuplexJob — full new signature (extends §6.3):
public PrintJobState CreateNormalDuplexJob(
    string pdfPath, string printerName, bool isDuplexPrinter,
    string? pageRange = null, int[]? singleSidedPages = null,
    WatermarkOptions? watermark = null, int[]? pageOrder = null,
    List<PageRotation>? pageRotations = null,
    string? duplexSide = null,       // NEW (§6.3)
    string? manualFlipDir = null)    // NEW (§6.6)

// Replace line 42 — flipDirection derivation:
FlipDirection flipDirection;
if (!string.IsNullOrEmpty(manualFlipDir) &&
    Enum.TryParse<FlipDirection>(manualFlipDir, out var parsedFlip))
{
    flipDirection = parsedFlip;
}
else
{
    flipDirection = pdfInfo.IsLandscape ? FlipDirection.ShortEdge : FlipDirection.LongEdge;
}
```

```csharp
// BackendStartup.cs ~line 193 — final call site:
jobState = printAlgorithm.CreateNormalDuplexJob(
    filePath, request.PrinterName, printer.IsDuplex,
    request.PageRange, request.SingleSidedPages,
    request.Watermark, request.PageOrder, request.PageRotations,
    duplexSide:    request.DuplexSide,      // NEW §6.3
    manualFlipDir: request.ManualFlipDir);  // NEW §6.6
```

---

## 7. ASCII Diagrams

### 7.1 Main example: 1L 2P 3P 4L 5L 6L

```
INPUT: pages = [1L  2P  3P  4L  5L  6L]
       L = landscape in original PDF
       P = portrait  in original PDF

======================================================
  SEPARATE MODE (unchanged)
======================================================

  Bước 1 groups by CONTIGUOUS runs of same orientation
  (NOT by collecting all L or all P globally):
    Group A (landscape): [1L]          ← run ends when 2P starts
    Group B (portrait):  [2P  3P]      ← run ends when 4L starts
    Group C (landscape): [4L  5L  6L]  ← run to end of document

  Bước 2 R4: pad each group independently to even count:
    Group A: [1L] → length 1 (odd) → pad → [1L  blank_L]
    Group B: [2P  3P] → length 2 (even) → no pad
    Group C: [4L  5L  6L] → length 3 (odd) → pad → [4L  5L  6L  blank_L]

  logicalPages = [1L, blank_L, 2P, 3P, 4L, 5L, 6L, blank_L]

  Bước 3: pair into sheets:

  +==========+   +----------+   +==========+   +==========+
  | Sheet 1  |   | Sheet 2  |   | Sheet 3  |   | Sheet 4  |
  |==========|   |----------|   |==========|   |==========|
  | Front:1L |   | Front:2P |   | Front:4L |   | Front:6L |
  | Back:    |   | Back: 3P |   | Back: 5L |   | Back:    |
  +==========+   +----------+   +==========+   +==========+
  landscape       portrait       landscape       landscape
  (blank back)                                  (blank back)

  4 sheets. Document order preserved within each orientation group.
  Group boundaries force blank padding (1L alone → blank back;
  6L alone → blank back).

======================================================
  TOGETHER MODE
======================================================

  [1] Cache-fill detects original orientations:
      _originalOrientationMap: {1:L, 2:P, 3:P, 4:L, 5:L, 6:L}

  [2] Inject CCW90 for pages 1, 4, 5, 6 (no pre-existing rotation):
      pageRotations:      {1->CCW90, 4->CCW90, 5->CCW90, 6->CCW90}
      _togetherRotations: {1, 4, 5, 6}

  [3] Re-detect pages 1,4,5,6 with CCW90 applied:
      orientationMap: {1:false, 2:false, 3:false, 4:false, 5:false, 6:false}
      => one group, all portrait-sized

  [4] buildSheetLayout pairs in original document order:

      [1L(r)] [2P]   [3P] [4L(r)]   [5L(r)] [6L(r)]
       \______/        \______/         \______/
        Sheet 1         Sheet 2          Sheet 3

  +------------+  +------------+  +------------+
  |  Sheet 1   |  |  Sheet 2   |  |  Sheet 3   |
  |------------|  |------------|  |------------|
  | 1L (CCW90) |  | 3P         |  | 5L (CCW90) |
  | 2P         |  | 4L (CCW90) |  | 6L (CCW90) |
  +------------+  +------------+  +------------+
  all portrait-sized (sheet-faces-row)

  3 sheets. Original document order preserved. No blank padding.

  CONTRAST with separate mode:
    Separate: 4 sheets — 1L alone (blank back), 2P+3P, 4L+5L, 6L alone (blank back)
    Together: 3 sheets — 1L+2P, 3P+4L, 5L+6L — cross-orientation pairing, no blanks
```

### 7.2 Single-sided page interaction

```
INPUT: [P1  SS:P2  L3  P4]   (P2 = single-sided)

Inject CCW90 for L3 only => pageRotations: {3->CCW90}, _togetherRotations: {3}
After re-detect: all isLandscape=false

Bước 2 processes the single group:
  i=0: P1 (normal)  => groupLogical=[P1]                   length=1 (odd)
  i=1: P2 (SS)      => R2 fires: push null                 length=2 (even)
                    => push P2                             length=3
                    => next=L3 (!=0): push null (auto-back) length=4
                    => i+=1
  i=2: L3 (normal)  => push L3                             length=5 (odd)
  i=3: P4 (normal)  => push P4                             length=6 (even)
  R4: already even, no pad.

Bước 3 pairs:
  +----------+  +-------------+  +----------+
  | Sheet 1  |  |   Sheet 2   |  | Sheet 3  |
  |----------|  |-------------|  |----------|
  | Front:P1 |  | Front:P2 SS |  | Front:L3 |
  | Back:null|  | Back: null  |  | Back: P4 |
  +----------+  +-------------+  +----------+
                 isSingleForced=true
```

### 7.3 All-landscape document — auto short-edge

```
INPUT: [1L  2L  3L  4L]  (entire document is landscape)

  _originalOrientationMap: {1:true, 2:true, 3:true, 4:true}
  Inject CCW90 for all 4 pages => all portrait-sized after re-detect.

  Sheet view:
  +------------+  +------------+
  |  Sheet 1   |  |  Sheet 2   |
  |------------|  |------------|
  | 1L (CCW90) |  | 3L (CCW90) |
  | 2L (CCW90) |  | 4L (CCW90) |
  +------------+  +------------+

  _startPrint():
    allLandscape = [1,2,3,4].every(p => _originalOrientationMap.get(p) === true)
    => true => duplexSide = 'ShortEdge'

  Physical result:
    All pages are portrait-sized (210x297) in the output PDF.
    ShortEdge duplex flips along the 210mm edge (like a wall calendar).
    Reader rotates sheet 90° CW to view the original landscape content.
    Back side reads correctly in sequence. Correct for all-landscape documents.
```

### 7.4 Mode switching — rotation preservation

```
State: user manually set P3 to CW90, then enables together mode.
pages = [1L  2L  3P(user:CW90)  4L]

Before together:
  pageRotations:      {3->CW90}
  _togetherRotations: {}

After cache-fill:
  _originalOrientationMap: {1:true, 2:true, 3:false, 4:true}

Step A (inject):
  p=1: landscape, no existing rotation => inject, track
  p=2: landscape, no existing rotation => inject, track
  p=3: portrait  => skip
  p=4: landscape, no existing rotation => inject, track

  pageRotations:      {1->CCW90, 2->CCW90, 3->CW90, 4->CCW90}
  _togetherRotations: {1, 2, 4}

_teardownTogether (on mode switch back to separate):
  delete pageRotations[1], [2], [4]

  pageRotations:      {3->CW90}   <-- user's manual rotation preserved
  _togetherRotations: {}
```

---

## 8. Invariants

| # | Condition | Expected behaviour |
|---|-----------|-------------------|
| I1 | `separate` mode | Unchanged. No injection, teardown, or duplexSide override. |
| I2 | User rotated page manually, then enables together | User's rotation preserved; only unrotated landscape pages get CCW90. |
| I3 | User rotates a page while together mode is active | Manual rotation stored in `pageRotations` but NOT in `_togetherRotations`; unaffected by teardown. |
| I4 | Switch together => separate => together | Teardown clears `_togetherRotations`; re-enable runs fresh injection. |
| I5 | All-portrait document in together mode | No CCW90 injected; `_togetherRotations` empty; `duplexSide='LongEdge'`. |
| I6 | All-landscape document in together mode | All L pages injected; `duplexSide='ShortEdge'`. |
| I7 | Mixed L+P document in together mode | Only L pages injected; `duplexSide='LongEdge'`. |
| I8 | Single-sided pages in together mode | Bước-2 R2/R6 logic runs unchanged on the single group. |
| I9 | User-inserted blank (p=0) in together mode | Blank placed in the single group; `isLandscape=false` (portrait blank). |
| I10 | File switch while together mode active | New file has fresh `_togetherRotations = new Set()`; injection runs on first render. |
| I11 | Non-active file with stale `_togetherRotations` | Defensive teardown at top of `_renderSheetView` cleans it up on next render. |
| I12 | `duplexSide` allLandscape scope | Checks `_originalOrientationMap` for all pages 1..totalPageCount (not just selected). |
| I13 | `_startPrint` before any sheet-view render | `_originalOrientationMap` is null; `duplexSide` defaults to `'LongEdge'` safely. |
| I14 | Rotation badge for auto-injected CCW90 | Badge suppressed for `_togetherRotations` pages in `_renderSheetPage`, `_syncSelectionUI`, `ThumbStripModule._renderThumb`, `ThumbStripModule._syncSelectionHighlights`, and `ZoomModal._buildPageContainer`; shown for user-rotated pages. |
| I15 | Switch from sheet view to page view while together mode active | `PreviewPanelModule.render()` calls `_teardownTogether` before page-view path; no stale CCW90 badges shown in page view. |

---

## 9. Scope of Changes

| File | Change |
|------|--------|
| `frontend/app.js` | Change `AppState.landscapeMode` default from `'separate'` to `'together'` (line ~34) |
| | Remove warn-guard in `buildSheetLayout` |
| | Add `_togetherRotations` and `_originalOrientationMap` to `createFileEntry()` |
| | Add `_teardownTogether(fileEntry)` helper function |
| | `PreviewPanelModule.render()`: call `_teardownTogether` when switching away from sheet view (§5.0) |
| | `_renderSheetView`: add defensive teardown (step [0]), reset `_blobQueue`/`_activeBlobRenders` in cleanup, add snapshot + Step A + Step B after cache-fill, add guard [2b] after step [2] |
| | Modebar click handler: call `_teardownTogether` before mode switch (§5.4) |
| | `_renderSheetPage`: badge suppression for `_togetherRotations` pages (§5.5) |
| | `_syncSelectionUI`: badge suppression for `_togetherRotations` pages (§5.6) |
| | `ThumbStripModule._renderThumb` + `_syncSelectionHighlights`: badge suppression (§5.5b) |
| | `ZoomModal._buildPageContainer`: badge suppression (§5.5c) |
| | Rotation handler (`ContextMenu._applyRotation`): evict P from `_togetherRotations` on user manual rotate (§5.7) |
| | `_startPrint`: compute `duplexSide` + `manualFlipDir` from `_originalOrientationMap`, add to POST body (§5.8) |
| `backend/Models/PrintModels.cs` | Add `DuplexSide` (string?) and `ManualFlipDir` (string?) to `PrintRequest` |
| | Add `DuplexSide` (**string?**, default null) to `PrintJobState` |
| `backend/BackendStartup.cs` | Pass `duplexSide: request.DuplexSide` and `manualFlipDir: request.ManualFlipDir` to `CreateNormalDuplexJob` call |
| `backend/Services/PrintAlgorithmService.cs` | Add `duplexSide` + `manualFlipDir` params to `CreateNormalDuplexJob`; store on `jobState.DuplexSide`; pass to `ExecutePrintJob` → `PrintPdf` |
| | Use `manualFlipDir` to override `flipDirection` heuristic for manual duplex |
| `backend/Services/IWordInteropService.cs` | Add `duplexSide` param to `PrintPdf` interface |
| `backend/Services/WordInteropService.cs` | Thread `duplexSide` through `PrintPdf` → `TryShellPrint` → `PrintWithSumatra`; add `-print-settings` arg |
| `frontend/styles.css` | No changes |
| `frontend/index.html` | No changes |

---

## 10. Out of Scope

- Booklet mode (not affected).
- Simplex mode: `buildSheetLayout` does not group by orientation for simplex, but
  together-mode CCW90 injection in `_renderSheetView` fires regardless of `printMode`.
  This means landscape pages are rotated CCW90 in preview and in the printed PDF when
  simplex is active with together mode. Simplex is not currently user-selectable in the
  UI (mode-select only exposes 'duplex' and 'booklet'), so this has no practical impact.
  If simplex is added to the UI in the future, add a `printMode !== 'simplex'` guard
  to the injection block in §5.2 steps [3]-[5].
- CSS canvas vs img `max-width` mismatch (pre-existing, unrelated to together mode).
- Per-page flip direction overrides.
