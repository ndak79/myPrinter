# Group 5 — UI Polish & Animations — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add animations, micro-interactions, and UI polish to make myPrinter feel premium — staggered card entrance, ripple effects, shimmer, frosted glass sidebar, upgraded toast, theme transition, per-item history delete, reprint from history.

**Architecture:** All changes are pure CSS/JS in the existing 3-file frontend (index.html, app.js, styles.css). No backend changes. No new dependencies. Each feature is isolated to its own CSS classes and JS module methods.

**Tech Stack:** Vanilla JS (ES5-compatible), CSS `@keyframes`, CSS `backdrop-filter`, `localStorage`, `file://` protocol compatible.

---

## File Map

| File | Changes |
|------|---------|
| `frontend/styles.css` | Add @keyframes for cardEntrance, pop, shimmer, ripple, modeSelect, borderGlow, toastSlideIn, skeletonPulse; add theme transitions; add frosted glass sidebar; add .sr-only |
| `frontend/index.html` | Add data-animate-delay to cards; replace `#toast` with `#toast-container`; add `#sr-status` |
| `frontend/app.js` | Rewrite ToastModule; add ripple to PrintModule; add shimmer to UploadModule; add HistoryModule.removeItem(); add history reprint; add StepAnimModule; add SRModule |

---

## Task 1: Theme Transition Animation (H)

**Files:**
- Modify: `frontend/styles.css` — add transition declarations

- [ ] **Step 1: Add CSS transitions to key elements for smooth theme switching**

In `frontend/styles.css`, find the `:root` block (line 1). After it, add:

```css
/* ── Theme Transition (H) ──────────────────────────────────── */
*, *::before, *::after {
    transition:
        background-color 300ms ease,
        background 300ms ease,
        border-color 300ms ease,
        color 300ms ease;
}

/* Exclude transitions that cause visual glitches */
.page-thumbnail,
.page-thumbnail canvas,
canvas,
.ripple,
.skeleton,
.upload-progress-bar {
    transition: none !important;
}
```

- [ ] **Step 2: Verify theme switch is smooth**

Open `frontend/index.html` in Chrome. Click the A/L/D theme buttons in top-right. The background, cards, sidebar, and text should smoothly crossfade instead of snapping instantly. Confirm no canvas or progress bar flickers.

- [ ] **Step 3: Commit**

```bash
git add frontend/styles.css
git commit -m "feat(ui): add smooth theme transition animation (H)"
```

---

## Task 2: Card Entrance Animation (K)

**Files:**
- Modify: `frontend/styles.css` — add @keyframes cardEntrance
- Modify: `frontend/index.html` — add style attributes for animation-delay on each card

- [ ] **Step 1: Add @keyframes and card animation CSS**

In `frontend/styles.css`, after the `.card:hover` block (around line 200), add:

```css
/* ── Card Entrance Animation (K) ──────────────────────────── */
@keyframes cardEntrance {
    from {
        opacity: 0;
        transform: translateY(20px);
    }
    to {
        opacity: 1;
        transform: translateY(0);
    }
}

.card-animate {
    animation: cardEntrance 400ms ease-out forwards;
    opacity: 0;
}
```

- [ ] **Step 2: Add .card-animate class and animation-delay to each card in index.html**

In `frontend/index.html`, update each step card's `<section class="card">` to add the class and delay:

```html
<!-- Step 1: Printer Selection -->
<section class="card card-animate" style="animation-delay: 0ms;">

<!-- Step 2: File Upload -->
<section class="card card-animate" style="animation-delay: 80ms;">

<!-- Step 3: Page Range Selection -->
<section class="card hidden card-animate" id="page-range-section" style="animation-delay: 160ms;">

<!-- Step 4: Print Mode -->
<section class="card card-animate" style="animation-delay: 240ms;">

<!-- Step 5: Print Button -->
<section class="action-section card-animate" style="animation-delay: 320ms;">
```

- [ ] **Step 3: Verify staggered entrance**

Reload `frontend/index.html` in Chrome. The 5 step sections should fade in with upward slide, one after another (card 1 first, card 5 last, ~80ms apart).

- [ ] **Step 4: Commit**

```bash
git add frontend/styles.css frontend/index.html
git commit -m "feat(ui): add staggered card entrance animation (K)"
```

---

## Task 3: Frosted Glass Sidebar (Q)

**Files:**
- Modify: `frontend/styles.css` — update `.sidebar` styles

- [ ] **Step 1: Update sidebar to frosted glass**

In `frontend/styles.css`, find the `.sidebar` block (around line 47). Replace the `background` line:

```css
/* Old: background: var(--bg-secondary); */

/* ── Frosted Glass Sidebar (Q) ─────────────────────────────── */
.sidebar {
    width: 280px;
    background: rgba(15, 23, 42, 0.75);
    backdrop-filter: blur(20px) saturate(180%);
    -webkit-backdrop-filter: blur(20px) saturate(180%);
    border-right: 1px solid rgba(255, 255, 255, 0.08);
    display: flex;
    flex-direction: column;
    position: sticky;
    top: 0;
    height: 100vh;
    overflow: hidden;
}
```

- [ ] **Step 2: Add light theme override for frosted glass**

After the `.sidebar` block, find where light theme overrides are defined (search for `[data-theme="light"]`). Add:

```css
[data-theme="light"] .sidebar {
    background: rgba(241, 245, 249, 0.80);
    backdrop-filter: blur(20px) saturate(160%);
    -webkit-backdrop-filter: blur(20px) saturate(160%);
    border-right: 1px solid rgba(0, 0, 0, 0.08);
}
```

- [ ] **Step 3: Verify frosted glass effect**

Open the app. Sidebar should look like frosted glass (content behind slightly visible through blur). Test in both dark and light themes.

- [ ] **Step 4: Commit**

```bash
git add frontend/styles.css
git commit -m "feat(ui): frosted glass sidebar (Q)"
```

---

## Task 4: Progress Bar Shimmer (M)

**Files:**
- Modify: `frontend/styles.css` — add shimmer keyframes + pseudo-element
- Modify: `frontend/app.js` — UploadModule adds/removes `.uploading` class

- [ ] **Step 1: Add shimmer CSS to styles.css**

In `frontend/styles.css`, find the `.upload-progress-bar` selector. Add after it:

```css
/* ── Progress Bar Shimmer (M) ──────────────────────────────── */
@keyframes shimmer {
    0%   { background-position: -200% center; }
    100% { background-position:  200% center; }
}

.upload-progress-bar {
    position: relative;
    overflow: hidden;
}

.upload-progress-bar.uploading::after {
    content: '';
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    background: linear-gradient(
        90deg,
        transparent 20%,
        rgba(255, 255, 255, 0.25) 50%,
        transparent 80%
    );
    background-size: 200% 100%;
    animation: shimmer 1.5s infinite linear;
}
```

- [ ] **Step 2: Add .uploading class in UploadModule._upload()**

In `frontend/app.js`, inside `UploadModule._upload()`, find where progress bar is shown (around line 235):

```js
// After: if (wrap) wrap.classList.remove('hidden');
// Add:
if (bar) bar.classList.add('uploading');
```

And after upload completes (around line 256, where `wrap.classList.add('hidden')`):

```js
// After: if (wrap) wrap.classList.add('hidden');
// Add:
if (bar) bar.classList.remove('uploading');
if (bar) bar.style.width = '0%';
```

- [ ] **Step 3: Verify shimmer**

Upload any PDF. During upload, the progress bar should show a moving shimmer highlight. After upload, shimmer stops.

- [ ] **Step 4: Commit**

```bash
git add frontend/styles.css frontend/app.js
git commit -m "feat(ui): progress bar shimmer animation (M)"
```

---

## Task 5: Print Button Ripple Effect (L)

**Files:**
- Modify: `frontend/styles.css` — add .ripple keyframes
- Modify: `frontend/app.js` — PrintModule.init() adds ripple handler

- [ ] **Step 1: Add ripple CSS**

In `frontend/styles.css`, add:

```css
/* ── Print Button Ripple (L) ───────────────────────────────── */
@keyframes rippleAnim {
    from {
        transform: scale(0);
        opacity: 0.5;
    }
    to {
        transform: scale(4);
        opacity: 0;
    }
}

#print-btn {
    position: relative;
    overflow: hidden;
}

.ripple {
    position: absolute;
    border-radius: 50%;
    background: rgba(255, 255, 255, 0.35);
    width: 60px;
    height: 60px;
    margin-top: -30px;
    margin-left: -30px;
    animation: rippleAnim 600ms ease-out forwards;
    pointer-events: none;
}
```

- [ ] **Step 2: Add ripple handler in PrintModule.init()**

In `frontend/app.js`, inside `PrintModule.init()` (around line 754), replace the click listener with:

```js
init() {
    const btn = document.getElementById('print-btn');
    btn.addEventListener('click', (e) => {
        // Ripple
        const ripple = document.createElement('span');
        ripple.className = 'ripple';
        const rect = btn.getBoundingClientRect();
        ripple.style.left = (e.clientX - rect.left) + 'px';
        ripple.style.top  = (e.clientY - rect.top)  + 'px';
        btn.appendChild(ripple);
        ripple.addEventListener('animationend', () => ripple.remove());

        this._startPrint();
    });
    document.getElementById('continue-btn')?.addEventListener('click', async () => {
        await this._continuePrint();
        document.getElementById('flip-modal').classList.add('hidden');
    });
},
```

- [ ] **Step 3: Verify ripple**

Click the "Bắt Đầu In" button (even when disabled will still show ripple — optionally gate it by checking `!btn.disabled` before creating ripple). A white ripple should expand from the click point.

- [ ] **Step 4: Commit**

```bash
git add frontend/styles.css frontend/app.js
git commit -m "feat(ui): print button ripple effect (L)"
```

---

## Task 6: Mode Card Selection Animation (P)

**Files:**
- Modify: `frontend/styles.css` — add @keyframes modeSelect + .mode-card.selected styles

- [ ] **Step 1: Add mode card animation CSS**

In `frontend/styles.css`, find the `.mode-card` selector. Add after it:

```css
/* ── Mode Card Animation (P) ───────────────────────────────── */
@keyframes modeSelect {
    0%   { transform: scale(1); }
    40%  { transform: scale(1.04); }
    100% { transform: scale(1); }
}

@keyframes iconPulse {
    0%   { transform: scale(1) rotate(0deg); }
    30%  { transform: scale(1.2) rotate(-10deg); }
    60%  { transform: scale(0.95) rotate(5deg); }
    100% { transform: scale(1) rotate(0deg); }
}

input[name="print-mode"]:checked + .mode-card {
    animation: modeSelect 250ms ease-out;
}

input[name="print-mode"]:checked + .mode-card .mode-icon {
    animation: iconPulse 350ms ease-out;
}

.mode-option:not(:has(input:checked)) .mode-card {
    opacity: 0.75;
    transition: opacity 200ms ease;
}

.mode-option:has(input:checked) .mode-card {
    opacity: 1;
}
```

- [ ] **Step 2: Verify mode animation**

Click each print mode radio (In 2 Mặt, In 1 Mặt, Sách A5). The newly selected card should do a brief scale-up animation; non-selected cards should dim slightly. The icon emoji should do a brief wiggle.

Note: `:has()` selector requires Chrome 105+. If it needs a fallback, skip the opacity logic — the animation alone is the main feature.

- [ ] **Step 3: Commit**

```bash
git add frontend/styles.css
git commit -m "feat(ui): mode card selection animation (P)"
```

---

## Task 7: Toast Notification Upgrade (S)

**Files:**
- Modify: `frontend/index.html` — replace `#toast` with `#toast-container`
- Modify: `frontend/styles.css` — add toast-item styles + @keyframes toastSlideIn + .toast-countdown
- Modify: `frontend/app.js` — rewrite ToastModule

- [ ] **Step 1: Replace toast HTML in index.html**

Find and replace the old toast HTML (around line 265):

```html
<!-- OLD: -->
<div id="toast" class="toast hidden">
    <span id="toast-message"></span>
</div>

<!-- NEW: -->
<div id="toast-container" class="toast-container"></div>
<div id="sr-status" aria-live="polite" aria-atomic="true" class="sr-only"></div>
```

- [ ] **Step 2: Add toast CSS to styles.css**

Find and replace old `.toast` CSS block. Add:

```css
/* ── Toast Upgrade (S) ─────────────────────────────────────── */
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

.toast-container {
    position: fixed;
    bottom: 2rem;
    right: 2rem;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    z-index: 9999;
    pointer-events: none;
}

@keyframes toastSlideIn {
    from {
        opacity: 0;
        transform: translateX(120%);
    }
    to {
        opacity: 1;
        transform: translateX(0);
    }
}

@keyframes toastSlideOut {
    from {
        opacity: 1;
        transform: translateX(0);
        max-height: 80px;
    }
    to {
        opacity: 0;
        transform: translateX(120%);
        max-height: 0;
        margin: 0;
        padding: 0;
    }
}

.toast-item {
    display: flex;
    flex-direction: column;
    min-width: 280px;
    max-width: 380px;
    background: var(--bg-card);
    backdrop-filter: blur(10px);
    border: 1px solid var(--border-color);
    border-radius: 12px;
    padding: 0.875rem 1rem 0;
    box-shadow: var(--shadow-lg);
    animation: toastSlideIn 250ms ease-out forwards;
    pointer-events: auto;
    overflow: hidden;
    cursor: pointer;
}

.toast-item.dismissing {
    animation: toastSlideOut 300ms ease-in forwards;
}

.toast-item-body {
    display: flex;
    align-items: center;
    gap: 0.625rem;
    padding-bottom: 0.75rem;
}

.toast-item-icon {
    font-size: 1.125rem;
    flex-shrink: 0;
}

.toast-item-msg {
    font-size: 0.875rem;
    color: var(--text-primary);
    flex: 1;
}

.toast-item-border-success { border-color: rgba(16, 185, 129, 0.5); }
.toast-item-border-error   { border-color: rgba(239, 68, 68, 0.5); }
.toast-item-border-info    { border-color: rgba(148, 163, 184, 0.3); }

@keyframes toastCountdown {
    from { width: 100%; }
    to   { width: 0%; }
}

.toast-countdown {
    height: 3px;
    border-radius: 0 0 12px 12px;
    animation: toastCountdown linear forwards;
    margin: 0 -1rem;
}

.toast-countdown-success { background: #10b981; }
.toast-countdown-error   { background: #ef4444; }
.toast-countdown-info    { background: #667eea; }
```

- [ ] **Step 3: Rewrite ToastModule in app.js**

Replace the entire `ToastModule` (lines 45-66 in app.js) with:

```js
// ═══════════════════════════════════════════════════════════════════
// ToastModule — Upgraded sliding toast notifications (S)
// ═══════════════════════════════════════════════════════════════════
const ToastModule = {
    _MAX: 3,

    show(message, type = 'info', duration = 3000) {
        const container = document.getElementById('toast-container');
        if (!container) return;

        // Enforce max stack
        const existing = container.querySelectorAll('.toast-item:not(.dismissing)');
        if (existing.length >= this._MAX) {
            this._dismiss(existing[0]);
        }

        const icons = { success: '✅', error: '❌', info: 'ℹ️' };
        const icon  = icons[type] || 'ℹ️';

        const item = document.createElement('div');
        item.className = `toast-item toast-item-border-${type}`;
        item.innerHTML = `
            <div class="toast-item-body">
                <span class="toast-item-icon">${icon}</span>
                <span class="toast-item-msg">${message}</span>
            </div>
            <div class="toast-countdown toast-countdown-${type}"
                 style="animation-duration: ${duration}ms;"></div>
        `;

        item.addEventListener('click', () => this._dismiss(item));
        container.appendChild(item);

        // Screen reader announce
        const sr = document.getElementById('sr-status');
        if (sr) { sr.textContent = message; setTimeout(() => { sr.textContent = ''; }, 1000); }

        setTimeout(() => this._dismiss(item), duration);
    },

    _dismiss(item) {
        if (!item || item.classList.contains('dismissing')) return;
        item.classList.add('dismissing');
        item.addEventListener('animationend', () => item.remove(), { once: true });
    },
};

const showToast = (msg, type = 'info') => ToastModule.show(msg, type);
```

- [ ] **Step 4: Verify upgraded toasts**

Trigger multiple toasts (upload a file, it fires a success toast; type an invalid page range to fire an error). Toasts should slide in from the right, stack (max 3), show a colored countdown bar at bottom, and slide out. Clicking a toast dismisses it early.

- [ ] **Step 5: Commit**

```bash
git add frontend/index.html frontend/styles.css frontend/app.js
git commit -m "feat(ui): upgraded toast notifications with slide-in + countdown (S)"
```

---

## Task 8: Per-item History Delete (4)

**Files:**
- Modify: `frontend/app.js` — HistoryModule: add `removeItem()`, update `_render()`
- Modify: `frontend/styles.css` — add `.history-delete-btn` styles

- [ ] **Step 1: Add history delete button CSS**

In `frontend/styles.css`, find `.history-item`. Add after it:

```css
/* ── Per-item History Delete (4) ───────────────────────────── */
.history-item {
    position: relative;
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
    padding: 0.75rem;
    border-radius: 8px;
    background: rgba(100, 116, 139, 0.1);
    border: 1px solid var(--border-color);
    transition: background 0.15s;
}

.history-item:hover {
    background: rgba(100, 116, 139, 0.15);
}

.history-item-actions {
    position: absolute;
    top: 0.5rem;
    right: 0.5rem;
    display: flex;
    gap: 0.25rem;
    opacity: 0;
    transition: opacity 0.15s;
}

.history-item:hover .history-item-actions {
    opacity: 1;
}

.history-action-btn {
    background: rgba(100, 116, 139, 0.2);
    border: 1px solid var(--border-color);
    border-radius: 6px;
    color: var(--text-secondary);
    cursor: pointer;
    font-size: 0.75rem;
    padding: 0.2rem 0.45rem;
    transition: all 0.15s;
    line-height: 1.4;
}

.history-action-btn:hover {
    background: rgba(239, 68, 68, 0.15);
    border-color: rgba(239, 68, 68, 0.4);
    color: #ef4444;
}

.history-reprint-btn:hover {
    background: rgba(102, 126, 234, 0.15);
    border-color: rgba(102, 126, 234, 0.4);
    color: #667eea;
}
```

- [ ] **Step 2: Add removeItem() to HistoryModule and update _render()**

In `frontend/app.js`, replace the `HistoryModule._render()` method and add `removeItem()`:

```js
removeItem(index) {
    const items = this._load();
    items.splice(index, 1);
    this._save(items);
    this._render();
    showToast('Đã xóa mục lịch sử', 'info');
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
    container.innerHTML = items.map((item, idx) => `
        <div class="history-item">
            <div class="history-item-actions">
                <button class="history-action-btn history-reprint-btn" data-idx="${idx}" title="In lại">🔁</button>
                <button class="history-action-btn" data-delete="${idx}" title="Xóa">✕</button>
            </div>
            <div class="history-file">📄 ${item.file}</div>
            <div class="history-meta">🖨️ ${item.printer} · ${item.pages} trang · ${modeLabel[item.mode] || item.mode} · ${item.copies} bản</div>
            <div class="history-time">${item.time}</div>
        </div>
    `).join('');

    // Attach delete handlers
    container.querySelectorAll('[data-delete]').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            this.removeItem(parseInt(btn.dataset.delete));
        });
    });

    // Attach reprint handlers (Task E)
    container.querySelectorAll('.history-reprint-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            this._reprint(parseInt(btn.dataset.idx));
        });
    });
},
```

- [ ] **Step 3: Verify per-item delete**

Print something to add history. Open history panel. Hover a history item — × and 🔁 buttons should appear. Click × to delete just that one item. The rest remain. "Xóa" (clear all) still works.

- [ ] **Step 4: Commit**

```bash
git add frontend/app.js frontend/styles.css
git commit -m "feat(ui): per-item history delete (4)"
```

---

## Task 9: Reprint from History (E)

**Files:**
- Modify: `frontend/app.js` — HistoryModule: add `_reprint()` method; `add()` stores additional fields

- [ ] **Step 1: Update HistoryModule.add() to store more fields**

In `frontend/app.js`, find `HistoryModule.add(entry)` (around line 723). The method currently stores what's passed. Update the call site in `PrintModule._startPrint()` (around line 821) to include `printerName`, `mode`, `pageRange`, `copies`:

```js
// In PrintModule._startPrint(), replace the HistoryModule.add() call:
HistoryModule.add({
    file:        AppState.uploadedFile.name,
    fileId:      AppState.uploadedFile.id,
    printer:     AppState.selectedPrinter.name,
    printerData: AppState.selectedPrinter,
    pages:       AppState.selectedPages.size,
    pageRange,
    mode:        document.querySelector('input[name="print-mode"]:checked')?.value || 'normal',
    copies:      CopiesModule.copies,
    collate:     CopiesModule.collate,
});
```

- [ ] **Step 2: Add _reprint() method to HistoryModule**

In `frontend/app.js`, add `_reprint()` inside HistoryModule (after `removeItem()`):

```js
_reprint(index) {
    const items = this._load();
    const item  = items[index];
    if (!item) return;

    // Restore printer selection
    if (item.printerData) {
        AppState.selectedPrinter = item.printerData;
        document.querySelectorAll('.printer-item').forEach(el => {
            const p = JSON.parse(el.dataset.printer || '{}');
            el.classList.toggle('selected', p.name === item.printerData.name);
        });
    }

    // Restore mode
    if (item.mode) {
        const modeInput = document.querySelector(`input[name="print-mode"][value="${item.mode}"]`);
        if (modeInput) modeInput.click();
    }

    // Restore copies
    if (item.copies) {
        CopiesModule._copies = item.copies;
        CopiesModule._update();
    }

    // Restore page range
    if (item.pageRange && AppState.totalPageCount > 0) {
        const input = document.getElementById('page-range-input');
        if (input) {
            input.value = item.pageRange;
            input.dispatchEvent(new Event('input'));
        }
    }

    showToast(`Đã khôi phục cài đặt in "${item.file}"`, 'info');
    PrintModule.updateButton();
},
```

- [ ] **Step 3: Verify reprint**

Print a job to create history. Remove the file (click ✕). Click 🔁 on the history item. Printer, mode, and copies should be restored. Page range restored if file is still in session.

- [ ] **Step 4: Commit**

```bash
git add frontend/app.js
git commit -m "feat(ui): reprint from history (E)"
```

---

## Task 10: Thumbnail Pop Animation (O)

**Files:**
- Modify: `frontend/styles.css` — add @keyframes pop
- Modify: `frontend/app.js` — PageSelectModule.toggle() adds pop class

- [ ] **Step 1: Add pop animation CSS**

In `frontend/styles.css`, after `.page-thumbnail` styles, add:

```css
/* ── Thumbnail Pop Animation (O) ───────────────────────────── */
@keyframes thumbPop {
    0%   { transform: scale(1); }
    40%  { transform: scale(1.07); }
    70%  { transform: scale(0.97); }
    100% { transform: scale(1); }
}

.page-thumbnail.pop {
    animation: thumbPop 180ms ease-out;
}
```

- [ ] **Step 2: Add pop class on toggle in PageSelectModule.toggle()**

In `frontend/app.js`, update `PageSelectModule.toggle(pageNum)` (around line 417):

```js
toggle(pageNum) {
    if (AppState.selectedPages.has(pageNum)) AppState.selectedPages.delete(pageNum);
    else AppState.selectedPages.add(pageNum);

    // Pop animation
    const thumb = document.querySelector(`.page-thumbnail[data-page-number="${pageNum}"]`);
    if (thumb) {
        thumb.classList.remove('pop');
        void thumb.offsetWidth; // force reflow to re-trigger animation
        thumb.classList.add('pop');
        thumb.addEventListener('animationend', () => thumb.classList.remove('pop'), { once: true });
    }

    PreviewModule.updateThumbnails();
    this.updateDisplay();
    PrintModule.updateButton();
},
```

- [ ] **Step 3: Verify pop animation**

Upload a PDF. Click thumbnails to select/deselect. Each click should trigger a brief scale-up-and-settle animation.

- [ ] **Step 4: Commit**

```bash
git add frontend/styles.css frontend/app.js
git commit -m "feat(ui): thumbnail pop animation on select (O)"
```

---

## Task 11: Final Verification

- [ ] **Step 1: Run full LSP diagnostics**

Open Chrome DevTools console. Load `frontend/index.html`. Verify zero JavaScript errors in console.

- [ ] **Step 2: Test all 9 features work together**

Test checklist:
- [ ] Theme toggle animates smoothly (H)
- [ ] Cards animate in staggered on load (K)
- [ ] Sidebar shows frosted glass effect (Q)
- [ ] Upload shows shimmer on progress bar (M)
- [ ] Print button shows ripple on click (L)
- [ ] Mode card animates when clicked (P)
- [ ] Toasts slide in from right, show countdown, stack up to 3 (S)
- [ ] History items show × and 🔁 on hover; × removes one item (4)
- [ ] 🔁 restores printer/mode/copies from history (E)
- [ ] Clicking thumbnail shows pop animation (O)

- [ ] **Step 3: Test dark and light themes**

Switch between themes. All animations should work in both modes. No visual artifacts.

- [ ] **Step 4: Final commit summary**

```bash
git log --oneline -10
```

Should show 9 commits from this plan.
