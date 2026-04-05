# Group 2 — Workflow & Navigation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve workflow navigation with a step indicator, live page range preview, keyboard navigation for thumbnails, aria-live status messages, and skeleton loading states.

**Architecture:** All changes are frontend-only (index.html, app.js, styles.css). New modules: `StepIndicatorModule`, `SRModule`. Modified modules: `PageSelectModule` (debounce), `PrinterModule` (skeleton), `PreviewModule` (skeleton).

**Tech Stack:** Vanilla JS, CSS animations, ARIA, `file://` protocol compatible.

---

## File Map

| File | Changes |
|------|---------|
| `frontend/index.html` | Add `#step-indicator` before first card; add `#sr-status` (if not already from Group 5 plan) |
| `frontend/styles.css` | Add step indicator styles; skeleton pulse; focus styles for keyboard nav |
| `frontend/app.js` | Add `StepIndicatorModule`; add `SRModule`; update `PageSelectModule.init()` (debounce); update `PrinterModule.init()` (skeleton → real); update `PreviewModule.render()` (skeleton thumbs) |

---

## Task 1: Step Indicator (2)

**Files:**
- Modify: `frontend/index.html` — add step indicator HTML before first card
- Modify: `frontend/styles.css` — add step indicator CSS
- Modify: `frontend/app.js` — add `StepIndicatorModule`, call `.update()` from relevant modules

- [ ] **Step 1: Add step indicator HTML to index.html**

In `frontend/index.html`, find `<main class="main-content">` (line 47). Add the step indicator immediately after it, before the first `<section class="card">`:

```html
<main class="main-content">
    <!-- Step Indicator (2) -->
    <nav class="step-indicator" id="step-indicator" aria-label="Tiến độ">
        <div class="step-indicator-item" id="step-ind-1" data-step="1">
            <div class="step-ind-circle"><span class="step-ind-num">1</span><span class="step-ind-check" hidden>✓</span></div>
            <div class="step-ind-label">Chọn Máy In</div>
        </div>
        <div class="step-indicator-line" id="step-line-1"></div>
        <div class="step-indicator-item" id="step-ind-2" data-step="2">
            <div class="step-ind-circle"><span class="step-ind-num">2</span><span class="step-ind-check" hidden>✓</span></div>
            <div class="step-ind-label">Tải File</div>
        </div>
        <div class="step-indicator-line" id="step-line-2"></div>
        <div class="step-indicator-item" id="step-ind-3" data-step="3">
            <div class="step-ind-circle"><span class="step-ind-num">3</span><span class="step-ind-check" hidden>✓</span></div>
            <div class="step-ind-label">Chọn Trang</div>
        </div>
        <div class="step-indicator-line" id="step-line-3"></div>
        <div class="step-indicator-item" id="step-ind-4" data-step="4">
            <div class="step-ind-circle"><span class="step-ind-num">4</span><span class="step-ind-check" hidden>✓</span></div>
            <div class="step-ind-label">Chế Độ In</div>
        </div>
        <div class="step-indicator-line" id="step-line-4"></div>
        <div class="step-indicator-item" id="step-ind-5" data-step="5">
            <div class="step-ind-circle"><span class="step-ind-num">5</span><span class="step-ind-check" hidden>✓</span></div>
            <div class="step-ind-label">In</div>
        </div>
    </nav>

    <!-- Step 1: Printer Selection -->
    <section class="card ...">
```

- [ ] **Step 2: Add step indicator CSS to styles.css**

In `frontend/styles.css`, add after `.main-content` block:

```css
/* ── Step Indicator (2) ────────────────────────────────────── */
.step-indicator {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 0;
    padding: 1.25rem 1rem;
    margin-bottom: 1.5rem;
}

.step-indicator-item {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.375rem;
    flex: 0 0 auto;
}

.step-ind-circle {
    width: 36px;
    height: 36px;
    border-radius: 50%;
    border: 2px solid var(--border-color);
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 0.875rem;
    font-weight: 700;
    color: var(--text-muted);
    background: var(--bg-card);
    transition: all 0.3s ease;
    position: relative;
}

.step-ind-label {
    font-size: 0.7rem;
    color: var(--text-muted);
    text-align: center;
    white-space: nowrap;
    transition: color 0.3s ease;
}

.step-indicator-line {
    flex: 1;
    height: 2px;
    background: var(--border-color);
    margin: 0 0.25rem;
    margin-bottom: 1rem; /* align with circles */
    transition: background 0.4s ease;
    min-width: 2rem;
    max-width: 4rem;
}

/* Active step */
.step-indicator-item.active .step-ind-circle {
    border-color: #667eea;
    background: rgba(102, 126, 234, 0.15);
    color: #667eea;
    box-shadow: 0 0 0 4px rgba(102, 126, 234, 0.15);
}

.step-indicator-item.active .step-ind-label {
    color: #667eea;
    font-weight: 600;
}

/* Completed step */
.step-indicator-item.done .step-ind-circle {
    border-color: #10b981;
    background: #10b981;
    color: white;
}

.step-indicator-item.done .step-ind-label {
    color: #10b981;
}

.step-indicator-item.done .step-ind-num  { display: none; }
.step-indicator-item.done .step-ind-check { display: inline !important; }

/* Completed line */
.step-indicator-line.done {
    background: #10b981;
}
```

- [ ] **Step 3: Add StepIndicatorModule to app.js**

In `frontend/app.js`, add before the `// BOOTSTRAP` section (around line 960):

```js
// ═══════════════════════════════════════════════════════════════════
// StepIndicatorModule — Progress indicator across 5 workflow steps (2)
// ═══════════════════════════════════════════════════════════════════
const StepIndicatorModule = {
    update() {
        const step1done = !!AppState.selectedPrinter;
        const step2done = !!AppState.uploadedFile;
        const step3done = AppState.totalPageCount > 0;
        const step4done = step3done; // always available once file loaded
        const step5done = false; // never "done" — it's the action

        // Determine current active step
        let activeStep;
        if (!step1done) activeStep = 1;
        else if (!step2done) activeStep = 2;
        else if (!step3done) activeStep = 3;
        else activeStep = 5;

        const doneSteps = [
            step1done,
            step2done,
            step3done,
            step4done,
            step5done,
        ];

        for (let i = 1; i <= 5; i++) {
            const item = document.getElementById(`step-ind-${i}`);
            if (!item) continue;
            const isDone   = doneSteps[i - 1] && i < activeStep;
            const isActive = i === activeStep;
            item.classList.toggle('done',   isDone && !isActive);
            item.classList.toggle('active', isActive);
        }

        // Update connecting lines
        for (let i = 1; i <= 4; i++) {
            const line = document.getElementById(`step-line-${i}`);
            if (!line) continue;
            line.classList.toggle('done', doneSteps[i - 1] && activeStep > i);
        }
    },
};
```

- [ ] **Step 4: Call StepIndicatorModule.update() from key modules**

In `frontend/app.js`:

1. In `PrinterModule.init()`, after `defItem?.click()`:
   ```js
   StepIndicatorModule.update();
   ```

2. In `PrinterModule` click handler (after `PrintModule.updateButton()`):
   ```js
   StepIndicatorModule.update();
   ```

3. In `UploadModule._upload()`, after `PrintModule.updateButton()`:
   ```js
   StepIndicatorModule.update();
   ```

4. In `UploadModule._remove()`, after `PrintModule.updateButton()`:
   ```js
   StepIndicatorModule.update();
   ```

5. In the BOOTSTRAP section, after `SummaryModule.update()`:
   ```js
   StepIndicatorModule.update();
   ```

- [ ] **Step 5: Verify step indicator**

Load app: Step 1 is active. Select printer: Step 1 turns green ✓, Step 2 becomes active. Upload file: Step 2 turns green ✓, Step 3 becomes active. Page range card visible: Step 3 turns green. Step 5 is always the last.

- [ ] **Step 6: Commit**

```bash
git add frontend/index.html frontend/styles.css frontend/app.js
git commit -m "feat(ui): step indicator progress bar (2)"
```

---

## Task 2: Page Range Live Preview (6)

**Files:**
- Modify: `frontend/app.js` — `PageSelectModule.init()`: add debounced `input` listener

**Note:** The current code already has an `input` listener (lines 405-414). This task improves it by adding proper debouncing and input validation feedback.

- [ ] **Step 1: Add debounce utility and update PageSelectModule.init()**

In `frontend/app.js`, find `PageSelectModule.init()` (around line 399). The `input` handler is there but needs debouncing and error feedback. Replace the existing `input` event listener block:

```js
// Inside PageSelectModule.init():
let _rangeDebounce = null;

input.addEventListener('input', e => {
    AppState.isUserTypingPageRange = true;
    clearTimeout(_rangeDebounce);
    _rangeDebounce = setTimeout(() => {
        const text = e.target.value.trim();
        if (!text) {
            AppState.selectAllPages();
            input.style.borderColor = '';
        } else {
            const parsed = this._parseRange(text);
            if (parsed.size === 0 && text.length > 0) {
                // Invalid range — show red border, don't change selection
                input.style.borderColor = 'rgba(239, 68, 68, 0.6)';
            } else {
                AppState.selectedPages = parsed;
                input.style.borderColor = '';
            }
        }
        PreviewModule.updateThumbnails();
        this._updateTexts();
        PrintModule.updateButton();
        StepIndicatorModule.update();
    }, 200);
});
```

- [ ] **Step 2: Verify live preview**

Upload a PDF with multiple pages. Type `1-3` in the page range box — without pressing Enter, the sidebar thumbnails should immediately highlight pages 1, 2, 3. Type an invalid range like `abc` — border turns red. Clear input — all pages selected again.

- [ ] **Step 3: Commit**

```bash
git add frontend/app.js
git commit -m "feat(ui): page range live preview with debounce + validation (6)"
```

---

## Task 3: Keyboard Navigation for Thumbnail Grid (B)

**Files:**
- Modify: `frontend/styles.css` — add focus ring styles for thumbnails
- Modify: `frontend/app.js` — `PreviewModule._createPlaceholder()` adds `tabindex`; `KeyboardModule.init()` adds arrow key handlers on grid

- [ ] **Step 1: Add focus ring CSS for thumbnails**

In `frontend/styles.css`, find `.sidebar-preview-grid .page-thumbnail`. Add:

```css
/* ── Keyboard Navigation focus styles (B) ─────────────────── */
.page-thumbnail:focus {
    outline: 2px solid #667eea;
    outline-offset: 2px;
}

.page-thumbnail:focus-visible {
    outline: 2px solid #667eea;
    outline-offset: 2px;
}
```

Also update the grid container:

```css
.sidebar-preview-grid {
    /* existing properties ... */
    /* Add: */
    /* role="listbox" set in JS */
}
```

- [ ] **Step 2: Add tabindex and ARIA to thumbnails in PreviewModule._createPlaceholder()**

In `frontend/app.js`, in `PreviewModule._createPlaceholder(pageNum)` (around line 347), add after `div.dataset.pageNumber = pageNum`:

```js
div.setAttribute('tabindex', '0');
div.setAttribute('role', 'option');
div.setAttribute('aria-label', `Trang ${pageNum}`);
div.setAttribute('aria-selected', 'true'); // updated in updateThumbnails()
```

Also add to `PreviewModule.render()`, after `grid.innerHTML = ''`:

```js
grid.setAttribute('role', 'listbox');
grid.setAttribute('aria-label', 'Danh sách trang');
grid.setAttribute('aria-multiselectable', 'true');
```

And update `PreviewModule.updateThumbnails()` to sync `aria-selected`:

```js
updateThumbnails() {
    document.querySelectorAll('.page-thumbnail').forEach(thumb => {
        const n      = parseInt(thumb.dataset.pageNumber);
        const sel    = AppState.selectedPages.has(n);
        const single = AppState.singleSidedPages.has(n);
        thumb.classList.toggle('selected', sel);
        thumb.style.borderColor = sel ? (single ? '#3b82f6' : '#22c55e') : 'rgba(148,163,184,0.2)';
        thumb.title = `Trang ${n} - In ${single ? '1' : '2'} mặt`;
        thumb.setAttribute('aria-selected', sel ? 'true' : 'false');
    });
},
```

- [ ] **Step 3: Add arrow key navigation in KeyboardModule.init()**

In `frontend/app.js`, inside `KeyboardModule.init()` (around line 880), add to the keydown handler:

```js
// Arrow key navigation for thumbnail grid
if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    const grid = document.getElementById('sidebar-preview-grid');
    if (!grid) return;
    const thumbs = Array.from(grid.querySelectorAll('.page-thumbnail'));
    if (!thumbs.length) return;
    const focused = document.activeElement;
    const idx = thumbs.indexOf(focused);
    if (idx === -1) {
        // No thumb focused — focus first
        thumbs[0].focus();
        return;
    }
    e.preventDefault();
    const next = e.key === 'ArrowDown'
        ? thumbs[Math.min(idx + 1, thumbs.length - 1)]
        : thumbs[Math.max(idx - 1, 0)];
    next.focus();
    next.scrollIntoView({ block: 'nearest' });
}

// Space/Enter to toggle selection when thumbnail is focused
if ((e.key === ' ' || e.key === 'Enter') && document.activeElement?.classList.contains('page-thumbnail')) {
    e.preventDefault();
    const n = parseInt(document.activeElement.dataset.pageNumber);
    if (!isNaN(n)) PageSelectModule.toggle(n);
}
```

- [ ] **Step 4: Verify keyboard navigation**

Upload a PDF. Tab to the first thumbnail. Press ArrowDown to move to next, ArrowUp to go back. Press Space to select/deselect. Each thumbnail should have a clear blue focus ring.

- [ ] **Step 5: Commit**

```bash
git add frontend/styles.css frontend/app.js
git commit -m "feat(ui): keyboard navigation for thumbnail grid (B)"
```

---

## Task 4: aria-live Status Messages (C)

**Files:**
- Modify: `frontend/index.html` — add `#sr-status` (if not already added in Group 5 plan)
- Modify: `frontend/styles.css` — add `.sr-only` (if not already added in Group 5 plan)
- Modify: `frontend/app.js` — add `SRModule`, call `SRModule.announce()` from key events

**Note:** If Group 5 plan was already implemented, `#sr-status` and `.sr-only` already exist. Skip those steps.

- [ ] **Step 1: Ensure #sr-status and .sr-only exist**

In `frontend/index.html`, verify before `</body>`:
```html
<div id="sr-status" aria-live="polite" aria-atomic="true" class="sr-only"></div>
```

In `frontend/styles.css`, verify `.sr-only` exists:
```css
.sr-only {
    position: absolute;
    width: 1px;
    height: 1px;
    padding: 0;
    margin: -1px;
    overflow: hidden;
    clip: rect(0, 0, 0, 0);
    white-space: nowrap;
    border: 0;
}
```

If either is missing, add it now.

- [ ] **Step 2: Add SRModule to app.js**

In `frontend/app.js`, add before the BOOTSTRAP section:

```js
// ═══════════════════════════════════════════════════════════════════
// SRModule — Screen reader announcements via aria-live (C)
// ═══════════════════════════════════════════════════════════════════
const SRModule = {
    _timer: null,
    announce(msg) {
        const el = document.getElementById('sr-status');
        if (!el) return;
        el.textContent = '';
        clearTimeout(this._timer);
        // Brief delay to ensure screen reader picks up the change
        this._timer = setTimeout(() => {
            el.textContent = msg;
            this._timer = setTimeout(() => { el.textContent = ''; }, 1500);
        }, 50);
    },
};
```

- [ ] **Step 3: Call SRModule.announce() from key state changes**

Add `SRModule.announce()` calls at these points in `app.js`:

1. **Printer selected** (in `PrinterModule.init()` click handler, after `AppState.selectedPrinter = ...`):
   ```js
   SRModule.announce(`Đã chọn máy in: ${AppState.selectedPrinter.name}`);
   ```

2. **File upload complete** (in `UploadModule._upload()`, after `showToast('Tai file thanh cong!'...)`):
   ```js
   SRModule.announce(`Đã tải file ${AppState.uploadedFile.name}, ${AppState.totalPageCount} trang`);
   ```

3. **Page selection changed** (in `PageSelectModule._updateTexts()`, end of function):
   ```js
   const msg = AppState.selectedPages.size === AppState.totalPageCount
       ? 'Đã chọn tất cả trang'
       : `Đã chọn ${AppState.selectedPages.size} trang`;
   SRModule.announce(msg);
   ```

4. **Print started** (in `PrintModule._startPrint()`, after `showToast('Dang gui lenh in...')`):
   ```js
   SRModule.announce('Đang gửi lệnh in...');
   ```

5. **Print complete** (in `PrintModule._startPrint()`, after `showToast('In thanh cong!'...)`):
   ```js
   SRModule.announce('In thành công!');
   ```

- [ ] **Step 4: Verify aria-live**

Use a screen reader (Windows Narrator: Win+Ctrl+Enter) or verify in Chrome accessibility tree (DevTools → Accessibility panel → aria-live). Interact with the app — select a printer, upload a file, change page range. Announcements should fire without visual change.

- [ ] **Step 5: Commit**

```bash
git add frontend/index.html frontend/styles.css frontend/app.js
git commit -m "feat(a11y): aria-live status announcements (C)"
```

---

## Task 5: Skeleton Loading State (T)

**Files:**
- Modify: `frontend/styles.css` — add skeleton pulse CSS
- Modify: `frontend/index.html` — replace loading text in printer-list with skeleton HTML
- Modify: `frontend/app.js` — `PrinterModule.init()` shows skeletons before data loads; `PreviewModule._createPlaceholder()` uses skeleton style

- [ ] **Step 1: Add skeleton CSS to styles.css**

In `frontend/styles.css`, add:

```css
/* ── Skeleton Loading State (T) ────────────────────────────── */
@keyframes skeletonPulse {
    0%   { background-position: -200% center; }
    100% { background-position:  200% center; }
}

.skeleton {
    background: linear-gradient(
        90deg,
        rgba(30, 41, 59, 0.8)    25%,
        rgba(51, 65, 85, 0.8)    50%,
        rgba(30, 41, 59, 0.8)    75%
    );
    background-size: 200% 100%;
    animation: skeletonPulse 1.5s infinite linear;
    border-radius: 6px;
}

[data-theme="light"] .skeleton {
    background: linear-gradient(
        90deg,
        rgba(226, 232, 240, 1)   25%,
        rgba(203, 213, 225, 1)   50%,
        rgba(226, 232, 240, 1)   75%
    );
    background-size: 200% 100%;
}

.skeleton-printer-item {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    padding: 1rem;
    border-radius: 12px;
    border: 2px solid transparent;
    background: rgba(100, 116, 139, 0.05);
}

.skeleton-icon {
    width: 36px;
    height: 36px;
    border-radius: 8px;
    flex-shrink: 0;
}

.skeleton-lines {
    flex: 1;
    display: flex;
    flex-direction: column;
    gap: 0.4rem;
}

.skeleton-line {
    height: 12px;
}

.skeleton-line-short {
    height: 10px;
    width: 60%;
}

.skeleton-thumb {
    width: 100%;
    min-height: 100px;
    border-radius: 8px;
    border: 2px solid transparent;
}
```

- [ ] **Step 2: Replace printer-list initial HTML with skeletons in index.html**

In `frontend/index.html`, find (line 54-56):
```html
<div id="printer-list" class="printer-list">
    <div class="loading">Đang tải danh sách máy in...</div>
</div>
```

Replace with:
```html
<div id="printer-list" class="printer-list">
    <div class="skeleton-printer-item">
        <div class="skeleton skeleton-icon"></div>
        <div class="skeleton-lines">
            <div class="skeleton skeleton-line"></div>
            <div class="skeleton skeleton-line-short"></div>
        </div>
    </div>
    <div class="skeleton-printer-item">
        <div class="skeleton skeleton-icon"></div>
        <div class="skeleton-lines">
            <div class="skeleton skeleton-line"></div>
            <div class="skeleton skeleton-line-short"></div>
        </div>
    </div>
</div>
```

- [ ] **Step 3: Replace sidebar initial HTML with skeleton in index.html**

In `frontend/index.html`, find (line 33-35):
```html
<div class="sidebar-preview-grid" id="sidebar-preview-grid">
    <div class="loading">Đang tải...</div>
</div>
```

Replace with (the skeleton thumbs appear here before PDF loads — sidebar is `display:none` by default so this only matters when sidebar is shown):
```html
<div class="sidebar-preview-grid" id="sidebar-preview-grid">
    <div class="skeleton skeleton-thumb"></div>
    <div class="skeleton skeleton-thumb"></div>
    <div class="skeleton skeleton-thumb"></div>
</div>
```

- [ ] **Step 4: Replace loading text in PreviewModule.render() with skeleton HTML**

In `frontend/app.js`, in `PreviewModule.render(fileId)` (around line 308), replace:
```js
grid.innerHTML = '<div class="loading">Dang tai preview...</div>';
```
With:
```js
grid.innerHTML = `
    <div class="skeleton skeleton-thumb"></div>
    <div class="skeleton skeleton-thumb"></div>
    <div class="skeleton skeleton-thumb"></div>
    <div class="skeleton skeleton-thumb"></div>
`;
```

- [ ] **Step 5: Verify skeleton states**

Load the app — printer list shows 2 pulsing skeleton rows instead of "Đang tải...". When printers load, skeletons are replaced with real items. Upload a PDF — sidebar shows skeleton thumbs while pages render, then real canvases replace them.

- [ ] **Step 6: Commit**

```bash
git add frontend/styles.css frontend/index.html frontend/app.js
git commit -m "feat(ui): skeleton loading states for printer list and thumbnails (T)"
```

---

## Task 6: Final Verification

- [ ] **Step 1: Verify all 5 Group 2 features**

- [ ] Step indicator shows correct active/done state at each workflow stage
- [ ] Page range input updates thumbnails live (200ms debounce), red border on invalid input
- [ ] Keyboard: Tab to thumbnails, ArrowDown/Up moves focus, Space toggles selection
- [ ] Aria-live announces: printer selected, file loaded, page count changed, print started/done
- [ ] Skeleton pulses shown for printer list and thumbnails while loading

- [ ] **Step 2: No regressions**

Verify existing features still work: drag/drop upload, print job, history panel, ZoomModal, context menu, keyboard shortcuts (Ctrl+P, Ctrl+O, A, Esc).

- [ ] **Step 3: Commit count**

```bash
git log --oneline -10
```

Should show 5 feature commits from this plan.
