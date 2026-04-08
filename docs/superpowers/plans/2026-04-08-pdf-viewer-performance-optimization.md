# PDF Viewer Performance Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make switching between loaded PDF files feel near-instant and reduce first-render latency for scan/image-heavy PDFs without rewriting the viewer architecture.

**Architecture:** Keep per-file preview/thumb DOM alive instead of destroying and rebuilding it on every tab switch, then reduce first-render stalls by allowing document prefetch/warmup and removing unnecessary JPEG re-encoding from hot rendering paths. Preserve the current Vanilla JS + PDF.js module structure and optimize the existing render/caching pipeline incrementally.

**Tech Stack:** Vanilla JavaScript, PDF.js, OffscreenCanvas, existing `LRUBlobCache`, existing `CanvasPool`

---

## File Map

**Primary file to modify**
- `frontend/app.js`
  - `TabsModule.setActive()` - stop forcing full rebuild behavior on file switch
  - `PreviewPanelModule` - add per-file DOM persistence and visibility switching
  - `ThumbStripModule` - add per-file DOM persistence and visibility switching
  - `PreviewModule.renderEntry()` - adjust PDF.js load options and optional warmup
  - `_renderBlobPage()` / sheet-thumb rendering path - remove or reduce expensive blob JPEG re-encode on hot path

**Secondary file to modify**
- `frontend/styles.css`
  - add hidden/inactive container classes
  - add optional containment/content-visibility hints only after behavior is stable

**Verification**
- Manual verification in the app using at least:
  - one text PDF
  - one scan/image-heavy PDF
  - switching repeatedly between 2 loaded files

---

## Optimization Priority

1. **Highest ROI:** Persist per-file preview/thumb DOM instead of `innerHTML = ''` rebuilds
2. **Low-risk quick win:** Stop forcing `disableAutoFetch: true` for every PDF load
3. **High-impact scan optimization:** Remove `convertToBlob({ type: 'image/jpeg' })` from the hot display path where possible
4. **Later only:** queue tuning, CSS containment, larger cache sizes

---

### Task 1: Persist preview DOM per file

**Files:**
- Modify: `frontend/app.js`
- Modify: `frontend/styles.css`

- [ ] **Step 1: Add per-file preview container state**

Add fields inside `PreviewPanelModule` for file-scoped DOM persistence.

```js
_fileRoots: new Map(),      // fileId -> wrapper element
_activeRoot: null,
```

Keep `_pageEls` keyed by `fileId-pageNum` as-is.

- [ ] **Step 2: Replace destructive preview switching with hide/show behavior**

In `PreviewPanelModule.render(fileEntry)`, stop doing this on every file switch:

```js
this._pageEls.clear();
this._container.innerHTML = '';
```

Instead:

```js
for (const [fid, root] of this._fileRoots) {
    root.classList.toggle('preview-file-root--active', fid === fileEntry.id);
    root.classList.toggle('preview-file-root--hidden', fid !== fileEntry.id);
}

let root = this._fileRoots.get(fileEntry.id);
if (!root) {
    root = document.createElement('div');
    root.className = 'preview-file-root preview-file-root--active';
    root.dataset.fileId = fileEntry.id;
    this._fileRoots.set(fileEntry.id, root);
    this._container.appendChild(root);

    // create placeholder cards only once for this file
}

this._activeRoot = root;
this._currentFileId = fileEntry.id;
```

- [ ] **Step 3: Create preview page cards only once per file**

Move the placeholder-card creation loop behind a `if (!rootAlreadyExists)` guard.

Existing creation logic to keep, but only for first build of that file:

```js
for (let p = 1; p <= fileEntry.totalPageCount; p++) {
    const card = document.createElement('div');
    card.className = 'preview-page-card';
    card.dataset.fileId = fileEntry.id;
    card.dataset.page = p;
    const canvas = document.createElement('canvas');
    card.appendChild(canvas);
    this._pageEls.set(`${fileEntry.id}-${p}`, card);
    root.appendChild(card);
}
```

- [ ] **Step 4: Scope rendering and visibility checks to the active preview root**

Update `_renderVisible()`, `_onScroll()`, and `_unmountOffScreen()` so they only iterate cards for `this._currentFileId` and only measure DOM in the active root.

Preserve the existing `fileId` guard:

```js
if (!key.startsWith(this._currentFileId + '-')) return;
```

but do **not** clear other files' DOM/maps during a simple tab switch.

- [ ] **Step 5: Add preview root CSS classes**

In `frontend/styles.css` add:

```css
.preview-file-root {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.preview-file-root--hidden {
  display: none;
}
```

- [ ] **Step 6: Verify switching no longer rebuilds preview DOM**

Manual checks:
- Load 2 PDFs
- Switch A -> B -> A repeatedly
- Confirm previously viewed file reappears without full rebuild feel
- Confirm page selection and rotation badges remain correct after switching

---

### Task 2: Persist thumb-strip DOM per file

**Files:**
- Modify: `frontend/app.js`
- Modify: `frontend/styles.css`

- [ ] **Step 1: Add file-scoped thumb roots**

Add fields inside `ThumbStripModule`:

```js
_fileRoots: new Map(),   // fileId -> wrapper element
_activeRoot: null,
```

- [ ] **Step 2: Stop clearing the thumb container on file switch**

Replace the unconditional destructive reset:

```js
this._container.innerHTML = '';
```

with hide/show logic similar to preview roots.

- [ ] **Step 3: Build thumb items once per file**

Only create the `.thumb-item` elements when a file root does not exist yet.

Keep the current item creation logic but append into a per-file root wrapper.

- [ ] **Step 4: Restrict thumb render queue to the active file root**

Ensure `_renderVisible()` and `_unmountOffScreen()` only operate on the visible file's thumbs.

- [ ] **Step 5: Verify thumb switching behavior**

Manual checks:
- Active highlight still follows preview scroll
- Switching back to a previously viewed file restores thumb visuals immediately
- No duplicate thumb DOM appears after repeated switching

---

### Task 3: Reduce first-render latency by changing PDF.js load behavior

**Files:**
- Modify: `frontend/app.js`

- [ ] **Step 1: Remove forced `disableAutoFetch: true` in entry loading**

In both `pdfjsLib.getDocument(...)` call sites, change:

```js
disableAutoFetch: true,
```

to either remove the option or set:

```js
disableAutoFetch: false,
```

- [ ] **Step 2: Keep streaming enabled**

Preserve:

```js
disableStream: false,
```

This helps warm data progressively while the user starts viewing.

- [ ] **Step 3: Add small background warmup after document load**

After `entry.pdfDoc` is ready in `renderEntry(entry)`, warm the first page in the background:

```js
queueMicrotask(async () => {
    try {
        const page = await entry.pdfDoc.getPage(1);
        page.cleanup();
    } catch (_) {}
});
```

If safe in testing, extend later to pages `1..2` or thumb-scale warmup only.

- [ ] **Step 4: Verify scan first-render improvement**

Manual checks:
- Open scan PDF as first file
- Measure perceived delay to first visible page
- Compare before/after with browser performance timeline if needed

---

### Task 4: Remove JPEG blob re-encode from the hot display path

**Files:**
- Modify: `frontend/app.js`

- [ ] **Step 1: Split cache responsibilities by display target**

Keep current cache keying, but separate the concept of:
- canvas-backed cached render for visible display
- optional blob URL only where `<img src>` is unavoidable

- [ ] **Step 2: Avoid `convertToBlob()` for the main preview path**

Current hot code:

```js
const blob = await off.convertToBlob({ type: 'image/jpeg', quality: 0.88 });
const url = URL.createObjectURL(blob);
```

Change strategy so the main page preview uses existing canvas draw behavior (`drawImage(off, 0, 0)`) without JPEG blob encoding.

- [ ] **Step 3: Limit blob generation to thumb/sheet paths only if required**

If sheet/thumb still need `<img>` elements, keep blob generation there temporarily. Do **not** add blob generation to page-view rendering.

- [ ] **Step 4: If supported, test `ImageBitmap` as a follow-up optimization**

Optional follow-up experiment:

```js
const bitmap = off.transferToImageBitmap?.();
```

Only keep this if it is measurably faster and does not complicate cache lifecycle.

- [ ] **Step 5: Verify scan-heavy PDFs**

Manual checks:
- Open a scan PDF with many image-heavy pages
- Compare first paint and page-switch responsiveness
- Confirm memory usage remains acceptable after repeated switching

---

### Task 5: Add containment hints only after behavior is stable

**Files:**
- Modify: `frontend/styles.css`

- [ ] **Step 1: Add safe containment hints to large repeated nodes**

Start with conservative CSS:

```css
.preview-page-card,
.thumb-item {
  contain: layout paint;
}
```

- [ ] **Step 2: Test `content-visibility` only on inactive roots or non-critical sections**

Do **not** apply aggressively to active visible rendering until behavior is verified.

Example experimental rule:

```css
.preview-file-root--hidden {
  content-visibility: hidden;
}
```

- [ ] **Step 3: Verify no broken measurement logic**

Because the code uses `getBoundingClientRect()` heavily, verify that containment changes do not break visibility calculations.

---

## What Not To Do Yet

- Do **not** rewrite the viewer to React or another framework
- Do **not** replace PDF.js
- Do **not** increase caches blindly before removing DOM rebuilds
- Do **not** start with queue tuning; queue behavior is secondary to full DOM destruction
- Do **not** add IndexedDB/disk caching before validating the lighter in-memory improvements

---

## Expected Outcome Order

### Biggest immediate payoff
1. Preview DOM persistence
2. Thumb DOM persistence

### Fastest low-risk quick win
1. `disableAutoFetch: false`

### Best next optimization for scan PDFs
1. Reduce/remove JPEG blob re-encode on display path

---

## Verification Checklist

- [ ] Switching between 2 already-loaded files feels substantially faster
- [ ] No duplicate DOM roots after repeated tab switching
- [ ] Selected pages and rotation state persist correctly per file
- [ ] Thumb highlight remains synced to preview scroll
- [ ] Scan PDF first render is noticeably faster than before
- [ ] No obvious memory leak after opening, switching, and closing multiple files

---

Plan complete and saved to `docs/superpowers/plans/2026-04-08-pdf-viewer-performance-optimization.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
