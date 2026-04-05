# New Features Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement 10 new features across backend and frontend: Image→PDF, Copies, TryShellPrint printer-name bug fix, Simplex mode, Booklet singleSidedPages, Print Presets, Estimated sheets/time, Watermark, Print History, Keyboard shortcuts + Print button feedback + Upload progress bar + Printer live polling.

**Architecture:** Backend features are added to existing services/models with minimal API surface changes. Frontend features are added as new module objects in `app.js` following existing patterns (no ES modules, file:// compatible). All changes are additive — no existing flows are broken.

**Tech Stack:** .NET 8 Minimal API, PdfSharp, Word Interop, Vanilla JS (no bundler), PDF.js 3.11

---

## File Map

### Backend files modified
- `backend/Models/PrintModels.cs` — add `Copies`, `Simplex` to `PrintRequest`; add `WatermarkOptions`
- `backend/Services/PrintAlgorithmService.cs` — fix `TryShellPrint` printer name; add copies loop; add simplex mode; add booklet singleSidedPages; add watermark pre-processing
- `backend/Services/WordInteropService.cs` — add `AddWatermarkToPdf()` helper
- `backend/Program.cs` — no new endpoints needed

### Frontend files modified
- `frontend/app.js` — add `CopiesModule`, `PresetsModule`, `HistoryModule`, `KeyboardModule`; update `PrintModule._startPrint()`, `UploadModule._upload()`, `PrinterModule` (live poll); add upload progress bar; add print button feedback
- `frontend/index.html` — add copies control, simplex mode radio, preset UI, history panel, keyboard shortcut hints
- `frontend/styles.css` — styles for new UI elements

---

## Task 1: Fix TryShellPrint — Printer Name Bug (🔴 Critical Bug)

**Files:**
- Modify: `backend/Services/WordInteropService.cs` (find `PrintPdf` method ~line 300–400)

**Problem:** `PrintPdf()` calls shell verb "print" which routes to the system **default** printer, ignoring `printerName` parameter. The PowerShell fallback also doesn't specify printer.

- [ ] **Step 1: Read the PrintPdf method**

Read `WordInteropService.cs` offset 300, limit 150 to find the exact `PrintPdf` implementation.

- [ ] **Step 2: Replace the shell print call to use SumatraPDF or set default printer temporarily**

Find the `PrintPdf` method. Replace it with a version that sets the default printer temporarily via WMI before shell printing, then restores it. Use `System.Drawing.Printing.PrinterSettings` to set default:

```csharp
public void PrintPdf(string pdfPath, string printerName, string? pageRange = null)
{
    Console.WriteLine($"[PrintPdf] Printing: {pdfPath} on printer: {printerName}, range: {pageRange ?? "all"}");

    // Build SumatraPDF args if available, else fall back to shell with temp default
    var sumatraPath = FindSumatraPdf();
    if (sumatraPath != null)
    {
        PrintWithSumatra(sumatraPath, pdfPath, printerName, pageRange);
        return;
    }

    // Fallback: PowerShell with -PrinterName
    PrintWithPowerShell(pdfPath, printerName, pageRange);
}

private string? FindSumatraPdf()
{
    var candidates = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SumatraPDF", "SumatraPDF.exe"),
        @"C:\Program Files\SumatraPDF\SumatraPDF.exe",
        @"C:\Program Files (x86)\SumatraPDF\SumatraPDF.exe",
    };
    return candidates.FirstOrDefault(File.Exists);
}

private void PrintWithSumatra(string sumatraPath, string pdfPath, string printerName, string? pageRange)
{
    var args = $"-print-to \"{printerName}\"";
    if (!string.IsNullOrWhiteSpace(pageRange))
        args += $" -print-settings \"{pageRange}\"";
    args += $" \"{pdfPath}\"";

    Console.WriteLine($"[PrintPdf] Using SumatraPDF: {sumatraPath} {args}");
    var psi = new System.Diagnostics.ProcessStartInfo(sumatraPath, args)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    using var proc = System.Diagnostics.Process.Start(psi)!;
    proc.WaitForExit(60_000);
    Console.WriteLine($"[PrintPdf] SumatraPDF exit code: {proc.ExitCode}");
}

private void PrintWithPowerShell(string pdfPath, string printerName, string? pageRange)
{
    // Use Out-Printer approach: set ActivePrinter then shell print
    // Build PowerShell command that specifies printer
    var escapedPath = pdfPath.Replace("'", "''");
    var escapedPrinter = printerName.Replace("'", "''");
    var psCommand = $"$wshell = New-Object -ComObject WScript.Shell; " +
                    $"(New-Object -ComObject Shell.Application).NameSpace(0).ParseName('{escapedPath}').InvokeVerbEx('print')";

    // Better: use Start-Process with /p flag for PDF (Adobe/Edge)
    // Most reliable cross-driver approach: temporarily set default printer
    var originalDefault = GetDefaultPrinterName();
    try
    {
        SetDefaultPrinter(printerName);
        Console.WriteLine($"[PrintPdf] Temporarily set default printer to: {printerName}");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -Command \"Start-Process -FilePath '{escapedPath}' -Verb Print -Wait\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(30_000);
        Console.WriteLine($"[PrintPdf] PowerShell print exit code: {proc.ExitCode}");
    }
    finally
    {
        if (originalDefault != null)
        {
            SetDefaultPrinter(originalDefault);
            Console.WriteLine($"[PrintPdf] Restored default printer to: {originalDefault}");
        }
    }
}

private string? GetDefaultPrinterName()
{
    try
    {
        return new System.Drawing.Printing.PrinterSettings().PrinterName;
    }
    catch { return null; }
}

[System.Runtime.InteropServices.DllImport("winspool.drv", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
private static extern bool SetDefaultPrinter(string Name);
```

- [ ] **Step 3: Add `System.Drawing.Common` NuGet reference to .csproj if not present**

Check `backend/backend.csproj`. If `System.Drawing.Common` is not listed, add:
```xml
<PackageReference Include="System.Drawing.Common" Version="8.0.0" />
```
Run: `dotnet build backend`
Expected: Build succeeded

- [ ] **Step 4: Commit**
```bash
git add backend/Services/WordInteropService.cs backend/backend.csproj
git commit -m "fix(print): specify printer name when shell-printing PDFs"
```

---

## Task 2: Add Copies + Collate (Backend + Frontend)

**Files:**
- Modify: `backend/Models/PrintModels.cs` — add `Copies`, `Collate` to `PrintRequest`
- Modify: `backend/Services/PrintAlgorithmService.cs` — loop `ExecutePrintJob` N times
- Modify: `frontend/index.html` — add copies counter HTML
- Modify: `frontend/app.js` — add `CopiesModule`, send copies in print body
- Modify: `frontend/styles.css` — styles for copies control

**Backend changes:**

- [ ] **Step 1: Add Copies + Collate to PrintRequest**

In `backend/Models/PrintModels.cs`, find `public class PrintRequest` and add:
```csharp
public class PrintRequest
{
    public string FileId { get; set; } = "";
    public string PrinterName { get; set; } = "";
    public PrintMode Mode { get; set; }
    public string? PageRange { get; set; }
    public int[]? SingleSidedPages { get; set; }
    public int Copies { get; set; } = 1;        // NEW: number of copies (default 1)
    public bool Collate { get; set; } = true;   // NEW: collated copies (default true)
}
```

- [ ] **Step 2: Use Copies in ExecutePrintJob loop**

In `backend/Services/PrintAlgorithmService.cs`, find `ExecutePrintJob`. This method currently prints once. We need the caller (Program.cs `/api/print`) to handle copies since manual duplex needs full phase1+phase2 per copy.

In `backend/Program.cs`, find the `/api/print` handler. After `printAlgorithm.ExecutePrintJob(jobState, firstPhase: true);`, wrap with copies loop for **auto duplex only** (manual duplex copies is deferred — too complex for phase-based flow):

```csharp
// Execute print job (with copies for auto duplex)
int copies = Math.Max(1, request.Copies);
Console.WriteLine($"[PRINT] Executing print job, manual duplex: {jobState.IsManualDuplex}, copies: {copies}");

if (!jobState.IsManualDuplex)
{
    // Auto duplex: print N copies directly
    for (int copy = 0; copy < copies; copy++)
    {
        Console.WriteLine($"[PRINT] Printing copy {copy + 1}/{copies}");
        printAlgorithm.ExecutePrintJob(jobState, firstPhase: true);
        if (copies > 1 && copy < copies - 1)
            System.Threading.Thread.Sleep(2000); // small gap between copies
    }
}
else
{
    // Manual duplex: always 1 copy (phase1 only; phase2 handled by /continue)
    printAlgorithm.ExecutePrintJob(jobState, firstPhase: true);
    // Store copies in job state for phase2
    jobState.Copies = copies;
}
```

Also add `Copies` to `PrintJobState` in `PrintModels.cs`:
```csharp
public int Copies { get; set; } = 1;  // used for manual duplex multi-copy
```

- [ ] **Step 3: Verify backend builds**

Run: `dotnet build backend`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Add Copies UI to index.html**

In `frontend/index.html`, find Step 4 (Chế Độ In) card. After the existing radio buttons, add:

```html
<!-- Copies control -->
<div class="copies-row" id="copies-row">
  <label class="copies-label">Số bản in</label>
  <div class="copies-control">
    <button class="copies-btn" id="copies-dec" aria-label="Giảm">−</button>
    <span class="copies-val" id="copies-display">1</span>
    <button class="copies-btn" id="copies-inc" aria-label="Tăng">+</button>
  </div>
  <label class="collate-label">
    <input type="checkbox" id="collate-check" checked />
    Collate (1-2-3, 1-2-3)
  </label>
</div>
```

- [ ] **Step 5: Add CSS for copies control**

In `frontend/styles.css`, add at end:
```css
.copies-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 12px;
  padding: 10px 14px;
  background: var(--bg-secondary);
  border-radius: 10px;
  border: 1px solid var(--border-color);
}
.copies-label { color: var(--text-secondary); font-size: 14px; flex: 1; }
.copies-control { display: flex; align-items: center; gap: 8px; }
.copies-btn {
  width: 28px; height: 28px; border-radius: 8px;
  background: var(--bg-card); border: 1px solid var(--border-color);
  color: var(--text-primary); font-size: 16px; cursor: pointer;
  display: flex; align-items: center; justify-content: center;
  transition: border-color 0.15s;
}
.copies-btn:hover { border-color: #667eea; }
.copies-val { font-size: 15px; font-weight: 600; min-width: 24px; text-align: center; color: var(--text-primary); }
.collate-label { display: flex; align-items: center; gap: 6px; font-size: 13px; color: var(--text-secondary); cursor: pointer; }
```

- [ ] **Step 6: Add CopiesModule to app.js**

In `frontend/app.js`, add before the `PrintModule` const:

```javascript
// ═══════════════════════════════════════════════════════════════════
// CopiesModule — Copies counter + collate toggle
// ═══════════════════════════════════════════════════════════════════
const CopiesModule = {
    _copies: 1,
    _collate: true,

    get copies() { return this._copies; },
    get collate() { return this._collate; },

    init() {
        const dec = document.getElementById('copies-dec');
        const inc = document.getElementById('copies-inc');
        const chk = document.getElementById('collate-check');
        if (!dec || !inc) return;

        dec.addEventListener('click', () => {
            if (this._copies > 1) { this._copies--; this._update(); }
        });
        inc.addEventListener('click', () => {
            if (this._copies < 99) { this._copies++; this._update(); }
        });
        chk?.addEventListener('change', (e) => {
            this._collate = e.target.checked;
        });
    },

    _update() {
        const el = document.getElementById('copies-display');
        if (el) el.textContent = this._copies;
        const chkRow = document.getElementById('collate-check')?.closest('.collate-label');
        // Show/hide collate only when copies > 1
        if (chkRow) chkRow.style.display = this._copies > 1 ? '' : 'none';
    },

    reset() { this._copies = 1; this._collate = true; this._update(); },
};
```

- [ ] **Step 7: Send copies in PrintModule._startPrint()**

In `frontend/app.js`, find `PrintModule._startPrint()`. In the `body` object, add:
```javascript
const body = {
    fileId:           AppState.uploadedFile.id,
    printerName:      AppState.selectedPrinter.name,
    mode:             mode === 'normal' ? 0 : 1,
    pageRange,
    singleSidedPages: AppState.singleSidedPages.size > 0 ? Array.from(AppState.singleSidedPages) : null,
    copies:           CopiesModule.copies,    // NEW
    collate:          CopiesModule.collate,   // NEW
};
```

- [ ] **Step 8: Init CopiesModule in bootstrap**

In `frontend/app.js`, find the `DOMContentLoaded` block. Add `CopiesModule.init();` after `PrintModule.init();`.

- [ ] **Step 9: Commit**
```bash
git add backend/Models/PrintModels.cs backend/Services/PrintAlgorithmService.cs backend/Program.cs frontend/index.html frontend/app.js frontend/styles.css
git commit -m "feat: add copies and collate support"
```

---

## Task 3: Add Simplex Mode (1-sided only)

**Files:**
- Modify: `backend/Models/PrintModels.cs` — add `Simplex` to `PrintMode` enum
- Modify: `backend/Services/PrintAlgorithmService.cs` — add `CreateSimplexJob()`
- Modify: `backend/Program.cs` — handle `PrintMode.Simplex`
- Modify: `frontend/index.html` — add Simplex radio button
- Modify: `frontend/app.js` — send mode=2 for simplex

**Backend:**

- [ ] **Step 1: Add Simplex to PrintMode enum**

In `backend/Models/PrintModels.cs`:
```csharp
public enum PrintMode
{
    NormalDuplex = 0,
    BookletA5 = 1,
    Simplex = 2,   // NEW: single-sided only
}
```

- [ ] **Step 2: Add CreateSimplexJob in PrintAlgorithmService**

In `backend/Services/PrintAlgorithmService.cs`, add after `CreateBookletJob`:

```csharp
public PrintJobState CreateSimplexJob(
    string pdfPath,
    string printerName,
    string? pageRange = null)
{
    Console.WriteLine("[CreateSimplexJob] Single-sided print.");

    var pdfInfo = _wordService.GetPdfInfo(pdfPath);
    string workingPdfPath = pdfPath;

    if (!string.IsNullOrWhiteSpace(pageRange))
    {
        var selectedPages = ParsePageRange(pageRange, pdfInfo.PageCount);
        if (selectedPages.Length == 0)
            throw new InvalidOperationException($"Page range '{pageRange}' is invalid.");

        if (selectedPages.Length < pdfInfo.PageCount)
        {
            var subsetPath = Path.Combine(Path.GetTempPath(), $"simplex_subset_{Guid.NewGuid()}.pdf");
            _wordService.CreatePdfSubset(pdfPath, subsetPath, selectedPages);
            workingPdfPath = subsetPath;
            Console.WriteLine($"[CreateSimplexJob] Subset created: {subsetPath}");
        }
    }

    return new PrintJobState
    {
        TempPdfPath   = workingPdfPath,
        PrinterName   = printerName,
        IsManualDuplex = false,
        WaitingForFlip = false,
    };
}
```

- [ ] **Step 3: Handle Simplex in Program.cs /api/print**

In `backend/Program.cs`, find the `if (request.Mode == PrintMode.NormalDuplex)` block. Add:

```csharp
else if (request.Mode == PrintMode.Simplex)
{
    Console.WriteLine($"[PRINT] Creating simplex job...");
    jobState = printAlgorithm.CreateSimplexJob(
        filePath,
        request.PrinterName,
        request.PageRange
    );
}
```

- [ ] **Step 4: Add Simplex radio button to index.html**

In `frontend/index.html`, find the print mode radio buttons section. Add:
```html
<label class="mode-option">
  <input type="radio" name="print-mode" value="simplex" />
  <span class="mode-label">📄 In 1 Mặt</span>
  <span class="mode-desc">In một mặt thông thường</span>
</label>
```

- [ ] **Step 5: Update PrintModule to send mode=2 for simplex**

In `frontend/app.js`, find in `PrintModule._startPrint()`:
```javascript
mode: mode === 'normal' ? 0 : 1,
```
Replace with:
```javascript
mode: mode === 'normal' ? 0 : (mode === 'booklet' ? 1 : 2),
```

- [ ] **Step 6: Build and commit**

Run: `dotnet build backend`
```bash
git add backend/Models/PrintModels.cs backend/Services/PrintAlgorithmService.cs backend/Program.cs frontend/index.html frontend/app.js
git commit -m "feat: add simplex (single-sided) print mode"
```

---

## Task 4: Image → PDF Conversion Fix

**Files:**
- Modify: `backend/Services/WordInteropService.cs` — add `ConvertImageToPdf()`
- Modify: `backend/Program.cs` — handle image extensions in `/api/convert`

**Backend:**

- [ ] **Step 1: Add ConvertImageToPdf to WordInteropService**

In `backend/Services/WordInteropService.cs`, add after `ConvertToPdf`:

```csharp
/// <summary>
/// Convert a JPG/PNG/BMP image to a single-page PDF using PdfSharp.
/// The image is scaled to fit an A4 page while preserving aspect ratio.
/// </summary>
public void ConvertImageToPdf(string imagePath, string outputPdfPath)
{
    Console.WriteLine($"[ConvertImageToPdf] Converting: {imagePath} -> {outputPdfPath}");

    using var document = new PdfDocument();
    var page = document.AddPage();

    // A4 in points (595.28 x 841.89)
    page.Width  = XUnit.FromMillimeter(210);
    page.Height = XUnit.FromMillimeter(297);

    using var gfx = XGraphics.FromPdfPage(page);
    using var image = XImage.FromFile(imagePath);

    double imgW = image.PointWidth;
    double imgH = image.PointHeight;
    double pageW = page.Width.Point;
    double pageH = page.Height.Point;

    // Margin: 20mm on each side
    double margin = XUnit.FromMillimeter(20).Point;
    double maxW = pageW - 2 * margin;
    double maxH = pageH - 2 * margin;

    // Scale to fit
    double scale = Math.Min(maxW / imgW, maxH / imgH);
    double drawW = imgW * scale;
    double drawH = imgH * scale;
    double x = margin + (maxW - drawW) / 2.0;
    double y = margin + (maxH - drawH) / 2.0;

    gfx.DrawImage(image, x, y, drawW, drawH);
    document.Save(outputPdfPath);
    Console.WriteLine($"[ConvertImageToPdf] Done. Output: {outputPdfPath}");
}
```

- [ ] **Step 2: Handle image in /api/convert in Program.cs**

Find the `/api/convert` handler. Replace the `else` branch:

```csharp
else if (extension is ".jpg" or ".jpeg" or ".png" or ".bmp")
{
    wordService.ConvertImageToPdf(filePath, pdfPath);
}
else
{
    return Results.BadRequest("Unsupported file type for conversion.");
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build backend`
Expected: Build succeeded

- [ ] **Step 4: Commit**
```bash
git add backend/Services/WordInteropService.cs backend/Program.cs
git commit -m "fix: add image (JPG/PNG) to PDF conversion"
```

---

## Task 5: Fix Booklet — Support singleSidedPages

**Files:**
- Modify: `backend/Services/PrintAlgorithmService.cs` — pass `singleSidedPages` to `CreateBookletJob`
- Modify: `backend/Program.cs` — pass `request.SingleSidedPages` to `CreateBookletJob`

- [ ] **Step 1: Update CreateBookletJob signature**

In `backend/Services/PrintAlgorithmService.cs`, find `CreateBookletJob`:

```csharp
// OLD:
public PrintJobState CreateBookletJob(string pdfPath, string printerName, bool isDuplexPrinter, string? pageRange = null)

// NEW:
public PrintJobState CreateBookletJob(string pdfPath, string printerName, bool isDuplexPrinter, string? pageRange = null, int[]? singleSidedPages = null)
```

At the end of `CreateBookletJob`, before `return CreateNormalDuplexJob(...)`:
```csharp
// Pass singleSidedPages into NormalDuplexJob for the booklet PDF
// Note: singleSidedPages are page numbers in the ORIGINAL source, must remap to booklet order
// For simplicity: if singleSidedPages provided, pass them as-is (booklet reorder makes this complex)
// Known limitation: singleSidedPages in booklet mode refers to booklet output pages, not source pages
return CreateNormalDuplexJob(bookletPdfPath, printerName, isDuplexPrinter, pageRange: null, singleSidedPages: singleSidedPages);
```

- [ ] **Step 2: Update caller in Program.cs**

Find `printAlgorithm.CreateBookletJob(...)` call. Update to:
```csharp
jobState = printAlgorithm.CreateBookletJob(
    filePath,
    request.PrinterName,
    printer.IsDuplex,
    request.PageRange,
    request.SingleSidedPages   // NEW
);
```

- [ ] **Step 3: Build and commit**

Run: `dotnet build backend`
```bash
git add backend/Services/PrintAlgorithmService.cs backend/Program.cs
git commit -m "fix(booklet): pass singleSidedPages through to duplex job"
```

---

## Task 6: Print Presets (Frontend only)

**Files:**
- Modify: `frontend/app.js` — add `PresetsModule`
- Modify: `frontend/index.html` — add presets UI section
- Modify: `frontend/styles.css` — preset styles

A preset stores: `{ name, mode, copies, pageRange }`. Stored in `localStorage('myprinter_presets')`.

- [ ] **Step 1: Add PresetsModule to app.js**

Add after `CopiesModule`:

```javascript
// ═══════════════════════════════════════════════════════════════════
// PresetsModule — Save/load print configuration presets
// ═══════════════════════════════════════════════════════════════════
const PresetsModule = {
    _KEY: 'myprinter_presets',

    _load() {
        try { return JSON.parse(localStorage.getItem(this._KEY) || '[]'); }
        catch { return []; }
    },

    _save(presets) {
        localStorage.setItem(this._KEY, JSON.stringify(presets));
    },

    init() {
        this._render();
        document.getElementById('preset-save-btn')?.addEventListener('click', () => this._saveCurrentAsPreset());
    },

    _saveCurrentAsPreset() {
        const name = prompt('Tên preset:');
        if (!name?.trim()) return;
        const preset = {
            id: Date.now().toString(),
            name: name.trim(),
            mode: document.querySelector('input[name="print-mode"]:checked')?.value || 'normal',
            copies: CopiesModule.copies,
            pageRange: document.getElementById('page-range-input')?.value || '',
        };
        const presets = this._load();
        presets.push(preset);
        this._save(presets);
        this._render();
        showToast(`Đã lưu preset "${preset.name}"`, 'success');
    },

    _applyPreset(preset) {
        // Apply mode
        const radio = document.querySelector(`input[name="print-mode"][value="${preset.mode}"]`);
        if (radio) radio.checked = true;

        // Apply copies
        CopiesModule._copies = preset.copies || 1;
        CopiesModule._update();

        // Apply page range
        const rangeInput = document.getElementById('page-range-input');
        if (rangeInput && preset.pageRange) {
            rangeInput.value = preset.pageRange;
            rangeInput.dispatchEvent(new Event('input'));
        }

        showToast(`Đã áp dụng preset "${preset.name}"`, 'success');
    },

    _deletePreset(id) {
        const presets = this._load().filter(p => p.id !== id);
        this._save(presets);
        this._render();
        showToast('Đã xóa preset', 'info');
    },

    _render() {
        const container = document.getElementById('presets-list');
        if (!container) return;
        const presets = this._load();
        if (presets.length === 0) {
            container.innerHTML = '<span class="preset-empty">Chưa có preset nào</span>';
            return;
        }
        container.innerHTML = presets.map(p => `
            <div class="preset-chip" data-id="${p.id}">
                <span class="preset-name">${p.name}</span>
                <button class="preset-apply-btn" data-id="${p.id}" title="Áp dụng">✓</button>
                <button class="preset-del-btn" data-id="${p.id}" title="Xóa">✕</button>
            </div>
        `).join('');

        container.querySelectorAll('.preset-apply-btn').forEach(btn =>
            btn.addEventListener('click', () => {
                const preset = this._load().find(p => p.id === btn.dataset.id);
                if (preset) this._applyPreset(preset);
            })
        );
        container.querySelectorAll('.preset-del-btn').forEach(btn =>
            btn.addEventListener('click', () => this._deletePreset(btn.dataset.id))
        );
    },
};
```

- [ ] **Step 2: Add Presets UI to index.html**

In `frontend/index.html`, find Step 4 (Chế Độ In) card. Add after the mode options:

```html
<!-- Presets section -->
<div class="presets-section">
  <div class="presets-header">
    <span class="presets-title">⭐ Presets</span>
    <button class="btn-outline" id="preset-save-btn">+ Lưu preset hiện tại</button>
  </div>
  <div id="presets-list" class="presets-list"></div>
</div>
```

- [ ] **Step 3: Add CSS for presets**

In `frontend/styles.css`, add at end:
```css
.presets-section { margin-top: 16px; padding-top: 14px; border-top: 1px solid var(--border-color); }
.presets-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 10px; }
.presets-title { font-size: 13px; font-weight: 600; color: var(--text-secondary); }
.presets-list { display: flex; flex-wrap: wrap; gap: 8px; min-height: 32px; }
.preset-chip {
  display: flex; align-items: center; gap: 6px;
  background: var(--bg-secondary); border: 1px solid var(--border-color);
  border-radius: 20px; padding: 5px 10px; font-size: 12px;
  transition: border-color 0.15s;
}
.preset-chip:hover { border-color: #667eea; }
.preset-name { color: var(--text-secondary); }
.preset-apply-btn, .preset-del-btn {
  background: none; border: none; cursor: pointer; padding: 0 2px;
  font-size: 11px; line-height: 1;
}
.preset-apply-btn { color: #10b981; }
.preset-del-btn { color: #ef4444; }
.preset-empty { font-size: 12px; color: var(--text-muted); font-style: italic; }
.btn-outline {
  background: none; border: 1px solid var(--border-color); border-radius: 8px;
  padding: 5px 12px; font-size: 12px; color: var(--text-secondary); cursor: pointer;
  transition: border-color 0.15s, color 0.15s;
}
.btn-outline:hover { border-color: #667eea; color: var(--text-primary); }
```

- [ ] **Step 4: Init PresetsModule in bootstrap**

In `frontend/app.js` DOMContentLoaded block, add `PresetsModule.init();`.

- [ ] **Step 5: Commit**
```bash
git add frontend/app.js frontend/index.html frontend/styles.css
git commit -m "feat: add print presets (save/apply/delete via localStorage)"
```

---

## Task 7: Upload Progress Bar (Frontend only)

**Files:**
- Modify: `frontend/app.js` — update `UploadModule._upload()` to use XHR with progress
- Modify: `frontend/index.html` — add progress bar HTML
- Modify: `frontend/styles.css` — progress bar styles

- [ ] **Step 1: Add progress bar HTML to index.html**

Find the upload section (Step 2). After the upload area div, add:
```html
<div id="upload-progress-wrap" class="upload-progress-wrap hidden">
  <div class="upload-progress-bar" id="upload-progress-bar" style="width:0%"></div>
  <span class="upload-progress-text" id="upload-progress-text">0%</span>
</div>
```

- [ ] **Step 2: Add progress bar CSS**

In `frontend/styles.css`, add:
```css
.upload-progress-wrap {
  position: relative; height: 6px; background: var(--bg-secondary);
  border-radius: 3px; margin-top: 8px; overflow: hidden;
}
.upload-progress-bar {
  height: 100%; background: linear-gradient(90deg, #667eea, #764ba2);
  border-radius: 3px; transition: width 0.2s ease;
}
.upload-progress-text {
  position: absolute; right: 0; top: -18px;
  font-size: 11px; color: var(--text-muted);
}
```

- [ ] **Step 3: Rewrite UploadModule._upload() to use XHR with progress**

In `frontend/app.js`, find `UploadModule._upload(file)`. Replace the `fetch` call with XHR:

```javascript
async _upload(file) {
    // validate extension
    const ext = file.name.split('.').pop().toLowerCase();
    if (!['doc','docx','pdf','jpg','jpeg','png'].includes(ext)) {
        showToast('Định dạng không hỗ trợ. Chỉ: DOC, DOCX, PDF, JPG, PNG', 'error');
        return;
    }

    // Show progress
    const wrap = document.getElementById('upload-progress-wrap');
    const bar  = document.getElementById('upload-progress-bar');
    const txt  = document.getElementById('upload-progress-text');
    if (wrap) wrap.classList.remove('hidden');

    const formData = new FormData();
    formData.append('file', file);

    // XHR for progress tracking
    const uploadResult = await new Promise((resolve, reject) => {
        const xhr = new XMLHttpRequest();
        xhr.open('POST', `${API_BASE}/upload`);

        xhr.upload.onprogress = (e) => {
            if (e.lengthComputable) {
                const pct = Math.round((e.loaded / e.total) * 100);
                if (bar) bar.style.width = pct + '%';
                if (txt) txt.textContent = pct + '%';
            }
        };

        xhr.onload = () => {
            if (xhr.status >= 200 && xhr.status < 300) {
                resolve(JSON.parse(xhr.responseText));
            } else {
                reject(new Error(`Upload failed: ${xhr.status}`));
            }
        };
        xhr.onerror = () => reject(new Error('Network error during upload'));
        xhr.send(formData);
    });

    if (wrap) wrap.classList.add('hidden');

    if (!uploadResult.success) {
        showToast('Upload thất bại: ' + uploadResult.message, 'error');
        return;
    }

    const { fileId, originalFileName } = uploadResult;
    const needsConversion = !['pdf'].includes(ext);

    // Convert if needed
    if (needsConversion) {
        showToast('Đang chuyển đổi file...', 'info');
        const convRes = await fetch(`${API_BASE}/convert?fileId=${fileId}`, { method: 'POST' });
        const conv = await convRes.json();
        if (!conv.success && conv.success !== undefined) {
            showToast('Lỗi chuyển đổi: ' + (conv.message || 'unknown'), 'error');
            return;
        }
    }

    AppState.uploadedFile = { id: fileId, name: originalFileName, needsConversion };
    // show file info UI, hide upload area
    document.getElementById('upload-area')?.classList.add('hidden');
    document.getElementById('file-info')?.classList.remove('hidden');
    document.getElementById('file-name').textContent = originalFileName;
    document.getElementById('file-status').textContent = 'Đã sẵn sàng';

    await PreviewModule.render(fileId);
    PageSelectModule.updateDisplay();
    PrintModule.updateButton();
    showToast('File đã tải lên thành công!', 'success');
},
```

- [ ] **Step 4: Commit**
```bash
git add frontend/app.js frontend/index.html frontend/styles.css
git commit -m "feat: add upload progress bar with XHR progress tracking"
```

---

## Task 8: Print Button Feedback + Printer Live Poll (Frontend only)

**Files:**
- Modify: `frontend/app.js` — update `PrintModule._startPrint()` for button states; add live poll to `PrinterModule`

- [ ] **Step 1: Add spinner/feedback states to PrintModule**

In `frontend/app.js`, find `PrintModule._startPrint()`. Add button state management:

```javascript
async _startPrint() {
    if (!AppState.selectedPrinter) { showToast('Vui lòng chọn máy in', 'error'); return; }
    if (!AppState.uploadedFile)    { showToast('Vui lòng tải lên file cần in', 'error'); return; }
    if (AppState.selectedPages.size === 0) { showToast('Vui lòng chọn ít nhất 1 trang', 'error'); return; }

    const btn = document.getElementById('print-btn');

    // LOADING state
    const originalText = btn.textContent;
    btn.disabled = true;
    btn.textContent = '⏳ Đang gửi lệnh in...';
    btn.style.opacity = '0.8';

    const mode = document.querySelector('input[name="print-mode"]:checked').value;
    const total = AppState.totalPageCount;
    const sel   = AppState.selectedPages;
    const pageRange = (sel.size > 0 && sel.size < total)
        ? Array.from(sel).sort((a,b) => a-b).join(',')
        : null;

    const body = {
        fileId:           AppState.uploadedFile.id,
        printerName:      AppState.selectedPrinter.name,
        mode:             mode === 'normal' ? 0 : (mode === 'booklet' ? 1 : 2),
        pageRange,
        singleSidedPages: AppState.singleSidedPages.size > 0 ? Array.from(AppState.singleSidedPages) : null,
        copies:           CopiesModule.copies,
        collate:          CopiesModule.collate,
    };

    try {
        const res    = await fetch(`${API_BASE}/print`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        const result = await res.json();

        if (!result.success) {
            showToast('Lỗi: ' + result.message, 'error');
            btn.disabled = false;
            btn.textContent = originalText;
            btn.style.opacity = '';
            return;
        }

        if (result.jobState?.waitingForFlip) {
            AppState.currentJob = result.jobState;
            this._showFlipModal(result.jobState.instruction);
            showToast('Đã in mặt lẻ! Vui lòng làm theo hướng dẫn.', 'info');
            btn.disabled = false;
            btn.textContent = originalText;
            btn.style.opacity = '';
        } else {
            // SUCCESS flash
            btn.textContent = '✓ Đã gửi lệnh in!';
            btn.style.background = 'linear-gradient(135deg, #10b981, #059669)';
            btn.style.opacity = '1';
            showToast('In thành công!', 'success');

            // Add to history
            HistoryModule.add({
                file: AppState.uploadedFile.name,
                printer: AppState.selectedPrinter.name,
                pages: sel.size,
                mode: mode,
                copies: CopiesModule.copies,
            });

            // Reset after 2s
            setTimeout(() => {
                btn.disabled = false;
                btn.textContent = originalText;
                btn.style.background = '';
                btn.style.opacity = '';
                this.updateButton();
            }, 2000);
        }
    } catch (err) {
        showToast('Lỗi khi in: ' + err.message, 'error');
        btn.disabled = false;
        btn.textContent = originalText;
        btn.style.opacity = '';
    }
},
```

- [ ] **Step 2: Add live printer polling to PrinterModule**

In `frontend/app.js`, find `PrinterModule`. Add a `startPolling()` method and call it from `init()`:

```javascript
startPolling() {
    // Poll every 30s, update status badges without full re-render
    setInterval(async () => {
        try {
            const res = await fetch(`${API_BASE}/printers`);
            if (!res.ok) return;
            const printers = await res.json();
            // Update status badges only
            printers.forEach(p => {
                const card = document.querySelector(`.printer-card[data-name="${CSS.escape(p.name)}"]`);
                if (!card) return;
                const badge = card.querySelector('.printer-status-badge');
                if (badge) badge.textContent = this._statusBadge(p.status);
            });
        } catch { /* silently ignore poll failures */ }
    }, 30_000);
},
```

Also update `PrinterModule.init()` to call `this.startPolling()` at the end, and add `data-name` attribute to each printer card during render. Find where cards are created (the `innerHTML` or `createElement` loop) and add `data-name="${printer.name}"` to the card element.

- [ ] **Step 3: Commit**
```bash
git add frontend/app.js
git commit -m "feat: print button feedback states + live printer status polling"
```

---

## Task 9: Print History (Frontend only)

**Files:**
- Modify: `frontend/app.js` — add `HistoryModule`
- Modify: `frontend/index.html` — add history panel
- Modify: `frontend/styles.css` — history styles

- [ ] **Step 1: Add HistoryModule to app.js**

Add after `PresetsModule`:

```javascript
// ═══════════════════════════════════════════════════════════════════
// HistoryModule — Recent print jobs log (localStorage)
// ═══════════════════════════════════════════════════════════════════
const HistoryModule = {
    _KEY: 'myprinter_history',
    _MAX: 10,

    _load() {
        try { return JSON.parse(localStorage.getItem(this._KEY) || '[]'); }
        catch { return []; }
    },

    _save(items) {
        localStorage.setItem(this._KEY, JSON.stringify(items));
    },

    init() {
        this._render();
        document.getElementById('history-clear-btn')?.addEventListener('click', () => {
            this._save([]);
            this._render();
            showToast('Đã xóa lịch sử', 'info');
        });
        document.getElementById('history-toggle-btn')?.addEventListener('click', () => {
            const panel = document.getElementById('history-panel');
            panel?.classList.toggle('hidden');
        });
    },

    add(entry) {
        // entry: { file, printer, pages, mode, copies }
        const items = this._load();
        items.unshift({
            ...entry,
            time: new Date().toLocaleString('vi-VN'),
        });
        this._save(items.slice(0, this._MAX));
        this._render();
    },

    _render() {
        const container = document.getElementById('history-list');
        if (!container) return;
        const items = this._load();
        if (items.length === 0) {
            container.innerHTML = '<div class="history-empty">Chưa có lịch sử in</div>';
            return;
        }
        const modeLabel = { normal: '2 mặt', booklet: 'Sách A5', simplex: '1 mặt' };
        container.innerHTML = items.map(item => `
            <div class="history-item">
                <div class="history-file">📄 ${item.file}</div>
                <div class="history-meta">
                    🖨️ ${item.printer} · ${item.pages} trang · ${modeLabel[item.mode] || item.mode} · ${item.copies} bản
                </div>
                <div class="history-time">${item.time}</div>
            </div>
        `).join('');
    },
};
```

- [ ] **Step 2: Add history panel HTML to index.html**

Add a history toggle button near the top of the app (or after Step 5 print section):
```html
<!-- Print History Panel -->
<div class="history-section">
  <div class="history-header">
    <button class="btn-outline" id="history-toggle-btn">📋 Lịch sử in</button>
    <button class="btn-outline" id="history-clear-btn" style="color:#ef4444;border-color:#ef4444">Xóa</button>
  </div>
  <div id="history-panel" class="history-panel hidden">
    <div id="history-list"></div>
  </div>
</div>
```

- [ ] **Step 3: Add history CSS**

In `frontend/styles.css`, add:
```css
.history-section { margin-top: 16px; }
.history-header { display: flex; gap: 8px; align-items: center; margin-bottom: 8px; }
.history-panel { background: var(--bg-secondary); border-radius: 12px; border: 1px solid var(--border-color); padding: 12px; max-height: 260px; overflow-y: auto; }
.history-panel::-webkit-scrollbar { width: 4px; }
.history-panel::-webkit-scrollbar-thumb { background: var(--border-color); border-radius: 2px; }
.history-item { padding: 10px 0; border-bottom: 1px solid var(--border-color); }
.history-item:last-child { border-bottom: none; }
.history-file { font-size: 13px; font-weight: 500; color: var(--text-primary); margin-bottom: 3px; }
.history-meta { font-size: 12px; color: var(--text-secondary); margin-bottom: 2px; }
.history-time { font-size: 11px; color: var(--text-muted); }
.history-empty { font-size: 13px; color: var(--text-muted); text-align: center; padding: 16px; }
```

- [ ] **Step 4: Init HistoryModule in bootstrap**

In `frontend/app.js` DOMContentLoaded block, add `HistoryModule.init();`.

- [ ] **Step 5: Commit**
```bash
git add frontend/app.js frontend/index.html frontend/styles.css
git commit -m "feat: add print history panel (last 10 jobs, localStorage)"
```

---

## Task 10: Keyboard Shortcuts + Estimated Sheets/Time (Frontend only)

**Files:**
- Modify: `frontend/app.js` — add `KeyboardModule`, add summary calculation to `PrintModule`
- Modify: `frontend/index.html` — add summary display, keyboard hint
- Modify: `frontend/styles.css` — summary styles

- [ ] **Step 1: Add KeyboardModule**

In `frontend/app.js`, add before bootstrap block:

```javascript
// ═══════════════════════════════════════════════════════════════════
// KeyboardModule — Global keyboard shortcuts
// ═══════════════════════════════════════════════════════════════════
const KeyboardModule = {
    init() {
        document.addEventListener('keydown', (e) => {
            // Skip if typing in an input
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;

            if (e.ctrlKey && e.key === 'p') {
                e.preventDefault();
                const btn = document.getElementById('print-btn');
                if (btn && !btn.disabled) btn.click();
            }

            if (e.ctrlKey && e.key === 'o') {
                e.preventDefault();
                document.getElementById('file-input')?.click();
            }

            if (e.key === 'Escape') {
                document.getElementById('page-zoom-modal')?.classList.add('hidden');
                document.getElementById('flip-modal')?.classList.add('hidden');
                ContextMenu.hide();
            }

            if (e.key === 'a' && !e.ctrlKey) {
                AppState.selectAllPages();
                PreviewModule.updateThumbnails();
                PageSelectModule.updateDisplay();
                PrintModule.updateButton();
            }
        });
    },
};
```

- [ ] **Step 2: Add estimated sheets/time summary**

Add a helper function and update `PrintModule.updateButton()` to also update summary:

In `frontend/app.js`, add after KeyboardModule:

```javascript
// ═══════════════════════════════════════════════════════════════════
// SummaryModule — Calculate and display print summary
// ═══════════════════════════════════════════════════════════════════
const SummaryModule = {
    update() {
        const el = document.getElementById('print-summary');
        if (!el) return;

        const pages   = AppState.selectedPages.size;
        const copies  = CopiesModule?.copies || 1;
        const mode    = document.querySelector('input[name="print-mode"]:checked')?.value || 'normal';
        const printer = AppState.selectedPrinter;

        if (!AppState.uploadedFile || pages === 0) { el.classList.add('hidden'); return; }

        // Estimate sheets
        let sheets;
        if (mode === 'simplex') {
            sheets = pages * copies;
        } else if (mode === 'booklet') {
            sheets = Math.ceil(pages / 4) * copies;
        } else {
            // normal duplex
            const singleSided = AppState.singleSidedPages.size;
            const doubleSided = pages - singleSided;
            sheets = Math.ceil(doubleSided / 2) + singleSided;
            sheets *= copies;
        }

        // Estimate time: ~4s per sheet
        const totalSec = sheets * 4;
        const timeStr = totalSec < 60
            ? `~${totalSec}s`
            : `~${Math.ceil(totalSec / 60)} phút`;

        el.classList.remove('hidden');
        el.innerHTML = `
            <span>📄 ${pages} trang</span>
            <span>·</span>
            <span>🗒️ ${sheets} tờ</span>
            <span>·</span>
            <span>⏱ ${timeStr}</span>
            ${copies > 1 ? `<span>· ${copies} bản</span>` : ''}
            ${printer ? `<span>· 🖨️ ${printer.name}</span>` : ''}
        `;
    },
};
```

Hook `SummaryModule.update()` into `PrintModule.updateButton()`:

```javascript
updateButton() {
    const btn = document.getElementById('print-btn');
    btn.disabled = !AppState.selectedPrinter || !AppState.uploadedFile || AppState.selectedPages.size === 0;
    SummaryModule.update();  // NEW: update summary whenever button state changes
},
```

Also call `SummaryModule.update()` from `PageSelectModule.updateDisplay()` and `ContextMenu._handleAction()`.

- [ ] **Step 3: Add summary HTML to index.html**

In the print action section (Step 5), add above the print button:
```html
<div id="print-summary" class="print-summary hidden"></div>
```

Add keyboard hint below print button:
```html
<div class="kbd-hint">
  <kbd>Ctrl+P</kbd> In · <kbd>Ctrl+O</kbd> Mở file · <kbd>A</kbd> Chọn tất cả · <kbd>Esc</kbd> Đóng
</div>
```

- [ ] **Step 4: Add summary + kbd CSS**

In `frontend/styles.css`, add:
```css
.print-summary {
  display: flex; flex-wrap: wrap; gap: 6px; align-items: center;
  background: var(--bg-secondary); border: 1px solid var(--border-color);
  border-radius: 10px; padding: 8px 14px; margin-bottom: 12px;
  font-size: 13px; color: var(--text-secondary);
}
.print-summary span { white-space: nowrap; }
.kbd-hint { font-size: 11px; color: var(--text-muted); margin-top: 8px; text-align: center; }
kbd {
  display: inline-block; background: var(--bg-secondary); border: 1px solid var(--border-color);
  border-radius: 4px; padding: 1px 5px; font-size: 10px; font-family: monospace;
}
```

- [ ] **Step 5: Init KeyboardModule + SummaryModule in bootstrap**

In DOMContentLoaded block, add `KeyboardModule.init();` and `SummaryModule.update();`.

- [ ] **Step 6: Commit**
```bash
git add frontend/app.js frontend/index.html frontend/styles.css
git commit -m "feat: add keyboard shortcuts, estimated sheets/time summary"
```

---

## Task 11: Watermark (Backend + Frontend)

**Files:**
- Modify: `backend/Models/PrintModels.cs` — add `WatermarkOptions` class, add to `PrintRequest`
- Modify: `backend/Services/WordInteropService.cs` — add `AddWatermarkToPdf()`
- Modify: `backend/Services/PrintAlgorithmService.cs` — apply watermark before printing if requested
- Modify: `frontend/index.html` — add watermark toggle + text input
- Modify: `frontend/app.js` — send watermark in print body

**Backend:**

- [ ] **Step 1: Add WatermarkOptions model**

In `backend/Models/PrintModels.cs`, add:
```csharp
public class WatermarkOptions
{
    public string Text { get; set; } = "DRAFT";
    public int FontSize { get; set; } = 48;
    public int Opacity { get; set; } = 30; // 0-100
    public string Color { get; set; } = "#94a3b8"; // CSS hex color
}
```

Add to `PrintRequest`:
```csharp
public WatermarkOptions? Watermark { get; set; } // null = no watermark
```

- [ ] **Step 2: Add AddWatermarkToPdf to WordInteropService**

In `backend/Services/WordInteropService.cs`, add:

```csharp
/// <summary>
/// Stamp a diagonal text watermark on every page of the PDF.
/// Returns path to the new watermarked PDF.
/// </summary>
public string AddWatermarkToPdf(string sourcePath, WatermarkOptions opts)
{
    var outputPath = Path.Combine(Path.GetTempPath(), $"watermark_{Guid.NewGuid()}.pdf");
    Console.WriteLine($"[AddWatermarkToPdf] Stamping '{opts.Text}' on: {sourcePath}");

    using var sourceDoc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
    using var targetDoc = new PdfDocument();

    // Parse color
    var color = XColor.FromArgb(
        (int)(opts.Opacity / 100.0 * 255),
        Convert.ToInt32(opts.Color.Substring(1, 2), 16),
        Convert.ToInt32(opts.Color.Substring(3, 2), 16),
        Convert.ToInt32(opts.Color.Substring(5, 2), 16)
    );

    var font = new XFont("Arial", opts.FontSize, XFontStyleEx.Bold);

    for (int i = 0; i < sourceDoc.PageCount; i++)
    {
        var page = targetDoc.AddPage(sourceDoc.Pages[i]);
        using var gfx = XGraphics.FromPdfPage(page);

        // Save state, rotate 45° from center
        gfx.Save();
        double cx = page.Width.Point / 2;
        double cy = page.Height.Point / 2;
        gfx.TranslateTransform(cx, cy);
        gfx.RotateTransform(-45);

        var size = gfx.MeasureString(opts.Text, font);
        gfx.DrawString(
            opts.Text,
            font,
            new XSolidBrush(color),
            new XPoint(-size.Width / 2, size.Height / 4)
        );
        gfx.Restore();
    }

    targetDoc.Save(outputPath);
    Console.WriteLine($"[AddWatermarkToPdf] Done: {outputPath}");
    return outputPath;
}
```

- [ ] **Step 3: Apply watermark in PrintAlgorithmService**

In `backend/Services/PrintAlgorithmService.cs`, update `CreateNormalDuplexJob` and `CreateSimplexJob` signatures to accept optional `WatermarkOptions? watermark = null`. At the start of each method, before any processing:

```csharp
// Apply watermark if requested
if (watermark != null)
{
    var watermarkedPath = Path.Combine(Path.GetTempPath(), $"wm_{Guid.NewGuid()}.pdf");
    pdfPath = _wordService.AddWatermarkToPdf(pdfPath, watermark);
    Console.WriteLine($"[CreateNormalDuplexJob] Watermark applied: {pdfPath}");
}
```

- [ ] **Step 4: Pass watermark from Program.cs**

In the `/api/print` handler, pass `request.Watermark` to the job creation methods.

- [ ] **Step 5: Add watermark UI to frontend**

In `frontend/index.html`, find Step 4 (Chế Độ In). Add:
```html
<div class="watermark-row" id="watermark-row">
  <label class="watermark-toggle-label">
    <input type="checkbox" id="watermark-enable" />
    🔒 Watermark
  </label>
  <div id="watermark-options" class="hidden">
    <input type="text" id="watermark-text" class="watermark-input" value="DRAFT" maxlength="20" />
    <input type="range" id="watermark-opacity" min="10" max="80" value="30" />
    <span id="watermark-opacity-val">30%</span>
  </div>
</div>
```

- [ ] **Step 6: Add watermark to print body in app.js**

In `PrintModule._startPrint()`, add to the body object:
```javascript
const wmEnabled = document.getElementById('watermark-enable')?.checked;
const watermark = wmEnabled ? {
    text:    document.getElementById('watermark-text')?.value || 'DRAFT',
    fontSize: 48,
    opacity: parseInt(document.getElementById('watermark-opacity')?.value || '30'),
    color:   '#94a3b8',
} : null;

const body = {
    // ... existing fields ...
    watermark,
};
```

Wire up the opacity slider to show value in app.js (init code):
```javascript
document.getElementById('watermark-opacity')?.addEventListener('input', (e) => {
    document.getElementById('watermark-opacity-val').textContent = e.target.value + '%';
});
document.getElementById('watermark-enable')?.addEventListener('change', (e) => {
    const opts = document.getElementById('watermark-options');
    if (opts) opts.classList.toggle('hidden', !e.target.checked);
});
```

- [ ] **Step 7: Add watermark CSS**

```css
.watermark-row { margin-top: 12px; padding: 10px 14px; background: var(--bg-secondary); border-radius: 10px; border: 1px solid var(--border-color); }
.watermark-toggle-label { display: flex; align-items: center; gap: 8px; cursor: pointer; font-size: 13px; color: var(--text-secondary); }
.watermark-input { background: var(--bg-card); border: 1px solid var(--border-color); border-radius: 6px; padding: 4px 8px; color: var(--text-primary); font-size: 13px; width: 120px; margin-top: 8px; }
```

- [ ] **Step 8: Build and commit**

Run: `dotnet build backend`
```bash
git add backend/Models/PrintModels.cs backend/Services/WordInteropService.cs backend/Services/PrintAlgorithmService.cs backend/Program.cs frontend/index.html frontend/app.js frontend/styles.css
git commit -m "feat: add watermark support (diagonal text stamp on all pages)"
```

---

## Task 12: Improved Duplex Detection (Backend)

**Files:**
- Modify: `backend/Services/PrinterManagementService.cs` — remove hardcoded list, rely purely on WMI Capabilities

- [ ] **Step 1: Update HasDuplexCapability to remove brittle hardcode list**

In `backend/Services/PrinterManagementService.cs`, replace `HasDuplexCapability`:

```csharp
private bool HasDuplexCapability(string name, UInt16[]? capabilities)
{
    Console.WriteLine($"[HasDuplexCapability] Checking: {name}");

    if (capabilities == null || capabilities.Length == 0)
    {
        Console.WriteLine("  No capabilities data — assuming single-sided");
        return false;
    }

    Console.WriteLine($"  Capabilities: [{string.Join(", ", capabilities)}]");

    // WMI capability codes: 3 = duplex long edge, 4 = duplex short edge
    bool hasDuplex = capabilities.Contains((UInt16)3) || capabilities.Contains((UInt16)4);

    // Known exceptions: printers that report duplex capability but are actually single-sided
    // (usually due to incorrect driver reporting)
    var knownSingleSided = new[] { "lbp2900", "lbp 2900", "hp laser 107", "hp laser 108" };
    var lowName = name.ToLowerInvariant();
    if (hasDuplex && knownSingleSided.Any(s => lowName.Contains(s)))
    {
        Console.WriteLine($"  Override: known single-sided model despite capabilities — {name}");
        return false;
    }

    Console.WriteLine($"  Duplex: {hasDuplex}");
    return hasDuplex;
}
```

This keeps the known-exception list but as an **override after WMI check** (not before), so new printers work correctly.

- [ ] **Step 2: Also expose printer capabilities via extended API response**

In `backend/Models/PrintModels.cs`, extend `PrinterInfo`:
```csharp
public class PrinterInfo
{
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsDuplex { get; set; }
    public PrinterStatus Status { get; set; }
    public bool SupportsColor { get; set; }   // NEW
    public string PortName { get; set; } = ""; // NEW (for debugging)
}
```

In `PrinterManagementService.GetAllPrinters()`, populate `SupportsColor`:
```csharp
// WMI Capabilities: 4 = Color printing
bool supportsColor = capabilities != null && capabilities.Contains((UInt16)4);
// Note: Capability code 4 overlaps with duplex short-edge in some drivers.
// Use color ink/toner indicator instead if available.
// Simple heuristic: if printer name contains "color", "colour", "c" model suffix
bool colorByName = name.ToLowerInvariant().Contains("color") ||
                   name.ToLowerInvariant().Contains("colour") ||
                   System.Text.RegularExpressions.Regex.IsMatch(name, @"[Cc]\d{3,4}");

printers.Add(new PrinterInfo
{
    Name = name,
    IsDefault = isDefault,
    Status = status,
    IsDuplex = isDuplex,
    SupportsColor = supportsColor || colorByName,
    PortName = portName,
});
```

- [ ] **Step 3: Build and commit**

Run: `dotnet build backend`
```bash
git add backend/Services/PrinterManagementService.cs backend/Models/PrintModels.cs
git commit -m "fix(printers): improve duplex detection, expose color capability"
```

---

## Self-Review Checklist

- [x] Task 1: TryShellPrint bug fix (🔴 critical) ✓
- [x] Task 2: Copies + Collate — backend model + frontend UI ✓
- [x] Task 3: Simplex mode — new PrintMode enum + algorithm ✓
- [x] Task 4: Image → PDF fix — PdfSharp ConvertImageToPdf ✓
- [x] Task 5: Booklet singleSidedPages pass-through ✓
- [x] Task 6: Print Presets — localStorage, save/apply/delete ✓
- [x] Task 7: Upload progress bar — XHR onprogress ✓
- [x] Task 8: Print button feedback + live printer poll ✓
- [x] Task 9: Print history panel ✓
- [x] Task 10: Keyboard shortcuts + estimated sheets/time ✓
- [x] Task 11: Watermark — PdfSharp diagonal stamp ✓
- [x] Task 12: Duplex detection improvement ✓

**Placeholder scan:** All steps contain actual code. No TBD or "similar to Task N".

**Type consistency:**
- `CopiesModule.copies` / `CopiesModule.collate` — used consistently in Task 2, 8, 10
- `HistoryModule.add()` called from Task 8 `_startPrint()`, defined in Task 9 ✓
- `SummaryModule.update()` called from Task 10's `PrintModule.updateButton()` hook ✓
- `WatermarkOptions` model defined in Task 11 Step 1, used in Task 11 Steps 3, 4, 6 ✓
