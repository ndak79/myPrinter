# Landscape Together Mode — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `landscapeMode = 'together'` — rotate all landscape pages CCW90 to make them portrait-sized, pair pages in original document order, and send `ShortEdge` duplex to backend for all-landscape documents.

**Architecture:** All CCW90 injection happens in `_renderSheetView` (frontend preview), tracked per-file in `_togetherRotations`; teardown restores state on mode switch. Backend threads a `duplexSide` parameter through the print chain to `PrintWithSumatra`, which conditionally emits `-print-settings "duplexshort"/"duplexlong"`.

**Tech Stack:** Vanilla JavaScript (frontend/app.js ~4234 lines), C# / ASP.NET Core (backend), PdfSharp, SumatraPDF CLI.

**Spec:** `docs/superpowers/specs/2026-04-07-landscape-together-mode.md`

---

## Files Modified

| File | Change Summary |
|------|----------------|
| `backend/Models/PrintModels.cs` | Add `DuplexSide?` + `ManualFlipDir?` to `PrintRequest`; add `DuplexSide?` to `PrintJobState` |
| `backend/Services/IWordInteropService.cs` | Add `duplexSide` param to `PrintPdf` interface |
| `backend/Services/PrintAlgorithmService.cs` | Thread `duplexSide` + `manualFlipDir` through `CreateNormalDuplexJob` → `ExecutePrintJob` → `PrintPdf` |
| `backend/Services/WordInteropService.cs` | Thread `duplexSide` → `TryShellPrint` → `PrintWithSumatra`; conditional `-print-settings` arg |
| `backend/BackendStartup.cs` | Pass `duplexSide: request.DuplexSide` + `manualFlipDir: request.ManualFlipDir` to `CreateNormalDuplexJob` |
| `frontend/app.js` | Data model, helper, renderSheetView sequence, view-mode teardown, badge suppression (5 locations), `_startPrint` POST body, default mode change |

---

## Task 1: Backend models — `PrintRequest` and `PrintJobState`

**Files:**
- Modify: `backend/Models/PrintModels.cs:27-48` (PrintRequest class)
- Modify: `backend/Models/PrintModels.cs:82-123` (PrintJobState class)

- [ ] **Step 1: Add fields to `PrintRequest`**

In `backend/Models/PrintModels.cs`, inside the `PrintRequest` class, add two nullable properties after `PageRotations`:

```csharp
// After line 47 (List<PageRotation>? PageRotations ...)
public string? DuplexSide    { get; set; }  // "LongEdge" | "ShortEdge" | null => LongEdge
public string? ManualFlipDir { get; set; }  // "LongEdge" | "ShortEdge" | null => auto-detect
```

- [ ] **Step 2: Add `DuplexSide` nullable property to `PrintJobState`**

In `backend/Models/PrintModels.cs`, inside the `PrintJobState` class, add after `ManualPlan`:

```csharp
// After line 122 (ManualDuplexPlan? ManualPlan ...)
/// <summary>
/// Duplex side to use for auto-duplex print. null = use printer default.
/// "LongEdge" (flip along long edge, standard portrait) or "ShortEdge" (flip along short edge, landscape booklets).
/// </summary>
public string? DuplexSide { get; set; }  // null = no override → PrintWithSumatra emits no -print-settings arg
```

**IMPORTANT:** Must be `string?` (nullable), NOT `string DuplexSide = "LongEdge"`. If non-nullable with default, `ExecutePrintJob` would pass `"LongEdge"` to all three `PrintPdf` call sites (including manual-duplex simplex branches), causing `-print-settings "duplexlong"` to be emitted on simplex printers — breaking them.

- [ ] **Step 3: Verify build**

```powershell
cd D:\Pro\myPrinter\backend
dotnet build
```
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/Models/PrintModels.cs
git commit -m "feat(backend): add DuplexSide + ManualFlipDir fields to PrintRequest and PrintJobState"
```

---

## Task 2: Backend interface — `IWordInteropService.PrintPdf`

**Files:**
- Modify: `backend/Services/IWordInteropService.cs:13`

- [ ] **Step 1: Add `duplexSide` param to interface**

In `backend/Services/IWordInteropService.cs`, change line 13:

```csharp
// Before:
void PrintPdf(string pdfPath, string printerName, string? pageRange = null);

// After:
void PrintPdf(string pdfPath, string printerName, string? pageRange = null, string? duplexSide = null);
```

- [ ] **Step 2: Verify build**

```powershell
cd D:\Pro\myPrinter\backend
dotnet build
```
Expected: Build errors about `WordInteropService` not implementing the updated interface. That's correct — we fix the implementation in Task 3.

---

## Task 3: Backend implementation — `WordInteropService` SumatraPDF duplexSide

**Files:**
- Modify: `backend/Services/WordInteropService.cs:420` (`PrintPdf`)
- Modify: `backend/Services/WordInteropService.cs:475` (`TryShellPrint`)
- Modify: `backend/Services/WordInteropService.cs:510` (`PrintWithSumatra`)

- [ ] **Step 1: Update `PrintPdf` signature and thread `duplexSide`**

In `backend/Services/WordInteropService.cs`, update the `PrintPdf` method (line ~420):

```csharp
// Change the signature (line 420):
public void PrintPdf(string pdfPath, string printerName, string? pageRange = null, string? duplexSide = null)
{
    Console.WriteLine($"[PrintPdf] Printing PDF: {pdfPath}");
    Console.WriteLine($"[PrintPdf] Printer: {printerName}");
    Console.WriteLine($"[PrintPdf] Page range: {pageRange ?? "all"}");
    Console.WriteLine($"[PrintPdf] DuplexSide: {duplexSide ?? "default"}");

    string fileToPrint = pdfPath;
    bool isTempFile = false;

    try
    {
        if (!string.IsNullOrEmpty(pageRange))
        {
            Console.WriteLine($"[PrintPdf] Splitting PDF for page range: {pageRange}");
            fileToPrint = CreateTempPdfWithPages(pdfPath, pageRange);
            isTempFile = true;
            Console.WriteLine($"[PrintPdf] Created temp PDF for printing: {fileToPrint}");
        }

        // Pass duplexSide through to TryShellPrint
        bool printSuccess = TryShellPrint(fileToPrint, printerName, duplexSide);

        if (!printSuccess)
        {
            Console.WriteLine($"[PrintPdf] Shell print failed, trying PowerShell fallback...");
            TryPowerShellPrint(fileToPrint, printerName);
            // NOTE: TryPowerShellPrint does NOT support duplexSide — acceptable degraded behavior
        }

        Console.WriteLine($"[PrintPdf] Print job sent successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[PrintPdf ERROR] {ex.Message}");
    }
    finally
    {
        if (isTempFile)
        {
            System.Threading.Thread.Sleep(5000);
            try
            {
                File.Delete(fileToPrint);
                Console.WriteLine($"[PrintPdf] Cleaned up temp file: {fileToPrint}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PrintPdf WARNING] Failed to delete temp file: {ex.Message}");
            }
        }
    }
}
```

- [ ] **Step 2: Update `TryShellPrint` signature and thread `duplexSide`**

In `backend/Services/WordInteropService.cs`, update `TryShellPrint` (line ~475):

```csharp
private bool TryShellPrint(string filePath, string printerName, string? duplexSide = null)
{
    try
    {
        var sumatraPath = FindSumatraPdf();
        if (sumatraPath != null)
        {
            Console.WriteLine($"[TryShellPrint] Found SumatraPDF at: {sumatraPath}");
            return PrintWithSumatra(sumatraPath, filePath, printerName, duplexSide);
        }

        // Strategy 2: PrintBySwappingDefaultPrinter does NOT support duplexSide — acceptable degraded behavior
        Console.WriteLine($"[TryShellPrint] SumatraPDF not found. Using default-printer swap strategy.");
        return PrintBySwappingDefaultPrinter(filePath, printerName);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[TryShellPrint ERROR] {ex.Message}");
        return false;
    }
}
```

- [ ] **Step 3: Update `PrintWithSumatra` with correct SumatraPDF syntax**

In `backend/Services/WordInteropService.cs`, update `PrintWithSumatra` (line ~510):

```csharp
private bool PrintWithSumatra(string sumatraPath, string pdfPath, string printerName, string? duplexSide = null)
{
    try
    {
        // Only emit -print-settings when duplexSide is explicitly set.
        // null → no arg → preserve existing printer behavior (no regression for simplex/booklet/manual-duplex).
        // CORRECT SumatraPDF syntax: "duplexshort" and "duplexlong" (NOT "short"/"long").
        var settingsPart = duplexSide switch {
            "ShortEdge" => "-print-settings \"duplexshort\" ",
            "LongEdge"  => "-print-settings \"duplexlong\" ",
            _           => ""   // null or unknown → no override
        };
        var args = $"-print-to \"{printerName}\" {settingsPart}\"{pdfPath}\"";
        Console.WriteLine($"[PrintWithSumatra] Args: {args}");

        var psi = new System.Diagnostics.ProcessStartInfo(sumatraPath, args)
        {
            UseShellExecute = false,
            CreateNoWindow  = true,
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null)
        {
            Console.WriteLine("[PrintWithSumatra] Failed to start SumatraPDF process.");
            return false;
        }
        bool completed = proc.WaitForExit(60_000);
        Console.WriteLine($"[PrintWithSumatra] Exit code: {proc.ExitCode}, completed in time: {completed}");
        return completed && proc.ExitCode == 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[PrintWithSumatra ERROR] {ex.Message}");
        return false;
    }
}
```

- [ ] **Step 4: Verify build**

```powershell
cd D:\Pro\myPrinter\backend
dotnet build
```
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add backend/Services/IWordInteropService.cs backend/Services/WordInteropService.cs
git commit -m "feat(backend): thread duplexSide through PrintPdf→TryShellPrint→PrintWithSumatra with correct SumatraPDF syntax"
```

---

## Task 4: Backend algorithm — thread `duplexSide` + `manualFlipDir`

**Files:**
- Modify: `backend/Services/PrintAlgorithmService.cs:23-31` (signature)
- Modify: `backend/Services/PrintAlgorithmService.cs:42` (flipDirection derivation)
- Modify: `backend/Services/PrintAlgorithmService.cs:44-49` (jobState init)
- Modify: `backend/Services/PrintAlgorithmService.cs:565-582` (ExecutePrintJob auto-duplex branch)
- Modify: `backend/BackendStartup.cs:193-194` (call site)

- [ ] **Step 1: Update `CreateNormalDuplexJob` signature**

In `backend/Services/PrintAlgorithmService.cs`, update the method signature (lines 23-31):

```csharp
public PrintJobState CreateNormalDuplexJob(
    string pdfPath,
    string printerName,
    bool isDuplexPrinter,
    string? pageRange = null,
    int[]? singleSidedPages = null,
    WatermarkOptions? watermark = null,
    int[]? pageOrder = null,
    List<PageRotation>? pageRotations = null,
    string? duplexSide = null,      // NEW: "LongEdge" | "ShortEdge" | null
    string? manualFlipDir = null)   // NEW: "LongEdge" | "ShortEdge" | null => auto-detect
{
```

- [ ] **Step 2: Replace `flipDirection` heuristic (line 42)**

Replace this line:
```csharp
var flipDirection = pdfInfo.IsLandscape ? FlipDirection.ShortEdge : FlipDirection.LongEdge;
```

With:
```csharp
// Use frontend-provided manualFlipDir when available (overrides first-page heuristic).
// In together mode the output PDF is all-portrait after CCW90, so pdfInfo.IsLandscape
// would return false even for all-landscape docs → wrong LongEdge flip.
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

- [ ] **Step 3: Store `duplexSide` on `jobState` (after line 44-49 jobState init)**

After the `var jobState = new PrintJobState { ... }` block (after line 49), add:

```csharp
// Store duplexSide (nullable — do NOT coalesce to "LongEdge").
// null means "no explicit override" → PrintWithSumatra will emit no -print-settings arg.
jobState.DuplexSide = duplexSide;
```

- [ ] **Step 4: Pass `duplexSide` in `ExecutePrintJob` auto-duplex branch only**

In `backend/Services/PrintAlgorithmService.cs`, update `ExecutePrintJob` (lines 577-581), the auto-duplex branch ONLY:

```csharp
// Auto-duplex branch (line ~577) — ADD duplexSide:
_wordService.PrintPdf(
    jobState.TempPdfPath,
    jobState.PrinterName,
    pageRange: null,
    duplexSide: jobState.DuplexSide   // NEW: null for booklet/default, "ShortEdge" for all-landscape
);
```

**Do NOT change** the manual-duplex branches at lines ~608 and ~636. They print to simplex printers and must not receive `duplexSide`.

- [ ] **Step 5: Update `BackendStartup.cs` call site**

In `backend/BackendStartup.cs`, update the `CreateNormalDuplexJob` call (line ~193):

```csharp
// Before:
jobState = printAlgorithm.CreateNormalDuplexJob(filePath, request.PrinterName, printer.IsDuplex,
    request.PageRange, request.SingleSidedPages, request.Watermark, request.PageOrder, request.PageRotations);

// After:
jobState = printAlgorithm.CreateNormalDuplexJob(filePath, request.PrinterName, printer.IsDuplex,
    request.PageRange, request.SingleSidedPages, request.Watermark, request.PageOrder, request.PageRotations,
    duplexSide:    request.DuplexSide,      // NEW §6.3
    manualFlipDir: request.ManualFlipDir);  // NEW §6.6
```

- [ ] **Step 6: Verify build**

```powershell
cd D:\Pro\myPrinter\backend
dotnet build
```
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 7: Commit**

```bash
git add backend/Services/PrintAlgorithmService.cs backend/BackendStartup.cs
git commit -m "feat(backend): thread duplexSide+manualFlipDir through CreateNormalDuplexJob and ExecutePrintJob"
```

---

## Task 5: Frontend data model — `createFileEntry` + `_teardownTogether` helper

**Files:**
- Modify: `frontend/app.js:59-70` (`createFileEntry`)
- Modify: `frontend/app.js` (add `_teardownTogether` as a standalone function after `createFileEntry`)

- [ ] **Step 1: Add two new fields to `createFileEntry`**

In `frontend/app.js`, update `createFileEntry` (lines 59-70) to add two new fields:

```javascript
createFileEntry(id, name, needsConversion) {
    return {
        id, name, needsConversion,
        pdfDoc:                  null,
        totalPageCount:          0,
        selectedPages:           new Set(),
        singleSidedPages:        new Set(),
        pageOrder:               [],
        pageRotations:           new Map(),
        blankAbsorbedBy:         new Map(), // populated by buildSheetLayout; reset each render cycle
        _togetherRotations:      new Set(), // Set<pageNum> — pages auto-rotated by together mode
        _originalOrientationMap: null,      // Map<pageNum, bool> | null — pre-injection snapshot
    };
},
```

- [ ] **Step 2: Add `_teardownTogether` helper function**

Add the following standalone function after the `AppState` block (after line 111, before the `lookAheadOrientation` function at line 113):

```javascript
// ─── _teardownTogether ────────────────────────────────────────────
// Restores fileEntry to pre-together-mode state. Idempotent.
// Removes only auto-injected CCW90 rotations (tracked in _togetherRotations).
// User-set rotations (not in _togetherRotations) are never touched.
function _teardownTogether(fileEntry) {
    if (!fileEntry) return;
    for (const p of fileEntry._togetherRotations) {
        fileEntry.pageRotations.delete(p);
        fileEntry._orientationMap?.delete(p);  // force re-detect at original orientation
    }
    fileEntry._togetherRotations.clear();
    fileEntry._originalOrientationMap = null;
}
```

- [ ] **Step 3: Manual verification**

Open the app in browser. Upload any PDF. Open DevTools console. Verify no errors.
Run: `AppState.createFileEntry('test', 'test.pdf', false)` in console.
Expected: object with `_togetherRotations: Set(0) {}` and `_originalOrientationMap: null`.

- [ ] **Step 4: Commit**

```bash
git add frontend/app.js
git commit -m "feat(frontend): add _togetherRotations + _originalOrientationMap to createFileEntry; add _teardownTogether helper"
```

---

## Task 6: Frontend — remove `buildSheetLayout` warn-guard

**Files:**
- Modify: `frontend/app.js:275-277`

- [ ] **Step 1: Remove the warn-guard block**

In `frontend/app.js`, remove lines 275-277:

```javascript
// REMOVE these 3 lines entirely:
if (landscapeMode !== 'separate') {
    console.warn('[buildSheetLayout] landscapeMode must be "separate"; got', landscapeMode, '— treating as separate');
}
```

The surrounding code before (line 273) and after (line 279) should remain:
- Before: `} else {` (the duplex branch opener)
- After: `// ── Bước 1: Group pages by orientation ──`

- [ ] **Step 2: Verify build** (no build step for JS — just check syntax)

Open the app in browser. Open DevTools console. Upload a PDF and switch to sheet view. Expected: no errors in console.

- [ ] **Step 3: Commit**

```bash
git add frontend/app.js
git commit -m "feat(frontend): remove buildSheetLayout warn-guard for together mode"
```

---

## Task 7: Frontend — `_renderSheetView` together-mode sequence

This is the core frontend change. It inserts the CCW90 injection logic into `_renderSheetView`.

**Files:**
- Modify: `frontend/app.js:3263-3309` (`_renderSheetView`)

- [ ] **Step 1: Add defensive teardown at top of `_renderSheetView` (step [0])**

In `frontend/app.js`, in `_renderSheetView` (line 3263), add immediately after the guard on line 3264:

```javascript
async _renderSheetView(fileEntry) {
    if (!this._container || !fileEntry?.pdfDoc) return;

    // [0] DEFENSIVE TEARDOWN: clean up stale together-mode state if mode was switched
    // while a different file was being viewed.
    if (AppState.landscapeMode !== 'together' && fileEntry._togetherRotations?.size > 0) {
        _teardownTogether(fileEntry);
    }

    // existing cleanup continues below...
    this._renderQueue = [];
```

- [ ] **Step 2: Add `_blobQueue` and `_activeBlobRenders` reset to the cleanup block**

In `frontend/app.js`, in the existing cleanup block (lines 3266-3273), add two resets:

```javascript
this._renderQueue = [];
this._activeRenders = 0;
for (const t of this._renderTasks.values()) { try { t.cancel(); } catch(_){} }
this._renderTasks.clear();
this._currentFileId = fileEntry.id;
this._pageEls.clear();
this._sheetEls.clear();
this._container.innerHTML = '';
// NEW: reset blob pipeline to prevent stale renders draining into new cycle
this._blobQueue = [];
this._activeBlobRenders = 0;
```

- [ ] **Step 3: Add steps [3]–[5] after the existing async guard (line 3305)**

After line 3305 (`if (this._currentFileId !== fileEntry.id) return;`), insert the together-mode logic. This replaces the existing guard comment and inserts new code before `buildSheetLayout`:

```javascript
        // Guard: user may have switched file during async detection
        if (this._currentFileId !== fileEntry.id) return;  // [2b] existing guard — stays here

        // ── TOGETHER MODE: snapshot → inject CCW90 → invalidate cache ──────────
        if (AppState.landscapeMode === 'together') {
            // [3] SNAPSHOT — taken once per file (null-guard prevents re-render overwrite)
            if (fileEntry._originalOrientationMap == null) {  // == catches both null and undefined
                // Detect INTRINSIC orientation (rotation=0, ignoring user pageRotations).
                // Step [2]'s cache-fill applies current pageRotations, so a user-rotated
                // portrait page could appear as landscape. The allLandscape check in _startPrint
                // must use physical PDF dimensions only.
                const intrinsicPromises = [];
                for (let p = 1; p <= fileEntry.totalPageCount; p++) {
                    intrinsicPromises.push(
                        pdfDoc.getPage(p).then(page => {
                            const vp = page.getViewport({ scale: 1, rotation: 0 });
                            return { p, isLandscape: vp.width > vp.height };
                        }).catch(() => ({ p, isLandscape: false }))
                    );
                }
                const intrinsicResults = await Promise.all(intrinsicPromises);
                const intrinsicMap = new Map();
                for (const { p, isLandscape } of intrinsicResults) {
                    intrinsicMap.set(p, isLandscape);
                }
                fileEntry._originalOrientationMap = intrinsicMap;
            }

            // [6 - NEW] Guard after intrinsic detection await
            if (this._currentFileId !== fileEntry.id) return;

            // [4] INJECT CCW90 for intrinsically landscape pages not already manually rotated
            for (let p = 1; p <= fileEntry.totalPageCount; p++) {
                if (fileEntry._originalOrientationMap.get(p) === true) {  // intrinsically landscape
                    if (!fileEntry.pageRotations.has(p)) {                // not manually rotated
                        fileEntry.pageRotations.set(p, 'CCW90');
                        fileEntry._togetherRotations.add(p);
                    }
                }
            }

            // [5] INVALIDATE STALE CACHE — synchronous, result is deterministic
            for (const p of fileEntry._togetherRotations) {
                orientationMap.delete(p);
            }
            for (const p of fileEntry._togetherRotations) {
                orientationMap.set(p, false);  // CCW90 makes landscape pages portrait-sized
            }
        }
        // ── END TOGETHER MODE ────────────────────────────────────────────────────

        const printMode = AppState.printMode;
        const { sheets, blankAbsorbedBy, deselectedPages } = buildSheetLayout(fileEntry, printMode, orientationMap, AppState.landscapeMode);
```

- [ ] **Step 4: Manual verification in browser**

1. Upload a PDF with a mix of portrait and landscape pages (e.g. a document where some pages are landscape)
2. Switch to sheet view
3. In the modebar, select "Together" mode
4. Expected: landscape pages appear rotated CCW90 (now portrait-sized), all pages pair in document order across orientation boundaries (e.g. page 1L pairs with page 2P on Sheet 1)
5. Expected: NO `-90°` badges on auto-rotated pages (this comes in Task 9)

- [ ] **Step 5: Commit**

```bash
git add frontend/app.js
git commit -m "feat(frontend): implement together-mode CCW90 injection in _renderSheetView (steps 0-6)"
```

---

## Task 8: Frontend — view-mode teardown and modebar handler

**Files:**
- Modify: `frontend/app.js:3199-3204` (`PreviewPanelModule.render`)
- Modify: `frontend/app.js:4151-4159` (modebar click handler)

- [ ] **Step 1: Add teardown in `PreviewPanelModule.render()` on sheet→page switch**

In `frontend/app.js`, in the `render(fileEntry)` method (line 3199), add a teardown call before the viewMode dispatch (between lines 3201 and 3203):

```javascript
render(fileEntry) {
    // Invariant 3: reset blankAbsorbedBy before each rebuild
    if (fileEntry) fileEntry.blankAbsorbedBy = new Map();

    // §5.0: Teardown together-mode CCW90 when leaving sheet view.
    // _renderSheetView is never called in page view, so its defensive teardown (step [0])
    // would not fire — stale CCW90 rotations would show as badges in page view.
    if (this._viewMode !== 'sheet' && fileEntry?._togetherRotations?.size > 0) {
        _teardownTogether(fileEntry);
    }

    if (this._viewMode === 'sheet') {
        return this._renderSheetView(fileEntry);
    }
    // ... rest unchanged
```

- [ ] **Step 2: Add teardown in modebar click handler**

In `frontend/app.js`, update the modebar click handler (lines 4151-4159):

```javascript
modeBar.addEventListener('click', e => {
    const btn = e.target.closest('[data-lsmode]');
    if (!btn) return;
    const newMode = btn.dataset.lsmode;

    // §5.4: Teardown together-mode state before switching away from together.
    if (AppState.landscapeMode === 'together' && newMode !== 'together') {
        if (AppState.activeFile) _teardownTogether(AppState.activeFile);
    }

    AppState.landscapeMode = newMode;
    modeBar.querySelectorAll('.sheet-modebar-btn').forEach(b => {
        b.classList.toggle('active', b.dataset.lsmode === AppState.landscapeMode);
    });
    PreviewPanelModule.render(AppState.activeFile);
});
```

- [ ] **Step 3: Manual verification**

1. Upload a PDF with landscape pages, switch to sheet view, enable together mode
2. Switch to "Separate" mode in the modebar → landscape pages should return to original landscape orientation (no stale CCW90)
3. Switch back to "Together" mode → CCW90 re-injected correctly
4. Switch from sheet view to page view (click "Page" view toggle) → page view should show original orientation (no stale CCW90 badges)
5. Switch back to sheet view → together mode active again, CCW90 re-injected

- [ ] **Step 4: Commit**

```bash
git add frontend/app.js
git commit -m "feat(frontend): add together-mode teardown in render() view-switch and modebar handler"
```

---

## Task 9: Frontend — badge suppression (5 locations)

**Files:**
- Modify: `frontend/app.js:3607-3608` (`_renderSheetPage`)
- Modify: `frontend/app.js:3806-3807` (`_syncSelectionUI`)
- Modify: `frontend/app.js:4025-4032` (`ThumbStripModule._renderThumb`)
- Modify: `frontend/app.js:4088-4096` (`ThumbStripModule._syncSelectionHighlights`)
- Modify: `frontend/app.js:1703-1705` (`ZoomModal._buildPageContainer`)

- [ ] **Step 1: Suppress badge in `_renderSheetPage` (line 3607-3608)**

```javascript
// Before (lines 3607-3608):
const rotation = fileEntry.pageRotations?.get(pageNum) ?? null;
RotationHelper.updateBadge(el, rotation);

// After:
const rotation      = fileEntry.pageRotations?.get(pageNum) ?? null;
const badgeRotation = fileEntry._togetherRotations?.has(pageNum) ? null : rotation;
RotationHelper.updateBadge(el, badgeRotation);
```

- [ ] **Step 2: Suppress badge in `_syncSelectionUI` (line 3806-3807)**

```javascript
// Before (lines 3806-3807):
const rotation = activeFile.pageRotations?.get(pageNum) ?? null;
RotationHelper.updateBadge(card, rotation);

// After:
const rotation      = activeFile.pageRotations?.get(pageNum) ?? null;
const badgeRotation = activeFile._togetherRotations?.has(pageNum) ? null : rotation;
RotationHelper.updateBadge(card, badgeRotation);
```

- [ ] **Step 3: Suppress badge in `ThumbStripModule._renderThumb` (line 4032)**

```javascript
// Before (line 4032):
RotationHelper.updateBadge(el, rotation);

// After (rotation was read on line 4026, fileEntry resolved on line 4020):
const badgeRotation = fileEntry._togetherRotations?.has(pageNum) ? null : rotation;
RotationHelper.updateBadge(el, badgeRotation);
```

- [ ] **Step 4: Suppress badge in `ThumbStripModule._syncSelectionHighlights` (line 4095-4096)**

```javascript
// Before (lines 4095-4096):
const rotation = entry.pageRotations?.get(pageNum) ?? null;
RotationHelper.updateBadge(el, rotation);

// After:
const rotation      = entry.pageRotations?.get(pageNum) ?? null;
const badgeRotation = entry._togetherRotations?.has(pageNum) ? null : rotation;
RotationHelper.updateBadge(el, badgeRotation);
```

- [ ] **Step 5: Suppress badge in `ZoomModal._buildPageContainer` (line 1705)**

```javascript
// Before (line 1704-1705):
canvas.style.transform = RotationHelper.toCSS(rotation);
RotationHelper.updateBadge(div, rotation);

// After:
canvas.style.transform = RotationHelper.toCSS(rotation);
const badgeRotation = AppState.activeFile?._togetherRotations?.has(n) ? null : rotation;
RotationHelper.updateBadge(div, badgeRotation);
```

Note: In `ZoomModal._buildPageContainer`, the page number variable is `n` (from the surrounding loop context at line ~1680 `for (let n = 1; n <= count; n++)`). Verify the exact variable name by reading lines 1680-1706.

- [ ] **Step 6: Manual verification**

1. Upload a PDF with landscape pages, enable together mode in sheet view
2. Verify: No `-90°` badges visible on any auto-rotated landscape pages in the sheet thumbnails
3. Click/select pages → no badges appear on auto-rotated pages
4. Open zoom modal on an auto-rotated page → no badge
5. Check left thumb strip → no badges on auto-rotated pages
6. Manually rotate a non-landscape page (right-click → rotate) → badge SHOULD still appear for user-rotated pages

- [ ] **Step 7: Commit**

```bash
git add frontend/app.js
git commit -m "feat(frontend): suppress rotation badges for together-mode auto-rotated pages (5 locations)"
```

---

## Task 10: Frontend — `ContextMenu._applyRotation` evict from `_togetherRotations`

**Files:**
- Modify: `frontend/app.js:1949-1986` (`ContextMenu._applyRotation`)

- [ ] **Step 1: Add eviction after rotation is applied**

In `frontend/app.js`, in `_applyRotation` (line 1949), add the eviction after the rotation is applied but before the cache invalidation block (after line 1958, before line 1960):

```javascript
_applyRotation(pageNum, rotation) {
    if (pageNum === null || pageNum === 0) return;
    if (rotation === null) {
        AppState.pageRotations.delete(pageNum);
        showToast(`Trang ${pageNum}: đã reset xoay`, 'info');
    } else {
        AppState.pageRotations.set(pageNum, rotation);
        const labels = { CW90: 'Xoay phải 90°', CCW90: 'Xoay trái 90°', Rotate180: 'Xoay 180°', FlipHorizontal: 'Lật ngang', FlipVertical: 'Lật dọc' };
        showToast(`Trang ${pageNum}: ${labels[rotation] || rotation}`, 'info');
    }

    // §5.7 (I3 invariant): If page was auto-rotated by together mode, evict it from
    // _togetherRotations. It is now "owned" by the user — teardown will not touch it.
    // Edge case: user resets rotation to null on a landscape page → p evicted, pageRotations
    // deleted → next _renderSheetView re-injects CCW90 (correct behavior).
    const fileEntry = AppState.activeFile;
    if (fileEntry?._togetherRotations?.has(pageNum)) {
        fileEntry._togetherRotations.delete(pageNum);
    }

    // Invalidate cached renders for this page (existing code below unchanged)
    if (fileEntry) {
        // ... rest of existing code unchanged
```

- [ ] **Step 2: Manual verification**

1. Upload a PDF with landscape pages, enable together mode
2. Right-click a landscape page that was auto-rotated (no badge) → rotate CW90
3. Badge should appear (CW90) — page is now "owned" by user
4. Switch to Separate mode → the manually-rotated page keeps its CW90 (teardown skipped it)
5. Switch back to Together mode → other landscape pages get CCW90 again; the user-rotated page is unaffected

- [ ] **Step 3: Commit**

```bash
git add frontend/app.js
git commit -m "feat(frontend): evict page from _togetherRotations when user manually rotates it (I3 invariant)"
```

---

## Task 11: Frontend — `_startPrint` POST body with `duplexSide`

**Files:**
- Modify: `frontend/app.js:2237-2252` (`_startPrint` body construction)

- [ ] **Step 1: Add `duplexSide` and `manualFlipDir` to POST body**

In `frontend/app.js`, in the `_startPrint` method, update the `body` object construction (lines 2237-2252):

```javascript
// Compute duplexSide for together mode
let duplexSide = 'LongEdge';  // default for all cases
if (AppState.landscapeMode === 'together' && file._originalOrientationMap != null) {
    let allLandscape = true;
    for (let p = 1; p <= file.totalPageCount; p++) {
        if (file._originalOrientationMap.get(p) !== true) {
            allLandscape = false;
            break;
        }
    }
    if (allLandscape) duplexSide = 'ShortEdge';
}

const body = {
    fileId:           file.id,
    printerName:      AppState.selectedPrinter.name,
    mode:             modeCode,
    pageRange,
    singleSidedPages: file.singleSidedPages.size > 0 ? Array.from(file.singleSidedPages) : null,
    copies:           CopiesModule.copies,
    collate:          CopiesModule.collate,
    pageOrder:        (() => {
        const effective = buildEffectivePageOrder(file);
        return effective.length > 0 ? effective : null;
    })(),
    pageRotations:    file.pageRotations.size > 0
        ? Array.from(file.pageRotations.entries()).map(([pageNumber, rotation]) => ({ pageNumber, rotation }))
        : null,
    duplexSide,       // NEW: 'LongEdge' | 'ShortEdge'
    manualFlipDir:    duplexSide,  // NEW: same value; maps to PrintRequest.ManualFlipDir on backend
};
```

Note: The `duplexSide` computation block must go BEFORE the `const body = {...}` line.

- [ ] **Step 2: Manual verification**

1. Upload an all-landscape PDF (e.g. a spreadsheet or wide document exported as landscape pages)
2. Enable together mode in sheet view
3. Open DevTools Network tab
4. Click "In" (print button)
5. Inspect the POST request to `/api/print` → look at the request body
6. Expected: `"duplexSide": "ShortEdge"` and `"manualFlipDir": "ShortEdge"` in the JSON body
7. For a mixed or all-portrait document: expected `"duplexSide": "LongEdge"` and `"manualFlipDir": "LongEdge"`

- [ ] **Step 3: Commit**

```bash
git add frontend/app.js
git commit -m "feat(frontend): compute duplexSide from _originalOrientationMap in _startPrint; send to backend"
```

---

## Task 12: Frontend — change `AppState.landscapeMode` default to `'together'`

**Files:**
- Modify: `frontend/app.js:34`

- [ ] **Step 1: Change the default**

In `frontend/app.js`, line 34, change:

```javascript
// Before:
landscapeMode:         'separate', // 'separate' | 'together' ('together' not backend-supported — see §4.8)

// After:
landscapeMode:         'together', // 'separate' | 'together'
```

Note: `§5.1` (warn-guard removal in `buildSheetLayout`) was done in Task 6, so this change is now safe. The HTML already marks `data-lsmode="together"` as `active` — this reconciles the code default with the UI state.

- [ ] **Step 2: Manual end-to-end verification**

1. Open the app fresh (no file loaded) → sheet modebar should show "Together" active by default
2. Upload a PDF with landscape pages → switch to sheet view → together mode should be active immediately
3. Landscape pages should be CCW90-rotated (portrait-sized) and paired with adjacent pages
4. Switch to separate mode → orientation-grouped layout returns
5. Switch back to together → CCW90 injection returns
6. For an all-landscape PDF → print → verify backend receives `duplexSide: "ShortEdge"`
7. For a mixed PDF → print → verify backend receives `duplexSide: "LongEdge"`

- [ ] **Step 3: Commit**

```bash
git add frontend/app.js
git commit -m "feat(frontend): change default landscapeMode from 'separate' to 'together' (reconciles HTML active state)"
```

---

## Self-Review Checklist

### Spec Coverage

| Spec Section | Task |
|---|---|
| §3.1 `createFileEntry` fields | Task 5 |
| §3.2 default landscapeMode | Task 12 |
| §3.3 POST body fields | Task 11 |
| §4 `_teardownTogether` helper | Task 5 |
| §5.0 view-mode teardown | Task 8 |
| §5.1 warn-guard removal | Task 6 |
| §5.2 steps [0]-[7] sequence | Tasks 7, 8 |
| §5.3 cleanup _blobQueue reset | Task 7 |
| §5.4 modebar handler teardown | Task 8 |
| §5.5 badge in `_renderSheetPage` | Task 9 |
| §5.5b badge in ThumbStripModule | Task 9 |
| §5.5c badge in ZoomModal | Task 9 |
| §5.6 badge in `_syncSelectionUI` | Task 9 |
| §5.7 evict on user rotate | Task 10 |
| §5.8 `_startPrint` duplexSide | Task 11 |
| §6.1 PrintRequest/PrintJobState | Task 1 |
| §6.2 BackendStartup call site | Task 4 |
| §6.3 CreateNormalDuplexJob signature | Task 4 |
| §6.4 PrintJobState.DuplexSide | Task 1 |
| §6.5 WordInteropService chain | Tasks 2, 3 |
| §6.6 flipDirection override | Task 4 |

All spec sections covered. ✓

### Type Consistency Check

- `_togetherRotations` — `Set` in createFileEntry (Task 5), `.has()` / `.add()` / `.delete()` / `.clear()` throughout ✓
- `_originalOrientationMap` — `null` initial (Task 5), `Map<number, boolean>` after snapshot (Task 7), reset to `null` in teardown (Task 5) ✓
- `DuplexSide` on `PrintJobState` — `string?` nullable (Task 1), set as-is `= duplexSide` (Task 4), passed as `duplexSide: jobState.DuplexSide` (Task 4) ✓
- `duplexSide` in `PrintWithSumatra` — switch on `"ShortEdge"` / `"LongEdge"` / `_` (Task 3) ✓
- Frontend sends `duplexSide: 'LongEdge' | 'ShortEdge'` (Task 11) — backend `Enum.TryParse<FlipDirection>` matches enum values `LongEdge`, `ShortEdge` exactly ✓
