# PDF Performance Optimization — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all PDF loading bottlenecks so previews feel instant — backend streaming + Range request support, pdf.js v5 upgrade, progressive render queue with cancel-and-replace, memory cleanup.

**Architecture:** Three independent layers: (1) Backend serves PDF as stream with Range + cache headers so pdf.js can fetch only needed chunks. (2) pdf.js upgraded to v5 with `disableAutoFetch`, `useWorkerFetch`, `rangeChunkSize`. (3) Frontend render queue renders thumbnails at scale 0.2 first, cancels superseded tasks, calls `page.cleanup()` after caching.

**Tech Stack:** ASP.NET Core Minimal API, pdf.js v5 (CDN), vanilla JS IntersectionObserver, RenderTask.cancel()

---

## File Map

| File | Change |
|------|--------|
| `backend/BackendStartup.cs` | Replace `ReadAllBytes` → `PhysicalFileResult` stream + `EnableRangeProcessing:true` + `Cache-Control` |
| `frontend/index.html` | Update CDN URLs: pdf.js 3.11.174 → 5.x, worker URL |
| `frontend/app.js` line 15-16 | Update `workerSrc` to match new version |
| `frontend/app.js` `PreviewModule.render()` ~line 885 | Add `disableAutoFetch`, `rangeChunkSize`, `useWorkerFetch` to `getDocument()` |
| `frontend/app.js` `PrintPreviewModule._renderThumb()` ~line 257 | Lower scale 0.3→0.2, add `page.cleanup()`, add `RenderTask` cancel tracking |
| `frontend/app.js` `PrintPreviewModule._renderMainPage()` ~line 329 | Add cancel-and-replace per-page render task tracking, `page.cleanup()` |

---

## Task 1: Backend — Stream PDF with Range support + cache headers

**Files:**
- Modify: `backend/BackendStartup.cs:104-119`

- [ ] **Step 1: Replace ReadAllBytes with streaming PhysicalFile**

Find the `/api/file/{fileId}` endpoint (lines 104-119) and replace:

```csharp
app.MapGet("/api/file/{fileId}", (string fileId, FileSessionService sessions, HttpContext ctx) =>
{
    try
    {
        var filePath = sessions.GetFilePath(fileId);
        if (filePath == null) return Results.NotFound("File not found");
        if (!File.Exists(filePath)) return Results.NotFound("File not found on disk");

        // Stream with Range support so pdf.js can fetch only needed chunks
        ctx.Response.Headers["Cache-Control"] = "private, max-age=3600";
        ctx.Response.Headers["Accept-Ranges"]  = "bytes";

        return Results.File(
            path:               filePath,
            contentType:        "application/pdf",
            fileDownloadName:   null,
            lastModified:       File.GetLastWriteTimeUtc(filePath),
            entityTag:          null,
            enableRangeProcessing: true);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error serving file: {ex.Message}");
    }
});
```

- [ ] **Step 2: Build backend and verify 0 errors**

```powershell
dotnet build backend/PrinterApp.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Verify Range headers with curl**

Start backend (`dotnet run --project backend/PrinterApp.csproj`), upload a file to get a fileId, then:

```powershell
curl -I http://localhost:8787/api/file/YOUR_FILE_ID
```

Expected output contains:
```
Accept-Ranges: bytes
Cache-Control: private, max-age=3600
Content-Type: application/pdf
```

- [ ] **Step 4: Commit**

```bash
git add backend/BackendStartup.cs
git commit -m "perf(backend): stream PDF with Range support and cache headers"
```

---

## Task 2: Upgrade pdf.js from v3.11.174 to v5

**Files:**
- Modify: `frontend/index.html` (CDN script tags)
- Modify: `frontend/app.js:15-16` (workerSrc)

- [ ] **Step 1: Check current pdf.js v5 CDN URL**

As of 2026, pdf.js v5 latest on cdnjs:
```
https://cdnjs.cloudflare.com/ajax/libs/pdf.js/5.0.375/pdf.min.mjs
https://cdnjs.cloudflare.com/ajax/libs/pdf.js/5.0.375/pdf.worker.min.mjs
```

Verify the version exists:
```powershell
curl -I "https://cdnjs.cloudflare.com/ajax/libs/pdf.js/5.0.375/pdf.min.mjs"
```

If 404, check https://cdnjs.com/libraries/pdf.js for the latest version and use that URL throughout this task.

- [ ] **Step 2: Update index.html CDN tags**

In `frontend/index.html`, find the existing pdf.js script tag (around line 14):
```html
<script src="https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.min.js"></script>
```

Replace with (note: v5 uses ES module `.mjs`):
```html
<script type="module">
  import * as pdfjsLib from 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/5.0.375/pdf.min.mjs';
  window.pdfjsLib = pdfjsLib;
</script>
```

**Important:** The `type="module"` script runs deferred by default. Move this tag BEFORE any `<script src="app.js">` tag to ensure `window.pdfjsLib` is set before app.js runs. If needed, convert the app.js script tag to also be `type="module"` or wrap app.js init in a `window.addEventListener('load', ...)`.

- [ ] **Step 3: Update workerSrc in app.js**

In `frontend/app.js` lines 14-16, replace:
```javascript
pdfjsLib.GlobalWorkerOptions.workerSrc =
    'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';
```

With:
```javascript
pdfjsLib.GlobalWorkerOptions.workerSrc =
    'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/5.0.375/pdf.worker.min.mjs';
```

- [ ] **Step 4: Test PDF loads in browser**

Open the app. Upload a PDF. Verify:
- No console errors about pdf.js version mismatch
- PDF thumbnails render correctly
- Print Preview modal opens and shows pages

- [ ] **Step 5: Commit**

```bash
git add frontend/index.html frontend/app.js
git commit -m "perf(frontend): upgrade pdf.js from v3.11 to v5"
```

---

## Task 3: getDocument() — enable progressive/chunked fetching

**Files:**
- Modify: `frontend/app.js` `PreviewModule.render()` ~line 885-892

- [ ] **Step 1: Update getDocument call with performance options**

Find `PreviewModule.render(fileId)` in app.js (~line 885). Replace the fetch+getDocument block:

```javascript
// OLD (fetch entire blob first):
// const blob     = await fetch(`${API_BASE}/file/${fileId}`).then(r => r.blob());
// const url      = URL.createObjectURL(blob);
// const loadTask = pdfjsLib.getDocument(url);

// NEW (stream directly via URL — works because backend now supports Range):
const loadTask = pdfjsLib.getDocument({
    url:              `${API_BASE}/file/${fileId}`,
    disableAutoFetch: true,   // don't download whole file upfront
    disableStream:    false,  // allow streaming
    rangeChunkSize:   65536,  // 64KB chunks
    useWorkerFetch:   true,   // worker handles HTTP, keeps main thread free
});
AppState.currentPdfDoc  = await loadTask.promise;
```

Remove the `URL.createObjectURL` call — no longer needed.

- [ ] **Step 2: Test first-page appears faster**

Upload a large PDF (>5MB). Open Print Preview. Verify:
- First page thumbnail appears before all pages are downloaded
- No "waiting for full file" delay before any UI appears

- [ ] **Step 3: Commit**

```bash
git add frontend/app.js
git commit -m "perf(frontend): stream PDF chunks via getDocument url instead of full blob fetch"
```

---

## Task 4: Render queue — cancel-and-replace + page.cleanup()

**Files:**
- Modify: `frontend/app.js` `PrintPreviewModule` (~lines 56-450)

- [ ] **Step 1: Add render task tracking maps to PrintPreviewModule**

At the top of `PrintPreviewModule` object (after `_scrollTimer: null`), add two new maps:

```javascript
_thumbRenderTasks: new Map(),  // pageNum → active RenderTask for thumbnail
_mainRenderTasks:  new Map(),  // pageNum → active RenderTask for main view
```

- [ ] **Step 2: Update _renderThumb to cancel superseded tasks + lower scale + cleanup**

Find `_renderThumb(pageNum)` (~line 254). Replace the render block inside it:

```javascript
async _renderThumb(pageNum) {
    if (this._thumbCache.has(pageNum)) {
        // Already rendered — just paint to visible canvas
        const el = this._thumbEls.get(pageNum);
        if (!el) return;
        const vis = el.querySelector('canvas');
        if (vis) {
            const ctx = vis.getContext('2d');
            ctx.drawImage(this._thumbCache.get(pageNum), 0, 0);
        }
        return;
    }

    // Cancel any in-progress render for this page
    const existing = this._thumbRenderTasks.get(pageNum);
    if (existing) { try { existing.cancel(); } catch (_) {} }

    const page = await AppState.currentPdfDoc.getPage(pageNum);
    const vp   = page.getViewport({ scale: 0.2 }); // 0.2 = fast low-res thumb

    const off     = document.createElement('canvas');
    off.width     = vp.width;
    off.height    = vp.height;
    const offCtx  = off.getContext('2d', { alpha: false });

    const task = page.render({ canvasContext: offCtx, viewport: vp, intent: 'display' });
    this._thumbRenderTasks.set(pageNum, task);

    try {
        await task.promise;
        this._thumbCache.set(pageNum, off);

        // Paint to visible canvas
        const el  = this._thumbEls.get(pageNum);
        const vis = el?.querySelector('canvas');
        if (vis) {
            vis.width  = vp.width;
            vis.height = vp.height;
            vis.getContext('2d').drawImage(off, 0, 0);
        }
    } catch (err) {
        if (err?.name !== 'RenderingCancelledException') console.warn('Thumb render error:', err);
    } finally {
        page.cleanup(); // release page resources
        this._thumbRenderTasks.delete(pageNum);
    }
},
```

- [ ] **Step 3: Update _renderMainPage to cancel-and-replace + cleanup**

Find `_renderMainPage(pageNum)` (~line 329). Replace the render block:

```javascript
async _renderMainPage(pageNum) {
    if (this._mainCache.has(pageNum)) {
        const el  = this._mainPageEls.get(pageNum);
        const vis = el?.querySelector('canvas');
        if (vis) {
            const ctx = vis.getContext('2d');
            ctx.drawImage(this._mainCache.get(pageNum), 0, 0);
        }
        return;
    }

    // Cancel superseded render for this page
    const existing = this._mainRenderTasks.get(pageNum);
    if (existing) { try { existing.cancel(); } catch (_) {} }

    const page = await AppState.currentPdfDoc.getPage(pageNum);
    const vp   = page.getViewport({ scale: 1.5 });

    const off    = document.createElement('canvas');
    off.width    = vp.width;
    off.height   = vp.height;
    const offCtx = off.getContext('2d', { alpha: false });

    const task = page.render({ canvasContext: offCtx, viewport: vp, intent: 'display' });
    this._mainRenderTasks.set(pageNum, task);

    try {
        await task.promise;
        this._mainCache.set(pageNum, off);

        const el  = this._mainPageEls.get(pageNum);
        const vis = el?.querySelector('canvas');
        if (vis) {
            vis.width  = vp.width;
            vis.height = vp.height;
            vis.getContext('2d').drawImage(off, 0, 0);
        }
    } catch (err) {
        if (err?.name !== 'RenderingCancelledException') console.warn('Main render error:', err);
    } finally {
        page.cleanup();
        this._mainRenderTasks.delete(pageNum);
    }
},
```

- [ ] **Step 4: Cancel all in-flight renders on modal close**

Find the `close()` method of `PrintPreviewModule`. Add at the start:

```javascript
close() {
    // Cancel all in-flight render tasks
    for (const task of this._thumbRenderTasks.values()) {
        try { task.cancel(); } catch (_) {}
    }
    for (const task of this._mainRenderTasks.values()) {
        try { task.cancel(); } catch (_) {}
    }
    this._thumbRenderTasks.clear();
    this._mainRenderTasks.clear();
    // ... rest of existing close() logic
},
```

- [ ] **Step 5: Test no console errors on rapid scroll**

Open Print Preview on a 20+ page PDF. Scroll rapidly up and down. Verify:
- No uncaught promise errors in console
- `RenderingCancelledException` does NOT appear in console (caught silently)
- Memory usage in DevTools doesn't grow unboundedly

- [ ] **Step 6: Commit**

```bash
git add frontend/app.js
git commit -m "perf(frontend): cancel-and-replace render queue, scale 0.2 thumbs, page.cleanup()"
```

---

## Verification Checklist

After all tasks complete:

- [ ] Upload a 50-page PDF — first thumbnail appears within 1s
- [ ] Range request visible in DevTools Network tab (status 206 Partial Content)
- [ ] Scrolling Print Preview: no console errors
- [ ] `Cache-Control: private, max-age=3600` present in response headers
- [ ] `dotnet build` on backend: 0 errors
